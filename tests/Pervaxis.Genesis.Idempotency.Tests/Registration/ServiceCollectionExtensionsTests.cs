/*
 ************************************************************************
 * Copyright (C) 2026 Clarivex Technologies Private Limited
 * All Rights Reserved.
 ************************************************************************
 */

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pervaxis.Genesis.Idempotency.Extensions;
using Pervaxis.Genesis.Idempotency.Options;
using Pervaxis.Genesis.Idempotency.Services;

namespace Pervaxis.Genesis.Idempotency.Tests.Registration;

public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddGenesisIdempotency_NullServices_ThrowsArgumentNullException()
    {
        IServiceCollection services = null!;
        var config = new ConfigurationBuilder().Build();

        var act = () => services.AddGenesisIdempotency(config);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("services");
    }

    [Fact]
    public void AddGenesisIdempotency_NullConfiguration_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();
        IConfiguration config = null!;

        var act = () => services.AddGenesisIdempotency(config);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("configuration");
    }

    [Fact]
    public void AddGenesisIdempotency_NullAction_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();
        Action<IdempotencyOptions> configAction = null!;

        var act = () => services.AddGenesisIdempotency(configAction);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("configureOptions");
    }

    [Fact]
    public void AddGenesisIdempotency_WithConfiguration_RegistersKeyValidator()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Genesis:Idempotency:TableName"] = "test-table",
                ["Genesis:Idempotency:TtlMinutes"] = "60"
            })
            .Build();

        services.AddGenesisIdempotency(config);

        var provider = services.BuildServiceProvider();
        var validator = provider.GetService<IIdempotencyKeyValidator>();

        validator.Should().NotBeNull();
        validator.Should().BeOfType<IdempotencyKeyValidator>();
    }

    [Fact]
    public void AddGenesisIdempotency_WithConfiguration_RegistersFingerprintComputer()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build();

        services.AddGenesisIdempotency(config);

        var provider = services.BuildServiceProvider();
        var computer = provider.GetService<IRequestFingerprintComputer>();

        computer.Should().NotBeNull();
        computer.Should().BeOfType<RequestFingerprintComputer>();
    }

    [Fact]
    public void AddGenesisIdempotency_WithAction_RegistersServices()
    {
        var services = new ServiceCollection();

        services.AddGenesisIdempotency(opts =>
        {
            opts.TableName = "custom-table";
            opts.TtlMinutes = 120;
        });

        var provider = services.BuildServiceProvider();
        var validator = provider.GetService<IIdempotencyKeyValidator>();

        validator.Should().NotBeNull();
    }

    [Fact]
    public void AddGenesisIdempotency_CalledTwice_DoesNotDuplicateRegistrations()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build();

        services.AddGenesisIdempotency(config);
        services.AddGenesisIdempotency(config);

        var validatorRegistrations = services
            .Where(s => s.ServiceType == typeof(IIdempotencyKeyValidator))
            .ToList();

        validatorRegistrations.Should().HaveCount(1);
    }
}
