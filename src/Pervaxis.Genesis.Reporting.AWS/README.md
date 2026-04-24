# Pervaxis.Genesis.Reporting.AWS

Metabase reporting and analytics provider for the Pervaxis Genesis platform. Supports query execution, dashboard management, and report exports via the Metabase REST API.

## Features

- **Query Execution**: Run SQL queries and get typed results
- **Dashboard Management**: Retrieve and create dashboards
- **Report Exports**: Export reports to CSV, JSON, or XLSX formats
- **Type-Safe Results**: Automatic mapping of query results to .NET types
- **Async/Await**: Modern async API throughout
- **Comprehensive Logging**: Structured logging with Microsoft.Extensions.Logging
- **HTTP Client Factory**: Proper HttpClient management with IHttpClientFactory

## Installation

```bash
dotnet add package Pervaxis.Genesis.Reporting.AWS
```

## Configuration

### appsettings.json

```json
{
  "Reporting": {
    "Region": "us-east-1",
    "BaseUrl": "https://metabase.company.com",
    "ApiKey": "mb_your_api_key_here",
    "DatabaseId": 1,
    "RequestTimeoutSeconds": 30,
    "MaxRetries": 3
  }
}
```

### Dependency Injection

```csharp
using Pervaxis.Genesis.Reporting.AWS.Extensions;

// Option 1: From configuration
services.AddGenesisReporting(configuration.GetSection("Reporting"));

// Option 2: Inline configuration
services.AddGenesisReporting(options =>
{
    options.Region = "us-east-1";
    options.BaseUrl = "https://metabase.company.com";
    options.ApiKey = "mb_your_api_key_here";
    options.DatabaseId = 1;
    options.RequestTimeoutSeconds = 30;
});
```

## Usage

### Query Execution

```csharp
using Pervaxis.Core.Abstractions.Genesis.Modules;

public class AnalyticsService
{
    private readonly IReporting _reporting;

    public AnalyticsService(IReporting reporting)
    {
        _reporting = reporting;
    }

    public async Task<IEnumerable<SalesRecord>> GetMonthlySalesAsync()
    {
        var query = @"
            SELECT 
                DATE_TRUNC('month', order_date) AS month,
                SUM(total_amount) AS revenue,
                COUNT(*) AS order_count
            FROM orders
            WHERE order_date >= NOW() - INTERVAL '12 months'
            GROUP BY month
            ORDER BY month DESC
        ";

        var results = await _reporting.ExecuteQueryAsync<SalesRecord>(query);
        return results;
    }

    public async Task<IEnumerable<CustomerMetric>> GetTopCustomersAsync(int limit = 10)
    {
        var query = $@"
            SELECT 
                customer_id AS customerId,
                customer_name AS customerName,
                SUM(total_amount) AS totalSpent,
                COUNT(*) AS orderCount
            FROM orders
            GROUP BY customer_id, customer_name
            ORDER BY totalSpent DESC
            LIMIT {limit}
        ";

        var results = await _reporting.ExecuteQueryAsync<CustomerMetric>(query);
        return results;
    }
}

public class SalesRecord
{
    public DateTime Month { get; set; }
    public decimal Revenue { get; set; }
    public int OrderCount { get; set; }
}

public class CustomerMetric
{
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public decimal TotalSpent { get; set; }
    public int OrderCount { get; set; }
}
```

### Dashboard Management

```csharp
public class DashboardService
{
    private readonly IReporting _reporting;

    public DashboardService(IReporting reporting)
    {
        _reporting = reporting;
    }

    public async Task<object> GetExecutiveDashboardAsync()
    {
        var dashboardId = "42"; // Dashboard ID from Metabase
        var dashboard = await _reporting.GetDashboardAsync(dashboardId);
        return dashboard;
    }

    public async Task<string> CreateSalesDashboardAsync(string name)
    {
        var definition = new
        {
            description = "Monthly sales performance metrics",
            parameters = new[] 
            {
                new { name = "start_date", type = "date" },
                new { name = "end_date", type = "date" }
            }
        };

        var dashboardId = await _reporting.CreateDashboardAsync(name, definition);
        return dashboardId;
    }
}
```

### Report Exports

```csharp
public class ReportExportService
{
    private readonly IReporting _reporting;

    public ReportExportService(IReporting reporting)
    {
        _reporting = reporting;
    }

    public async Task<byte[]> ExportSalesReportToCsvAsync(string reportId)
    {
        var data = await _reporting.ExportReportAsync(reportId, "csv");
        return data;
    }

    public async Task ExportToFileAsync(string reportId, string format, string outputPath)
    {
        var data = await _reporting.ExportReportAsync(reportId, format);
        await File.WriteAllBytesAsync(outputPath, data);
    }

    public async Task<string> ExportAsJsonStringAsync(string reportId)
    {
        var data = await _reporting.ExportReportAsync(reportId, "json");
        return System.Text.Encoding.UTF8.GetString(data);
    }
}
```

## Configuration Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `Region` | string | (required) | AWS region for deployment context |
| `BaseUrl` | string | (required) | Metabase instance URL (http/https only) |
| `ApiKey` | string | (required) | API key for authentication |
| `DatabaseId` | int? | null | Default database ID for queries |
| `RequestTimeoutSeconds` | int | 30 | HTTP request timeout |
| `MaxRetries` | int | 3 | Retry attempts for transient failures |

## API Key Generation

### Creating an API Key in Metabase

1. **Admin Access Required**: Navigate to Metabase Admin panel
2. **Settings → Authentication**: Click on "API Keys" section
3. **Create Key**: Click "Create API Key"
4. **Configure**:
   - Name: `pervaxis-genesis-api`
   - Group: Select appropriate user group
   - Permissions: Ensure query execution permissions
5. **Save Key**: Copy the generated key (starts with `mb_`)

### Security Best Practices

```json
{
  "Reporting": {
    "ApiKey": "${METABASE_API_KEY}"
  }
}
```

Store API keys in:
- **Development**: User secrets (`dotnet user-secrets set "Reporting:ApiKey" "mb_..."`)
- **Production**: Azure Key Vault, AWS Secrets Manager, or environment variables

## Query Result Mapping

The provider automatically maps Metabase query results to .NET types:

```csharp
// Metabase query result:
// cols: [{ name: "user_id", ... }, { name: "user_name", ... }]
// rows: [[1, "Alice"], [2, "Bob"]]

public class User
{
    public int UserId { get; set; }  // Mapped from user_id (case-insensitive)
    public string UserName { get; set; } = string.Empty; // Mapped from user_name
}

var users = await _reporting.ExecuteQueryAsync<User>(query);
```

**Mapping Rules:**
- Property names match column names (case-insensitive)
- Automatic type conversion (int, decimal, string, DateTime, etc.)
- Unmapped columns are ignored
- Failed conversions skip the property

## Export Formats

### CSV Export
```csharp
var csv = await _reporting.ExportReportAsync("100", "csv");
// Returns: "id,name\n1,Alice\n2,Bob"
```

### JSON Export
```csharp
var json = await _reporting.ExportReportAsync("100", "json");
// Returns: [{"id":1,"name":"Alice"},{"id":2,"name":"Bob"}]
```

### Excel Export
```csharp
var xlsx = await _reporting.ExportReportAsync("100", "xlsx");
// Returns: Binary XLSX file data
await File.WriteAllBytesAsync("report.xlsx", xlsx);
```

**Supported Formats**: `csv`, `json`, `xlsx`

## Database Configuration

If you have multiple databases in Metabase:

```csharp
services.AddGenesisReporting(options =>
{
    options.BaseUrl = "https://metabase.company.com";
    options.ApiKey = "mb_...";
    options.DatabaseId = 5; // Use specific database by default
});
```

Or specify per-query (requires custom Metabase API integration):

```csharp
// Default implementation uses configured DatabaseId
// For per-query database selection, extend the provider
```

## Error Handling

All operations throw `GenesisException` on errors:

```csharp
try
{
    var results = await _reporting.ExecuteQueryAsync<SalesRecord>(query);
}
catch (GenesisException ex)
{
    _logger.LogError(ex, "Query execution failed: {Message}", ex.Message);
    // Handle error appropriately
}
```

**Common Error Scenarios:**
- Invalid API key: 401 Unauthorized
- Query timeout: Increase `RequestTimeoutSeconds`
- Invalid SQL: Check query syntax
- Dashboard not found: 404 Not Found

## Performance Considerations

### Query Optimization

```csharp
// ❌ Bad: Fetches all historical data
var query = "SELECT * FROM orders";

// ✅ Good: Limits time range and columns
var query = @"
    SELECT order_id, customer_id, total_amount, order_date
    FROM orders
    WHERE order_date >= NOW() - INTERVAL '30 days'
    LIMIT 1000
";
```

### Caching Results

```csharp
public class CachedReportingService
{
    private readonly IReporting _reporting;
    private readonly IMemoryCache _cache;

    public async Task<IEnumerable<T>> GetCachedResultsAsync<T>(
        string query,
        TimeSpan cacheDuration) where T : class
    {
        var cacheKey = $"query:{Convert.ToBase64String(Encoding.UTF8.GetBytes(query))}";

        if (!_cache.TryGetValue(cacheKey, out IEnumerable<T>? results))
        {
            results = await _reporting.ExecuteQueryAsync<T>(query);
            _cache.Set(cacheKey, results, cacheDuration);
        }

        return results!;
    }
}
```

### Async Pagination

```csharp
public async Task<IEnumerable<T>> GetPagedResultsAsync<T>(
    string baseQuery,
    int pageSize = 1000) where T : class
{
    var allResults = new List<T>();
    int offset = 0;

    while (true)
    {
        var query = $"{baseQuery} LIMIT {pageSize} OFFSET {offset}";
        var page = await _reporting.ExecuteQueryAsync<T>(query);
        var pageList = page.ToList();

        if (pageList.Count == 0)
        {
            break;
        }

        allResults.AddRange(pageList);
        offset += pageSize;

        if (pageList.Count < pageSize)
        {
            break; // Last page
        }
    }

    return allResults;
}
```

## Metabase REST API Endpoints

This provider uses the following Metabase API endpoints:

- `POST /api/dataset` - Execute native SQL queries
- `GET /api/dashboard/{id}` - Retrieve dashboard metadata
- `POST /api/dashboard` - Create new dashboard
- `POST /api/card/{id}/query/{format}` - Export report data

For full API documentation, see: https://www.metabase.com/docs/latest/api-documentation

## Deployment

### Docker Compose (Metabase + Application)

```yaml
version: '3.8'
services:
  metabase:
    image: metabase/metabase:latest
    ports:
      - "3000:3000"
    environment:
      MB_DB_TYPE: postgres
      MB_DB_DBNAME: metabase
      MB_DB_PORT: 5432
      MB_DB_USER: metabase
      MB_DB_PASS: metabase
      MB_DB_HOST: postgres
    depends_on:
      - postgres

  postgres:
    image: postgres:15
    environment:
      POSTGRES_DB: metabase
      POSTGRES_USER: metabase
      POSTGRES_PASSWORD: metabase
    volumes:
      - metabase-data:/var/lib/postgresql/data

  app:
    build: .
    environment:
      Reporting__BaseUrl: "http://metabase:3000"
      Reporting__ApiKey: "${METABASE_API_KEY}"
      Reporting__DatabaseId: "1"
    depends_on:
      - metabase

volumes:
  metabase-data:
```

### AWS Deployment

Metabase can be hosted on:
- **EC2**: Direct VM deployment
- **ECS Fargate**: Containerized deployment
- **RDS**: PostgreSQL backend for Metabase metadata

## Troubleshooting

### Connection Timeout
**Error**: `TaskCanceledException` or timeout errors
**Solution**: Increase `RequestTimeoutSeconds` for slow queries

```json
{
  "Reporting": {
    "RequestTimeoutSeconds": 60
  }
}
```

### Unauthorized (401)
**Error**: `401 Unauthorized`
**Solution**: Verify API key is valid and has correct permissions

```bash
# Test API key with curl
curl -H "X-API-KEY: mb_your_key" https://metabase.company.com/api/user/current
```

### Query Syntax Error
**Error**: SQL syntax error in query execution
**Solution**: Test query directly in Metabase UI first

### Empty Results
**Issue**: Query returns no data when data exists
**Solution**: Check database ID configuration and query filters

## Best Practices

### 1. Use Parameterized Queries

```csharp
// ❌ Avoid: String concatenation (SQL injection risk)
var query = $"SELECT * FROM users WHERE id = {userId}";

// ✅ Better: Use safe parameter substitution
var query = $"SELECT * FROM users WHERE id = {int.Parse(userId)}"; // Validate input

// ✅ Best: Use Metabase parameters feature
```

### 2. Limit Result Sets

```csharp
// Always include LIMIT clause for unbounded queries
var query = @"
    SELECT * FROM orders
    WHERE order_date >= '2024-01-01'
    LIMIT 10000
";
```

### 3. Cache Expensive Queries

```csharp
// Cache dashboard data that changes infrequently
var cacheKey = $"dashboard:{dashboardId}";
var cachedDashboard = await _cache.GetOrCreateAsync(cacheKey, async entry =>
{
    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15);
    return await _reporting.GetDashboardAsync(dashboardId);
});
```

### 4. Handle Large Exports

```csharp
// Stream large exports to disk
var reportData = await _reporting.ExportReportAsync("large-report", "csv");
await using var fileStream = new FileStream("report.csv", FileMode.Create);
await fileStream.WriteAsync(reportData);
```

### 5. Monitor Query Performance

```csharp
using var activity = Activity.StartActivity("ReportingQuery");
activity?.SetTag("query.type", "sales_report");

var stopwatch = Stopwatch.StartNew();
var results = await _reporting.ExecuteQueryAsync<SalesRecord>(query);
stopwatch.Stop();

_logger.LogInformation(
    "Query executed in {Duration}ms, returned {Count} rows",
    stopwatch.ElapsedMilliseconds, results.Count());
```

## License

Copyright (C) 2026 Clarivex Technologies Private Limited. All Rights Reserved.

## Support

- **Documentation**: https://clarivex.tech/docs/genesis/reporting
- **Issues**: https://github.com/clarivex/pervaxis-genesis/issues
- **Email**: support@clarivex.tech
