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

using System.Globalization;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pervaxis.Genesis.Idempotency.Abstractions;
using Pervaxis.Genesis.Idempotency.Options;

namespace Pervaxis.Genesis.Idempotency.AWS.Providers.DynamoDb;

/// <summary>
/// DynamoDB-backed implementation of <see cref="IIdempotencyStore"/>.
/// Uses conditional writes for atomicity and DynamoDB TTL for automatic expiration.
/// </summary>
internal sealed class DynamoDbIdempotencyStore : IIdempotencyStore
{
    private readonly IAmazonDynamoDB _dynamoDb;
    private readonly IdempotencyOptions _options;
    private readonly ILogger<DynamoDbIdempotencyStore> _logger;

    public DynamoDbIdempotencyStore(
        IAmazonDynamoDB dynamoDb,
        IOptions<IdempotencyOptions> options,
        ILogger<DynamoDbIdempotencyStore> logger)
    {
        _dynamoDb = dynamoDb;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<IdempotencyRecord?> TryGetRecordAsync(
        string tenantId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var compositeKey = BuildCompositeKey(tenantId, idempotencyKey);

        var response = await _dynamoDb.GetItemAsync(new GetItemRequest
        {
            TableName = _options.TableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["PK"] = new AttributeValue { S = compositeKey }
            },
            ConsistentRead = true
        }, cancellationToken);

        if (!response.IsItemSet || response.Item.Count == 0)
        {
            return null;
        }

        var record = MapFromDynamoDb(response.Item);

        // Check if expired (belt-and-suspenders alongside DynamoDB TTL)
        if (record.ExpiresAtEpoch <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        {
            return null;
        }

        return record;
    }

    /// <inheritdoc/>
    public async Task<bool> CreateInFlightRecordAsync(
        string tenantId,
        string idempotencyKey,
        string fingerprint,
        CancellationToken cancellationToken = default)
    {
        var compositeKey = BuildCompositeKey(tenantId, idempotencyKey);
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(_options.TtlMinutes).ToUnixTimeSeconds();

        var item = new Dictionary<string, AttributeValue>
        {
            ["PK"] = new AttributeValue { S = compositeKey },
            ["IdempotencyKey"] = new AttributeValue { S = idempotencyKey },
            ["Fingerprint"] = new AttributeValue { S = fingerprint },
            ["IsCompleted"] = new AttributeValue { BOOL = false },
            ["CreatedAt"] = new AttributeValue { S = now.ToString("O", CultureInfo.InvariantCulture) },
            ["ExpiresAt"] = new AttributeValue { N = expiresAt.ToString(CultureInfo.InvariantCulture) }
        };

        try
        {
            await _dynamoDb.PutItemAsync(new PutItemRequest
            {
                TableName = _options.TableName,
                Item = item,
                // Only succeed if no unexpired record exists
                ConditionExpression = "attribute_not_exists(PK) OR ExpiresAt < :now",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":now"] = new AttributeValue { N = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture) }
                }
            }, cancellationToken);

            return true;
        }
        catch (ConditionalCheckFailedException)
        {
            // Record already exists and hasn't expired
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> CompleteRecordAsync(
        string tenantId,
        string idempotencyKey,
        IdempotencyRecord record,
        CancellationToken cancellationToken = default)
    {
        var compositeKey = BuildCompositeKey(tenantId, idempotencyKey);

        var expressionValues = new Dictionary<string, AttributeValue>
        {
            [":completed"] = new AttributeValue { BOOL = true },
            [":notCompleted"] = new AttributeValue { BOOL = false },
            [":statusCode"] = new AttributeValue { N = (record.StatusCode ?? 200).ToString(CultureInfo.InvariantCulture) },
            [":expiresAt"] = new AttributeValue { N = record.ExpiresAtEpoch.ToString(CultureInfo.InvariantCulture) }
        };

        var updateExpression = "SET IsCompleted = :completed, StatusCode = :statusCode, ExpiresAt = :expiresAt";

        if (!string.IsNullOrEmpty(record.ResponseHeaders))
        {
            expressionValues[":headers"] = new AttributeValue { S = record.ResponseHeaders };
            updateExpression += ", ResponseHeaders = :headers";
        }

        if (!string.IsNullOrEmpty(record.ResponseBody))
        {
            expressionValues[":body"] = new AttributeValue { S = record.ResponseBody };
            updateExpression += ", ResponseBody = :body";
        }

        try
        {
            await _dynamoDb.UpdateItemAsync(new UpdateItemRequest
            {
                TableName = _options.TableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    ["PK"] = new AttributeValue { S = compositeKey }
                },
                UpdateExpression = updateExpression,
                ExpressionAttributeValues = expressionValues,
                ConditionExpression = "attribute_exists(PK) AND IsCompleted = :notCompleted"
            }, cancellationToken);

            return true;
        }
        catch (ConditionalCheckFailedException)
        {
            _logger.LogWarning("Cannot complete idempotency record for key {IdempotencyKey}: no in-flight record found.", idempotencyKey);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task DeleteRecordAsync(
        string tenantId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var compositeKey = BuildCompositeKey(tenantId, idempotencyKey);

        await _dynamoDb.DeleteItemAsync(new DeleteItemRequest
        {
            TableName = _options.TableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["PK"] = new AttributeValue { S = compositeKey }
            }
        }, cancellationToken);
    }

    private static string BuildCompositeKey(string tenantId, string idempotencyKey)
    {
        return $"{tenantId}#{idempotencyKey}";
    }

    private static IdempotencyRecord MapFromDynamoDb(Dictionary<string, AttributeValue> item)
    {
        return new IdempotencyRecord
        {
            IdempotencyKey = item.GetValueOrDefault("IdempotencyKey")?.S ?? string.Empty,
            CompositeKey = item.GetValueOrDefault("PK")?.S ?? string.Empty,
            Fingerprint = item.GetValueOrDefault("Fingerprint")?.S ?? string.Empty,
            IsCompleted = item.GetValueOrDefault("IsCompleted")?.BOOL ?? false,
            StatusCode = item.TryGetValue("StatusCode", out var sc) && sc.N is not null
                ? int.Parse(sc.N, CultureInfo.InvariantCulture) : null,
            ResponseHeaders = item.GetValueOrDefault("ResponseHeaders")?.S,
            ResponseBody = item.GetValueOrDefault("ResponseBody")?.S,
            CreatedAt = item.TryGetValue("CreatedAt", out var ca) && ca.S is not null
                ? DateTimeOffset.Parse(ca.S, CultureInfo.InvariantCulture) : DateTimeOffset.UtcNow,
            ExpiresAtEpoch = item.TryGetValue("ExpiresAt", out var ea) && ea.N is not null
                ? long.Parse(ea.N, CultureInfo.InvariantCulture) : 0
        };
    }
}
