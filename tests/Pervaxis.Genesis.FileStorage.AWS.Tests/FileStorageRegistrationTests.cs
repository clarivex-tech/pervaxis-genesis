/*
 ************************************************************************
 * Copyright (C) 2026 Clarivex Technologies Private Limited
 * All Rights Reserved.
 ************************************************************************
 */

using Amazon.S3;
using Amazon.S3.Model;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Pervaxis.Core.Abstractions.Genesis.Modules;
using Pervaxis.Core.Abstractions.MultiTenancy;
using Pervaxis.Genesis.FileStorage.AWS.Extensions;
using Pervaxis.Genesis.FileStorage.AWS.Providers.S3;
using Pervaxis.Genesis.FileStorage.AWS.Security;

namespace Pervaxis.Genesis.FileStorage.AWS.Tests;

/// <summary>
/// Verifies the DI lifetimes: provider and resolver are Scoped (per-request tenant),
/// the S3 client is a Singleton shared by every scope and never disposed by a scope.
/// </summary>
public sealed class FileStorageRegistrationTests
{
    private static ServiceProvider BuildProvider(Mock<IAmazonS3>? s3 = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // ITenantContext is per request in the host; model that with a Scoped registration
        // whose tenant id is unique per scope.
        services.AddScoped<ITenantContext>(_ =>
        {
            var tenant = new Mock<ITenantContext>();
            tenant.Setup(x => x.IsResolved).Returns(true);
            tenant.Setup(x => x.TenantId).Returns(new TenantId($"tenant-{Guid.NewGuid():N}"[..20]));
            tenant.Setup(x => x.TenantName).Returns("scope-tenant");
            return tenant.Object;
        });

        services.AddGenesisFileStorage(o =>
        {
            o.Region = "us-east-1";
            o.BucketName = "test-bucket";
        });

        if (s3 != null)
        {
            // Swap the real keyed client for a mock, keeping the Singleton lifetime.
            var descriptor = services.Single(d =>
                d.ServiceType == typeof(IAmazonS3) && d.IsKeyedService && Equals(d.ServiceKey, FileStorageConstants.S3ClientServiceKey));
            services.Remove(descriptor);
            services.AddKeyedSingleton(FileStorageConstants.S3ClientServiceKey, s3.Object);
        }

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    [Fact]
    public void Registrations_HaveExpectedLifetimes()
    {
        var services = new ServiceCollection();
        services.AddGenesisFileStorage(o =>
        {
            o.Region = "us-east-1";
            o.BucketName = "test-bucket";
        });

        services.Single(d => d.ServiceType == typeof(IFileStorage)).Lifetime.Should().Be(ServiceLifetime.Scoped);
        services.Single(d => d.ServiceType == typeof(TenantKeyResolver)).Lifetime.Should().Be(ServiceLifetime.Scoped);
        services.Single(d => d.ServiceType == typeof(IAmazonS3) && d.IsKeyedService
                             && Equals(d.ServiceKey, FileStorageConstants.S3ClientServiceKey))
            .Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.Should().NotContain(d => d.ServiceType == typeof(IAmazonS3) && !d.IsKeyedService,
            "the module must not register an unkeyed IAmazonS3 that could collide with another module");
    }

    [Fact]
    public void ResolveInsideScope_GivesScopedProviderAndSharedSingletonClient()
    {
        using var root = BuildProvider();

        using var scope1 = root.CreateScope();
        using var scope2 = root.CreateScope();

        var storage1 = scope1.ServiceProvider.GetRequiredService<IFileStorage>();
        var storage2 = scope2.ServiceProvider.GetRequiredService<IFileStorage>();

        storage1.Should().BeOfType<S3FileStorageProvider>();
        storage1.Should().NotBeSameAs(storage2, "each request scope gets its own provider bound to its own tenant");
        scope1.ServiceProvider.GetRequiredService<IFileStorage>().Should().BeSameAs(storage1);

        var client1 = scope1.ServiceProvider.GetRequiredKeyedService<IAmazonS3>(FileStorageConstants.S3ClientServiceKey);
        var client2 = scope2.ServiceProvider.GetRequiredKeyedService<IAmazonS3>(FileStorageConstants.S3ClientServiceKey);
        client1.Should().BeSameAs(client2, "the S3 client is a Singleton shared by all scopes");
    }

    [Fact]
    public void ResolveFromRootProvider_FailsScopeValidation()
    {
        using var root = BuildProvider();

        var act = () => root.GetRequiredService<IFileStorage>();

        act.Should().Throw<InvalidOperationException>("a Singleton or background service must create a scope to use IFileStorage");
    }

    [Fact]
    public void SingletonConsumerOfFileStorage_FailsValidateOnBuild()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => Mock.Of<ITenantContext>());
        services.AddGenesisFileStorage(o =>
        {
            o.Region = "us-east-1";
            o.BucketName = "test-bucket";
        });
        services.AddSingleton<SingletonConsumer>();

        var act = () => services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        act.Should().Throw<AggregateException>().WithInnerException<InvalidOperationException>();
    }

    [Fact]
    public async Task ScopedProviders_UseInjectedSingletonClient_AndDisposingScopeDoesNotDisposeIt()
    {
        var s3 = new Mock<IAmazonS3>();
        var requestedKeys = new List<string>();
        s3.Setup(x => x.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), It.IsAny<CancellationToken>()))
            .Callback<DeleteObjectRequest, CancellationToken>((r, _) => requestedKeys.Add(r.Key))
            .ReturnsAsync(new DeleteObjectResponse());

        using var root = BuildProvider(s3);

        using (var scope = root.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IFileStorage>().DeleteAsync("a.txt");
        }

        using (var scope = root.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IFileStorage>().DeleteAsync("b.txt");
        }

        requestedKeys.Should().HaveCount(2);
        requestedKeys.Should().OnlyContain(k => k.StartsWith("tenants/tenant-", StringComparison.Ordinal));
        requestedKeys[0].Split('/')[1].Should().NotBe(requestedKeys[1].Split('/')[1], "each scope resolved its own tenant");
        s3.Verify(x => x.Dispose(), Times.Never, "a scope must never dispose the shared Singleton client");
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1812:Avoid uninstantiated internal classes",
        Justification = "Instantiated by the DI container to prove a Singleton consumer of IFileStorage fails validation.")]
    private sealed class SingletonConsumer
    {
        public SingletonConsumer(IFileStorage storage) => Storage = storage;

        public IFileStorage Storage { get; }
    }
}
