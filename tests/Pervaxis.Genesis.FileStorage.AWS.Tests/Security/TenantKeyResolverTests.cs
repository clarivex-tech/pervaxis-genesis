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
    // CRITICAL TEST 3: Path Traversal - URL-Encoded Variants (with decode loop)
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("%2e%2e/etc/passwd")]           // URL-encoded ../
    [InlineData("%2e%2e%2fetc/passwd")]         // URL-encoded ../
    [InlineData("%2e%2e%5cwindows")]            // URL-encoded ..\
    [InlineData("%252e%252e%252f")]             // Double-encoded ../ (%25 = %)
    [InlineData("documents/%2e%2e/sensitive")]  // Mixed
    [InlineData("%2E%2E/config")]               // Uppercase encoding
    [InlineData("%252e%252e%252fetc")]          // Double-encoded with path
    [InlineData("%25252e%25252e%25252f")]       // Triple-encoded
    [InlineData("%2e%2e%2F%2e%2e%2Fsecrets")]   // Mixed case multiple levels
    public void ResolveKey_WithUrlEncodedTraversal_ThrowsGenesisException(string encodedKey)
    {
        // Arrange
        var mockContext = CreateMockTenantContext("acme");
        var resolver = CreateResolver(mockContext.Object);

        // Act
        var act = () => resolver.ResolveKey(encodedKey);

        // Assert - NOW REJECTS AT ENCODING CHECK (before traversal detection)
        // This prevents silent key rewriting and data-integrity issues
        act.Should().Throw<GenesisException>()
            .WithMessage("*contains URL-encoded sequences*")
            .Where(ex => ex.ErrorCode == FileStorageConstants.FailureReasons.InvalidKeyFormat);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CRITICAL TEST 4: Unicode Lookalike Characters
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("documents／file.pdf")]          // Fullwidth solidus (U+FF0F)
    [InlineData("documents．．／sensitive")]      // Fullwidth dots and slash
    [InlineData("．．／etc／passwd")]            // All fullwidth
    [InlineData("files⁄secrets")]               // Fraction slash (U+2044)
    [InlineData("data∕config")]                 // Division slash (U+2215)
    public void ResolveKey_WithUnicodeLookalikes_ThrowsGenesisException(string keyWithLookalikes)
    {
        // Arrange
        var mockContext = CreateMockTenantContext("acme");
        var resolver = CreateResolver(mockContext.Object);

        // Act
        var act = () => resolver.ResolveKey(keyWithLookalikes);

        // Assert
        act.Should().Throw<GenesisException>()
            .WithMessage("*Unicode lookalike*")
            .Where(ex => ex.ErrorCode == FileStorageConstants.FailureReasons.InvalidKeyFormat);
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
    [InlineData("")]
    [InlineData("/")]
    [InlineData("//")]
    [InlineData("///")]
    public void ResolveKey_WithEmptyOrSlashOnly_ThrowsException(string emptyKey)
    {
        // Arrange
        var mockContext = CreateMockTenantContext("acme");
        var resolver = CreateResolver(mockContext.Object);

        // Act
        var act = () => resolver.ResolveKey(emptyKey);

        // Assert
        act.Should().Throw<Exception>(); // ArgumentException or GenesisException
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
    // CRITICAL TEST 5a: Multi-Level URL Encoding & Decode Loop Limits
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ResolveKey_WithExcessiveEncoding_ThrowsAfter10Iterations()
    {
        // Arrange
        var mockContext = CreateMockTenantContext("acme");
        var resolver = CreateResolver(mockContext.Object);

        // Build a key that would require 11+ decode iterations to stabilize
        // Start with "../" which encodes to layers: ../ → ..%2F → ..%252F → ..%25252F
        var key = "../";
        for (int i = 0; i < 11; i++)
        {
            key = Uri.EscapeDataString(key);
        }
        
        // After 11 encodings, this will require 11 decode passes to reach "../"
        // Our limit is 10, so it should throw before stabilizing

        // Act
        var act = () => resolver.ResolveKey(key);

        // Assert - should throw because it doesn't stabilize within 10 iterations
        // Even if it would eventually decode to a path traversal, we reject it
        // for excessive encoding before we even get to that check
        act.Should().Throw<GenesisException>()
            .WithMessage("*Excessive URL encoding*")
            .Where(ex => ex.ErrorCode == FileStorageConstants.FailureReasons.InvalidKeyFormat);
    }

    [Theory]
    [InlineData("report%20final.pdf")]     // URL-encoded space
    [InlineData("file%2Ename.txt")]        // URL-encoded dot  
    [InlineData("doc%2Fument.pdf")]        // URL-encoded slash
    public void ResolveKey_WithPercentInKey_ThrowsGenesisException(string keyWithPercent)
    {
        // Arrange
        var mockContext = CreateMockTenantContext("acme");
        var resolver = CreateResolver(mockContext.Object);

        // Act
        var act = () => resolver.ResolveKey(keyWithPercent);

        // Assert - MUST reject to prevent data-integrity issues:
        // - "report%20final.pdf" would silently become "report final.pdf" in S3
        // - "a%20b" and "a b" would collide
        // - "%2F" would create unexpected subdirectories
        act.Should().Throw<GenesisException>()
            .WithMessage("*contains URL-encoded sequences*")
            .Where(ex => ex.ErrorCode == FileStorageConstants.FailureReasons.InvalidKeyFormat);
    }

    [Fact]
    public void ResolveKey_WithPercentEncodedTraversal_ThrowsGenesisException()
    {
        // Arrange
        var mockContext = CreateMockTenantContext("acme");
        var resolver = CreateResolver(mockContext.Object);

        // Act - decodes to "../secrets" which would be path traversal
        var act = () => resolver.ResolveKey("..%2Fsecrets");

        // Assert - rejected because decoding changes the key (before traversal check)
        act.Should().Throw<GenesisException>()
            .WithMessage("*contains URL-encoded sequences*")
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

    [Theory]
    [InlineData("%2e%2e/")]
    [InlineData("%2e%2e/files")]
    public void ResolvePrefix_WithEncodedPrefix_ThrowsGenesisException(string encodedPrefix)
    {
        // Arrange
        var mockContext = CreateMockTenantContext("acme");
        var resolver = CreateResolver(mockContext.Object);

        // Act
        var act = () => resolver.ResolvePrefix(encodedPrefix);

        // Assert - rejects at encoding check (not traversal check)
        act.Should().Throw<GenesisException>()
            .WithMessage("*contains URL-encoded sequences*")
            .Where(ex => ex.ErrorCode == FileStorageConstants.FailureReasons.InvalidKeyFormat);
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
    public void StripTenantPrefix_WithWrongTenantPrefix_ThrowsGenesisException()
    {
        // Arrange: Current context is for "acme" but key belongs to "globex"
        var mockContext = CreateMockTenantContext("acme");
        var resolver = CreateResolver(mockContext.Object);

        // Act
        var act = () => resolver.StripTenantPrefix("tenants/globex/documents/report.pdf");

        // Assert - should throw since it doesn't match current tenant
        act.Should().Throw<GenesisException>()
            .WithMessage("*does not belong to*tenant*")
            .Where(ex => ex.ErrorCode == FileStorageConstants.FailureReasons.TenantMismatchOnDownload);
    }

    [Fact]
    public void StripTenantPrefix_WithoutTenantPrefix_ThrowsGenesisException()
    {
        // Arrange
        var mockContext = CreateMockTenantContext("acme");
        var resolver = CreateResolver(mockContext.Object);

        // Act
        var act = () => resolver.StripTenantPrefix("documents/report.pdf");

        // Assert - should throw since it doesn't start with tenant prefix
        act.Should().Throw<GenesisException>()
            .WithMessage("*does not belong to*tenant*")
            .Where(ex => ex.ErrorCode == FileStorageConstants.FailureReasons.TenantMismatchOnDownload);
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
            ("../../globex/secrets.txt", FileStorageConstants.FailureReasons.KeyPathTraversal),
            ("../../../tenants/globex/data.pdf", FileStorageConstants.FailureReasons.KeyPathTraversal),
            ("%2e%2e/globex/confidential.doc", FileStorageConstants.FailureReasons.InvalidKeyFormat) // Encoding rejected first
        };

        // Act & Assert
        foreach (var (attempt, expectedError) in escapeAttempts)
        {
            var act = () => resolver.ResolveKey(attempt);
            act.Should().Throw<GenesisException>()
                .Where(ex => ex.ErrorCode == expectedError,
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
    
    // ═══════════════════════════════════════════════════════════════════════
    // CRITICAL TEST 15: Prefix Collision Prevention
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ResolveKey_WithSimilarTenantIds_PreventsPrefixCollision()
    {
        // Arrange: Test that "acme" and "acme-corp" don't collide
        var mockContextAcme = CreateMockTenantContext("acme");
        var mockContextAcmeCorp = CreateMockTenantContext("acme-corp");
        
        var resolverAcme = CreateResolver(mockContextAcme.Object);
        var resolverAcmeCorp = CreateResolver(mockContextAcmeCorp.Object);

        // Act
        var acmeKey = resolverAcme.ResolveKey("shared/document.pdf");
        var acmeCorpKey = resolverAcmeCorp.ResolveKey("shared/document.pdf");

        // Assert: Keys should be completely different due to trailing slash
        acmeKey.Should().Be("tenants/acme/shared/document.pdf");
        acmeCorpKey.Should().Be("tenants/acme-corp/shared/document.pdf");
        acmeKey.Should().NotStartWith("tenants/acme-corp/");
        acmeCorpKey.Should().NotStartWith("tenants/acme/");
    }

    [Fact]
    public void ResolvePrefix_WithPrefixMatchingAnotherTenantId_StaysInScope()
    {
        // Arrange: Tenant "acme" tries to use prefix "corp" which could match "acme-corp"
        var mockContext = CreateMockTenantContext("acme");
        var resolver = CreateResolver(mockContext.Object);

        // Act
        var prefix = resolver.ResolvePrefix("corp");

        // Assert: Should resolve to tenants/acme/corp, NOT tenants/acme-corp
        prefix.Should().Be("tenants/acme/corp");
        prefix.Should().StartWith("tenants/acme/");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CRITICAL TEST 16: StripTenantPrefix - Foreign Key Detection
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void StripTenantPrefix_WithForeignTenantKey_Throws()
    {
        // Arrange: Current tenant is "acme" but key belongs to "globex"
        var mockContext = CreateMockTenantContext("acme");
        var resolver = CreateResolver(mockContext.Object);
        var foreignKey = "tenants/globex/documents/secret.pdf";

        // Act
        var act = () => resolver.StripTenantPrefix(foreignKey);

        // Assert: Must throw, never return foreign key silently
        act.Should().Throw<GenesisException>()
            .WithMessage("*does not belong to tenant*")
            .Where(ex => ex.ErrorCode == FileStorageConstants.FailureReasons.TenantMismatchOnDownload);
    }

    [Fact]
    public void StripTenantPrefix_WithNonTenantKey_Throws()
    {
        // Arrange
        var mockContext = CreateMockTenantContext("acme");
        var resolver = CreateResolver(mockContext.Object);
        var nonTenantKey = "documents/report.pdf"; // No tenant prefix

        // Act
        var act = () => resolver.StripTenantPrefix(nonTenantKey);

        // Assert: Must throw, never process non-tenant keys
        act.Should().Throw<GenesisException>()
            .WithMessage("*does not belong to tenant*");
    }

    [Fact]
    public void StripTenantPrefix_WithUnresolvedContext_Throws()
    {
        // Arrange
        var mockContext = CreateMockTenantContext("acme", isResolved: false);
        var resolver = CreateResolver(mockContext.Object);

        // Act
        var act = () => resolver.StripTenantPrefix("tenants/acme/file.pdf");

        // Assert
        act.Should().Throw<GenesisException>()
            .WithMessage("*Tenant context not resolved*");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CRITICAL TEST 17: ResolvePrefix Edge Cases
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("/")]
    [InlineData("//")]
    [InlineData("///")]
    public void ResolvePrefix_WithSlashOnly_ReturnsTenantPrefix(string slashPrefix)
    {
        // Arrange
        var mockContext = CreateMockTenantContext("acme");
        var resolver = CreateResolver(mockContext.Object);

        // Act
        var result = resolver.ResolvePrefix(slashPrefix);

        // Assert: Slashes should normalize away, returning just tenant prefix
        result.Should().Be("tenants/acme/");
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../")]
    [InlineData("../other")]
    public void ResolvePrefix_WithTraversalPrefix_Throws(string traversalPrefix)
    {
        // Arrange
        var mockContext = CreateMockTenantContext("acme");
        var resolver = CreateResolver(mockContext.Object);

        // Act
        var act = () => resolver.ResolvePrefix(traversalPrefix);

        // Assert: Must block traversal in prefixes
        act.Should().Throw<GenesisException>()
            .WithMessage("*path traversal*");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CRITICAL TEST 18: UTF-8 Byte Length Validation
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ResolveKey_ValidatesUtf8ByteLengthNotCharCount()
    {
        // Arrange: Multi-byte UTF-8 characters (each emoji is 4 bytes)
        var mockContext = CreateMockTenantContext("acme");
        var resolver = CreateResolver(mockContext.Object);

        // Create a key with emojis that's under char limit but over byte limit
        // tenants/acme/ = 13 bytes
        // Each emoji (🔥) = 4 bytes in UTF-8
        // 253 emojis = 1012 bytes, total = 1025 bytes > 1024 limit
        var emoji = "🔥";
        var longEmojiKey = string.Concat(Enumerable.Repeat(emoji, 253));

        // Act
        var act = () => resolver.ResolveKey(longEmojiKey);

        // Assert: Should fail on byte length, not character count
        act.Should().Throw<GenesisException>()
            .WithMessage("*exceeds maximum length*")
            .Where(ex => ex.ErrorCode == FileStorageConstants.FailureReasons.KeyTooLong);
    }

    [Fact]
    public void ResolveKey_AcceptsKeyAtExactByteLimit()
    {
        // Arrange
        var mockContext = CreateMockTenantContext("acme");
        var resolver = CreateResolver(mockContext.Object);

        // tenants/acme/ = 13 bytes, so 1011 ASCII chars should work
        var keyAtLimit = new string('a', 1011);

        // Act
        var result = resolver.ResolveKey(keyAtLimit);

        // Assert
        var byteCount = System.Text.Encoding.UTF8.GetByteCount(result);
        byteCount.Should().BeLessOrEqualTo(1024);
        result.Should().StartWith("tenants/acme/");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CRITICAL TEST 19: Property-Based Random String Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ResolveKey_WithDangerousFragmentCombinations_AlwaysThrowsOrStaysInPrefix()
    {
        // Arrange
        var mockContext = CreateMockTenantContext("acme");
        var resolver = CreateResolver(mockContext.Object);
        var random = new Random(42); // Seeded for reproducibility

        // Pool of dangerous fragments that should trigger validation
        var dangerousFragments = new[]
        {
            "..", ".", "/", "\\", 
            "%2e", "%2E", "%25", "%252e", "%252E",
            "\uFF0F", "\uFF0E",  // Fullwidth slash and dot
            "%00", "\x00",        // Null byte
            "../", ".\\",
            ".../"
        };

        // Generate 1000 random keys from dangerous fragments
        for (int i = 0; i < 1000; i++)
        {
            var fragmentCount = random.Next(1, 6); // 1-5 fragments per key
            var keyParts = new List<string>();
            
            for (int j = 0; j < fragmentCount; j++)
            {
                keyParts.Add(dangerousFragments[random.Next(dangerousFragments.Length)]);
            }
            
            var randomKey = string.Join("", keyParts);

            // Act & Assert: Either throws or returns safe key in tenant scope
            try
            {
                var result = resolver.ResolveKey(randomKey);
                
                // If it didn't throw, verify safety invariants:
                result.Should().StartWith("tenants/acme/", $"random key: {randomKey}");
                
                // MUST NOT contain .. segments
                var segments = result.Split('/', StringSplitOptions.RemoveEmptyEntries);
                segments.Should().NotContain("..", $"random key: {randomKey}");
                
                // Should not contain null bytes
                result.Should().NotContain("\x00", $"random key: {randomKey}");
            }
            catch (GenesisException ex)
            {
                // Expected for invalid keys
                ex.ErrorCode.Should().BeOneOf(
                    FileStorageConstants.FailureReasons.KeyPathTraversal,
                    FileStorageConstants.FailureReasons.InvalidKeyFormat,
                    FileStorageConstants.FailureReasons.TenantContextNotResolved);
            }
            catch (ArgumentException)
            {
                // Expected for empty/whitespace keys
            }
        }
    }

    [Fact]
    public void ResolveKey_With10LevelEncoding_RejectedForEncoding()
    {
        // Arrange
        var mockContext = CreateMockTenantContext("acme");
        var resolver = CreateResolver(mockContext.Object);

        // Encode "../" exactly 10 times (creates 10 encoding layers)
        var key = "../";
        for (int i = 0; i < 10; i++)
        {
            key = Uri.EscapeDataString(key);
        }
        
        // After 10 encodings: ../ → ..%2F → ..%252F → ..%25252F → ... (10 levels)
        // This requires 10 decode iterations to reach "../", which is within our limit
        // But we reject it because decoding changes the key (data integrity)

        // Act & Assert
        var act = () => resolver.ResolveKey(key);
        
        // Could fail for either reason depending on implementation:
        // 1. "Excessive encoding" if it hits iteration limit
        // 2. "contains URL-encoded sequences" if it stabilizes but has encoding
        act.Should().Throw<GenesisException>()
            .Where(ex => ex.ErrorCode == FileStorageConstants.FailureReasons.InvalidKeyFormat);
    }

    [Fact]
    public void ResolveKey_With11LevelEncoding_RejectedForExcessiveEncoding()
    {
        // Arrange
        var mockContext = CreateMockTenantContext("acme");
        var resolver = CreateResolver(mockContext.Object);

        // Encode "../" exactly 11 times (creates nested encoding)
        var key = "../";
        for (int i = 0; i < 11; i++)
        {
            key = Uri.EscapeDataString(key);
        }

        // Act & Assert
        // After 11 encodings, it won't stabilize within 10 iterations
        var act = () => resolver.ResolveKey(key);
        act.Should().Throw<GenesisException>()
            .Where(ex => ex.ErrorCode == FileStorageConstants.FailureReasons.InvalidKeyFormat);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CRITICAL TEST 20: Final Key Double-Check
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ResolveKey_FinalResultAlwaysStartsWithTenantPrefix()
    {
        // Arrange
        var mockContext = CreateMockTenantContext("acme");
        var resolver = CreateResolver(mockContext.Object);

        var testKeys = new[]
        {
            "documents/report.pdf",
            "a/b/c/d/e/f/g.txt",
            "file-with-many---dashes.doc",
            "deep/nested/folder/structure/file.pdf"
        };

        // Act & Assert
        foreach (var key in testKeys)
        {
            var result = resolver.ResolveKey(key);
            
            // MUST start with exact tenant prefix
            result.Should().StartWith("tenants/acme/");
            
            // MUST NOT contain .. after normalization
            result.Should().NotContain("..");
            
            // Split and verify no .. segments
            var segments = result.Split('/', StringSplitOptions.RemoveEmptyEntries);
            segments.Should().NotContain("..");
            segments[0].Should().Be("tenants");
            segments[1].Should().Be("acme");
        }
    }
}
