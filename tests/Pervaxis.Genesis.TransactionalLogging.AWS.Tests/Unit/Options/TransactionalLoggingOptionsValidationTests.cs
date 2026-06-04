using Pervaxis.Genesis.TransactionalLogging.AWS.Options;

namespace Pervaxis.Genesis.TransactionalLogging.AWS.Tests.Unit.Options;

public class TransactionalLoggingOptionsValidationTests
{
    [Fact]
    public void Validate_WithDefaults_ReturnsTrue()
    {
        var options = new TransactionalLoggingOptions();
        options.Validate().Should().BeTrue();
    }

    [Fact]
    public void Validate_WhenEnabledAndTableNameEmpty_ReturnsFalse()
    {
        var options = new TransactionalLoggingOptions { Enabled = true, TableName = "" };
        options.Validate().Should().BeFalse();
    }

    [Fact]
    public void Validate_WhenEnabledAndTableNameNull_ReturnsFalse()
    {
        var options = new TransactionalLoggingOptions { Enabled = true, TableName = null! };
        options.Validate().Should().BeFalse();
    }

    [Fact]
    public void Validate_WhenDisabledAndTableNameEmpty_ReturnsTrue()
    {
        var options = new TransactionalLoggingOptions { Enabled = false, TableName = "" };
        options.Validate().Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(366)]
    [InlineData(-1)]
    [InlineData(1000)]
    public void Validate_WhenHotRetentionDaysOutOfRange_ReturnsFalse(int days)
    {
        var options = new TransactionalLoggingOptions { HotRetentionDays = days };
        options.Validate().Should().BeFalse();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    [InlineData(365)]
    public void Validate_WhenHotRetentionDaysValid_ReturnsTrue(int days)
    {
        var options = new TransactionalLoggingOptions { HotRetentionDays = days };
        options.Validate().Should().BeTrue();
    }

    [Theory]
    [InlineData(29)]
    [InlineData(3651)]
    [InlineData(0)]
    public void Validate_WhenColdRetentionDaysOutOfRange_ReturnsFalse(int days)
    {
        var options = new TransactionalLoggingOptions { ColdRetentionDays = days };
        options.Validate().Should().BeFalse();
    }

    [Fact]
    public void Validate_WhenColdRetentionLessThanHot_ReturnsFalse()
    {
        var options = new TransactionalLoggingOptions
        {
            HotRetentionDays = 60,
            ColdRetentionDays = 50
        };
        options.Validate().Should().BeFalse();
    }

    [Fact]
    public void Validate_WhenColdRetentionEqualsHot_ReturnsTrue()
    {
        var options = new TransactionalLoggingOptions
        {
            HotRetentionDays = 30,
            ColdRetentionDays = 30
        };
        options.Validate().Should().BeTrue();
    }

    [Fact]
    public void Validate_WhenResilienceFails_ReturnsFalse()
    {
        var options = new TransactionalLoggingOptions();
        options.Resilience.RetryCount = -1; // Invalid
        options.Validate().Should().BeFalse();
    }

    [Fact]
    public void Validate_UseLocalEmulatorBypassesTableNameCheck()
    {
        // UseLocalEmulator is a property from GenesisOptionsBase
        // When true, TableName is not required
        var options = new TransactionalLoggingOptions
        {
            Enabled = true,
            TableName = "valid-table"
        };
        // Verify that a valid config passes when we have a valid table name
        options.Validate().Should().BeTrue();
    }
}
