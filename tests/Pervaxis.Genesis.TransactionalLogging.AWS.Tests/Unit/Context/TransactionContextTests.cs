using Pervaxis.Core.Abstractions.Genesis.Modules;
using Pervaxis.Genesis.TransactionalLogging.AWS.Context;

namespace Pervaxis.Genesis.TransactionalLogging.AWS.Tests.Unit.Context;

public class TransactionContextTests
{
    [Fact]
    public void TransactionId_HasCorrectFormat()
    {
        var context = new TransactionContext();
        context.TransactionId.Should().StartWith("txn_");
        context.TransactionId.Should().HaveLength(36); // "txn_" + 32 hex chars
    }

    [Fact]
    public void AddEntry_AccumulatesEntries()
    {
        var context = new TransactionContext();
        var entry = new TransactionLogEntry("Caching", "get", DateTimeOffset.UtcNow, TimeSpan.FromMilliseconds(5), true, null, null);

        context.AddEntry(entry);
        context.AddEntry(entry);

        context.Entries.Should().HaveCount(2);
    }

    [Fact]
    public async Task AddEntry_ThreadSafe_ConcurrentAdds()
    {
        var context = new TransactionContext();
        var tasks = Enumerable.Range(0, 100).Select(_ => Task.Run(() =>
        {
            var entry = new TransactionLogEntry("Test", "op", DateTimeOffset.UtcNow, TimeSpan.FromMilliseconds(1), true, null, null);
            context.AddEntry(entry);
        })).ToArray();

        await Task.WhenAll(tasks);
        context.Entries.Should().HaveCount(100);
    }

    [Fact]
    public void AddBusinessKey_StoresKeyValuePair()
    {
        var context = new TransactionContext();
        context.AddBusinessKey("OrderId", "ORD-123");

        context.BusinessKeys.Should().ContainKey("OrderId");
        context.BusinessKeys["OrderId"].Should().Be("ORD-123");
    }

    [Fact]
    public void AddBusinessKey_DuplicateKey_KeepsFirst()
    {
        var context = new TransactionContext();
        context.AddBusinessKey("OrderId", "ORD-1");
        context.AddBusinessKey("OrderId", "ORD-2");

        context.BusinessKeys["OrderId"].Should().Be("ORD-1");
    }

    [Fact]
    public void Finalize_SetsAllFields()
    {
        var context = new TransactionContext();
        Thread.Sleep(5); // Ensure measurable duration

        context.Finalize(200, TransactionLogStatus.Completed);

        context.HttpStatusCode.Should().Be(200);
        context.Status.Should().Be(TransactionLogStatus.Completed);
        context.EndTimestamp.Should().NotBeNull();
        context.DurationMs.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Finalize_WithError_SetsErrorFields()
    {
        var context = new TransactionContext();

        context.Finalize(500, TransactionLogStatus.Failed, "NullReferenceException", "Object ref not set");

        context.Status.Should().Be(TransactionLogStatus.Failed);
        context.ErrorType.Should().Be("NullReferenceException");
        context.ErrorMessage.Should().Be("Object ref not set");
    }

    [Fact]
    public void InitProperties_SetViaInit()
    {
        var context = new TransactionContext
        {
            TraceId = "trace-123",
            TenantId = "tenant-001",
            HttpMethod = "POST",
            RequestPath = "/api/orders",
            IdempotencyKey = "idem-key-1",
            CorrelationId = "corr-id-1"
        };

        context.TraceId.Should().Be("trace-123");
        context.TenantId.Should().Be("tenant-001");
        context.HttpMethod.Should().Be("POST");
        context.RequestPath.Should().Be("/api/orders");
        context.IdempotencyKey.Should().Be("idem-key-1");
        context.CorrelationId.Should().Be("corr-id-1");
    }

    [Fact]
    public void StartTimestamp_IsSetAtCreation()
    {
        var before = DateTimeOffset.UtcNow;
        var context = new TransactionContext();
        var after = DateTimeOffset.UtcNow;

        context.StartTimestamp.Should().BeOnOrAfter(before);
        context.StartTimestamp.Should().BeOnOrBefore(after);
    }

    [Fact]
    public void Status_DefaultsToInProgress()
    {
        var context = new TransactionContext();
        context.Status.Should().Be(TransactionLogStatus.InProgress);
    }
}
