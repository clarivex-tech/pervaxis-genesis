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
using Microsoft.Extensions.Options;
using Moq;
using OpenSearch.Client;
using OpenSearch.Net;
using Pervaxis.Genesis.Base.Exceptions;
using Pervaxis.Genesis.Search.AWS.Options;
using Pervaxis.Genesis.Search.AWS.Providers.OpenSearch;

namespace Pervaxis.Genesis.Search.AWS.Tests;

public abstract class OpenSearchProviderTests
{
    protected static IOptions<SearchOptions> CreateValidOptions()
    {
        return Microsoft.Extensions.Options.Options.Create(new SearchOptions
        {
            Region = "us-east-1",
            DomainEndpoint = "https://test-domain.us-east-1.es.amazonaws.com",
            DefaultPageSize = 10,
            RequestTimeoutSeconds = 30,
            MaxRetries = 3
        });
    }

    protected static Mock<IOpenSearchClient> CreateMockClient()
    {
        return new Mock<IOpenSearchClient>();
    }

    public class Constructor : OpenSearchProviderTests
    {
        [Fact]
        public void NullOptions_ThrowsArgumentNullException()
        {
            // Arrange
            var logger = Mock.Of<ILogger<OpenSearchProvider>>();

            // Act & Assert
            var act = () => new OpenSearchProvider(null!, logger);
            act.Should().Throw<ArgumentNullException>().WithParameterName("options");
        }

        [Fact]
        public void NullLogger_ThrowsArgumentNullException()
        {
            // Arrange
            var options = CreateValidOptions();

            // Act & Assert
            var act = () => new OpenSearchProvider(options, null!);
            act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
        }

        [Fact]
        public void EmptyRegion_ThrowsArgumentException()
        {
            // Arrange
            var options = Microsoft.Extensions.Options.Options.Create(new SearchOptions
            {
                Region = "",
                DomainEndpoint = "https://test-domain.us-east-1.es.amazonaws.com"
            });
            var logger = Mock.Of<ILogger<OpenSearchProvider>>();

            // Act & Assert
            var act = () => new OpenSearchProvider(options, logger);
            act.Should().Throw<ArgumentException>();
        }

        [Fact]
        public void EmptyDomainEndpoint_ThrowsArgumentException()
        {
            // Arrange
            var options = Microsoft.Extensions.Options.Options.Create(new SearchOptions
            {
                Region = "us-east-1",
                DomainEndpoint = ""
            });
            var logger = Mock.Of<ILogger<OpenSearchProvider>>();

            // Act & Assert
            var act = () => new OpenSearchProvider(options, logger);
            act.Should().Throw<ArgumentException>();
        }

        [Fact]
        public void InvalidDomainEndpointUrl_ThrowsGenesisConfigurationException()
        {
            // Arrange
            var options = Microsoft.Extensions.Options.Options.Create(new SearchOptions
            {
                Region = "us-east-1",
                DomainEndpoint = "not-a-valid-url"
            });
            var logger = Mock.Of<ILogger<OpenSearchProvider>>();

            // Act & Assert
            var act = () => new OpenSearchProvider(options, logger);
            act.Should().Throw<GenesisConfigurationException>();
        }

        [Fact]
        public void NegativeDefaultPageSize_ThrowsGenesisConfigurationException()
        {
            // Arrange
            var options = Microsoft.Extensions.Options.Options.Create(new SearchOptions
            {
                Region = "us-east-1",
                DomainEndpoint = "https://test-domain.us-east-1.es.amazonaws.com",
                DefaultPageSize = -1
            });
            var logger = Mock.Of<ILogger<OpenSearchProvider>>();

            // Act & Assert
            var act = () => new OpenSearchProvider(options, logger);
            act.Should().Throw<GenesisConfigurationException>();
        }

        [Fact]
        public void NegativeRequestTimeout_ThrowsGenesisConfigurationException()
        {
            // Arrange
            var options = Microsoft.Extensions.Options.Options.Create(new SearchOptions
            {
                Region = "us-east-1",
                DomainEndpoint = "https://test-domain.us-east-1.es.amazonaws.com",
                RequestTimeoutSeconds = -1
            });
            var logger = Mock.Of<ILogger<OpenSearchProvider>>();

            // Act & Assert
            var act = () => new OpenSearchProvider(options, logger);
            act.Should().Throw<GenesisConfigurationException>();
        }

        [Fact]
        public void NegativeMaxRetries_ThrowsGenesisConfigurationException()
        {
            // Arrange
            var options = Microsoft.Extensions.Options.Options.Create(new SearchOptions
            {
                Region = "us-east-1",
                DomainEndpoint = "https://test-domain.us-east-1.es.amazonaws.com",
                MaxRetries = -1
            });
            var logger = Mock.Of<ILogger<OpenSearchProvider>>();

            // Act & Assert
            var act = () => new OpenSearchProvider(options, logger);
            act.Should().Throw<GenesisConfigurationException>();
        }

        [Fact]
        public void ValidOptions_CreatesProviderSuccessfully()
        {
            // Arrange
            var options = CreateValidOptions();
            var logger = Mock.Of<ILogger<OpenSearchProvider>>();

            // Act
            var provider = new OpenSearchProvider(options, logger);

            // Assert
            provider.Should().NotBeNull();
        }
    }

    public class IndexAsync : OpenSearchProviderTests
    {
        [Fact]
        public async Task NullIndex_ThrowsArgumentException()
        {
            // Arrange
            var options = CreateValidOptions();
            var logger = Mock.Of<ILogger<OpenSearchProvider>>();
            var client = CreateMockClient();
            var provider = new OpenSearchProvider(options, logger, client.Object);
            var document = new TestDocument { Id = "1", Name = "Test" };

            // Act & Assert
            var act = async () => await provider.IndexAsync(null!, "doc-1", document);
            await act.Should().ThrowAsync<ArgumentException>();
        }

        [Fact]
        public async Task EmptyIndex_ThrowsArgumentException()
        {
            // Arrange
            var options = CreateValidOptions();
            var logger = Mock.Of<ILogger<OpenSearchProvider>>();
            var client = CreateMockClient();
            var provider = new OpenSearchProvider(options, logger, client.Object);
            var document = new TestDocument { Id = "1", Name = "Test" };

            // Act & Assert
            var act = async () => await provider.IndexAsync("", "doc-1", document);
            await act.Should().ThrowAsync<ArgumentException>();
        }

        [Fact]
        public async Task NullId_ThrowsArgumentException()
        {
            // Arrange
            var options = CreateValidOptions();
            var logger = Mock.Of<ILogger<OpenSearchProvider>>();
            var client = CreateMockClient();
            var provider = new OpenSearchProvider(options, logger, client.Object);
            var document = new TestDocument { Id = "1", Name = "Test" };

            // Act & Assert
            var act = async () => await provider.IndexAsync("test-index", null!, document);
            await act.Should().ThrowAsync<ArgumentException>();
        }

        [Fact]
        public async Task NullDocument_ThrowsArgumentNullException()
        {
            // Arrange
            var options = CreateValidOptions();
            var logger = Mock.Of<ILogger<OpenSearchProvider>>();
            var client = CreateMockClient();
            var provider = new OpenSearchProvider(options, logger, client.Object);

            // Act & Assert
            var act = async () => await provider.IndexAsync<TestDocument>("test-index", "doc-1", null!);
            await act.Should().ThrowAsync<ArgumentNullException>();
        }

        [Fact]
        public async Task ValidRequest_CallsClientIndexAsync()
        {
            // Arrange
            var options = CreateValidOptions();
            var logger = Mock.Of<ILogger<OpenSearchProvider>>();
            var client = CreateMockClient();
            var document = new TestDocument { Id = "1", Name = "Test" };

            var mockResponse = new Mock<IndexResponse>();
            mockResponse.Setup(r => r.IsValid).Returns(true);

            client.Setup(c => c.IndexAsync(
                It.IsAny<TestDocument>(),
                It.IsAny<Func<IndexDescriptor<TestDocument>, IIndexRequest<TestDocument>>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockResponse.Object);

            var provider = new OpenSearchProvider(options, logger, client.Object);

            // Act
            var result = await provider.IndexAsync("test-index", "doc-1", document);

            // Assert
            result.Should().BeTrue();
            client.Verify(c => c.IndexAsync(
                document,
                It.IsAny<Func<IndexDescriptor<TestDocument>, IIndexRequest<TestDocument>>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task WithIndexPrefix_AppliesPrefix()
        {
            // Arrange
            var options = Microsoft.Extensions.Options.Options.Create(new SearchOptions
            {
                Region = "us-east-1",
                DomainEndpoint = "https://test-domain.us-east-1.es.amazonaws.com",
                IndexPrefix = "prod-"
            });
            var logger = Mock.Of<ILogger<OpenSearchProvider>>();
            var client = CreateMockClient();
            var document = new TestDocument { Id = "1", Name = "Test" };

            var mockResponse = new Mock<IndexResponse>();
            mockResponse.Setup(r => r.IsValid).Returns(true);

            string? capturedIndex = null;
            client.Setup(c => c.IndexAsync(
                It.IsAny<TestDocument>(),
                It.IsAny<Func<IndexDescriptor<TestDocument>, IIndexRequest<TestDocument>>>(),
                It.IsAny<CancellationToken>()))
                .Callback<TestDocument, Func<IndexDescriptor<TestDocument>, IIndexRequest<TestDocument>>, CancellationToken>(
                    (doc, func, ct) =>
                    {
                        var descriptor = new IndexDescriptor<TestDocument>(doc);
                        var request = func(descriptor);
                        capturedIndex = request.Index?.Name;
                    })
                .ReturnsAsync(mockResponse.Object);

            var provider = new OpenSearchProvider(options, logger, client.Object);

            // Act
            await provider.IndexAsync("test-index", "doc-1", document);

            // Assert
            capturedIndex.Should().Be("prod-test-index");
        }

    }

    public class SearchAsync : OpenSearchProviderTests
    {
        [Fact]
        public async Task NullIndex_ThrowsArgumentException()
        {
            // Arrange
            var options = CreateValidOptions();
            var logger = Mock.Of<ILogger<OpenSearchProvider>>();
            var client = CreateMockClient();
            var provider = new OpenSearchProvider(options, logger, client.Object);

            // Act & Assert
            var act = async () => await provider.SearchAsync<TestDocument>(null!, "test query");
            await act.Should().ThrowAsync<ArgumentException>();
        }

        [Fact]
        public async Task EmptyIndex_ThrowsArgumentException()
        {
            // Arrange
            var options = CreateValidOptions();
            var logger = Mock.Of<ILogger<OpenSearchProvider>>();
            var client = CreateMockClient();
            var provider = new OpenSearchProvider(options, logger, client.Object);

            // Act & Assert
            var act = async () => await provider.SearchAsync<TestDocument>("", "test query");
            await act.Should().ThrowAsync<ArgumentException>();
        }

        [Fact]
        public async Task NullQuery_ThrowsArgumentException()
        {
            // Arrange
            var options = CreateValidOptions();
            var logger = Mock.Of<ILogger<OpenSearchProvider>>();
            var client = CreateMockClient();
            var provider = new OpenSearchProvider(options, logger, client.Object);

            // Act & Assert
            var act = async () => await provider.SearchAsync<TestDocument>("test-index", null!);
            await act.Should().ThrowAsync<ArgumentException>();
        }

        [Fact]
        public async Task EmptyQuery_ThrowsArgumentException()
        {
            // Arrange
            var options = CreateValidOptions();
            var logger = Mock.Of<ILogger<OpenSearchProvider>>();
            var client = CreateMockClient();
            var provider = new OpenSearchProvider(options, logger, client.Object);

            // Act & Assert
            var act = async () => await provider.SearchAsync<TestDocument>("test-index", "");
            await act.Should().ThrowAsync<ArgumentException>();
        }

        [Fact]
        public async Task ValidRequest_ReturnsDocuments()
        {
            // Arrange
            var options = CreateValidOptions();
            var logger = Mock.Of<ILogger<OpenSearchProvider>>();
            var client = CreateMockClient();

            var expectedDocuments = new List<TestDocument>
            {
                new() { Id = "1", Name = "Test 1" },
                new() { Id = "2", Name = "Test 2" }
            };

            var mockResponse = new Mock<ISearchResponse<TestDocument>>();
            mockResponse.Setup(r => r.IsValid).Returns(true);
            mockResponse.Setup(r => r.Documents).Returns(expectedDocuments);

            client.Setup(c => c.SearchAsync<TestDocument>(
                It.IsAny<Func<SearchDescriptor<TestDocument>, ISearchRequest>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockResponse.Object);

            var provider = new OpenSearchProvider(options, logger, client.Object);

            // Act
            var results = await provider.SearchAsync<TestDocument>("test-index", "test query");

            // Assert
            results.Should().HaveCount(2);
            results.Should().BeEquivalentTo(expectedDocuments);
        }

    }

    public class DeleteAsync : OpenSearchProviderTests
    {
        [Fact]
        public async Task NullIndex_ThrowsArgumentException()
        {
            // Arrange
            var options = CreateValidOptions();
            var logger = Mock.Of<ILogger<OpenSearchProvider>>();
            var client = CreateMockClient();
            var provider = new OpenSearchProvider(options, logger, client.Object);

            // Act & Assert
            var act = async () => await provider.DeleteAsync(null!, "doc-1");
            await act.Should().ThrowAsync<ArgumentException>();
        }

        [Fact]
        public async Task EmptyIndex_ThrowsArgumentException()
        {
            // Arrange
            var options = CreateValidOptions();
            var logger = Mock.Of<ILogger<OpenSearchProvider>>();
            var client = CreateMockClient();
            var provider = new OpenSearchProvider(options, logger, client.Object);

            // Act & Assert
            var act = async () => await provider.DeleteAsync("", "doc-1");
            await act.Should().ThrowAsync<ArgumentException>();
        }

        [Fact]
        public async Task NullId_ThrowsArgumentException()
        {
            // Arrange
            var options = CreateValidOptions();
            var logger = Mock.Of<ILogger<OpenSearchProvider>>();
            var client = CreateMockClient();
            var provider = new OpenSearchProvider(options, logger, client.Object);

            // Act & Assert
            var act = async () => await provider.DeleteAsync("test-index", null!);
            await act.Should().ThrowAsync<ArgumentException>();
        }

    }

    public class BulkIndexAsync : OpenSearchProviderTests
    {
        [Fact]
        public async Task NullIndex_ThrowsArgumentException()
        {
            // Arrange
            var options = CreateValidOptions();
            var logger = Mock.Of<ILogger<OpenSearchProvider>>();
            var client = CreateMockClient();
            var provider = new OpenSearchProvider(options, logger, client.Object);
            var documents = new Dictionary<string, TestDocument>
            {
                ["doc-1"] = new TestDocument { Id = "1", Name = "Test 1" }
            };

            // Act & Assert
            var act = async () => await provider.BulkIndexAsync(null!, documents);
            await act.Should().ThrowAsync<ArgumentException>();
        }

        [Fact]
        public async Task EmptyIndex_ThrowsArgumentException()
        {
            // Arrange
            var options = CreateValidOptions();
            var logger = Mock.Of<ILogger<OpenSearchProvider>>();
            var client = CreateMockClient();
            var provider = new OpenSearchProvider(options, logger, client.Object);
            var documents = new Dictionary<string, TestDocument>
            {
                ["doc-1"] = new TestDocument { Id = "1", Name = "Test 1" }
            };

            // Act & Assert
            var act = async () => await provider.BulkIndexAsync("", documents);
            await act.Should().ThrowAsync<ArgumentException>();
        }

        [Fact]
        public async Task NullDocuments_ThrowsArgumentNullException()
        {
            // Arrange
            var options = CreateValidOptions();
            var logger = Mock.Of<ILogger<OpenSearchProvider>>();
            var client = CreateMockClient();
            var provider = new OpenSearchProvider(options, logger, client.Object);

            // Act & Assert
            var act = async () => await provider.BulkIndexAsync<TestDocument>("test-index", null!);
            await act.Should().ThrowAsync<ArgumentNullException>();
        }

        [Fact]
        public async Task EmptyDocuments_ReturnsZero()
        {
            // Arrange
            var options = CreateValidOptions();
            var logger = Mock.Of<ILogger<OpenSearchProvider>>();
            var client = CreateMockClient();
            var provider = new OpenSearchProvider(options, logger, client.Object);
            var documents = new Dictionary<string, TestDocument>();

            // Act
            var result = await provider.BulkIndexAsync("test-index", documents);

            // Assert
            result.Should().Be(0);
        }

        [Fact]
        public async Task ValidRequest_CallsBulkAsync()
        {
            // Arrange
            var options = CreateValidOptions();
            var logger = Mock.Of<ILogger<OpenSearchProvider>>();
            var client = CreateMockClient();
            var documents = new Dictionary<string, TestDocument>
            {
                ["doc-1"] = new TestDocument { Id = "1", Name = "Test 1" },
                ["doc-2"] = new TestDocument { Id = "2", Name = "Test 2" }
            };

            var mockResponse = new Mock<BulkResponse>();
            mockResponse.Setup(r => r.IsValid).Returns(true);

            client.Setup(c => c.BulkAsync(
                It.IsAny<BulkDescriptor>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockResponse.Object);

            var provider = new OpenSearchProvider(options, logger, client.Object);

            // Act
            await provider.BulkIndexAsync("test-index", documents);

            // Assert
            client.Verify(c => c.BulkAsync(
                It.IsAny<BulkDescriptor>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

    }

    public class Dispose : OpenSearchProviderTests
    {
        [Fact]
        public void Dispose_CompletesSuccessfully()
        {
            // Arrange
            var options = CreateValidOptions();
            var logger = Mock.Of<ILogger<OpenSearchProvider>>();
            var provider = new OpenSearchProvider(options, logger);

            // Act
            var act = () => provider.Dispose();

            // Assert
            act.Should().NotThrow();
        }

        [Fact]
        public void DisposeTwice_DoesNotThrow()
        {
            // Arrange
            var options = CreateValidOptions();
            var logger = Mock.Of<ILogger<OpenSearchProvider>>();
            var provider = new OpenSearchProvider(options, logger);

            // Act
            provider.Dispose();
            var act = () => provider.Dispose();

            // Assert
            act.Should().NotThrow();
        }
    }

    public sealed class TestDocument
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }
}
