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

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Pervaxis.Genesis.Base.Exceptions;
using Pervaxis.Genesis.Caching.AWS.Options;
using Pervaxis.Genesis.Caching.AWS.Providers.ElastiCache;
using StackExchange.Redis;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Pervaxis.Genesis.Caching.AWS.Tests;

public class ElastiCacheProviderTests
{
    private readonly Mock<IConnectionMultiplexer> _mockMultiplexer;
    private readonly Mock<IDatabase> _mockDatabase;
    private readonly Mock<ILogger<ElastiCacheProvider>> _mockLogger;
    private readonly CachingOptions _options;

    public ElastiCacheProviderTests()
    {
        _mockMultiplexer = new Mock<IConnectionMultiplexer>();
        _mockDatabase = new Mock<IDatabase>();
        _mockLogger = new Mock<ILogger<ElastiCacheProvider>>();

        _mockMultiplexer
            .Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object?>()))
            .Returns(_mockDatabase.Object);

        _options = new CachingOptions
        {
            ConnectionString = "localhost:6379",
            Region = "us-east-1",
            UseSsl = false,
            DefaultExpiry = TimeSpan.FromMinutes(30)
        };
    }

    private ElastiCacheProvider CreateProvider(CachingOptions? options = null) =>
        new(MsOptions.Create(options ?? _options), _mockLogger.Object, _mockMultiplexer.Object);

    private static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = false });

    // ── Helpers ──────────────────────────────────────────────────────────

    private sealed record TestModel(int Id, string Name);

    // ── Constructor ──────────────────────────────────────────────────────

    public class Constructor : ElastiCacheProviderTests
    {
        [Fact]
        public void NullOptions_ThrowsArgumentNullException()
        {
            var act = () => new ElastiCacheProvider(null!, _mockLogger.Object, _mockMultiplexer.Object);
            act.Should().Throw<ArgumentNullException>().WithParameterName("options");
        }

        [Fact]
        public void NullLogger_ThrowsArgumentNullException()
        {
            var act = () => new ElastiCacheProvider(MsOptions.Create(_options), null!, _mockMultiplexer.Object);
            act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
        }

        [Fact]
        public void NullConnection_ThrowsArgumentNullException()
        {
            var act = () => new ElastiCacheProvider(MsOptions.Create(_options), _mockLogger.Object, null!);
            act.Should().Throw<ArgumentNullException>().WithParameterName("connection");
        }

        [Fact]
        public void EmptyConnectionString_ThrowsGenesisConfigurationException()
        {
            var invalidOptions = new CachingOptions { ConnectionString = string.Empty, Region = "us-east-1" };
            var act = () => new ElastiCacheProvider(MsOptions.Create(invalidOptions), _mockLogger.Object, _mockMultiplexer.Object);
            act.Should().Throw<GenesisConfigurationException>();
        }

        [Fact]
        public void ValidOptions_CreatesProviderSuccessfully()
        {
            var act = () => CreateProvider();
            act.Should().NotThrow();
        }
    }

    // ── GetAsync ─────────────────────────────────────────────────────────

    public class GetAsync : ElastiCacheProviderTests
    {
        [Fact]
        public async Task CacheHit_ReturnsDeserializedValue()
        {
            var expected = new TestModel(1, "Test");
            _mockDatabase
                .Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(Serialize(expected));

            var result = await CreateProvider().GetAsync<TestModel>("key");

            result.Should().NotBeNull();
            result!.Id.Should().Be(1);
            result.Name.Should().Be("Test");
        }

        [Fact]
        public async Task CacheMiss_ReturnsDefault()
        {
            _mockDatabase
                .Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(RedisValue.Null);

            var result = await CreateProvider().GetAsync<TestModel>("key");

            result.Should().BeNull();
        }

        [Fact]
        public async Task WithKeyPrefix_UsesFullKey()
        {
            var options = new CachingOptions
            {
                ConnectionString = "localhost:6379",
                Region = "us-east-1",
                KeyPrefix = "myapp",
                UseSsl = false
            };
            _mockDatabase
                .Setup(d => d.StringGetAsync(new RedisKey("myapp:mykey"), It.IsAny<CommandFlags>()))
                .ReturnsAsync(Serialize(new TestModel(1, "Test")));

            var result = await CreateProvider(options).GetAsync<TestModel>("mykey");

            result.Should().NotBeNull();
        }

        [Fact]
        public async Task EmptyKey_ThrowsArgumentException()
        {
            var act = async () => await CreateProvider().GetAsync<TestModel>("");
            await act.Should().ThrowAsync<ArgumentException>();
        }

        [Fact]
        public async Task RedisException_ThrowsGenesisException()
        {
            _mockDatabase
                .Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ThrowsAsync(new RedisException("Connection failed"));

            var act = async () => await CreateProvider().GetAsync<TestModel>("key");
            await act.Should().ThrowAsync<GenesisException>()
                .WithMessage("*Cache get operation failed*");
        }
    }

    // ── SetAsync ─────────────────────────────────────────────────────────

    public class SetAsync : ElastiCacheProviderTests
    {
        [Fact]
        public async Task ValidKeyAndValue_ReturnsTrue()
        {
            _mockDatabase
                .Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(),
                    It.IsAny<TimeSpan?>(), It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

            var result = await CreateProvider().SetAsync("key", new TestModel(1, "Test"));

            result.Should().BeTrue();
        }

        [Fact]
        public async Task UsesDefaultExpiry_WhenExpiryNotProvided()
        {
            _mockDatabase
                .Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(),
                    _options.DefaultExpiry, It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

            var result = await CreateProvider().SetAsync("key", new TestModel(1, "Test"));

            result.Should().BeTrue();
            _mockDatabase.Verify(d => d.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(),
                _options.DefaultExpiry, It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()), Times.Once);
        }

        [Fact]
        public async Task UsesCustomExpiry_WhenProvided()
        {
            var customExpiry = TimeSpan.FromMinutes(5);
            _mockDatabase
                .Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(),
                    customExpiry, It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

            var result = await CreateProvider().SetAsync("key", new TestModel(1, "Test"), customExpiry);

            result.Should().BeTrue();
        }

        [Fact]
        public async Task NullValue_ThrowsArgumentNullException()
        {
            var act = async () => await CreateProvider().SetAsync<TestModel>("key", null!);
            await act.Should().ThrowAsync<ArgumentNullException>();
        }

        [Fact]
        public async Task RedisException_ThrowsGenesisException()
        {
            _mockDatabase
                .Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(),
                    It.IsAny<TimeSpan?>(), It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
                .ThrowsAsync(new RedisException("Connection failed"));

            var act = async () => await CreateProvider().SetAsync("key", new TestModel(1, "Test"));
            await act.Should().ThrowAsync<GenesisException>()
                .WithMessage("*Cache set operation failed*");
        }
    }

    // ── RemoveAsync ───────────────────────────────────────────────────────

    public class RemoveAsync : ElastiCacheProviderTests
    {
        [Fact]
        public async Task KeyExists_ReturnsTrue()
        {
            _mockDatabase
                .Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

            var result = await CreateProvider().RemoveAsync("key");

            result.Should().BeTrue();
        }

        [Fact]
        public async Task KeyNotFound_ReturnsFalse()
        {
            _mockDatabase
                .Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(false);

            var result = await CreateProvider().RemoveAsync("key");

            result.Should().BeFalse();
        }

        [Fact]
        public async Task EmptyKey_ThrowsArgumentException()
        {
            var act = async () => await CreateProvider().RemoveAsync("  ");
            await act.Should().ThrowAsync<ArgumentException>();
        }

        [Fact]
        public async Task RedisException_ThrowsGenesisException()
        {
            _mockDatabase
                .Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ThrowsAsync(new RedisException("Connection failed"));

            var act = async () => await CreateProvider().RemoveAsync("key");
            await act.Should().ThrowAsync<GenesisException>()
                .WithMessage("*Cache remove operation failed*");
        }
    }

    // ── ExistsAsync ───────────────────────────────────────────────────────

    public class ExistsAsync : ElastiCacheProviderTests
    {
        [Fact]
        public async Task KeyExists_ReturnsTrue()
        {
            _mockDatabase
                .Setup(d => d.KeyExistsAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

            var result = await CreateProvider().ExistsAsync("key");

            result.Should().BeTrue();
        }

        [Fact]
        public async Task KeyNotFound_ReturnsFalse()
        {
            _mockDatabase
                .Setup(d => d.KeyExistsAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(false);

            var result = await CreateProvider().ExistsAsync("key");

            result.Should().BeFalse();
        }

        [Fact]
        public async Task RedisException_ThrowsGenesisException()
        {
            _mockDatabase
                .Setup(d => d.KeyExistsAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ThrowsAsync(new RedisException("Connection failed"));

            var act = async () => await CreateProvider().ExistsAsync("key");
            await act.Should().ThrowAsync<GenesisException>()
                .WithMessage("*Cache exists operation failed*");
        }
    }

    // ── GetManyAsync ──────────────────────────────────────────────────────

    public class GetManyAsync : ElastiCacheProviderTests
    {
        [Fact]
        public async Task EmptyKeys_ReturnsEmptyDictionary()
        {
            var result = await CreateProvider().GetManyAsync<TestModel>(Array.Empty<string>());

            result.Should().BeEmpty();
        }

        [Fact]
        public async Task MixedHitAndMiss_ReturnsCorrectDictionary()
        {
            var model = new TestModel(1, "Test");
            var values = new RedisValue[] { Serialize(model), RedisValue.Null };

            _mockDatabase
                .Setup(d => d.StringGetAsync(It.IsAny<RedisKey[]>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(values);

            var result = await CreateProvider().GetManyAsync<TestModel>(["key1", "key2"]);

            result.Should().HaveCount(2);
            result["key1"].Should().NotBeNull();
            result["key1"]!.Id.Should().Be(1);
            result["key2"].Should().BeNull();
        }

        [Fact]
        public async Task NullKeys_ThrowsArgumentNullException()
        {
            var act = async () => await CreateProvider().GetManyAsync<TestModel>(null!);
            await act.Should().ThrowAsync<ArgumentNullException>();
        }

        [Fact]
        public async Task RedisException_ThrowsGenesisException()
        {
            _mockDatabase
                .Setup(d => d.StringGetAsync(It.IsAny<RedisKey[]>(), It.IsAny<CommandFlags>()))
                .ThrowsAsync(new RedisException("Connection failed"));

            var act = async () => await CreateProvider().GetManyAsync<TestModel>(["key1"]);
            await act.Should().ThrowAsync<GenesisException>()
                .WithMessage("*Cache get many operation failed*");
        }
    }

    // ── SetManyAsync ──────────────────────────────────────────────────────

    public class SetManyAsync : ElastiCacheProviderTests
    {
        [Fact]
        public async Task EmptyItems_ReturnsTrueWithoutCallingRedis()
        {
            var result = await CreateProvider().SetManyAsync<TestModel>(new Dictionary<string, TestModel>());

            result.Should().BeTrue();
            _mockDatabase.Verify(d => d.CreateBatch(It.IsAny<object>()), Times.Never);
        }

        [Fact]
        public async Task AllItemsSet_ReturnsTrue()
        {
            var mockBatch = new Mock<IBatch>();
            mockBatch
                .Setup(b => b.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(),
                    It.IsAny<TimeSpan?>(), It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

            _mockDatabase
                .Setup(d => d.CreateBatch(It.IsAny<object>()))
                .Returns(mockBatch.Object);

            var items = new Dictionary<string, TestModel>
            {
                ["key1"] = new TestModel(1, "One"),
                ["key2"] = new TestModel(2, "Two")
            };

            var result = await CreateProvider().SetManyAsync(items);

            result.Should().BeTrue();
        }
    }

    // ── RefreshAsync ──────────────────────────────────────────────────────

    public class RefreshAsync : ElastiCacheProviderTests
    {
        [Fact]
        public async Task KeyExists_ReturnsTrue()
        {
            _mockDatabase
                .Setup(d => d.KeyExpireAsync(It.IsAny<RedisKey>(), It.IsAny<TimeSpan?>(), It.IsAny<ExpireWhen>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

            var result = await CreateProvider().RefreshAsync("key");

            result.Should().BeTrue();
        }

        [Fact]
        public async Task KeyNotFound_ReturnsFalse()
        {
            _mockDatabase
                .Setup(d => d.KeyExpireAsync(It.IsAny<RedisKey>(), It.IsAny<TimeSpan?>(), It.IsAny<ExpireWhen>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(false);

            var result = await CreateProvider().RefreshAsync("key");

            result.Should().BeFalse();
        }

        [Fact]
        public async Task UsesCustomExpiry_WhenProvided()
        {
            var customExpiry = TimeSpan.FromMinutes(10);
            _mockDatabase
                .Setup(d => d.KeyExpireAsync(It.IsAny<RedisKey>(), customExpiry, It.IsAny<ExpireWhen>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

            var result = await CreateProvider().RefreshAsync("key", customExpiry);

            result.Should().BeTrue();
            _mockDatabase.Verify(d => d.KeyExpireAsync(
                It.IsAny<RedisKey>(), customExpiry, It.IsAny<ExpireWhen>(), It.IsAny<CommandFlags>()), Times.Once);
        }

        [Fact]
        public async Task RedisException_ThrowsGenesisException()
        {
            _mockDatabase
                .Setup(d => d.KeyExpireAsync(It.IsAny<RedisKey>(), It.IsAny<TimeSpan?>(), It.IsAny<ExpireWhen>(), It.IsAny<CommandFlags>()))
                .ThrowsAsync(new RedisException("Connection failed"));

            var act = async () => await CreateProvider().RefreshAsync("key");
            await act.Should().ThrowAsync<GenesisException>()
                .WithMessage("*Cache refresh operation failed*");
        }
    }

    // ── Dispose ───────────────────────────────────────────────────────────

    public class Dispose : ElastiCacheProviderTests
    {
        [Fact]
        public void WhenConnectionNotUsed_DoesNotDisposeMultiplexer()
        {
            var provider = CreateProvider();
            provider.Dispose();

            _mockMultiplexer.Verify(m => m.Dispose(), Times.Never);
        }

        [Fact]
        public async Task WhenConnectionWasUsed_DisposesMultiplexer()
        {
            _mockDatabase
                .Setup(d => d.KeyExistsAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

            var provider = CreateProvider();
            await provider.ExistsAsync("key");
            provider.Dispose();

            _mockMultiplexer.Verify(m => m.Dispose(), Times.Once);
        }
    }
}
