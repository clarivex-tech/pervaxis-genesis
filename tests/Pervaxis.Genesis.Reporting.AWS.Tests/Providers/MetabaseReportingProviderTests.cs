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

using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Contrib.HttpClient;
using Pervaxis.Genesis.Base.Exceptions;
using Pervaxis.Genesis.Reporting.AWS.Options;
using Pervaxis.Genesis.Reporting.AWS.Providers;

namespace Pervaxis.Genesis.Reporting.AWS.Tests.Providers;

public class MetabaseReportingProviderTests
{
    private readonly Mock<ILogger<MetabaseReportingProvider>> _mockLogger;
    private readonly Mock<HttpMessageHandler> _mockHttpHandler;
    private readonly ReportingOptions _options;

    public MetabaseReportingProviderTests()
    {
        _mockLogger = new Mock<ILogger<MetabaseReportingProvider>>();
        _mockHttpHandler = new Mock<HttpMessageHandler>();
        _options = new ReportingOptions
        {
            Region = "us-east-1",
            BaseUrl = "https://metabase.example.com",
            ApiKey = "mb_test_api_key_12345",
            DatabaseId = 1,
            RequestTimeoutSeconds = 30,
            MaxRetries = 3
        };
    }

    [Fact]
    public void Constructor_WithValidParameters_ShouldSucceed()
    {
        // Arrange
        var httpClient = _mockHttpHandler.CreateClient();
        httpClient.BaseAddress = new Uri(_options.BaseUrl);

        // Act
        var provider = new MetabaseReportingProvider(_options, httpClient, _mockLogger.Object, true);

        // Assert
        provider.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithNullOptions_ShouldThrowArgumentNullException()
    {
        // Arrange
        var httpClient = _mockHttpHandler.CreateClient();
        httpClient.BaseAddress = new Uri(_options.BaseUrl);

        // Act
        var act = () => new MetabaseReportingProvider(null!, httpClient, _mockLogger.Object, true);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("options");
    }

    [Fact]
    public void Constructor_WithNullHttpClient_ShouldThrowArgumentNullException()
    {
        // Act
        var act = () => new MetabaseReportingProvider(_options, null!, _mockLogger.Object, true);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("httpClient");
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Arrange
        var httpClient = _mockHttpHandler.CreateClient();
        httpClient.BaseAddress = new Uri(_options.BaseUrl);

        // Act
        var act = () => new MetabaseReportingProvider(_options, httpClient, null!, true);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    [Fact]
    public async Task ExecuteQueryAsync_WithValidQuery_ShouldReturnResults()
    {
        // Arrange
        var responseJson = """
        {
            "data": {
                "cols": [
                    { "name": "id", "display_name": "ID", "base_type": "type/Integer" },
                    { "name": "name", "display_name": "Name", "base_type": "type/Text" }
                ],
                "rows": [
                    [1, "Alice"],
                    [2, "Bob"]
                ]
            }
        }
        """;

        _mockHttpHandler
            .SetupRequest(HttpMethod.Post, "https://metabase.example.com/api/dataset")
            .ReturnsResponse(HttpStatusCode.OK, responseJson, "application/json");

        var httpClient = _mockHttpHandler.CreateClient();
        httpClient.BaseAddress = new Uri(_options.BaseUrl);
        var provider = new MetabaseReportingProvider(_options, httpClient, _mockLogger.Object, true);

        // Act
        var results = await provider.ExecuteQueryAsync<TestResult>("SELECT * FROM users");

        // Assert
        results.Should().NotBeNull();
        results.Should().HaveCount(2);
        var resultList = results.ToList();
        resultList[0].Id.Should().Be(1);
        resultList[0].Name.Should().Be("Alice");
        resultList[1].Id.Should().Be(2);
        resultList[1].Name.Should().Be("Bob");
    }

    [Fact]
    public async Task ExecuteQueryAsync_WithEmptyResults_ShouldReturnEmpty()
    {
        // Arrange
        var responseJson = """
        {
            "data": {
                "cols": [],
                "rows": []
            }
        }
        """;

        _mockHttpHandler
            .SetupRequest(HttpMethod.Post, "https://metabase.example.com/api/dataset")
            .ReturnsResponse(HttpStatusCode.OK, responseJson, "application/json");

        var httpClient = _mockHttpHandler.CreateClient();
        httpClient.BaseAddress = new Uri(_options.BaseUrl);
        var provider = new MetabaseReportingProvider(_options, httpClient, _mockLogger.Object, true);

        // Act
        var results = await provider.ExecuteQueryAsync<TestResult>("SELECT * FROM empty_table");

        // Assert
        results.Should().NotBeNull();
        results.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task ExecuteQueryAsync_WithInvalidQuery_ShouldThrowArgumentException(string? query)
    {
        // Arrange
        var httpClient = _mockHttpHandler.CreateClient();
        httpClient.BaseAddress = new Uri(_options.BaseUrl);
        var provider = new MetabaseReportingProvider(_options, httpClient, _mockLogger.Object, true);

        // Act
        var act = async () => await provider.ExecuteQueryAsync<TestResult>(query!);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ExecuteQueryAsync_WhenHttpFails_ShouldThrowGenesisException()
    {
        // Arrange
        _mockHttpHandler
            .SetupRequest(HttpMethod.Post, "https://metabase.example.com/api/dataset")
            .ReturnsResponse(HttpStatusCode.InternalServerError);

        var httpClient = _mockHttpHandler.CreateClient();
        httpClient.BaseAddress = new Uri(_options.BaseUrl);
        var provider = new MetabaseReportingProvider(_options, httpClient, _mockLogger.Object, true);

        // Act
        var act = async () => await provider.ExecuteQueryAsync<TestResult>("SELECT * FROM users");

        // Assert
        await act.Should().ThrowAsync<GenesisException>()
            .WithMessage("*Failed to execute query*");
    }

    [Fact]
    public async Task GetDashboardAsync_WithValidId_ShouldReturnDashboard()
    {
        // Arrange
        var responseJson = """
        {
            "id": 123,
            "name": "Sales Dashboard",
            "description": "Monthly sales metrics",
            "parameters": []
        }
        """;

        _mockHttpHandler
            .SetupRequest(HttpMethod.Get, "https://metabase.example.com/api/dashboard/123")
            .ReturnsResponse(HttpStatusCode.OK, responseJson, "application/json");

        var httpClient = _mockHttpHandler.CreateClient();
        httpClient.BaseAddress = new Uri(_options.BaseUrl);
        var provider = new MetabaseReportingProvider(_options, httpClient, _mockLogger.Object, true);

        // Act
        var dashboard = await provider.GetDashboardAsync("123");

        // Assert
        dashboard.Should().NotBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task GetDashboardAsync_WithInvalidId_ShouldThrowArgumentException(string? dashboardId)
    {
        // Arrange
        var httpClient = _mockHttpHandler.CreateClient();
        httpClient.BaseAddress = new Uri(_options.BaseUrl);
        var provider = new MetabaseReportingProvider(_options, httpClient, _mockLogger.Object, true);

        // Act
        var act = async () => await provider.GetDashboardAsync(dashboardId!);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetDashboardAsync_WhenNotFound_ShouldThrowGenesisException()
    {
        // Arrange
        _mockHttpHandler
            .SetupRequest(HttpMethod.Get, "https://metabase.example.com/api/dashboard/999")
            .ReturnsResponse(HttpStatusCode.NotFound);

        var httpClient = _mockHttpHandler.CreateClient();
        httpClient.BaseAddress = new Uri(_options.BaseUrl);
        var provider = new MetabaseReportingProvider(_options, httpClient, _mockLogger.Object, true);

        // Act
        var act = async () => await provider.GetDashboardAsync("999");

        // Assert
        await act.Should().ThrowAsync<GenesisException>()
            .WithMessage("*Failed to get dashboard*");
    }

    [Fact]
    public async Task CreateDashboardAsync_WithValidData_ShouldReturnDashboardId()
    {
        // Arrange
        var responseJson = """
        {
            "id": 456,
            "name": "New Dashboard"
        }
        """;

        _mockHttpHandler
            .SetupRequest(HttpMethod.Post, "https://metabase.example.com/api/dashboard")
            .ReturnsResponse(HttpStatusCode.OK, responseJson, "application/json");

        var httpClient = _mockHttpHandler.CreateClient();
        httpClient.BaseAddress = new Uri(_options.BaseUrl);
        var provider = new MetabaseReportingProvider(_options, httpClient, _mockLogger.Object, true);

        // Act
        var dashboardId = await provider.CreateDashboardAsync("New Dashboard", new { });

        // Assert
        dashboardId.Should().Be("456");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task CreateDashboardAsync_WithInvalidName_ShouldThrowArgumentException(string? name)
    {
        // Arrange
        var httpClient = _mockHttpHandler.CreateClient();
        httpClient.BaseAddress = new Uri(_options.BaseUrl);
        var provider = new MetabaseReportingProvider(_options, httpClient, _mockLogger.Object, true);

        // Act
        var act = async () => await provider.CreateDashboardAsync(name!, new { });

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task CreateDashboardAsync_WithNullDefinition_ShouldThrowArgumentNullException()
    {
        // Arrange
        var httpClient = _mockHttpHandler.CreateClient();
        httpClient.BaseAddress = new Uri(_options.BaseUrl);
        var provider = new MetabaseReportingProvider(_options, httpClient, _mockLogger.Object, true);

        // Act
        var act = async () => await provider.CreateDashboardAsync("Test Dashboard", null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task CreateDashboardAsync_WhenHttpFails_ShouldThrowGenesisException()
    {
        // Arrange
        _mockHttpHandler
            .SetupRequest(HttpMethod.Post, "https://metabase.example.com/api/dashboard")
            .ReturnsResponse(HttpStatusCode.BadRequest);

        var httpClient = _mockHttpHandler.CreateClient();
        httpClient.BaseAddress = new Uri(_options.BaseUrl);
        var provider = new MetabaseReportingProvider(_options, httpClient, _mockLogger.Object, true);

        // Act
        var act = async () => await provider.CreateDashboardAsync("Test Dashboard", new { });

        // Assert
        await act.Should().ThrowAsync<GenesisException>()
            .WithMessage("*Failed to create dashboard*");
    }

    [Fact]
    public async Task ExportReportAsync_WithCsvFormat_ShouldReturnData()
    {
        // Arrange
        var csvData = "id,name\n1,Alice\n2,Bob";
        var responseBytes = Encoding.UTF8.GetBytes(csvData);

        _mockHttpHandler
            .SetupRequest(HttpMethod.Post, "https://metabase.example.com/api/card/100/query/csv")
            .ReturnsResponse(HttpStatusCode.OK, new ByteArrayContent(responseBytes));

        var httpClient = _mockHttpHandler.CreateClient();
        httpClient.BaseAddress = new Uri(_options.BaseUrl);
        var provider = new MetabaseReportingProvider(_options, httpClient, _mockLogger.Object, true);

        // Act
        var data = await provider.ExportReportAsync("100", "csv");

        // Assert
        data.Should().NotBeNull();
        data.Should().BeEquivalentTo(responseBytes);
    }

    [Fact]
    public async Task ExportReportAsync_WithJsonFormat_ShouldReturnData()
    {
        // Arrange
        var jsonData = "[{\"id\":1,\"name\":\"Alice\"},{\"id\":2,\"name\":\"Bob\"}]";
        var responseBytes = Encoding.UTF8.GetBytes(jsonData);

        _mockHttpHandler
            .SetupRequest(HttpMethod.Post, "https://metabase.example.com/api/card/100/query/json")
            .ReturnsResponse(HttpStatusCode.OK, new ByteArrayContent(responseBytes));

        var httpClient = _mockHttpHandler.CreateClient();
        httpClient.BaseAddress = new Uri(_options.BaseUrl);
        var provider = new MetabaseReportingProvider(_options, httpClient, _mockLogger.Object, true);

        // Act
        var data = await provider.ExportReportAsync("100", "json");

        // Assert
        data.Should().NotBeNull();
        data.Should().BeEquivalentTo(responseBytes);
    }

    [Fact]
    public async Task ExportReportAsync_WithXlsxFormat_ShouldReturnData()
    {
        // Arrange
        var xlsxData = new byte[] { 0x50, 0x4B, 0x03, 0x04 }; // XLSX header

        _mockHttpHandler
            .SetupRequest(HttpMethod.Post, "https://metabase.example.com/api/card/100/query/xlsx")
            .ReturnsResponse(HttpStatusCode.OK, new ByteArrayContent(xlsxData));

        var httpClient = _mockHttpHandler.CreateClient();
        httpClient.BaseAddress = new Uri(_options.BaseUrl);
        var provider = new MetabaseReportingProvider(_options, httpClient, _mockLogger.Object, true);

        // Act
        var data = await provider.ExportReportAsync("100", "xlsx");

        // Assert
        data.Should().NotBeNull();
        data.Should().BeEquivalentTo(xlsxData);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task ExportReportAsync_WithInvalidReportId_ShouldThrowArgumentException(string? reportId)
    {
        // Arrange
        var httpClient = _mockHttpHandler.CreateClient();
        httpClient.BaseAddress = new Uri(_options.BaseUrl);
        var provider = new MetabaseReportingProvider(_options, httpClient, _mockLogger.Object, true);

        // Act
        var act = async () => await provider.ExportReportAsync(reportId!, "csv");

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task ExportReportAsync_WithInvalidFormat_ShouldThrowArgumentException(string? format)
    {
        // Arrange
        var httpClient = _mockHttpHandler.CreateClient();
        httpClient.BaseAddress = new Uri(_options.BaseUrl);
        var provider = new MetabaseReportingProvider(_options, httpClient, _mockLogger.Object, true);

        // Act
        var act = async () => await provider.ExportReportAsync("100", format!);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ExportReportAsync_WithUnsupportedFormat_ShouldThrowArgumentException()
    {
        // Arrange
        var httpClient = _mockHttpHandler.CreateClient();
        httpClient.BaseAddress = new Uri(_options.BaseUrl);
        var provider = new MetabaseReportingProvider(_options, httpClient, _mockLogger.Object, true);

        // Act
        var act = async () => await provider.ExportReportAsync("100", "pdf");

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Unsupported format*");
    }

    [Fact]
    public async Task ExportReportAsync_WhenHttpFails_ShouldThrowGenesisException()
    {
        // Arrange
        _mockHttpHandler
            .SetupRequest(HttpMethod.Post, "https://metabase.example.com/api/card/100/query/csv")
            .ReturnsResponse(HttpStatusCode.InternalServerError);

        var httpClient = _mockHttpHandler.CreateClient();
        httpClient.BaseAddress = new Uri(_options.BaseUrl);
        var provider = new MetabaseReportingProvider(_options, httpClient, _mockLogger.Object, true);

        // Act
        var act = async () => await provider.ExportReportAsync("100", "csv");

        // Assert
        await act.Should().ThrowAsync<GenesisException>()
            .WithMessage("*Failed to export report*");
    }

    [Fact]
    public void Dispose_ShouldDisposeHttpClient()
    {
        // Arrange
        var httpClient = _mockHttpHandler.CreateClient();
        httpClient.BaseAddress = new Uri(_options.BaseUrl);
        var provider = new MetabaseReportingProvider(_options, httpClient, _mockLogger.Object, true);

        // Act
        provider.Dispose();

        // Assert - Should not throw
    }

    [Fact]
    public void Dispose_CalledMultipleTimes_ShouldNotThrow()
    {
        // Arrange
        var httpClient = _mockHttpHandler.CreateClient();
        httpClient.BaseAddress = new Uri(_options.BaseUrl);
        var provider = new MetabaseReportingProvider(_options, httpClient, _mockLogger.Object, true);

        // Act
        provider.Dispose();
        provider.Dispose();
        provider.Dispose();

        // Assert - Should not throw
    }

    public class TestResult
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
