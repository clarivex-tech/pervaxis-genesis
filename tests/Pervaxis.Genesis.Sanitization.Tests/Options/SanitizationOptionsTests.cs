/*
 ************************************************************************
 * Copyright (C) 2026 Clarivex Technologies Private Limited
 * All Rights Reserved.
 ************************************************************************
 */

using Pervaxis.Genesis.Sanitization.Options;

namespace Pervaxis.Genesis.Sanitization.Tests.Options;

public class SanitizationOptionsTests
{
    [Fact]
    public void Validate_DefaultOptions_ReturnsTrue()
    {
        var options = new SanitizationOptions();
        options.Validate().Should().BeTrue();
    }

    [Fact]
    public void Validate_NullDefaultProfile_ReturnsFalse()
    {
        var options = new SanitizationOptions { DefaultProfile = null! };
        options.Validate().Should().BeFalse();
    }

    [Fact]
    public void Validate_EmptyDefaultProfile_ReturnsFalse()
    {
        var options = new SanitizationOptions { DefaultProfile = "" };
        options.Validate().Should().BeFalse();
    }

    [Fact]
    public void Validate_WhitespaceDefaultProfile_ReturnsFalse()
    {
        var options = new SanitizationOptions { DefaultProfile = "   " };
        options.Validate().Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10_000_001)]
    public void Validate_MaxInputLengthOutOfRange_ReturnsFalse(int value)
    {
        var options = new SanitizationOptions { MaxInputLength = value };
        options.Validate().Should().BeFalse();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1_000_000)]
    [InlineData(10_000_000)]
    public void Validate_MaxInputLengthInRange_ReturnsTrue(int value)
    {
        var options = new SanitizationOptions { MaxInputLength = value };
        options.Validate().Should().BeTrue();
    }

    [Fact]
    public void Validate_CustomProfileConflictsWithBuiltIn_ReturnsFalse()
    {
        var options = new SanitizationOptions
        {
            AllowCustomProfiles = true,
            CustomProfiles = new Dictionary<string, CustomProfileDefinition>
            {
                ["conflict"] = new CustomProfileDefinition { Name = "PlainText" }
            }
        };
        options.Validate().Should().BeFalse();
    }

    [Fact]
    public void Validate_CustomProfileWithEmptyName_ReturnsFalse()
    {
        var options = new SanitizationOptions
        {
            AllowCustomProfiles = true,
            CustomProfiles = new Dictionary<string, CustomProfileDefinition>
            {
                ["test"] = new CustomProfileDefinition { Name = "" }
            }
        };
        options.Validate().Should().BeFalse();
    }

    [Fact]
    public void Validate_CustomProfileWithValidName_ReturnsTrue()
    {
        var options = new SanitizationOptions
        {
            AllowCustomProfiles = true,
            CustomProfiles = new Dictionary<string, CustomProfileDefinition>
            {
                ["rich"] = new CustomProfileDefinition { Name = "RichContent" }
            }
        };
        options.Validate().Should().BeTrue();
    }

    [Fact]
    public void Validate_CustomProfilesDisabled_IgnoresConflicts()
    {
        var options = new SanitizationOptions
        {
            AllowCustomProfiles = false,
            CustomProfiles = new Dictionary<string, CustomProfileDefinition>
            {
                ["conflict"] = new CustomProfileDefinition { Name = "PlainText" }
            }
        };
        // Conflicts are ignored when custom profiles are disabled
        options.Validate().Should().BeTrue();
    }
}
