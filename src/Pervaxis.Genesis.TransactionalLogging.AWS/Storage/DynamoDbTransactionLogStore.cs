/*
 ************************************************************************
 * Copyright (C) 2026 Clarivex Technologies Private Limited
 * All Rights Reserved.
 *
 * NOTICE: All intellectual and technical concepts contained
 * herein are proprietary to Clarivex Technologies Private Limited
 * and may be covered by Indian and Foreign Patents,
 * patents in process, and are protected by trade secret or
 * copyright law. Dissemination of this information or reproduction
 * of this material is strictly forbidden unless prior written
 * permission is obtained from Clarivex Technologies Private Limited.
 *
 * Product:   Pervaxis Platform
 * Website:   https://clarivex.tech
 ************************************************************************
 */

using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Pervaxis.Genesis.Base.Resilience;
using Pervaxis.Genesis.TransactionalLogging.AWS.Context;
using Pervaxis.Genesis.TransactionalLogging.AWS.Diagnostics;
using Pervaxis.Genesis.TransactionalLogging.AWS.Models;
using Pervaxis.Genesis.TransactionalLogging.AWS.Options;

namespace Pervaxis.Genesis.TransactionalLogging.AWS.Storage;

/// <summary>
/// DynamoDB implementation of <see cref="ITransactionLogStore"/>.
/// Handles persistence with S3 overflow for records exceeding 400KB,
/// and supports resilience policies for all operations.
/// </summary>
internal sealed class DynamoDbTransactionLogStore : ITransactionLogStore, IDisposable
{
    private readonly IAmazonDynamoDB _dynamoDb;
    private readonly S3OverflowStore _s3Overflow;
    private readonly TransactionalLoggingOptions _options;
    private readonly ILogger<DynamoDbTransactionLogStore> _logger;
    private readonly ResiliencePipeline _resiliencePipeline;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    // DynamoDB item size limit (400KB)
    private const int MaxItemSizeBytes = 400 * 1024;

    /// <summary>
    /// Initializes a new instance of the <see cref="DynamoDbTransactionLogStore"/> class.
    /// </summary>
    public DynamoDbTransactionLogStore(
        IAmazonDynamoDB dynamoDb,
        S3OverflowStore s3Overflow,
        IOptions<TransactionalLoggingOptions> options,
        ILogger<DynamoDbTransactionLogStore> logger)
    {
        ArgumentNullException.ThrowIfNull(dynamoDb);
        ArgumentNullException.ThrowIfNull(s3Overflow);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _dynamoDb = dynamoDb;
        _s3Overflow = s3Overflow;
        _options = options.Value;
        _logger = logger;
        _resiliencePipeline = GenesisResiliencePipelineBuilder.BuildPipeline(
            _options.Resilience, _logger, "TransactionalLogging-DynamoDB");
    }

    /// <inheritdoc/>
    public async Task PersistAsync(TransactionContext context, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        using var activity = TransactionLogTracing.StartPersistActivity("dynamodb");

        try
        {
            await _resiliencePipeline.ExecuteAsync(async ct =>
            {
                var item = BuildDynamoDbItem(context);
                var entriesJson = JsonSerializer.Serialize(context.Entries, JsonOptions);

                // Check if item exceeds DynamoDB size limit
                if (System.Text.Encoding.UTF8.GetByteCount(entriesJson) > MaxItemSizeBytes)
                {
                    // Overflow to S3
                    var s3Key = await _s3Overflow.WriteOverflowAsync(context, ct);
                    item["S3OverflowKey"] = new AttributeValue { S = s3Key };
                    item["Entries"] = new AttributeValue { S = "[]" }; // Summary only in DDB

                    TransactionLogLogMessages.S3Overflow(
                        _logger, context.TransactionId, context.Entries.Count, s3Key);
                }
                else
                {
                    item["Entries"] = new AttributeValue { S = entriesJson };
                }

                await _dynamoDb.PutItemAsync(new PutItemRequest
                {
                    TableName = _options.TableName,
                    Item = item
                }, ct);
            }, cancellationToken);

            stopwatch.Stop();
            TransactionLogMetrics.RecordPersistDuration(stopwatch.Elapsed.TotalMilliseconds, "success");
            TransactionLogTracing.FinalizePersistActivity(activity, "success");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            TransactionLogMetrics.RecordPersistDuration(stopwatch.Elapsed.TotalMilliseconds, "error");
            TransactionLogTracing.FinalizePersistActivity(activity, "error");

            var reason = ex switch
            {
                TimeoutException => "timeout",
                ProvisionedThroughputExceededException => "throttled",
                _ => "other"
            };
            TransactionLogMetrics.RecordPersistFailure(reason);

            TransactionLogLogMessages.PersistenceFailed(
                _logger, context.TransactionId, ex.Message, false);

            // Fail-open: never propagate to caller
        }
    }

    /// <inheritdoc/>
    public async Task<TransactionLogQueryResult> QueryAsync(
        TransactionLogQuery query, CancellationToken cancellationToken = default)
    {
        return await _resiliencePipeline.ExecuteAsync(async ct =>
        {
            // Query by TransactionId via GSI
            if (!string.IsNullOrEmpty(query.TransactionId))
            {
                return await QueryByTransactionIdAsync(query.TransactionId, ct);
            }

            // Query by tenant + date range
            return await QueryByTenantDateAsync(query, ct);
        }, cancellationToken);
    }

    private async Task<TransactionLogQueryResult> QueryByTransactionIdAsync(
        string transactionId, CancellationToken ct)
    {
        var response = await _dynamoDb.QueryAsync(new QueryRequest
        {
            TableName = _options.TableName,
            IndexName = "TransactionId-index",
            KeyConditionExpression = "TransactionId = :txnId",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":txnId"] = new() { S = transactionId }
            }
        }, ct);

        // Return empty result if not found
        return new TransactionLogQueryResult(); // Placeholder — real implementation parses items
    }

    private async Task<TransactionLogQueryResult> QueryByTenantDateAsync(
        TransactionLogQuery query, CancellationToken ct)
    {
        var tenantId = _options.EnableTenantIsolation
            ? (query.TenantId ?? "__global__")
            : "__global__";

        var startDate = query.RangeStart?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            ?? DateTimeOffset.UtcNow.AddDays(-7).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var partitionKey = $"{tenantId}#{startDate}";

        var response = await _dynamoDb.QueryAsync(new QueryRequest
        {
            TableName = _options.TableName,
            KeyConditionExpression = "PK = :pk",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":pk"] = new() { S = partitionKey }
            },
            Limit = query.MaxResults > 0 ? query.MaxResults : 50
        }, ct);

        return new TransactionLogQueryResult(); // Placeholder — real implementation parses items
    }

    private Dictionary<string, AttributeValue> BuildDynamoDbItem(TransactionContext context)
    {
        var tenantId = _options.EnableTenantIsolation
            ? (context.TenantId ?? "__global__")
            : "__global__";

        var dateKey = context.StartTimestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var ttlEpoch = context.StartTimestamp.AddDays(_options.HotRetentionDays).ToUnixTimeSeconds();

        var item = new Dictionary<string, AttributeValue>
        {
            ["PK"] = new() { S = $"{tenantId}#{dateKey}" },
            ["SK"] = new() { S = context.TransactionId },
            ["TransactionId"] = new() { S = context.TransactionId },
            ["TenantId"] = new() { S = tenantId },
            ["StartTimestamp"] = new() { S = context.StartTimestamp.ToString("O", CultureInfo.InvariantCulture) },
            ["Status"] = new() { S = context.Status.ToString() },
            ["EntryCount"] = new() { N = context.Entries.Count.ToString(CultureInfo.InvariantCulture) },
            ["ExpiresAt"] = new() { N = ttlEpoch.ToString(CultureInfo.InvariantCulture) }
        };

        if (context.TraceId is not null)
        {
            item["TraceId"] = new() { S = context.TraceId };
        }

        if (context.CorrelationId is not null)
        {
            item["CorrelationId"] = new() { S = context.CorrelationId };
        }

        if (context.IdempotencyKey is not null)
        {
            item["IdempotencyKey"] = new() { S = context.IdempotencyKey };
        }

        if (context.HttpMethod is not null)
        {
            item["HttpMethod"] = new() { S = context.HttpMethod };
        }

        if (context.RequestPath is not null)
        {
            item["RequestPath"] = new() { S = context.RequestPath };
        }

        if (context.HttpStatusCode.HasValue)
        {
            item["StatusCode"] = new() { N = context.HttpStatusCode.Value.ToString(CultureInfo.InvariantCulture) };
        }

        if (context.EndTimestamp.HasValue)
        {
            item["EndTimestamp"] = new() { S = context.EndTimestamp.Value.ToString("O", CultureInfo.InvariantCulture) };
        }

        if (context.DurationMs.HasValue)
        {
            item["DurationMs"] = new() { N = context.DurationMs.Value.ToString("F1", CultureInfo.InvariantCulture) };
        }

        if (context.ErrorType is not null)
        {
            item["ErrorType"] = new() { S = context.ErrorType };
        }

        if (context.ErrorMessage is not null)
        {
            item["ErrorMessage"] = new() { S = context.ErrorMessage };
        }

        if (context.BusinessKeys.Count > 0)
        {
            item["BusinessKeys"] = new()
            {
                M = context.BusinessKeys.ToDictionary(
                    kv => kv.Key,
                    kv => new AttributeValue { S = kv.Value })
            };
        }

        return item;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _dynamoDb.Dispose();
    }
}
