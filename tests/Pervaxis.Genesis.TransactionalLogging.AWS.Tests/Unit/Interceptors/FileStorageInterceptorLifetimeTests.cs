using Microsoft.Extensions.DependencyInjection;
using Pervaxis.Core.Abstractions.Genesis.Modules;
using Pervaxis.Genesis.TransactionalLogging.AWS.Extensions;

namespace Pervaxis.Genesis.TransactionalLogging.AWS.Tests.Unit.Interceptors;

/// <summary>
/// The file storage provider is registered Scoped so each request gets its own tenant.
/// Decorating it must keep that lifetime; a Singleton decorator would capture one request's tenant.
/// </summary>
public class FileStorageInterceptorLifetimeTests
{
    private static ServiceCollection ServicesWithScopedFileStorage()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => Substitute.For<IFileStorage>());
        services.AddGenesisTransactionalLogging(_ => { });
        return services;
    }

    [Fact]
    public void Decorator_KeepsScopedLifetimeOfFileStorage()
    {
        var services = ServicesWithScopedFileStorage();

        // Scrutor keeps the original provider as a keyed "Decorated" registration and adds the
        // decorator as the unkeyed IFileStorage. Both must stay Scoped.
        var registrations = services.Where(d => d.ServiceType == typeof(IFileStorage)).ToList();

        registrations.Should().HaveCount(2);
        registrations.Should().ContainSingle(d => !d.IsKeyedService);
        registrations.Should().OnlyContain(d => d.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public void Decorator_ResolvesPerScopeAndWrapsTheProvider()
    {
        using var root = ServicesWithScopedFileStorage()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        using var scope1 = root.CreateScope();
        using var scope2 = root.CreateScope();

        var storage1 = scope1.ServiceProvider.GetRequiredService<IFileStorage>();
        var storage2 = scope2.ServiceProvider.GetRequiredService<IFileStorage>();

        storage1.GetType().Name.Should().Be("FileStorageTransactionInterceptor");
        storage1.Should().NotBeSameAs(storage2);
        scope1.ServiceProvider.GetRequiredService<IFileStorage>().Should().BeSameAs(storage1);
    }

    [Fact]
    public void Decorator_ResolvedFromRoot_FailsScopeValidation()
    {
        using var root = ServicesWithScopedFileStorage()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        var act = () => root.GetRequiredService<IFileStorage>();

        act.Should().Throw<InvalidOperationException>();
    }
}
