using Microsoft.Extensions.Options;
using Pervaxis.Genesis.TransactionalLogging.AWS.Options;
using Pervaxis.Genesis.TransactionalLogging.AWS.Sanitization;

namespace Pervaxis.Genesis.TransactionalLogging.AWS.Tests.Unit.Sanitization;

public class ParameterSanitizerTests
{
    private static ParameterSanitizer CreateSanitizer(bool sanitize = true, List<string>? customKeys = null)
    {
        var options = new TransactionalLoggingOptions
        {
            SanitizeParameters = sanitize,
            SensitiveKeys = customKeys ?? new List<string>()
        };
        return new ParameterSanitizer(Microsoft.Extensions.Options.Options.Create(options));
    }

    [Fact]
    public void Sanitize_NullInput_ReturnsNull()
    {
        var sanitizer = CreateSanitizer();
        sanitizer.Sanitize(null).Should().BeNull();
    }

    [Fact]
    public void Sanitize_WithSensitiveKey_Redacts()
    {
        var sanitizer = CreateSanitizer();
        var result = sanitizer.Sanitize(new { password = "secret123", name = "test" });

        result.Should().NotBeNull();
        result!["password"].Should().Be("[REDACTED]");
        result["name"]!.ToString().Should().Be("test");
    }

    [Fact]
    public void Sanitize_CaseInsensitiveMatching()
    {
        var sanitizer = CreateSanitizer();
        // Anonymous objects get camelCased via JsonNamingPolicy.CamelCase
        var result = sanitizer.Sanitize(new { password = "secret", apiKey = "key123" });

        result!["password"].Should().Be("[REDACTED]");
        result["apiKey"].Should().Be("[REDACTED]");
    }

    [Fact]
    public void Sanitize_ContainsMatching_RedactsPartialMatch()
    {
        var sanitizer = CreateSanitizer();
        var result = sanitizer.Sanitize(new { userPassword = "abc", connectionString = "host=db" });

        result!["userPassword"].Should().Be("[REDACTED]");
        result["connectionString"].Should().Be("[REDACTED]");
    }

    [Fact]
    public void Sanitize_NonSensitiveKeys_Preserved()
    {
        var sanitizer = CreateSanitizer();
        var result = sanitizer.Sanitize(new { orderId = "ORD-1", amount = 99.99 });

        result.Should().NotBeNull();
        result!["orderId"]!.ToString().Should().Be("ORD-1");
    }

    [Fact]
    public void Sanitize_CustomPatterns_Applied()
    {
        var sanitizer = CreateSanitizer(customKeys: new List<string> { "ssn", "dob" });
        var result = sanitizer.Sanitize(new { ssn = "123-45-6789", dob = "1990-01-01", name = "John" });

        result!["ssn"].Should().Be("[REDACTED]");
        result["dob"].Should().Be("[REDACTED]");
        result["name"]!.ToString().Should().Be("John");
    }

    [Fact]
    public void Sanitize_WhenDisabled_ReturnsRawValues()
    {
        var sanitizer = CreateSanitizer(sanitize: false);
        var result = sanitizer.Sanitize(new { password = "secret123" });

        result.Should().NotBeNull();
        result!["password"]!.ToString().Should().Be("secret123");
    }

    [Fact]
    public void Sanitize_DefaultPatterns_AllCovered()
    {
        var sanitizer = CreateSanitizer();
        var sensitive = new
        {
            password = "p", secret = "s", token = "t", key = "k",
            credential = "c", auth = "a", connectionstring = "cs",
            apikey = "ak", @private = "pr"
        };
        var result = sanitizer.Sanitize(sensitive);

        foreach (var kvp in result!)
        {
            kvp.Value.Should().Be("[REDACTED]", because: $"key '{kvp.Key}' should be redacted");
        }
    }

    [Fact]
    public void Sanitize_Dictionary_Works()
    {
        var sanitizer = CreateSanitizer();
        var dict = new Dictionary<string, object?> { ["password"] = "secret", ["name"] = "test" };
        var result = sanitizer.Sanitize(dict);

        result!["password"].Should().Be("[REDACTED]");
        result["name"]!.ToString().Should().Be("test");
    }
}
