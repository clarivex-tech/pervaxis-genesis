using Moq;
using Pervaxis.Core.Abstractions.MultiTenancy;
using Pervaxis.Genesis.Base.Exceptions;
using Pervaxis.Genesis.Caching.AWS.Security;

namespace Pervaxis.Genesis.Caching.AWS.Tests.Security;

public sealed class TenantCacheKeyResolverTests
{
    private static Mock<ITenantContext> Context(string tenant = "acme", bool resolved = true)
    {
        var context = new Mock<ITenantContext>();
        context.SetupGet(x => x.IsResolved).Returns(resolved);
        context.SetupGet(x => x.TenantId).Returns(new TenantId(tenant));
        return context;
    }

    [Theory]
    [InlineData("key")]
    [InlineData("..")]
    [InlineData("a:b")]
    [InlineData("*?[]\\")]
    [InlineData("café")]
    [InlineData("report%20final")]
    public void ResolveKey_PreservesLiteralKeyAndRoundTrips(string key)
    {
        var resolver = new TenantCacheKeyResolver(Context().Object);
        var fullKey = resolver.ResolveKey(key);
        Assert.Equal("tenants:acme:" + key, fullKey);
        Assert.Equal(key, resolver.StripPrefix(fullKey));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("a\0b")]
    [InlineData("a\nb")]
    [InlineData("a\rb")]
    [InlineData("a\tb")]
    [InlineData("a\u007fb")]
    [InlineData("a\u0085b")]
    [InlineData("a{b")]
    [InlineData("a}b")]
    [InlineData("tenants:other:key")]
    [InlineData("tenants:acme-corp:key")]
    [InlineData("tenants:acme:key")]
    public void ResolveKey_RejectsInvalidCallerKeys(string? key)
    {
        var resolver = new TenantCacheKeyResolver(Context().Object);
        Assert.Throws<GenesisException>(() => resolver.ResolveKey(key!));
    }

    [Fact]
    public void AllOperations_RejectMissingContext()
    {
        AssertContextRejected(new TenantCacheKeyResolver(null));
        AssertContextRejected(new TenantCacheKeyResolver(Context(resolved: false).Object));
    }

    [Theory]
    [InlineData("Invalid_Tenant!")]
    [InlineData("ab")]
    [InlineData("platform")]
    [InlineData("acme--corp")]
    [InlineData("acme\n")]
    public void AllOperations_RejectInvalidTenant(string tenant)
        => AssertContextRejected(new TenantCacheKeyResolver(Context(tenant).Object));

    private static void AssertContextRejected(TenantCacheKeyResolver resolver)
    {
        Assert.Throws<GenesisException>(() => resolver.ResolveKey("key"));
        Assert.Throws<GenesisException>(() => resolver.StripPrefix("tenants:acme:key"));
    }

    [Fact]
    public void ResolveKey_EnforcesTotalUtf8ByteLimitIncludingPrefix()
    {
        var resolver = new TenantCacheKeyResolver(Context().Object);
        // tenants:acme: occupies 13 bytes; é occupies two bytes.
        var key = new string('é', 505) + "x";
        Assert.Equal(1024, System.Text.Encoding.UTF8.GetByteCount(resolver.ResolveKey(key)));
        Assert.Throws<GenesisException>(() => resolver.ResolveKey(key + "x"));
        Assert.Throws<GenesisException>(() => resolver.ResolveKey(new string('x', 1012)));
        Assert.Throws<GenesisException>(() => new TenantCacheKeyResolver(Context("acme-corp").Object).ResolveKey(key));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("key")]
    [InlineData("tenants:acme-corp:key")]
    [InlineData("tenants:other:key")]
    [InlineData("tenants:acme:")]
    [InlineData("tenants:acme:a{b")]
    [InlineData("tenants:acme:a\nb")]
    [InlineData("tenants:acme:tenants:other:key")]
    public void StripPrefix_RejectsForeignOrMalformedKeys(string? key)
    {
        var resolver = new TenantCacheKeyResolver(Context().Object);
        Assert.Throws<GenesisException>(() => resolver.StripPrefix(key!));
    }

    [Fact]
    public void StripPrefix_RejectsOversizeStoredKey()
    {
        var resolver = new TenantCacheKeyResolver(Context().Object);
        Assert.Throws<GenesisException>(() => resolver.StripPrefix("tenants:acme:" + new string('x', 1012)));
    }

    [Fact]
    public void AllOperations_RejectMalformedUnicodeInsteadOfAliasingKeys()
    {
        var resolver = new TenantCacheKeyResolver(Context().Object);
        var invalidKey = new string((char)0xD800, 1);
        Assert.Throws<GenesisException>(() => resolver.ResolveKey(invalidKey));
        Assert.Throws<GenesisException>(() => resolver.StripPrefix("tenants:acme:" + invalidKey));
    }

    [Fact]
    public void SameInstance_ReadsTenantOnEveryCall()
    {
        var context = Context();
        var resolver = new TenantCacheKeyResolver(context.Object);
        var first = resolver.ResolveKey("key");
        context.SetupGet(x => x.TenantId).Returns(new TenantId("acme-corp"));
        Assert.Equal("tenants:acme-corp:key", resolver.ResolveKey("key"));
        Assert.Throws<GenesisException>(() => resolver.StripPrefix(first));
        context.SetupGet(x => x.IsResolved).Returns(false);
        AssertContextRejected(resolver);
    }
}
