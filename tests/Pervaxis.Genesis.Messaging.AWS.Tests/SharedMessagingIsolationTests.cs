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

using System.Collections.Concurrent;
using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using Amazon.SimpleNotificationService;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Pervaxis.Core.Abstractions.Genesis.Modules;
using Pervaxis.Core.Abstractions.MultiTenancy;
using Pervaxis.Genesis.Base.Exceptions;
using Pervaxis.Genesis.Messaging.AWS.Extensions;
using Pervaxis.Genesis.Messaging.AWS.Options;
using Pervaxis.Genesis.Messaging.AWS.Providers.Sqs;
using Sns = Amazon.SimpleNotificationService.Model;

namespace Pervaxis.Genesis.Messaging.AWS.Tests;

public sealed class SharedMessagingIsolationTests
{
    private const string Queue = "https://sqs.us-east-1.amazonaws.com/123/orders-shared";
    private const string Dlq = "https://sqs.us-east-1.amazonaws.com/123/orders-dlq";
    private const string Topic = "arn:aws:sns:us-east-1:123:orders-shared";
    private static readonly string[] TenantIds = ["acme", "globex"];

    private sealed class Reset(Action reset) : IDisposable
    {
        public void Dispose() => reset();
    }

    private sealed class Ambient : ITenantContext, ITenantScopeFactory
    {
        public AsyncLocal<string?> Current { get; } = new();
        public TenantId TenantId => new(Current.Value ?? "unset");
        public string TenantName => Current.Value ?? string.Empty;
        public TenantStatus Status => TenantStatus.Active;
        public bool IsResolved => Current.Value is not null;
        public IDisposable BeginScope(TenantId tenantId)
        {
            var previous = Current.Value;
            if (previous is not null && previous != tenantId.Value)
            {
                throw new InvalidOperationException("Cannot nest a foreign tenant.");
            }
            Current.Value = tenantId.Value;
            return new Reset(() => Current.Value = previous);
        }
    }

    private sealed class HandlerDependency(ITenantContext context) : IAsyncDisposable
    {
        public Guid Id { get; } = Guid.NewGuid();
        public string Tenant { get; } = context.TenantId.Value;
        public bool Disposed { get; private set; }
        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Fixture : IDisposable
    {
        public Ambient Context { get; } = new();
        public Mock<IAmazonSQS> Sqs { get; } = new();
        public Mock<IAmazonSimpleNotificationService> Sns { get; } = new();
        public ServiceProvider Root { get; }
        public ConcurrentBag<DeleteMessageRequest> Deletes { get; } = [];

        public Fixture(bool worker = true)
        {
            Sqs.Setup(c => c.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SendMessageResponse { MessageId = "sent" });
            Sqs.Setup(c => c.DeleteMessageAsync(It.IsAny<DeleteMessageRequest>(), It.IsAny<CancellationToken>()))
                .Callback<DeleteMessageRequest, CancellationToken>((r, _) => Deletes.Add(r))
                .ReturnsAsync(new DeleteMessageResponse());
            Sqs.Setup(c => c.ChangeMessageVisibilityAsync(It.IsAny<ChangeMessageVisibilityRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ChangeMessageVisibilityResponse());
            Sns.Setup(c => c.PublishAsync(It.IsAny<Sns.PublishRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Sns.PublishResponse { MessageId = "sent" });

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<ITenantContext>(Context);
            services.AddSingleton<ITenantScopeFactory>(Context);
            services.AddScoped(sp => new HandlerDependency(sp.GetRequiredService<ITenantContext>()));
            services.AddKeyedSingleton<IAmazonSQS>(MessagingServiceCollectionExtensions.SqsClientKey, (_, _) => Sqs.Object);
            services.AddKeyedSingleton<IAmazonSimpleNotificationService>(MessagingServiceCollectionExtensions.SnsClientKey, (_, _) => Sns.Object);
            services.AddGenesisMessaging(Configure);
            if (worker)
            {
                services.AddGenesisSharedQueueConsumer(Configure);
            }
            Root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        }

        public static void Configure(MessagingOptions options)
        {
            options.Region = "us-east-1";
            options.Resilience.Enabled = false;
            options.Sqs.QueueUrlMappings["orders"] = Queue;
            options.Sqs.DlqUrl = Dlq;
            options.Sns.TopicArnMappings["orders"] = Topic;
        }

        public void Poll(params Message[] messages)
            => Sqs.Setup(c => c.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReceiveMessageResponse { Messages = [.. messages] });

        public void Dispose() => Root.Dispose();
    }

    private static Message Envelope(string tenant, string receipt, string? body = null)
        => new()
        {
            MessageId = receipt, ReceiptHandle = receipt,
            Body = body ?? JsonSerializer.Serialize(new { Tenant = tenant }),
            MessageAttributes = new Dictionary<string, MessageAttributeValue>
            {
                [MessagingConstants.TenantIdAttribute] = new() { DataType = "String", StringValue = tenant }
            }
        };

    [Theory]
    [InlineData("sqs")]
    [InlineData("sns")]
    public async Task Publish_UsesSharedResourceAndCurrentScopeTenant(string key)
    {
        using var fixture = new Fixture();
        var stamps = new List<(string Resource, string Tenant)>();
        fixture.Sqs.Setup(c => c.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .Callback<SendMessageRequest, CancellationToken>((r, _) => stamps.Add((r.QueueUrl, r.MessageAttributes[MessagingConstants.TenantIdAttribute].StringValue)))
            .ReturnsAsync(new SendMessageResponse { MessageId = "sent" });
        fixture.Sns.Setup(c => c.PublishAsync(It.IsAny<Sns.PublishRequest>(), It.IsAny<CancellationToken>()))
            .Callback<Sns.PublishRequest, CancellationToken>((r, _) => stamps.Add((r.TopicArn, r.MessageAttributes[MessagingConstants.TenantIdAttribute].StringValue)))
            .ReturnsAsync(new Sns.PublishResponse { MessageId = "sent" });
        foreach (var tenant in TenantIds)
        {
            using var identity = fixture.Context.BeginScope(new TenantId(tenant));
            using var scope = fixture.Root.CreateScope();
            await scope.ServiceProvider.GetRequiredKeyedService<IMessaging>(key).PublishAsync("orders", new { Value = 1 });
        }
        Assert.Equal(TenantIds, stamps.Select(s => s.Tenant));
        Assert.All(stamps, s => Assert.Equal(key == "sqs" ? Queue : Topic, s.Resource));
        fixture.Sqs.Verify(c => c.Dispose(), Times.Never);
        fixture.Sns.Verify(c => c.Dispose(), Times.Never);
    }

    [Theory]
    [InlineData("sqs")]
    [InlineData("sns")]
    public async Task PublishAndEmptyBatch_RequireValidStableTenant(string key)
    {
        using var fixture = new Fixture();
        using var scope = fixture.Root.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredKeyedService<IMessaging>(key);
        await Assert.ThrowsAsync<GenesisException>(() => provider.PublishAsync("orders", new { Value = 1 }));
        await Assert.ThrowsAsync<GenesisException>(() => provider.PublishBatchAsync("orders", Array.Empty<object>()));
        fixture.Context.Current.Value = "INVALID";
        await Assert.ThrowsAsync<GenesisException>(() => provider.PublishAsync("orders", new { Value = 1 }));
        fixture.Context.Current.Value = "platform";
        await Assert.ThrowsAsync<GenesisException>(() => provider.PublishAsync("orders", new { Value = 1 }));
        fixture.Context.Current.Value = "acme";
        await provider.PublishAsync("orders", new { Value = 1 });
        fixture.Context.Current.Value = "globex";
        await Assert.ThrowsAsync<GenesisException>(() => provider.PublishAsync("orders", new { Value = 1 }));
        await Assert.ThrowsAsync<GenesisException>(() => provider.PublishBatchAsync("orders", Array.Empty<object>()));
        fixture.Context.Current.Value = null;
    }

    [Theory]
    [InlineData("sqs")]
    [InlineData("sns")]
    public async Task Batch_StampsEveryChunkAndPayloadCannotSpoofEnvelopeTenant(string key)
    {
        using var fixture = new Fixture();
        using var identity = fixture.Context.BeginScope(new TenantId("acme"));
        using var scope = fixture.Root.CreateScope();
        var stamps = new List<string>();
        fixture.Sqs.Setup(c => c.SendMessageBatchAsync(It.IsAny<SendMessageBatchRequest>(), It.IsAny<CancellationToken>()))
            .Returns<SendMessageBatchRequest, CancellationToken>((request, _) =>
            {
                stamps.AddRange(request.Entries.Select(e => e.MessageAttributes[MessagingConstants.TenantIdAttribute].StringValue));
                Assert.Equal(Queue, request.QueueUrl);
                return Task.FromResult(new SendMessageBatchResponse
                {
                    Successful = request.Entries.Select(e => new SendMessageBatchResultEntry { Id = e.Id, MessageId = e.Id }).ToList(), Failed = []
                });
            });
        fixture.Sns.Setup(c => c.PublishBatchAsync(It.IsAny<Sns.PublishBatchRequest>(), It.IsAny<CancellationToken>()))
            .Returns<Sns.PublishBatchRequest, CancellationToken>((request, _) =>
            {
                stamps.AddRange(request.PublishBatchRequestEntries.Select(e => e.MessageAttributes[MessagingConstants.TenantIdAttribute].StringValue));
                Assert.Equal(Topic, request.TopicArn);
                return Task.FromResult(new Sns.PublishBatchResponse
                {
                    Successful = request.PublishBatchRequestEntries.Select(e => new Sns.PublishBatchResultEntry { Id = e.Id, MessageId = e.Id }).ToList(), Failed = []
                });
            });
        var payloads = Enumerable.Range(0, 11).Select(_ => new Dictionary<string, string>
        {
            [MessagingConstants.TenantIdAttribute] = "globex"
        });
        var result = await scope.ServiceProvider.GetRequiredKeyedService<IMessaging>(key).PublishBatchAsync("orders", payloads);
        Assert.Equal(11, result.Count());
        Assert.Equal(11, stamps.Count);
        Assert.All(stamps, tenant => Assert.Equal("acme", tenant));
    }

    [Fact]
    public async Task TenantReceive_ExposesOnlyOwnPayloadAndCannotDeleteForeignOrUnknownReceipt()
    {
        using var fixture = new Fixture();
        using var identity = fixture.Context.BeginScope(new TenantId("acme"));
        using var scope = fixture.Root.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<SqsMessagingProvider>();
        fixture.Poll(Envelope("globex", "foreign", "malformed"), Envelope("acme", "owned"));
        var result = (await provider.ReceiveWithMetadataAsync<Dictionary<string, string>>("orders")).ToList();
        Assert.Single(result);
        Assert.Equal("acme", result[0].TenantId);
        Assert.Equal("acme", result[0].Message["Tenant"]);
        await Assert.ThrowsAsync<GenesisException>(() => provider.DeleteAsync("orders", "foreign"));
        await Assert.ThrowsAsync<GenesisException>(() => provider.DeleteAsync("orders", "unknown"));
        await Assert.ThrowsAsync<GenesisException>(() => provider.DeleteAsync("other-queue", "owned"));
        Assert.True(await provider.DeleteAsync("orders", "owned"));
        Assert.Single(fixture.Deletes);
        fixture.Sqs.Verify(c => c.ChangeMessageVisibilityAsync(It.Is<ChangeMessageVisibilityRequest>(r => r.ReceiptHandle == "foreign" && r.VisibilityTimeout == 0), It.IsAny<CancellationToken>()), Times.Once);
        fixture.Sqs.Verify(c => c.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TenantReceiveAndDelete_RequireTenantBeforeAnyAwsCall()
    {
        using var fixture = new Fixture();
        using var scope = fixture.Root.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<SqsMessagingProvider>();
        await Assert.ThrowsAsync<GenesisException>(() => provider.ReceiveAsync<object>("orders"));
        await Assert.ThrowsAsync<GenesisException>(() => provider.ReceiveWithMetadataAsync<object>("orders"));
        await Assert.ThrowsAsync<GenesisException>(() => provider.DeleteAsync("orders", "receipt"));
        fixture.Sqs.Verify(c => c.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(fixture.Deletes);
    }

    [Fact]
    public async Task ReceiptCannotBeReusedAcrossProviderScopesOrTenants()
    {
        using var fixture = new Fixture();
        fixture.Context.Current.Value = "acme";
        using (var first = fixture.Root.CreateScope())
        {
            fixture.Poll(Envelope("acme", "owned"));
            await first.ServiceProvider.GetRequiredService<SqsMessagingProvider>().ReceiveWithMetadataAsync<object>("orders");
        }
        foreach (var tenant in TenantIds)
        {
            fixture.Context.Current.Value = tenant;
            using var second = fixture.Root.CreateScope();
            await Assert.ThrowsAsync<GenesisException>(() => second.ServiceProvider.GetRequiredService<SqsMessagingProvider>().DeleteAsync("orders", "owned"));
        }
        fixture.Context.Current.Value = null;
        Assert.Empty(fixture.Deletes);
    }

    [Fact]
    public async Task Worker_OverlappingHandlersHaveSeparateTenantAndDependencyScopes()
    {
        using var fixture = new Fixture();
        using var scope = fixture.Root.CreateScope();
        var worker = scope.ServiceProvider.GetRequiredService<ISharedQueueConsumer>();
        fixture.Poll(Envelope("acme", "one"), Envelope("globex", "two"));
        var dependencies = new ConcurrentBag<HandlerDependency>();
        fixture.Sqs.Setup(c => c.DeleteMessageAsync(It.IsAny<DeleteMessageRequest>(), It.IsAny<CancellationToken>()))
            .Callback<DeleteMessageRequest, CancellationToken>((request, _) =>
            {
                Assert.False(fixture.Context.IsResolved);
                var tenant = request.ReceiptHandle == "one" ? "acme" : "globex";
                Assert.True(Assert.Single(dependencies, d => d.Tenant == tenant).Disposed);
                fixture.Deletes.Add(request);
            }).ReturnsAsync(new DeleteMessageResponse());
        var bothEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = 0;
        await worker.ProcessAsync<Dictionary<string, string>>("orders", async (payload, sp, ct) =>
        {
            var dependency = sp.GetRequiredService<HandlerDependency>();
            dependencies.Add(dependency);
            Assert.Equal(payload["Tenant"], dependency.Tenant);
            if (Interlocked.Increment(ref entered) == 2)
            {
                bothEntered.SetResult();
            }
            await bothEntered.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
            Assert.Equal(payload["Tenant"], sp.GetRequiredService<ITenantContext>().TenantId.Value);
            Assert.False(dependency.Disposed);
        });
        Assert.Equal(2, entered);
        Assert.Equal(2, dependencies.Select(d => d.Id).Distinct().Count());
        Assert.All(dependencies, d => Assert.True(d.Disposed));
        Assert.Equal(2, fixture.Deletes.Count);
        Assert.False(fixture.Context.IsResolved);
    }

    [Fact]
    public async Task Worker_FailedHandlerIsNotAcknowledgedAndOtherTenantStillSucceeds()
    {
        using var fixture = new Fixture();
        using var scope = fixture.Root.CreateScope();
        fixture.Poll(Envelope("acme", "fail"), Envelope("globex", "success"));
        var dependencies = new ConcurrentBag<HandlerDependency>();
        await scope.ServiceProvider.GetRequiredService<ISharedQueueConsumer>().ProcessAsync<Dictionary<string, string>>(
            "orders", async (payload, sp, _) =>
            {
                dependencies.Add(sp.GetRequiredService<HandlerDependency>());
                await Task.Yield();
                if (payload["Tenant"] == "acme")
                {
                    throw new InvalidOperationException("Handler failed.");
                }
            });
        Assert.Equal("success", Assert.Single(fixture.Deletes).ReceiptHandle);
        Assert.All(dependencies, d => Assert.True(d.Disposed));
        Assert.False(fixture.Context.IsResolved);
    }

    [Fact]
    public async Task Worker_CancellationRestoresContextAndDoesNotAcknowledge()
    {
        using var fixture = new Fixture();
        using var scope = fixture.Root.CreateScope();
        fixture.Poll(Envelope("acme", "cancel"));
        using var cancellation = new CancellationTokenSource();
        HandlerDependency? dependency = null;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scope.ServiceProvider.GetRequiredService<ISharedQueueConsumer>()
            .ProcessAsync<Dictionary<string, string>>("orders", async (_, sp, _) =>
            {
                dependency = sp.GetRequiredService<HandlerDependency>();
                await cancellation.CancelAsync();
            }, cancellationToken: cancellation.Token));
        Assert.True(dependency!.Disposed);
        Assert.Empty(fixture.Deletes);
        Assert.False(fixture.Context.IsResolved);
    }

    [Fact]
    public async Task Worker_ContextChangedByHandlerIsNotAcknowledged()
    {
        using var fixture = new Fixture();
        using var scope = fixture.Root.CreateScope();
        fixture.Poll(Envelope("acme", "changed"));
        await scope.ServiceProvider.GetRequiredService<ISharedQueueConsumer>().ProcessAsync<object>(
            "orders", (_, _, _) =>
            {
                fixture.Context.Current.Value = "globex";
                return Task.CompletedTask;
            });
        Assert.Empty(fixture.Deletes);
        Assert.False(fixture.Context.IsResolved);
    }

    [Fact]
    public async Task WorkerCannotRunInsideTenantScope_AndRequiresExplicitRegistration()
    {
        using var fixture = new Fixture();
        using var identity = fixture.Context.BeginScope(new TenantId("acme"));
        using var scope = fixture.Root.CreateScope();
        await Assert.ThrowsAsync<GenesisException>(() => scope.ServiceProvider.GetRequiredService<ISharedQueueConsumer>()
            .ProcessAsync<object>("orders", (_, _, _) => Task.CompletedTask));
        fixture.Sqs.Verify(c => c.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        using var tenantOnly = new Fixture(worker: false);
        using var tenantScope = tenantOnly.Root.CreateScope();
        Assert.Null(tenantScope.ServiceProvider.GetService<ISharedQueueConsumer>());
    }

    [Theory]
    [InlineData("Number")]
    [InlineData("String.custom")]
    [InlineData("Binary")]
    public async Task InvalidTenantAttributeType_IsQuarantinedBeforePayloadDeserialization(string type)
    {
        using var fixture = new Fixture();
        using var scope = fixture.Root.CreateScope();
        var message = Envelope("acme", "invalid", "malformed");
        message.MessageAttributes[MessagingConstants.TenantIdAttribute].DataType = type;
        fixture.Poll(message);
        var handled = false;
        await scope.ServiceProvider.GetRequiredService<ISharedQueueConsumer>().ProcessAsync<object>("orders", (_, _, _) =>
        {
            handled = true;
            return Task.CompletedTask;
        });
        Assert.False(handled);
        Assert.Single(fixture.Deletes);
        fixture.Sqs.Verify(c => c.SendMessageAsync(It.Is<SendMessageRequest>(r => r.QueueUrl == Dlq), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MalformedPayload_DlqFailureLeavesOriginalMessageUnacknowledged()
    {
        using var fixture = new Fixture();
        using var scope = fixture.Root.CreateScope();
        fixture.Poll(Envelope("acme", "invalid", "malformed"));
        fixture.Sqs.Setup(c => c.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonSQSException("DLQ unavailable"));
        await Assert.ThrowsAsync<GenesisException>(() => scope.ServiceProvider.GetRequiredService<ISharedQueueConsumer>()
            .ProcessAsync<object>("orders", (_, _, _) => Task.CompletedTask));
        Assert.Empty(fixture.Deletes);
        Assert.False(fixture.Context.IsResolved);
    }

    [Fact]
    public async Task MalformedPayload_DoesNotBlockValidOtherTenantAfterQuarantine()
    {
        using var fixture = new Fixture();
        using var scope = fixture.Root.CreateScope();
        fixture.Poll(Envelope("acme", "invalid", "malformed"), Envelope("globex", "valid"));
        var handled = 0;
        await scope.ServiceProvider.GetRequiredService<ISharedQueueConsumer>().ProcessAsync<object>("orders", (_, _, _) =>
        {
            handled++;
            return Task.CompletedTask;
        });
        Assert.Equal(1, handled);
        Assert.Equal(2, fixture.Deletes.Count);
        fixture.Sqs.Verify(c => c.SendMessageAsync(It.Is<SendMessageRequest>(r =>
            r.QueueUrl == Dlq && r.MessageAttributes[MessagingConstants.FailureReasonAttribute].StringValue == "invalid_payload"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void IsolationCannotBeDisabled()
    {
        var options = new MessagingOptions { Region = "us-east-1", EnableTenantIsolation = false };
        Assert.Throws<GenesisConfigurationException>(() => options.Validate());
    }
}
