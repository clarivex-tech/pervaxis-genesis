using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pervaxis.Core.Abstractions.Genesis.Modules;
using Pervaxis.Core.Abstractions.MultiTenancy;
using Pervaxis.Genesis.Base.Exceptions;
using Pervaxis.Genesis.Caching.AWS.Extensions;
using Pervaxis.Genesis.Caching.AWS.Options;
using Pervaxis.Genesis.Caching.AWS.Providers.ElastiCache;
using Pervaxis.Genesis.Caching.AWS.Security;
using StackExchange.Redis;

namespace Pervaxis.Genesis.Caching.AWS.Tests;

public sealed class ProviderIsolationTests
{
    private static CachingOptions Options() => new()
    {
        ConnectionString = "localhost:6379", Region = "us-east-1",
        DefaultExpiry = TimeSpan.FromMinutes(5), MaxTtl = TimeSpan.FromHours(1),
        Resilience = new() { Enabled = false }
    };

    private static Mock<ITenantContext> Context(Func<string> tenant)
    {
        var context = new Mock<ITenantContext>();
        context.SetupGet(x => x.IsResolved).Returns(true);
        context.SetupGet(x => x.TenantId).Returns(() => new TenantId(tenant()));
        return context;
    }

    private sealed class Redis
    {
        public Mock<IConnectionMultiplexer> Connection { get; } = new();
        public Mock<IDatabase> Database { get; } = new();
        public Mock<IBatch> Batch { get; } = new();
        public Redis()
        {
            Connection.Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object?>())).Returns(Database.Object);
            Database.Setup(x => x.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>())).ReturnsAsync(RedisValue.Null);
            Database.Setup(x => x.StringGetAsync(It.IsAny<RedisKey[]>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync([RedisValue.Null]);
            Database.Setup(x => x.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(), It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>())).ReturnsAsync(true);
            Database.Setup(x => x.CreateBatch(It.IsAny<object?>())).Returns(Batch.Object);
            Batch.Setup(x => x.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(), It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>())).ReturnsAsync(true);
        }

        public ElastiCacheProvider Provider(ITenantContext? context, CachingOptions? options = null) => new(
            Microsoft.Extensions.Options.Options.Create(options ?? Options()),
            NullLogger<ElastiCacheProvider>.Instance, Connection.Object, context);
    }

    private static Task Invoke(ICache cache, string operation, string key = "logical:key", TimeSpan? ttl = null) => operation switch
    {
        "get" => cache.GetAsync<string>(key),
        "set" => cache.SetAsync(key, "value", ttl),
        "remove" => cache.RemoveAsync(key),
        "exists" => cache.ExistsAsync(key),
        "get_many" => cache.GetManyAsync<string>([key]),
        "set_many" => cache.SetManyAsync(new Dictionary<string, string> { [key] = "value" }, ttl),
        "refresh" => cache.RefreshAsync(key, ttl),
        "empty_get_many" => cache.GetManyAsync<string>([]),
        "empty_set_many" => cache.SetManyAsync(new Dictionary<string, string>(), ttl),
        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };

    public static TheoryData<string> Operations => new()
    {
        "get", "set", "remove", "exists", "get_many", "set_many", "refresh"
    };

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task EveryOperation_UsesResolver(string operation)
    {
        var redis = new Redis();
        await Invoke(redis.Provider(Context(() => "acme").Object), operation);
        var calls = redis.Database.Invocations.Concat(redis.Batch.Invocations)
            .Where(x => x.Arguments.Count > 0 && (x.Arguments[0] is RedisKey || x.Arguments[0] is RedisKey[])).ToArray();
        Assert.Single(calls);
        var key = calls[0].Arguments[0] is RedisKey[] keys ? Assert.Single(keys) : (RedisKey)calls[0].Arguments[0];
        Assert.Equal("tenants:acme:logical:key", key.ToString());
    }

    [Theory]
    [MemberData(nameof(Operations))]
    [InlineData("empty_get_many")]
    [InlineData("empty_set_many")]
    public async Task EveryOperation_RejectsChangedAmbientTenantBeforeRedis(string operation)
    {
        var ambient = new AsyncLocal<string> { Value = "acme" };
        var redis = new Redis();
        var provider = redis.Provider(Context(() => ambient.Value!).Object);
        await provider.ExistsAsync("initial");
        ambient.Value = "other";
        redis.Connection.Invocations.Clear();
        redis.Database.Invocations.Clear();
        var exception = await Assert.ThrowsAsync<GenesisException>(() => Invoke(provider, operation));
        Assert.Equal("TenantContextChanged", exception.ErrorCode);
        Assert.Empty(redis.Connection.Invocations);
        Assert.Empty(redis.Database.Invocations);
        Assert.Empty(redis.Batch.Invocations);
    }

    [Theory]
    [MemberData(nameof(Operations))]
    [InlineData("empty_get_many")]
    [InlineData("empty_set_many")]
    public async Task EveryOperation_RejectsMissingTenant(string operation)
    {
        var redis = new Redis();
        await Assert.ThrowsAsync<GenesisException>(() => Invoke(redis.Provider(null), operation));
        Assert.Empty(redis.Connection.Invocations);
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task EveryOperation_RejectsForeignNamespace(string operation)
    {
        var redis = new Redis();
        await Assert.ThrowsAsync<GenesisException>(() => Invoke(redis.Provider(Context(() => "acme").Object),
            operation, "tenants:other:secret"));
        Assert.Empty(redis.Database.Invocations);
        Assert.Empty(redis.Batch.Invocations);
    }

    [Fact]
    public async Task BulkWrite_ValidatesAllKeysBeforeSendingAnyCommand()
    {
        var redis = new Redis();
        var provider = redis.Provider(Context(() => "acme").Object);
        await Assert.ThrowsAsync<GenesisException>(() => provider.SetManyAsync(new Dictionary<string, string>
        {
            ["valid"] = "one", ["tenants:other:secret"] = "two"
        }));
        Assert.Empty(redis.Connection.Invocations);
        Assert.Empty(redis.Batch.Invocations);
    }

    [Theory]
    [InlineData("set")]
    [InlineData("set_many")]
    [InlineData("refresh")]
    public async Task Writes_EnforceTtlBounds(string operation)
    {
        var redis = new Redis();
        var provider = redis.Provider(Context(() => "acme").Object);
        foreach (var ttl in new[] { TimeSpan.Zero, TimeSpan.FromSeconds(-1), TimeSpan.FromHours(1) + TimeSpan.FromTicks(1) })
        {
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Invoke(provider, operation, ttl: ttl));
        }
        Assert.Empty(redis.Database.Invocations);
        await Invoke(provider, operation, ttl: TimeSpan.FromHours(1));
        var calls = redis.Database.Invocations.Concat(redis.Batch.Invocations).Where(x =>
            x.Method.Name is "StringSetAsync" or "KeyExpireAsync").ToArray();
        Assert.Single(calls);
        Assert.Contains(TimeSpan.FromHours(1), calls[0].Arguments);
    }

    [Theory]
    [InlineData(0, 3600)]
    [InlineData(-1, 3600)]
    [InlineData(60, 0)]
    [InlineData(3601, 3600)]
    public void Options_RejectInvalidTtlConfiguration(int defaultSeconds, int maxSeconds)
    {
        var options = Options();
        options.DefaultExpiry = TimeSpan.FromSeconds(defaultSeconds);
        options.MaxTtl = TimeSpan.FromSeconds(maxSeconds);
        Assert.Throws<GenesisConfigurationException>(() => new Redis().Provider(Context(() => "acme").Object, options));
    }

    [Theory]
    [InlineData("set")]
    [InlineData("set_many")]
    [InlineData("refresh")]
    public async Task Writes_ApplyPositiveDefaultTtl(string operation)
    {
        var redis = new Redis();
        await Invoke(redis.Provider(Context(() => "acme").Object), operation);
        var call = Assert.Single(redis.Database.Invocations.Concat(redis.Batch.Invocations), x =>
            x.Method.Name is "StringSetAsync" or "KeyExpireAsync");
        Assert.Contains(Options().DefaultExpiry, call.Arguments);
    }

    [Fact]
    public async Task LegacyFlag_CannotDisableTenantIsolation()
    {
        var redis = new Redis();
        var options = Options();
        options.EnableTenantIsolation = false;
        var provider = redis.Provider(Context(() => "acme").Object, options);
        await provider.ExistsAsync("key");
        redis.Database.Verify(x => x.KeyExistsAsync((RedisKey)"tenants:acme:key", It.IsAny<CommandFlags>()), Times.Once);
        await Assert.ThrowsAsync<GenesisException>(() => redis.Provider(null, options).ExistsAsync("key"));
    }

    [Fact]
    public async Task ConcurrentScopes_IsolateSameLogicalKeyAndShareConnection()
    {
        var ambient = new AsyncLocal<string>();
        var redis = new Redis();
        var stored = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        redis.Database.Setup(x => x.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(), It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .Callback<RedisKey, RedisValue, TimeSpan?, bool, When, CommandFlags>((key, value, _, _, _, _) => stored[key.ToString()] = value.ToString())
            .ReturnsAsync(true);
        redis.Database.Setup(x => x.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisKey key, CommandFlags _) => (RedisValue)stored[key.ToString()]);
        var services = new ServiceCollection();
        services.AddSingleton<ILogger<ElastiCacheProvider>>(NullLogger<ElastiCacheProvider>.Instance);
        services.AddScoped<ITenantContext>(_ => Context(() => ambient.Value!).Object);
        services.AddGenesisCaching(options =>
        {
            options.ConnectionString = "localhost:6379";
            options.Region = "us-east-1";
        });
        Assert.Equal(ServiceLifetime.Scoped, services.Single(x => x.ServiceType == typeof(ICache)).Lifetime);
        Assert.Equal(ServiceLifetime.Scoped, services.Single(x => x.ServiceType == typeof(TenantCacheKeyResolver)).Lifetime);
        Assert.Equal(ServiceLifetime.Singleton, services.Single(x => x.ServiceType == typeof(IConnectionMultiplexer)).Lifetime);
        // Assert the package's real lifetime before replacing its network factory.
        // Factory registration keeps disposal owned by the container.
        services.Replace(ServiceDescriptor.Singleton<IConnectionMultiplexer>(_ => redis.Connection.Object));
        var instances = new ConcurrentBag<ICache>();
        var resolvers = new ConcurrentBag<TenantCacheKeyResolver>();
        using (var container = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }))
        {
            var bothWritten = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var written = 0;
            async Task Write(string tenant)
            {
                ambient.Value = tenant;
                using var scope = container.CreateScope();
                var cache = scope.ServiceProvider.GetRequiredService<ICache>();
                var resolver = scope.ServiceProvider.GetRequiredService<TenantCacheKeyResolver>();
                Assert.Same(resolver, scope.ServiceProvider.GetRequiredService<TenantCacheKeyResolver>());
                resolvers.Add(resolver);
                Assert.Same(cache, scope.ServiceProvider.GetRequiredService<ICache>());
                Assert.Same(redis.Connection.Object, scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>());
                instances.Add(cache);
                Assert.True(await cache.SetAsync("shared", tenant));
                if (Interlocked.Increment(ref written) == 2)
                {
                    bothWritten.SetResult();
                }
                await bothWritten.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(tenant, await cache.GetAsync<string>("shared"));
            }
            await Task.WhenAll(Task.Run(() => Write("acme")), Task.Run(() => Write("other")));
            Assert.Equal(2, instances.Distinct().Count());
            Assert.Equal(2, resolvers.Distinct().Count());
            Assert.Equal(2, stored.Count);
            redis.Connection.Verify(x => x.Dispose(), Times.Never);
        }
        redis.Connection.Verify(x => x.Dispose(), Times.Once);
    }
}
