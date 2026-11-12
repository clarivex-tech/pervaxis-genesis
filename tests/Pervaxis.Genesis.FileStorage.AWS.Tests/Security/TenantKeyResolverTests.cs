/*
 ************************************************************************
 * Copyright (C) 2026 Clarivex Technologies Private Limited
 * All Rights Reserved.
 *
 * NOTICE: All intellectual and technical concepts contained
 * herein are proprietary to Clarivex Technologies Private Limited
 * and may be covered by Indian and Foreign Patents,
 * patents in process, and are protected by trade secret or
 * copyright law. Dissemination of this information or reproduction
 * of this material is strictly forbidden unless prior written
 * permission is obtained from Clarivex Technologies Private Limited.
 *
 * Product:   Pervaxis Platform
 * Website:   https://clarivex.tech
 ************************************************************************
 */

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Pervaxis.Core.Abstractions.MultiTenancy;
using Pervaxis.Genesis.Base.Exceptions;
using Pervaxis.Genesis.FileStorage.AWS.Security;

namespace Pervaxis.Genesis.FileStorage.AWS.Tests.Security;

/// <summary>
/// Phase 1 Security Tests: TenantKeyResolver validation for tenant isolation.
/// These tests verify that the resolver correctly enforces tenant boundaries
/// and rejects all path traversal and encoding attack vectors.
/// </summary>
public sealed class TenantKeyResolverTests
{
    private readonly Mock<ILogger<TenantKeyResolver>> _mockLogger;

    public TenantKeyResolverTests()
    {
        _mockLogger = new Mock<ILogger<TenantKeyResolver>>();
    }

    private TenantKeyResolver CreateResolver(
        ITenantContext? tenantContext = null,
        bool enableTenantIsolation = true)
    {
        return new TenantKeyResolver(
            tenantContext,
            _mockLogger.Object,
            enableTenantIsolation);
    }

    private Mock<ITenantContext> CreateMockTenantContext(string tenantId, bool isResolved = true)
    {
        var mock = new Mock<ITenantContext>();
        mock.Setup(x => x.IsResolved).Returns(isResolved);
        mock.Setup(x => x.TenantId).Returns(new TenantId(tenantId));
        mock.Setup(x => x.TenantName).Returns($"{tenantId} Corp");
        return mock;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CRITICAL TEST 1: Missing or Invalid Tenant Context
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ResolveKey_WithNullTenantContext_ThrowsGenesisException()
    {
        // Arrange
        var resolver = CreateResolver(tenantContext: null, enableTenantIsolation: true);

        // Act
        var act = () => resolver.ResolveKey("documents/report.pdf");

        // Assert
        act.Should().Throw<GenesisException>()
            .WithMessage("*Tenant context not resolved*")
            .Where(ex => ex.ErrorCode == FileStorageConstants.FailureReasons.TenantContextNotResolved);
    }

    [Fact]
    public void ResolveKey_WithUnresolvedTenantContext_ThrowsGenesisException()
    {
        // Arrange
        var mockContext = CreateMockTenantContext("acme", isResolved: false);
        var resolver = CreateResolver(mockContext.Object);

        // Act
        var act = () => resolver.ResolveKey("documents/report.pdf");

        // Assert
        act.Should().Throw<GenesisException>()
            .WithMessage("*Tenant context not resolved*")
            .Where(ex => ex.ErrorCode == FileStorageConstants.FailureReasons.TenantContextNotResolved);
    }

    [Fact]
    public void ResolveKey_WithInvalidTenantId_ThrowsGenesisException()
    {
        // Arrange: Tenant ID with invalid characters (uppercase, underscores not allowed)
        var mockContext = CreateMockTenantContext("Invalid_Tenant!", isResolved: true);
        var resolver = CreateResolver(mockContext.Object);

        // Act
        var act = () => resolver.ResolveKey("documents/report.pdf");

        // Assert
        act.Should().Throw<GenesisException>()
            .WithMessage("*Invalid tenant ID format*")
            .Where(ex => ex.ErrorCode == FileStorageConstants.FailureReasons.InvalidTenantId);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CRITICAL TEST 2: Path Traversal - Basic Patterns
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("../etc/passwd")]
    [InlineData("../../etc/passwd")]
    [InlineData("documents/../../../etc/passwd")]
    [InlineData("..\\windows\\system32")]
    [InlineData("documents\\..\\..\\sensitive")]
    public void ResolveKey_WithPathTraversal_ThrowsGenesisException(string maliciousKey)
    {
        // Arrange
        var mockContext = CreateMockTenantContext("acme");
        var resolver = CreateResolver(mockContext.Object);

        // Act
        var act = () => resolver.ResolveKey(maliciousKey);

        // Assert
        act.Should().Throw<GenesisException>()
            .WithMessage("*path traversal*")
            .Where(ex => ex.ErrorCode == FileStorageConstants.FailureReasons.KeyPathTraversal);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CRITICAL TEST 3: Path Traversal - URL-Encoded Variants
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("%2e%2e/etc/passwd")]           // URL-encoded ../
    [InlineData("%2e%2e%2fetc/passwd")]         // URL-encoded ../
    [InlineData("%2e%2e%5cwindows")]            // URL-encoded ..\
    [InlineData("%252e%252e%252f")]             // Double-encoded ../
    [InlineData("documents/%2e%2e/sensitive")]  // Mixed
    [InlineData("%2E%2E/config")]               // Uppercase encoding
    public void ResolveKey_WithUrlEncodedTraversal_ThrowsGenesisException(string encodedKey)
    {
        // Arrange
        var mockContext = CreateMockTenantContext("acme");
        var resolver = CreateResolver(mockContext.Object);

        // Act
        var act = () => resolver.ResolveKey(encodedKey);

        // Assert
        act.Should().Throw<GenesisException>()
            .WithMessage("*path traversal*")
            .Where(ex => ex.ErrorCode == FileStorageConstants.FailureReasons.KeyPathTraversal);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CRITICAL TEST 4: Dot Segments and Empty Segments
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("documents/.")]
    [InlineData("documents/..")]
    [InlineData("./documents")]
    [InlineData("../documents")]
    public void ResolveKey_WithDotSegments_ThrowsGenesisException(string keyWithDots)
    {
        // Arrange
        var mockContext = CreateMockTenantContext("acme");
        var resolver = CreateResolver(mockContext.Object);

        // Act
        var act = () => resolver.ResolveKey(keyWithDots);

        // Assert
        act.Should().Throw<GenesisException>()
            .Where(ex => ex.ErrorCode == FileStorageConstants.FailureReasons.KeyPathTraversal ||
                        ex.ErrorCode == FileStorageConstants.FailureReasons.InvalidKeyFormat);
    }

    [Theory]
    [InlineData("documents//report.pdf")]       // Double slash
    [InlineData("//documents")]                 // Leading double slash
    [InlineData("documents///deep")]            // Triple slash
    public void ResolveKey_WithEmptySegments_HandlesGracefully(string keyWithEmptySegments)
    {
        // Arrange
        var mockContext = CreateMockTenantContext("acme");
        var resolver = CreateResolver(mockContext.Object);

        // Act - normalization should handle multiple slashes
        var result = resolver.ResolveKey(keyWithEmptySegments);

        // Assert - should normalize to single slashes
        result.Should().StartWith("tenants/acme/");
        result.Should().NotContain("//");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CRITICAL TEST 5: Control Characters
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("document\x00.pdf")]            // Null byte
    [InlineData("document\x01.pdf")]            // Start of heading
    [InlineData("document\x1f.pdf")]            // Unit separator
    [InlineData("document\x7f.pdf")]            // DEL character
    [InlineData("doc\nument.pdf")]              // Newline
    [InlineData("doc\rument.pdf")]              // Carriage return
    public void ResolveKey_WithControlCharacters_ThrowsGenesisException(string keyWithControlChars)
    {
        // Arrange
        var mockContext = CreateMockTenantContext("acme");
        var resolver = CreateResolver(mockContext.Object);

        // Act
        var act = () => resolver.ResolveKey(keyWithControlChars);

        // Assert
        act.Should().Throw<GenesisException>()
            .WithMessage("*control characters*")
            .Where(ex => ex.ErrorCode == FileStorageConstants.FailureReasons.InvalidKeyFormat);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CRITICAL TEST 6: Key Length Validation
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ResolveKey_ExceedingMaxLength_ThrowsGenesisException()
    {
        // Arrange
        var mockContext = CreateMockTenantContext("acme");
        var resolver = CreateResolver(mockContext.Object);

        // Create a key that will exceed 1024 bytes after adding tenant prefix
        // tenants/acme/ = 13 bytes, so create a 1020-byte user key
        var longKey = new string('a', 1020);

        // Act
        var act = () => resolver.ResolveKey(longKey);

        // Assert
        act.Should().Throw<GenesisException>()
            .WithMessage("*exceeds maximum length*")
            .Where(ex => ex.ErrorCode == FileStorageConstants.FailureReasons.KeyTooLong);
    }

    [Fact]
    public void ResolveKey_AtMaxLength_Succeeds()
    {
        // Arrange
        var mockContext = CreateMockTenantContext("acme");
        var resolver = CreateResolver(mockContext.Object);

        // Create a key that's exactly at the limit after adding tenant prefix
        // tenants/acme/ = 13 bytes
        var longKey = new string('a', 1024 - 13);

        // Act
        var result = resolver.ResolveKey(longKey);

        // Assert
        result.Should().StartWith("tenants/acme/");
        System.Text.Encoding.UTF8.GetByteCount(result).Should().BeLessOrEqualTo(1024);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CRITICAL TEST 7: Valid Key Resolution
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("documents/report.pdf", "tenants/acme/documents/report.pdf")]
    [InlineData("images/logo.png", "tenants/acme/images/logo.png")]
    [InlineData("deep/nested/folder/file.txt", "tenants/acme/deep/nested/folder/file.txt")]
    [InlineData("file-with-dashes.pdf", "tenants/acme/file-with-dashes.pdf")]
    [InlineData("file_with_underscores.pdf", "tenants/acme/file_with_underscores.pdf")]
    [InlineData("file.with.dots.pdf", "tenants/acme/file.with.dots.pdf")]
    public void ResolveKey_WithValidKey_ReturnsCorrectTenantKey(string userKey, string expectedKey)
    {
        // Arrange
        var mockContext = CreateMockTenantContext("acme");
        var resolver = CreateResolver(mockContext.Object);

        // Act
        var result = resolver.ResolveKey(userKey);

        // Assert
        result.Should().Be(expectedKey);
    }

    [Theory]
    [InlineData("/leading/slash.pdf", "tenants/acme/leading/slash.pdf")]
    [InlineData("\\backslashes\\file.pdf", "tenants/acme/backslashes/file.pdf")]
    [InlineData("mixed/\\slashes.pdf", "tenants/acme/mixed/slashes.pdf")]
    public void ResolveKey_WithLeadingSlashOrBackslashes_NormalizesCorrectly(string userKey, string expectedKey)
    {
        // Arrange
        var mockContext = CreateMockTenantContext("acme");
        var resolver = CreateResolver(mockContext.Object);

        // Act
        var result = resolver.ResolveKey(userKey);

        // Assert
        result.Should().Be(expectedKey);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CRITICAL TEST 8: Prefix Resolution (List Operations)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ResolvePrefix_WithEmptyPrefix_ReturnsTenantPrefix()
    {
        // Arrange
        var mockContext = CreateMockTenantContext("acme");
        var resolver = CreateResolver(mockContext.Object);

        // Act
        var result = resolver.ResolvePrefix(null);

        // Assert
        result.Should().Be("tenants/acme/");
    }

    [Fact]
    public void ResolvePrefix_WithNullPrefix_ReturnsTenantPrefix()
    {
        // Arrange
        var mockContext = CreateMockTenantContext("acme");
        var resolver = CreateResolver(mockContext.Object);

        // Act
        var result = resolver.ResolvePrefix(null);

        // Assert
        result.Should().Be("tenants/acme/");
    }

    [Theory]
    [InlineData("documents/", "tenants/acme/documents/")]
    [InlineData("images", "tenants/acme/images")]
    [InlineData("reports/2024/", "tenants/acme/reports/2024/")]
    public void ResolvePrefix_WithValidPrefix_ReturnsCorrectTenantPrefix(string userPrefix, string expectedPrefix)
    {
        // Arrange
        var mockContext = CreateMockTenantContext("acme");
        var resolver = CreateResolver(mockContext.Object);

        // Act
        var result = resolver.ResolvePrefix(userPrefix);

        // Assert
        result.Should().Be(expectedPrefix);
    }

    [Theory]
    [InlineData("../")]
    [InlineData("../../sensitive/")]
    [InlineData("%2e%2e/")]
    public void ResolvePrefix_WithTraversalAttempt_ThrowsGenesisException(string maliciousPrefix)
    {
        // Arrange
        var mockContext = CreateMockTenantContext("acme");
        var resolver = CreateResolver(mockContext.Object);

        // Act
        var act = () => resolver.ResolvePrefix(maliciousPrefix);

        // Assert
        act.Should().Throw<GenesisException>()
            .WithMessage("*path traversal*")
            .Where(ex => ex.ErrorCode == FileStorageConstants.FailureReasons.KeyPathTraversal);
    }

    [Fact]
    public void ResolvePrefix_WithUnresolvedContext_ThrowsGenesisException()
    {
        // Arrange
        var mockContext = CreateMockTenantContext("acme", isResolved: false);
        var resolver = CreateResolver(mockContext.Object);

        // Act
        var act = () => resolver.ResolvePrefix("documents/");

        // Assert
        act.Should().Throw<GenesisException>()
            .WithMessage("*Tenant context not resolved*")
            .Where(ex => ex.ErrorCode == FileStorageConstants.FailureReasons.TenantContextNotResolved);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CRITICAL TEST 9: Prefix Stripping
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("tenants/acme/documents/report.pdf", "documents/report.pdf")]
    [InlineData("tenants/acme/images/logo.png", "images/logo.png")]
    [InlineData("tenants/acme/file.txt", "file.txt")]
    public void StripTenantPrefix_WithValidTenantKey_ReturnsUserKey(string fullKey, string expectedUserKey)
    {
        // Arrange
        var mockContext = CreateMockTenantContext("acme");
        var resolver = CreateResolver(mockContext.Object);

        // Act
        var result = resolver.StripTenantPrefix(fullKey);

        // Assert
        result.Should().Be(expectedUserKey);
    }

    [Fact]
    public void StripTenantPrefix_WithWrongTenantPrefix_ReturnsOriginalKey()
    {
        // Arrange: Current context is for "acme" but key belongs to "globex"
        var mockContext = CreateMockTenantContext("acme");
        var resolver = CreateResolver(mockContext.Object);

        // Act
        var result = resolver.StripTenantPrefix("tenants/globex/documents/report.pdf");

        // Assert - should return as-is since it doesn't match current tenant
        result.Should().Be("tenants/globex/documents/report.pdf");
    }

    [Fact]
    public void StripTenantPrefix_WithoutTenantPrefix_ReturnsOriginalKey()
    {
        // Arrange
        var mockContext = CreateMockTenantContext("acme");
        var resolver = CreateResolver(mockContext.Object);

        // Act
        var result = resolver.StripTenantPrefix("documents/report.pdf");

        // Assert
        result.Should().Be("documents/report.pdf");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CRITICAL TEST 10: Cross-Tenant Key Access Prevention
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ResolveKey_ForDifferentTenants_ProducesDifferentKeys()
    {
        // Arrange
        var mockContextAcme = CreateMockTenantContext("acme");
        var mockContextGlobex = CreateMockTenantContext("globex");
        
        var resolverAcme = CreateResolver(mockContextAcme.Object);
        var resolverGlobex = CreateResolver(mockContextGlobex.Object);

        var userKey = "shared/document.pdf";

        // Act
        var acmeKey = resolverAcme.ResolveKey(userKey);
        var globexKey = resolverGlobex.ResolveKey(userKey);

        // Assert
        acmeKey.Should().Be("tenants/acme/shared/document.pdf");
        globexKey.Should().Be("tenants/globex/shared/document.pdf");
        acmeKey.Should().NotBe(globexKey);
    }

    [Fact]
    public void ResolveKey_CannotEscapeTenantBoundary()
    {
        // Arrange: Attempt to construct a key that would escape to another tenant
        var mockContext = CreateMockTenantContext("acme");
        var resolver = CreateResolver(mockContext.Object);

        // Try various escape attempts
        var escapeAttempts = new[]
        {
            "../../globex/secrets.txt",
            "../../../tenants/globex/data.pdf",
            "%2e%2e/globex/confidential.doc"
        };

        // Act & Assert
        foreach (var attempt in escapeAttempts)
        {
            var act = () => resolver.ResolveKey(attempt);
            act.Should().Throw<GenesisException>()
                .Where(ex => ex.ErrorCode == FileStorageConstants.FailureReasons.KeyPathTraversal,
                    $"Escape attempt should be blocked: {attempt}");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CRITICAL TEST 11: Tenant Isolation Disabled (Dev/Test Only)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ResolveKey_WithTenantIsolationDisabled_ReturnsNonTenantKey()
    {
        // Arrange
        var resolver = CreateResolver(tenantContext: null, enableTenantIsolation: false);

        // Act
        var result = resolver.ResolveKey("documents/report.pdf");

        // Assert
        result.Should().Be("documents/report.pdf");
        result.Should().NotContain("tenants/");
    }

    [Fact]
    public void ResolvePrefix_WithTenantIsolationDisabled_ReturnsNonTenantPrefix()
    {
        // Arrange
        var resolver = CreateResolver(tenantContext: null, enableTenantIsolation: false);

        // Act
        var result = resolver.ResolvePrefix("documents/");

        // Assert
        result.Should().Be("documents/");
        result.Should().NotContain("tenants/");
    }

    [Fact]
    public void ResolvePrefix_WithEmptyPrefixAndIsolationDisabled_ReturnsEmpty()
    {
        // Arrange
        var resolver = CreateResolver(tenantContext: null, enableTenantIsolation: false);

        // Act
        var result = resolver.ResolvePrefix(null);

        // Assert
        result.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CRITICAL TEST 12: GetTenantId
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void GetTenantId_WithResolvedContext_ReturnsTenantId()
    {
        // Arrange
        var mockContext = CreateMockTenantContext("acme");
        var resolver = CreateResolver(mockContext.Object);

        // Act
        var result = resolver.GetTenantId();

        // Assert
        result.Should().Be("acme");
    }

    [Fact]
    public void GetTenantId_WithUnresolvedContext_ThrowsGenesisException()
    {
        // Arrange
        var mockContext = CreateMockTenantContext("acme", isResolved: false);
        var resolver = CreateResolver(mockContext.Object);

        // Act
        var act = () => resolver.GetTenantId();

        // Assert
        act.Should().Throw<GenesisException>()
            .WithMessage("*Tenant context not resolved*");
    }

    [Fact]
    public void GetTenantId_WithIsolationDisabled_ThrowsGenesisException()
    {
        // Arrange
        var resolver = CreateResolver(tenantContext: null, enableTenantIsolation: false);

        // Act
        var act = () => resolver.GetTenantId();

        // Assert
        act.Should().Throw<GenesisException>()
            .WithMessage("*tenant isolation is disabled*");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CRITICAL TEST 13: Unicode and Special Characters (valid S3 keys)
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("documents/файл.pdf")]          // Cyrillic
    [InlineData("文档/报告.pdf")]                 // Chinese
    [InlineData("documents/café.pdf")]          // Accented characters
    [InlineData("documents/file-名前.pdf")]     // Mixed
    public void ResolveKey_WithUnicodeCharacters_Succeeds(string userKey)
    {
        // Arrange
        var mockContext = CreateMockTenantContext("acme");
        var resolver = CreateResolver(mockContext.Object);

        // Act
        var result = resolver.ResolveKey(userKey);

        // Assert
        result.Should().StartWith("tenants/acme/");
        result.Should().Contain(userKey.TrimStart('/'));
    }

    [Theory]
    [InlineData("file!name.pdf")]
    [InlineData("file@name.pdf")]
    [InlineData("file#name.pdf")]
    [InlineData("file$name.pdf")]
    [InlineData("file&name.pdf")]
    [InlineData("file(name).pdf")]
    [InlineData("file=name.pdf")]
    [InlineData("file+name.pdf")]
    [InlineData("file,name.pdf")]
    public void ResolveKey_WithSpecialCharacters_Succeeds(string userKey)
    {
        // Arrange
        var mockContext = CreateMockTenantContext("acme");
        var resolver = CreateResolver(mockContext.Object);

        // Act
        var result = resolver.ResolveKey(userKey);

        // Assert
        result.Should().StartWith("tenants/acme/");
        result.Should().EndWith(userKey);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CRITICAL TEST 14: Edge Cases
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   ")]
    public void ResolveKey_WithEmptyOrWhitespace_ThrowsArgumentException(string emptyKey)
    {
        // Arrange
        var mockContext = CreateMockTenantContext("acme");
        var resolver = CreateResolver(mockContext.Object);

        // Act
        var act = () => resolver.ResolveKey(emptyKey);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void StripTenantPrefix_WithEmptyKey_ThrowsArgumentException()
    {
        // Arrange
        var mockContext = CreateMockTenantContext("acme");
        var resolver = CreateResolver(mockContext.Object);

        // Act
        var act = () => resolver.StripTenantPrefix("");

        // Assert
        act.Should().Throw<ArgumentException>();
    }
}
