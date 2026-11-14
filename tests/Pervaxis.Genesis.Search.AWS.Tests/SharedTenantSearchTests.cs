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

using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenSearch.Client;
using OpenSearch.Net;
using Pervaxis.Core.Abstractions.MultiTenancy;
using Pervaxis.Genesis.Base.Exceptions;
using Pervaxis.Genesis.Search.AWS.Options;
using Pervaxis.Genesis.Search.AWS.Providers.OpenSearch;

namespace Pervaxis.Genesis.Search.AWS.Tests;

public sealed class SharedTenantSearchTests
{
    private static Mock<ITenantContext> Context(Func<string> tenant)
    {
        var context = new Mock<ITenantContext>();
        context.SetupGet(c => c.IsResolved).Returns(true);
        context.SetupGet(c => c.TenantId).Returns(() => new TenantId(tenant()));
        return context;
    }

    private static OpenSearchProvider Provider(IOpenSearchClient client, ITenantContext? tenant)
        => new(Microsoft.Extensions.Options.Options.Create(new SearchOptions
        {
            Region = "us-east-1", DomainEndpoint = "http://localhost:9200",
            IndexPrefix = "prod-", EnableTenantIsolation = false,
            Resilience = new() { Enabled = false }
        }), NullLogger<OpenSearchProvider>.Instance, client, tenant);

    private static IOpenSearchClient Client(string response, Action<IApiCallDetails> capture)
        => new OpenSearchClient(new ConnectionSettings(
            new SingleNodeConnectionPool(new Uri("http://localhost:9200")),
            new InMemoryConnection(Encoding.UTF8.GetBytes(response)))
            .DisableDirectStreaming().OnRequestCompleted(capture));

    [Theory]
    [InlineData("acme")]
    [InlineData("other")]
    public async Task Index_StampsTenantAndNamespacesId_InSameSharedIndex(string tenant)
    {
        IApiCallDetails? call = null;
        var client = Client("{\"_index\":\"prod-products\",\"_id\":\"1\",\"result\":\"created\",\"_shards\":{\"successful\":1}}", c => call = c);
        using var provider = Provider(client, Context(() => tenant).Object);
        Assert.True(await provider.IndexAsync("products", "same-id", new { Name = "Coffee", Nested = new { Count = 3 } }));
        Assert.Contains("/prod-products/_doc/" + tenant + "%3Asame-id", call!.Uri.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
        using var json = JsonDocument.Parse(call.RequestBodyInBytes);
        Assert.Equal(tenant, json.RootElement.GetProperty("tenantId").GetString());
        Assert.Equal("Coffee", json.RootElement.GetProperty("name").GetString());
        Assert.Equal(3, json.RootElement.GetProperty("nested").GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task Search_UserQueryCannotReplaceMandatoryTenantFilter()
    {
        IApiCallDetails? call = null;
        var client = Client("{\"hits\":{\"total\":{\"value\":0,\"relation\":\"eq\"},\"hits\":[]}}", c => call = c);
        using var provider = Provider(client, Context(() => "acme").Object);
        await provider.SearchAsync<object>("products", "* OR tenantId:other");
        using var json = JsonDocument.Parse(call!.RequestBodyInBytes);
        var boolean = json.RootElement.GetProperty("query").GetProperty("bool");
        Assert.Equal("acme", boolean.GetProperty("filter")[0].GetProperty("term").GetProperty("tenantId.keyword").GetProperty("value").GetString());
        Assert.Equal("* OR tenantId:other", boolean.GetProperty("must")[0].GetProperty("query_string").GetProperty("query").GetString());
    }

    [Theory]
    [InlineData("acme")]
    [InlineData("other")]
    public async Task Delete_UsesTenantIdEvenForForeignLookingLogicalId(string tenant)
    {
        IApiCallDetails? call = null;
        var client = Client("{\"result\":\"deleted\",\"_shards\":{\"successful\":1}}", c => call = c);
        using var provider = Provider(client, Context(() => tenant).Object);
        await provider.DeleteAsync("products", "other:same-id");
        Assert.Contains(tenant + "%3Aother%3Asame-id", call!.Uri.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MissingTenant_BlocksEveryOperationIncludingEmptyBulk_DespiteLegacyFlag()
    {
        var calls = 0;
        var client = Client("{}", _ => calls++);
        using var provider = Provider(client, null);
        await Assert.ThrowsAsync<GenesisException>(() => provider.IndexAsync("products", "1", new { Name = "Test" }));
        await Assert.ThrowsAsync<GenesisException>(() => provider.SearchAsync<object>("products", "*"));
        await Assert.ThrowsAsync<GenesisException>(() => provider.DeleteAsync("products", "1"));
        await Assert.ThrowsAsync<GenesisException>(() => provider.BulkIndexAsync("products", new Dictionary<string, object>()));
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData("Invalid_Tenant!")]
    [InlineData("ab")]
    [InlineData("platform")]
    [InlineData("acme--corp")]
    [InlineData("acme\n")]
    public async Task InvalidTenant_BlocksEveryOperation(string tenant)
    {
        var calls = 0;
        using var provider = Provider(Client("{}", _ => calls++), Context(() => tenant).Object);
        await Assert.ThrowsAsync<GenesisException>(() => provider.IndexAsync("products", "1", new { Name = "Test" }));
        await Assert.ThrowsAsync<GenesisException>(() => provider.SearchAsync<object>("products", "*"));
        await Assert.ThrowsAsync<GenesisException>(() => provider.DeleteAsync("products", "1"));
        await Assert.ThrowsAsync<GenesisException>(() => provider.BulkIndexAsync("products", new Dictionary<string, object>()));
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task UnresolvedTenant_BlocksEveryOperation()
    {
        var context = Context(() => "acme");
        context.SetupGet(c => c.IsResolved).Returns(false);
        var calls = 0;
        using var provider = Provider(Client("{}", _ => calls++), context.Object);
        await Assert.ThrowsAsync<GenesisException>(() => provider.IndexAsync("products", "1", new { Name = "Test" }));
        await Assert.ThrowsAsync<GenesisException>(() => provider.SearchAsync<object>("products", "*"));
        await Assert.ThrowsAsync<GenesisException>(() => provider.DeleteAsync("products", "1"));
        await Assert.ThrowsAsync<GenesisException>(() => provider.BulkIndexAsync("products", new Dictionary<string, object>()));
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData("tenantId")]
    [InlineData("TenantId")]
    [InlineData("tenantId.keyword")]
    public async Task CallerCannotSupplyTenantStamp(string field)
    {
        var calls = 0;
        var client = Client("{}", _ => calls++);
        using var provider = Provider(client, Context(() => "acme").Object);
        var doc = new Dictionary<string, object> { [field] = "other" };
        await Assert.ThrowsAsync<GenesisException>(() => provider.IndexAsync("products", "1", doc));
        await Assert.ThrowsAsync<GenesisException>(() => provider.BulkIndexAsync("products", new Dictionary<string, Dictionary<string, object>> { ["1"] = doc }));
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Bulk_StampsEachDocumentAndNamespacesEveryId()
    {
        IApiCallDetails? call = null;
        var client = Client("{\"errors\":false,\"items\":[]}", c => call = c);
        using var provider = Provider(client, Context(() => "acme").Object);
        await provider.BulkIndexAsync("products", new Dictionary<string, object> { ["1"] = new { Name = "One" }, ["2"] = new { Name = "Two" } });
        var lines = Encoding.UTF8.GetString(call!.RequestBodyInBytes).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(4, lines.Length);
        for (var i = 0; i < 4; i += 2)
        {
            using var metadata = JsonDocument.Parse(lines[i]);
            using var document = JsonDocument.Parse(lines[i + 1]);
            Assert.Equal("prod-products", metadata.RootElement.GetProperty("index").GetProperty("_index").GetString());
            Assert.StartsWith("acme:", metadata.RootElement.GetProperty("index").GetProperty("_id").GetString(), StringComparison.Ordinal);
            Assert.Equal("acme", document.RootElement.GetProperty("tenantId").GetString());
        }
    }

    [Theory]
    [InlineData("*")]
    [InlineData("products,other")]
    [InlineData("../other")]
    public async Task MultiIndexTargetsAreRejected(string index)
    {
        var calls = 0;
        var client = Client("{}", _ => calls++);
        using var provider = Provider(client, Context(() => "acme").Object);
        await Assert.ThrowsAsync<GenesisException>(() => provider.SearchAsync<object>(index, "*"));
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task ScopeCannotSwitchTenant()
    {
        var tenant = "acme";
        var calls = 0;
        using var provider = Provider(Client("{}", _ => calls++), Context(() => tenant).Object);
        await provider.BulkIndexAsync("products", new Dictionary<string, object>());
        tenant = "other";
        await Assert.ThrowsAsync<GenesisException>(() => provider.IndexAsync("products", "1", new { Name = "Test" }));
        await Assert.ThrowsAsync<GenesisException>(() => provider.SearchAsync<object>("products", "*"));
        await Assert.ThrowsAsync<GenesisException>(() => provider.DeleteAsync("products", "1"));
        await Assert.ThrowsAsync<GenesisException>(() => provider.BulkIndexAsync("products", new Dictionary<string, object>()));
        Assert.Equal(0, calls);
    }
}
