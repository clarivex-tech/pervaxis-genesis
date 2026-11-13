using FluentAssertions;
using Pervaxis.Genesis.Base.Validation;

namespace Pervaxis.Genesis.Base.Tests;

public class TenantIdValidatorTests
{
    [Fact]
    public void Validate_ReservedWordAsSuffixWithoutTrailingHyphen_IsValid()
    {
        // Reserved substrings need hyphens on both sides ("-platform-"), so a tenant whose
        // ID merely ends in a reserved word, such as "acme-platform", is allowed.
        var result = TenantIdValidator.Validate("acme-platform");

        result.IsValid.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();
    }
}
