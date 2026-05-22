using FluentAssertions;
using Pervaxis.Genesis.Base.Exceptions;

namespace Pervaxis.Genesis.Base.Tests;

public class GenesisExceptionTests
{
    [Fact]
    public void ExceptionId_IsGeneratedWithExpectedFormatAndIsUnique()
    {
        var first = new GenesisException("first");
        var second = new GenesisException("second");

        first.ExceptionId.Should().MatchRegex("^ex_[a-f0-9]{16}$");
        second.ExceptionId.Should().MatchRegex("^ex_[a-f0-9]{16}$");
        first.ExceptionId.Should().NotBe(second.ExceptionId);
    }

    [Fact]
    public void ErrorCodeAndContext_AreOptional()
    {
        var exception = new GenesisException("message");

        exception.ErrorCode.Should().BeNull();
        exception.Context.Should().BeNull();
    }

    [Fact]
    public void ErrorCodeAndContext_CanBeProvided()
    {
        var context = new Dictionary<string, object>
        {
            ["provider"] = "cache",
            ["attempt"] = 2
        };

        var exception = new GenesisException("message", "ERR_TEST", context);

        exception.ErrorCode.Should().Be("ERR_TEST");
        exception.Context.Should().BeSameAs(context);
    }
}
