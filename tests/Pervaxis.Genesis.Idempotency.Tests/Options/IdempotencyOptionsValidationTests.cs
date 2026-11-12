/*
 ************************************************************************
 * Copyright (C) 2026 Clarivex Technologies Private Limited
 * All Rights Reserved.
 ************************************************************************
 */

using Pervaxis.Genesis.Idempotency.Options;

namespace Pervaxis.Genesis.Idempotency.Tests.Options;

public sealed class IdempotencyOptionsValidationTests
{
    private static IdempotencyOptions CreateValidOptions() => new()
    {
        TableName = "test-table",
        TtlMinutes = 1440,
        HeaderName = "Idempotency-Key",
        UseLocalEmulator = false,
        Region = "us-east-1"
    };

    [Fact]
    public void Validate_ValidDefaults_ReturnsTrue()
    {
        var options = new IdempotencyOptions
        {
            UseLocalEmulator = true,
            LocalEmulatorUrl = new Uri("http://localhost:4566"),
            Region = "us-east-1"
        };

        options.Validate().Should().BeTrue();
    }

    [Fact]
    public void Validate_ValidCustomOptions_ReturnsTrue()
    {
        var options = CreateValidOptions();

        options.Validate().Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyTableName_NonEmulator_ReturnsFalse(string? tableName)
    {
        var options = CreateValidOptions();
        options.TableName = tableName!;
        options.UseLocalEmulator = false;

        options.Validate().Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Validate_EmptyTableName_Emulator_ReturnsTrue(string? tableName)
    {
        var options = CreateValidOptions();
        options.TableName = tableName!;
        options.UseLocalEmulator = true;
        options.LocalEmulatorUrl = new Uri("http://localhost:4566");

        options.Validate().Should().BeTrue();
    }

    [Fact]
    public void Validate_TableNameExceeds255_ReturnsFalse()
    {
        var options = CreateValidOptions();
        options.TableName = new string('a', 256);

        options.Validate().Should().BeFalse();
    }

    [Fact]
    public void Validate_TableNameExactly255_ReturnsTrue()
    {
        var options = CreateValidOptions();
        options.TableName = new string('a', 255);

        options.Validate().Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10081)]
    public void Validate_TtlOutOfRange_ReturnsFalse(int ttl)
    {
        var options = CreateValidOptions();
        options.TtlMinutes = ttl;

        options.Validate().Should().BeFalse();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1440)]
    [InlineData(10080)]
    public void Validate_TtlInRange_ReturnsTrue(int ttl)
    {
        var options = CreateValidOptions();
        options.TtlMinutes = ttl;

        options.Validate().Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyHeaderName_ReturnsFalse(string? headerName)
    {
        var options = CreateValidOptions();
        options.HeaderName = headerName!;

        options.Validate().Should().BeFalse();
    }

    [Fact]
    public void Validate_HeaderNameExceeds128_ReturnsFalse()
    {
        var options = CreateValidOptions();
        options.HeaderName = new string('X', 129);

        options.Validate().Should().BeFalse();
    }

    [Fact]
    public void Validate_HeaderNameExactly128_ReturnsTrue()
    {
        var options = CreateValidOptions();
        options.HeaderName = new string('X', 128);

        options.Validate().Should().BeTrue();
    }

    [Fact]
    public void Validate_InvalidResilience_ReturnsFalse()
    {
        var options = CreateValidOptions();
        options.Resilience.RetryCount = -1; // Invalid

        options.Validate().Should().BeFalse();
    }

    [Fact]
    public void Validate_ValidResilience_ReturnsTrue()
    {
        var options = CreateValidOptions();
        options.Resilience.RetryCount = 5;

        options.Validate().Should().BeTrue();
    }
}
