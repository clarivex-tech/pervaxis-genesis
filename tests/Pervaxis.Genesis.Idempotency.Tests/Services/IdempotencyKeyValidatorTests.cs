/*
 ************************************************************************
 * Copyright (C) 2026 Clarivex Technologies Private Limited
 * All Rights Reserved.
 ************************************************************************
 */

using Pervaxis.Genesis.Idempotency.Services;

namespace Pervaxis.Genesis.Idempotency.Tests.Services;

public sealed class IdempotencyKeyValidatorTests
{
    private readonly IdempotencyKeyValidator _validator = new();

    [Theory]
    [InlineData("abc-123")]
    [InlineData("a")]
    [InlineData("ABC_DEF.ghi-123")]
    [InlineData("order-12345678-abcd-efgh-ijkl-123456789012")]
    public void Validate_ValidKeys_ReturnsValid(string key)
    {
        var result = _validator.Validate(key, hasMultipleValues: false);

        result.IsValid.Should().BeTrue();
        result.ErrorCode.Should().BeNull();
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void Validate_NullKey_ReturnsInvalid()
    {
        var result = _validator.Validate(null, hasMultipleValues: false);

        result.IsValid.Should().BeFalse();
        result.ErrorCode.Should().Be("IDEMPOTENCY_KEY_INVALID");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Validate_EmptyOrWhitespace_ReturnsInvalid(string key)
    {
        var result = _validator.Validate(key, hasMultipleValues: false);

        result.IsValid.Should().BeFalse();
        result.ErrorCode.Should().Be("IDEMPOTENCY_KEY_INVALID");
    }

    [Fact]
    public void Validate_MultipleValues_ReturnsInvalid()
    {
        var result = _validator.Validate("valid-key", hasMultipleValues: true);

        result.IsValid.Should().BeFalse();
        result.ErrorCode.Should().Be("IDEMPOTENCY_KEY_INVALID");
        result.ErrorMessage.Should().Contain("exactly one value");
    }

    [Fact]
    public void Validate_ExceedsMaxLength_ReturnsInvalid()
    {
        var longKey = new string('a', 257);

        var result = _validator.Validate(longKey, hasMultipleValues: false);

        result.IsValid.Should().BeFalse();
        result.ErrorCode.Should().Be("IDEMPOTENCY_KEY_INVALID");
        result.ErrorMessage.Should().Contain("256");
    }

    [Fact]
    public void Validate_ExactlyMaxLength_ReturnsValid()
    {
        var key = new string('a', 256);

        var result = _validator.Validate(key, hasMultipleValues: false);

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("key with spaces")]
    [InlineData("key#hash")]
    [InlineData("key@at")]
    [InlineData("key/slash")]
    [InlineData("key\\backslash")]
    [InlineData("key!bang")]
    [InlineData("key$dollar")]
    public void Validate_InvalidCharacters_ReturnsInvalid(string key)
    {
        var result = _validator.Validate(key, hasMultipleValues: false);

        result.IsValid.Should().BeFalse();
        result.ErrorCode.Should().Be("IDEMPOTENCY_KEY_INVALID");
        result.ErrorMessage.Should().Contain("alphanumeric");
    }

    [Theory]
    [InlineData("key-with-hyphens")]
    [InlineData("key_with_underscores")]
    [InlineData("key.with.dots")]
    [InlineData("MiXeD-CaSe_Key.123")]
    public void Validate_AllowedSpecialCharacters_ReturnsValid(string key)
    {
        var result = _validator.Validate(key, hasMultipleValues: false);

        result.IsValid.Should().BeTrue();
    }
}
