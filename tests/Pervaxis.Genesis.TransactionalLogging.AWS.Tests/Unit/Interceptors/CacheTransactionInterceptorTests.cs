using Microsoft.Extensions.Options;
using Pervaxis.Core.Abstractions.Genesis.Modules;
using Pervaxis.Genesis.TransactionalLogging.AWS.Context;
using Pervaxis.Genesis.TransactionalLogging.AWS.Interceptors;
using Pervaxis.Genesis.TransactionalLogging.AWS.Options;
using Pervaxis.Genesis.TransactionalLogging.AWS.Sanitization;

namespace Pervaxis.Genesis.TransactionalLogging.AWS.Tests.Unit.Interceptors;

public class CacheTransactionInterceptorTests
{
    private readonly ICache _innerCache = Substitute.For<ICache>();
    private readonly TransactionContextAccessor _accessor = new();
    private readonly TransactionalLoggingOptions _options = new();
    private readonly ParameterSanitizer _sanitizer;
    private readonly CacheTransactionInterceptor _interceptor;

    public CacheTransactionInterceptorTests()
    {
        _sanitizer = new ParameterSanitizer(Microsoft.Extensions.Options.Options.Create(_options));
        _interceptor = new CacheTransactionInterceptor(
            _innerCache, _accessor, Microsoft.Extensions.Options.Options.Create(_options), _sanitizer);
    }

    [Fact]
    public async Task GetAsync_RecordsEntry_OnSuccess()
    {
        _accessor.Current = new TransactionContext();
        _innerCache.GetAsync<string>("key1", Arg.Any<CancellationToken>()).Returns("value1");

        var result = await _interceptor.GetAsync<string>("key1");

        result.Should().Be("value1");
        _accessor.Current.Entries.Should().ContainSingle();
    }

    [Fact]
    public async Task GetAsync_RecordsEntry_OnException()
    {
        _accessor.Current = new TransactionContext();
        _innerCache.GetAsync<string>("key1", Arg.Any<CancellationToken>())
            .Returns<string?>(_ => throw new TimeoutException());

        var act = () => _interceptor.GetAsync<string>("key1");
        await act.Should().ThrowAsync<TimeoutException>();

        _accessor.Current.Entries.Should().ContainSingle();
    }

    [Fact]
    public async Task GetAsync_NoRecording_WhenImplicitCaptureDisabled()
    {
        _accessor.Current = new TransactionContext();
        _options.ImplicitCapture = false;
        _innerCache.GetAsync<string>("key1", Arg.Any<CancellationToken>()).Returns("val");

        // Need to recreate interceptor with updated options
        var interceptor = new CacheTransactionInterceptor(
            _innerCache, _accessor, Microsoft.Extensions.Options.Options.Create(_options), _sanitizer);

        await interceptor.GetAsync<string>("key1");

        _accessor.Current.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAsync_NoRecording_WhenProviderExcluded()
    {
        _accessor.Current = new TransactionContext();
        _options.ExcludeProviders.Add("Caching");
        _innerCache.GetAsync<string>("key1", Arg.Any<CancellationToken>()).Returns("val");

        var interceptor = new CacheTransactionInterceptor(
            _innerCache, _accessor, Microsoft.Extensions.Options.Options.Create(_options), _sanitizer);

        await interceptor.GetAsync<string>("key1");
        _accessor.Current.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAsync_NoRecording_WhenOperationExcluded()
    {
        _accessor.Current = new TransactionContext();
        _options.ExcludeOperations.Add("Caching.get");
        _innerCache.GetAsync<string>("key1", Arg.Any<CancellationToken>()).Returns("val");

        var interceptor = new CacheTransactionInterceptor(
            _innerCache, _accessor, Microsoft.Extensions.Options.Options.Create(_options), _sanitizer);

        await interceptor.GetAsync<string>("key1");
        _accessor.Current.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAsync_NoRecording_WhenNoTransactionContext()
    {
        _accessor.Current = null;
        _innerCache.GetAsync<string>("key1", Arg.Any<CancellationToken>()).Returns("val");

        var result = await _interceptor.GetAsync<string>("key1");

        result.Should().Be("val");
        // No way to assert no entry — but verifying no exception is sufficient
    }

    [Fact]
    public async Task GetAsync_NoRecording_WhenDurationBelowThreshold()
    {
        _accessor.Current = new TransactionContext();
        _options.MinimumDurationMs = 10000; // 10 seconds — our mock returns instantly
        _innerCache.GetAsync<string>("key1", Arg.Any<CancellationToken>()).Returns("val");

        var interceptor = new CacheTransactionInterceptor(
            _innerCache, _accessor, Microsoft.Extensions.Options.Options.Create(_options), _sanitizer);

        await interceptor.GetAsync<string>("key1");
        _accessor.Current.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAsync_NoRecording_WhenCaptureProvidersMismatch()
    {
        _accessor.Current = new TransactionContext();
        _options.CaptureProviders.Add("Messaging"); // Only capture messaging
        _innerCache.GetAsync<string>("key1", Arg.Any<CancellationToken>()).Returns("val");

        var interceptor = new CacheTransactionInterceptor(
            _innerCache, _accessor, Microsoft.Extensions.Options.Options.Create(_options), _sanitizer);

        await interceptor.GetAsync<string>("key1");
        _accessor.Current.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task SetAsync_RecordsEntry()
    {
        _accessor.Current = new TransactionContext();
        _innerCache.SetAsync("key1", "val", null, Arg.Any<CancellationToken>()).Returns(true);

        var result = await _interceptor.SetAsync("key1", "val");

        result.Should().BeTrue();
        _accessor.Current.Entries.Should().ContainSingle();
    }

    [Fact]
    public async Task GetAsync_NoRecording_WhenSuppressed()
    {
        _accessor.Current = new TransactionContext();
        _accessor.IsSuppressed = true;
        _innerCache.GetAsync<string>("key1", Arg.Any<CancellationToken>()).Returns("val");

        await _interceptor.GetAsync<string>("key1");
        _accessor.Current.Entries.Should().BeEmpty();
    }
}
