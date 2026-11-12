using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pervaxis.Core.Abstractions.Genesis.Modules;
using Pervaxis.Genesis.TransactionalLogging.AWS.Attributes;
using Pervaxis.Genesis.TransactionalLogging.AWS.Context;
using Pervaxis.Genesis.TransactionalLogging.AWS.Middleware;
using Pervaxis.Genesis.TransactionalLogging.AWS.Options;
using Pervaxis.Genesis.TransactionalLogging.AWS.Storage;

namespace Pervaxis.Genesis.TransactionalLogging.AWS.Tests.Unit.Middleware;

public class TransactionLoggingMiddlewareTests
{
    private readonly TransactionalLoggingOptions _options = new();
    private readonly InMemoryTransactionLogStore _store = new();
    private readonly ILogger<TransactionLoggingMiddleware> _logger = Substitute.For<ILogger<TransactionLoggingMiddleware>>();

    private TransactionLoggingMiddleware CreateMiddleware(RequestDelegate next)
    {
        return new TransactionLoggingMiddleware(next, Microsoft.Extensions.Options.Options.Create(_options), _logger);
    }

    private HttpContext CreateHttpContext(string path = "/api/test", string method = "GET")
    {
        var services = new ServiceCollection();
        services.AddScoped<TransactionContextAccessor>();
        services.AddSingleton<ITransactionLogStore>(_store);
        var provider = services.BuildServiceProvider();

        var context = new DefaultHttpContext();
        context.RequestServices = provider.CreateScope().ServiceProvider;
        context.Request.Path = path;
        context.Request.Method = method;
        return context;
    }

    [Fact]
    public async Task InvokeAsync_CreatesTransactionContext()
    {
        TransactionContextAccessor? capturedAccessor = null;
        var middleware = CreateMiddleware(async ctx =>
        {
            capturedAccessor = ctx.RequestServices.GetRequiredService<TransactionContextAccessor>();
            capturedAccessor.Current.Should().NotBeNull();
            ctx.Response.StatusCode = 200;
        });

        var httpContext = CreateHttpContext();
        await middleware.InvokeAsync(httpContext);

        capturedAccessor!.Current.Should().BeNull("context should be cleared after request");
    }

    [Fact]
    public async Task InvokeAsync_WhenDisabled_SkipsContextCreation()
    {
        _options.Enabled = false;
        TransactionContextAccessor? capturedAccessor = null;
        var middleware = CreateMiddleware(async ctx =>
        {
            capturedAccessor = ctx.RequestServices.GetRequiredService<TransactionContextAccessor>();
            capturedAccessor.Current.Should().BeNull();
        });

        var httpContext = CreateHttpContext();
        await middleware.InvokeAsync(httpContext);
    }

    [Fact]
    public async Task InvokeAsync_WhenSuppressedRoute_SkipsContextCreation()
    {
        _options.SuppressRoutes.Add("/health");
        TransactionContextAccessor? capturedAccessor = null;
        var middleware = CreateMiddleware(async ctx =>
        {
            capturedAccessor = ctx.RequestServices.GetRequiredService<TransactionContextAccessor>();
            capturedAccessor.Current.Should().BeNull();
        });

        var httpContext = CreateHttpContext(path: "/health");
        await middleware.InvokeAsync(httpContext);
    }

    [Fact]
    public async Task InvokeAsync_CapturesIdempotencyKey()
    {
        string? capturedKey = null;
        var middleware = CreateMiddleware(async ctx =>
        {
            var accessor = ctx.RequestServices.GetRequiredService<TransactionContextAccessor>();
            capturedKey = accessor.Current?.IdempotencyKey;
        });

        var httpContext = CreateHttpContext();
        httpContext.Request.Headers["Idempotency-Key"] = "idem-123";
        await middleware.InvokeAsync(httpContext);

        capturedKey.Should().Be("idem-123");
    }

    [Fact]
    public async Task InvokeAsync_CapturesCorrelationId()
    {
        string? capturedCorr = null;
        var middleware = CreateMiddleware(async ctx =>
        {
            var accessor = ctx.RequestServices.GetRequiredService<TransactionContextAccessor>();
            capturedCorr = accessor.Current?.CorrelationId;
        });

        var httpContext = CreateHttpContext();
        httpContext.Request.Headers["X-Correlation-Id"] = "corr-456";
        await middleware.InvokeAsync(httpContext);

        capturedCorr.Should().Be("corr-456");
    }

    [Fact]
    public async Task InvokeAsync_OnException_SetsFailedStatus()
    {
        var middleware = CreateMiddleware(_ => throw new InvalidOperationException("boom"));
        var httpContext = CreateHttpContext();

        var act = () => middleware.InvokeAsync(httpContext);
        await act.Should().ThrowAsync<InvalidOperationException>();

        // Give fire-and-forget a moment to complete
        await Task.Delay(50);
        _store.Store.Should().ContainSingle();
        var stored = _store.Store.Values.First();
        stored.Status.Should().Be(TransactionLogStatus.Failed);
        stored.ErrorType.Should().Be("InvalidOperationException");
    }

    [Fact]
    public async Task InvokeAsync_PersistsToStore()
    {
        var middleware = CreateMiddleware(ctx => { ctx.Response.StatusCode = 201; return Task.CompletedTask; });
        var httpContext = CreateHttpContext(method: "POST");

        await middleware.InvokeAsync(httpContext);

        // Give fire-and-forget a moment
        await Task.Delay(50);
        _store.Store.Should().ContainSingle();
        var stored = _store.Store.Values.First();
        stored.Status.Should().Be(TransactionLogStatus.Completed);
        stored.HttpStatusCode.Should().Be(201);
        stored.HttpMethod.Should().Be("POST");
    }
}
