# Genesis Provider Integration Guide

This guide shows how to integrate and use all 8 Genesis AWS providers in your microservice.

**Genesis provides infrastructure concerns** - caching, messaging, storage, search, notifications, workflows, AI, and reporting. Your code focuses on business logic while Genesis handles AWS connectivity, resilience, and observability.

---

## Table of Contents

1. [Caching (ElastiCache)](#1-caching-elasticache)
2. [Messaging (SQS/SNS)](#2-messaging-sqssns)
3. [File Storage (S3)](#3-file-storage-s3)
4. [Search (OpenSearch)](#4-search-opensearch)
5. [Notifications (SES/SNS)](#5-notifications-sessns)
6. [Workflow (Step Functions)](#6-workflow-step-functions)
7. [AI Assistance (Bedrock)](#7-ai-assistance-bedrock)
8. [Reporting (Metabase)](#8-reporting-metabase)

---

## 1. Caching (ElastiCache)

**Use for:** Session storage, API response caching, rate limiting, temporary data.

### Configuration

```json
{
  "Genesis": {
    "Caching": {
      "UseLocalStack": true,
      "LocalStackUrl": "http://localhost:4566",
      "Region": "us-east-1",
      "ConnectionString": "localhost:6379",
      "KeyPrefix": "myservice",
      "EnableTenantIsolation": true,
      "DefaultExpiration": "01:00:00"
    }
  }
}
```

### Dependency Injection

```csharp
// Program.cs
using Pervaxis.Genesis.Caching.AWS.Extensions;

builder.Services.AddGenesisCaching(
    builder.Configuration.GetSection("Genesis:Caching"));
```

### NuGet Package

```xml
<PackageReference Include="Pervaxis.Genesis.Caching.AWS" Version="1.0.0" />
```

### Usage Example

```csharp
using Pervaxis.Core.Abstractions.Genesis.Modules;

public class ProductService
{
    private readonly ICache _cache;
    private readonly ILogger<ProductService> _logger;

    public ProductService(ICache cache, ILogger<ProductService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task<Product?> GetProductAsync(string productId, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"product:{productId}";

        // Try cache first
        var cached = await _cache.GetAsync<Product>(cacheKey, cancellationToken);
        if (cached.IsSuccess && cached.Data != null)
        {
            _logger.LogInformation("Cache hit for product {ProductId}", productId);
            return cached.Data;
        }

        // Cache miss - fetch from database
        _logger.LogInformation("Cache miss for product {ProductId}, fetching from database", productId);
        var product = await FetchFromDatabaseAsync(productId, cancellationToken);

        if (product != null)
        {
            // Cache for 1 hour
            await _cache.SetAsync(cacheKey, product, TimeSpan.FromHours(1), cancellationToken);
        }

        return product;
    }

    public async Task InvalidateProductCacheAsync(string productId, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"product:{productId}";
        await _cache.RemoveAsync(cacheKey, cancellationToken);
        _logger.LogInformation("Invalidated cache for product {ProductId}", productId);
    }

    private async Task<Product?> FetchFromDatabaseAsync(string productId, CancellationToken cancellationToken)
    {
        // Your database logic here
        await Task.Delay(100, cancellationToken); // Simulate DB call
        return new Product { Id = productId, Name = "Sample Product", Price = 99.99m };
    }
}

public record Product
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public decimal Price { get; init; }
}
```

### Testing

```csharp
using NSubstitute;
using Pervaxis.Core.Abstractions.Genesis.Modules;
using Pervaxis.Genesis.Base.Result;

public class ProductServiceTests
{
    [Fact]
    public async Task GetProductAsync_CacheHit_ReturnsCachedProduct()
    {
        // Arrange
        var mockCache = Substitute.For<ICache>();
        var mockLogger = Substitute.For<ILogger<ProductService>>();
        
        var cachedProduct = new Product { Id = "123", Name = "Cached Product", Price = 49.99m };
        mockCache.GetAsync<Product>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ProviderResult<Product>.Success(cachedProduct));

        var service = new ProductService(mockCache, mockLogger);

        // Act
        var result = await service.GetProductAsync("123");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Cached Product", result.Name);
        await mockCache.Received(1).GetAsync<Product>(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetProductAsync_CacheMiss_FetchesAndCaches()
    {
        // Arrange
        var mockCache = Substitute.For<ICache>();
        var mockLogger = Substitute.For<ILogger<ProductService>>();
        
        mockCache.GetAsync<Product>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ProviderResult<Product>.Success(null));

        var service = new ProductService(mockCache, mockLogger);

        // Act
        var result = await service.GetProductAsync("123");

        // Assert
        Assert.NotNull(result);
        await mockCache.Received(1).SetAsync(
            Arg.Any<string>(), 
            Arg.Any<Product>(), 
            Arg.Any<TimeSpan>(), 
            Arg.Any<CancellationToken>());
    }
}
```

---

## 2. Messaging (SQS/SNS)

**Use for:** Event-driven architecture, asynchronous processing, decoupled communication between services.

### Configuration

```json
{
  "Genesis": {
    "Messaging": {
      "UseLocalStack": true,
      "LocalStackUrl": "http://localhost:4566",
      "Region": "us-east-1",
      "QueueUrl": "http://localhost:4566/000000000000/orders-queue",
      "TopicArn": "arn:aws:sns:us-east-1:000000000000:orders-topic",
      "EnableTenantIsolation": true,
      "MessageRetentionPeriod": 345600,
      "VisibilityTimeout": 30
    }
  }
}
```

### Dependency Injection

```csharp
// Program.cs
using Pervaxis.Genesis.Messaging.AWS.Extensions;

builder.Services.AddGenesisMessaging(
    builder.Configuration.GetSection("Genesis:Messaging"));
```

### NuGet Package

```xml
<PackageReference Include="Pervaxis.Genesis.Messaging.AWS" Version="1.0.0" />
```

### Usage Example

```csharp
using Pervaxis.Core.Abstractions.Genesis.Modules;

public class OrderService
{
    private readonly IMessaging _messaging;
    private readonly ILogger<OrderService> _logger;

    public OrderService(IMessaging messaging, ILogger<OrderService> logger)
    {
        _messaging = messaging;
        _logger = logger;
    }

    // Publishing events
    public async Task CreateOrderAsync(Order order, CancellationToken cancellationToken = default)
    {
        // Save order to database (not shown)
        
        // Publish event for other services
        var orderCreatedEvent = new OrderCreatedEvent
        {
            OrderId = order.Id,
            CustomerId = order.CustomerId,
            TotalAmount = order.TotalAmount,
            CreatedAt = DateTime.UtcNow
        };

        var result = await _messaging.PublishAsync(
            "order-created", 
            orderCreatedEvent, 
            cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Published OrderCreated event for order {OrderId}", order.Id);
        }
        else
        {
            _logger.LogError("Failed to publish OrderCreated event: {Error}", result.ErrorMessage);
        }
    }

    // Consuming messages
    public async Task ProcessOrderMessagesAsync(CancellationToken cancellationToken = default)
    {
        var result = await _messaging.ReceiveAsync<OrderCreatedEvent>(10, 30, cancellationToken);

        if (result.IsSuccess && result.Data.Any())
        {
            foreach (var message in result.Data)
            {
                _logger.LogInformation("Processing order {OrderId}", message.Content.OrderId);
                
                // Process the order
                await ProcessOrderAsync(message.Content, cancellationToken);

                // Delete message after successful processing
                await _messaging.DeleteAsync(message.MessageId, cancellationToken);
            }
        }
    }

    private async Task ProcessOrderAsync(OrderCreatedEvent orderEvent, CancellationToken cancellationToken)
    {
        // Your business logic here
        await Task.Delay(100, cancellationToken);
        _logger.LogInformation("Order {OrderId} processed successfully", orderEvent.OrderId);
    }
}

public record Order
{
    public string Id { get; init; } = string.Empty;
    public string CustomerId { get; init; } = string.Empty;
    public decimal TotalAmount { get; init; }
}

public record OrderCreatedEvent
{
    public string OrderId { get; init; } = string.Empty;
    public string CustomerId { get; init; } = string.Empty;
    public decimal TotalAmount { get; init; }
    public DateTime CreatedAt { get; init; }
}
```

### Testing

```csharp
using NSubstitute;
using Pervaxis.Core.Abstractions.Genesis.Modules;
using Pervaxis.Genesis.Base.Result;

public class OrderServiceTests
{
    [Fact]
    public async Task CreateOrderAsync_PublishesEvent()
    {
        // Arrange
        var mockMessaging = Substitute.For<IMessaging>();
        var mockLogger = Substitute.For<ILogger<OrderService>>();
        
        mockMessaging.PublishAsync(
                Arg.Any<string>(), 
                Arg.Any<OrderCreatedEvent>(), 
                Arg.Any<CancellationToken>())
            .Returns(ProviderResult.Success());

        var service = new OrderService(mockMessaging, mockLogger);
        var order = new Order { Id = "ORD-123", CustomerId = "CUST-456", TotalAmount = 199.99m };

        // Act
        await service.CreateOrderAsync(order);

        // Assert
        await mockMessaging.Received(1).PublishAsync(
            "order-created",
            Arg.Is<OrderCreatedEvent>(e => e.OrderId == "ORD-123"),
            Arg.Any<CancellationToken>());
    }
}
```

---

## 3. File Storage (S3)

**Use for:** Document storage, image uploads, file downloads, large object storage.

### Configuration

```json
{
  "Genesis": {
    "FileStorage": {
      "UseLocalStack": true,
      "LocalStackUrl": "http://localhost:4566",
      "Region": "us-east-1",
      "BucketName": "myservice-files",
      "KeyPrefix": "uploads",
      "EnableTenantIsolation": true,
      "EnableServerSideEncryption": true,
      "PresignedUrlExpiration": "00:15:00"
    }
  }
}
```

### Dependency Injection

```csharp
// Program.cs
using Pervaxis.Genesis.FileStorage.AWS.Extensions;

builder.Services.AddGenesisFileStorage(
    builder.Configuration.GetSection("Genesis:FileStorage"));
```

### NuGet Package

```xml
<PackageReference Include="Pervaxis.Genesis.FileStorage.AWS" Version="1.0.0" />
```

### Usage Example

```csharp
using Pervaxis.Core.Abstractions.Genesis.Modules;

public class InvoiceService
{
    private readonly IFileStorage _fileStorage;
    private readonly ILogger<InvoiceService> _logger;

    public InvoiceService(IFileStorage fileStorage, ILogger<InvoiceService> logger)
    {
        _fileStorage = fileStorage;
        _logger = logger;
    }

    public async Task<string> GenerateAndStoreInvoiceAsync(
        string orderId, 
        CancellationToken cancellationToken = default)
    {
        // Generate invoice PDF (not shown)
        var invoicePdf = GenerateInvoicePdf(orderId);
        
        var fileName = $"invoices/{orderId}/invoice.pdf";
        
        var result = await _fileStorage.UploadAsync(
            fileName, 
            invoicePdf, 
            "application/pdf", 
            cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Invoice stored for order {OrderId}", orderId);
            return fileName;
        }
        else
        {
            _logger.LogError("Failed to store invoice: {Error}", result.ErrorMessage);
            throw new InvalidOperationException($"Failed to store invoice: {result.ErrorMessage}");
        }
    }

    public async Task<Stream> DownloadInvoiceAsync(
        string orderId, 
        CancellationToken cancellationToken = default)
    {
        var fileName = $"invoices/{orderId}/invoice.pdf";
        
        var result = await _fileStorage.DownloadAsync(fileName, cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Invoice downloaded for order {OrderId}", orderId);
            return result.Data;
        }
        else
        {
            _logger.LogError("Failed to download invoice: {Error}", result.ErrorMessage);
            throw new FileNotFoundException($"Invoice not found for order {orderId}");
        }
    }

    public async Task<string> GetInvoiceDownloadLinkAsync(
        string orderId, 
        CancellationToken cancellationToken = default)
    {
        var fileName = $"invoices/{orderId}/invoice.pdf";
        
        var result = await _fileStorage.GetPresignedDownloadUrlAsync(
            fileName, 
            TimeSpan.FromMinutes(15), 
            cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Generated presigned URL for order {OrderId}", orderId);
            return result.Data;
        }
        else
        {
            _logger.LogError("Failed to generate presigned URL: {Error}", result.ErrorMessage);
            throw new InvalidOperationException($"Failed to generate download link: {result.ErrorMessage}");
        }
    }

    private Stream GenerateInvoicePdf(string orderId)
    {
        // Your PDF generation logic here
        var content = $"Invoice for Order {orderId}"u8.ToArray();
        return new MemoryStream(content);
    }
}
```

### Testing

```csharp
using NSubstitute;
using Pervaxis.Core.Abstractions.Genesis.Modules;
using Pervaxis.Genesis.Base.Result;

public class InvoiceServiceTests
{
    [Fact]
    public async Task GenerateAndStoreInvoiceAsync_UploadsFile()
    {
        // Arrange
        var mockFileStorage = Substitute.For<IFileStorage>();
        var mockLogger = Substitute.For<ILogger<InvoiceService>>();
        
        mockFileStorage.UploadAsync(
                Arg.Any<string>(), 
                Arg.Any<Stream>(), 
                Arg.Any<string>(), 
                Arg.Any<CancellationToken>())
            .Returns(ProviderResult.Success());

        var service = new InvoiceService(mockFileStorage, mockLogger);

        // Act
        var fileName = await service.GenerateAndStoreInvoiceAsync("ORD-123");

        // Assert
        Assert.Equal("invoices/ORD-123/invoice.pdf", fileName);
        await mockFileStorage.Received(1).UploadAsync(
            Arg.Is<string>(s => s.Contains("ORD-123")),
            Arg.Any<Stream>(),
            "application/pdf",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetInvoiceDownloadLinkAsync_ReturnsPresignedUrl()
    {
        // Arrange
        var mockFileStorage = Substitute.For<IFileStorage>();
        var mockLogger = Substitute.For<ILogger<InvoiceService>>();
        
        mockFileStorage.GetPresignedDownloadUrlAsync(
                Arg.Any<string>(), 
                Arg.Any<TimeSpan>(), 
                Arg.Any<CancellationToken>())
            .Returns(ProviderResult<string>.Success("https://s3.amazonaws.com/presigned-url"));

        var service = new InvoiceService(mockFileStorage, mockLogger);

        // Act
        var url = await service.GetInvoiceDownloadLinkAsync("ORD-123");

        // Assert
        Assert.Contains("presigned-url", url);
    }
}
```

---

## 4. Search (OpenSearch)

**Use for:** Full-text search, product catalogs, log analytics, document search.

### Configuration

```json
{
  "Genesis": {
    "Search": {
      "UseLocalStack": true,
      "LocalStackUrl": "http://localhost:4566",
      "Region": "us-east-1",
      "ServiceUrl": "http://localhost:4566",
      "IndexPrefix": "myservice",
      "EnableTenantIsolation": true,
      "DefaultPageSize": 20,
      "MaxPageSize": 100
    }
  }
}
```

### Dependency Injection

```csharp
// Program.cs
using Pervaxis.Genesis.Search.AWS.Extensions;

builder.Services.AddGenesisSearch(
    builder.Configuration.GetSection("Genesis:Search"));
```

### NuGet Package

```xml
<PackageReference Include="Pervaxis.Genesis.Search.AWS" Version="1.0.0" />
```

### Usage Example

```csharp
using Pervaxis.Core.Abstractions.Genesis.Modules;

public class ProductSearchService
{
    private readonly ISearch _search;
    private readonly ILogger<ProductSearchService> _logger;

    public ProductSearchService(ISearch search, ILogger<ProductSearchService> logger)
    {
        _search = search;
        _logger = logger;
    }

    // Index a product
    public async Task IndexProductAsync(Product product, CancellationToken cancellationToken = default)
    {
        var result = await _search.IndexAsync(
            "products", 
            product.Id, 
            product, 
            cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Indexed product {ProductId}", product.Id);
        }
        else
        {
            _logger.LogError("Failed to index product: {Error}", result.ErrorMessage);
        }
    }

    // Search products
    public async Task<List<Product>> SearchProductsAsync(
        string query, 
        int page = 1, 
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _search.SearchAsync<Product>(
            "products", 
            query, 
            page, 
            pageSize, 
            cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation(
                "Found {Count} products for query '{Query}'", 
                result.Data.Items.Count, 
                query);
            return result.Data.Items;
        }
        else
        {
            _logger.LogError("Search failed: {Error}", result.ErrorMessage);
            return new List<Product>();
        }
    }

    // Delete product from index
    public async Task DeleteProductAsync(string productId, CancellationToken cancellationToken = default)
    {
        var result = await _search.DeleteAsync("products", productId, cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Deleted product {ProductId} from index", productId);
        }
        else
        {
            _logger.LogError("Failed to delete product: {Error}", result.ErrorMessage);
        }
    }

    // Bulk index multiple products
    public async Task BulkIndexProductsAsync(
        List<Product> products, 
        CancellationToken cancellationToken = default)
    {
        var documents = products.ToDictionary(p => p.Id, p => (object)p);
        
        var result = await _search.BulkIndexAsync("products", documents, cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Bulk indexed {Count} products", products.Count);
        }
        else
        {
            _logger.LogError("Bulk index failed: {Error}", result.ErrorMessage);
        }
    }
}
```

### Testing

```csharp
using NSubstitute;
using Pervaxis.Core.Abstractions.Genesis.Modules;
using Pervaxis.Genesis.Base.Result;

public class ProductSearchServiceTests
{
    [Fact]
    public async Task SearchProductsAsync_ReturnsResults()
    {
        // Arrange
        var mockSearch = Substitute.For<ISearch>();
        var mockLogger = Substitute.For<ILogger<ProductSearchService>>();
        
        var products = new List<Product>
        {
            new() { Id = "1", Name = "Laptop", Price = 999.99m },
            new() { Id = "2", Name = "Mouse", Price = 29.99m }
        };

        var searchResult = new SearchResult<Product>
        {
            Items = products,
            TotalCount = 2,
            Page = 1,
            PageSize = 20
        };

        mockSearch.SearchAsync<Product>(
                Arg.Any<string>(), 
                Arg.Any<string>(), 
                Arg.Any<int>(), 
                Arg.Any<int>(), 
                Arg.Any<CancellationToken>())
            .Returns(ProviderResult<SearchResult<Product>>.Success(searchResult));

        var service = new ProductSearchService(mockSearch, mockLogger);

        // Act
        var results = await service.SearchProductsAsync("laptop");

        // Assert
        Assert.Equal(2, results.Count);
        Assert.Contains(results, p => p.Name == "Laptop");
    }

    [Fact]
    public async Task IndexProductAsync_IndexesSuccessfully()
    {
        // Arrange
        var mockSearch = Substitute.For<ISearch>();
        var mockLogger = Substitute.For<ILogger<ProductSearchService>>();
        
        mockSearch.IndexAsync(
                Arg.Any<string>(), 
                Arg.Any<string>(), 
                Arg.Any<Product>(), 
                Arg.Any<CancellationToken>())
            .Returns(ProviderResult.Success());

        var service = new ProductSearchService(mockSearch, mockLogger);
        var product = new Product { Id = "123", Name = "Keyboard", Price = 79.99m };

        // Act
        await service.IndexProductAsync(product);

        // Assert
        await mockSearch.Received(1).IndexAsync(
            "products",
            "123",
            Arg.Any<Product>(),
            Arg.Any<CancellationToken>());
    }
}
```

---

## 5. Notifications (SES/SNS)

**Use for:** Transactional emails, SMS alerts, push notifications, customer communications.

### Configuration

```json
{
  "Genesis": {
    "Notifications": {
      "UseLocalStack": true,
      "LocalStackUrl": "http://localhost:4566",
      "Region": "us-east-1",
      "SenderEmail": "noreply@myservice.com",
      "SenderName": "MyService",
      "EnableTenantIsolation": true,
      "DefaultSmsTopicArn": "arn:aws:sns:us-east-1:000000000000:sms-topic",
      "DefaultPushTopicArn": "arn:aws:sns:us-east-1:000000000000:push-topic"
    }
  }
}
```

### Dependency Injection

```csharp
// Program.cs
using Pervaxis.Genesis.Notifications.AWS.Extensions;

builder.Services.AddGenesisNotifications(
    builder.Configuration.GetSection("Genesis:Notifications"));
```

### NuGet Package

```xml
<PackageReference Include="Pervaxis.Genesis.Notifications.AWS" Version="1.0.0" />
```

### Usage Example

```csharp
using Pervaxis.Core.Abstractions.Genesis.Modules;

public class CustomerNotificationService
{
    private readonly INotifications _notifications;
    private readonly ILogger<CustomerNotificationService> _logger;

    public CustomerNotificationService(
        INotifications notifications, 
        ILogger<CustomerNotificationService> logger)
    {
        _notifications = notifications;
        _logger = logger;
    }

    // Send order confirmation email
    public async Task SendOrderConfirmationAsync(
        Order order, 
        string customerEmail,
        CancellationToken cancellationToken = default)
    {
        var subject = $"Order Confirmation - {order.Id}";
        var body = $@"
            <html>
            <body>
                <h1>Thank you for your order!</h1>
                <p>Order ID: <strong>{order.Id}</strong></p>
                <p>Total Amount: <strong>${order.TotalAmount:F2}</strong></p>
                <p>We'll send you another email when your order ships.</p>
            </body>
            </html>
        ";

        var result = await _notifications.SendEmailAsync(
            customerEmail,
            subject,
            body,
            isHtml: true,
            cancellationToken: cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Sent order confirmation email for {OrderId}", order.Id);
        }
        else
        {
            _logger.LogError("Failed to send email: {Error}", result.ErrorMessage);
        }
    }

    // Send SMS alert
    public async Task SendShippingAlertAsync(
        string phoneNumber, 
        string trackingNumber,
        CancellationToken cancellationToken = default)
    {
        var message = $"Your order has shipped! Track it here: https://track.myservice.com/{trackingNumber}";

        var result = await _notifications.SendSmsAsync(
            phoneNumber, 
            message, 
            cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Sent shipping SMS to {PhoneNumber}", phoneNumber);
        }
        else
        {
            _logger.LogError("Failed to send SMS: {Error}", result.ErrorMessage);
        }
    }

    // Send push notification
    public async Task SendPushNotificationAsync(
        string deviceToken, 
        string title,
        string body,
        CancellationToken cancellationToken = default)
    {
        var result = await _notifications.SendPushNotificationAsync(
            deviceToken,
            title,
            body,
            cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Sent push notification to device");
        }
        else
        {
            _logger.LogError("Failed to send push notification: {Error}", result.ErrorMessage);
        }
    }

    // Send bulk emails (e.g., promotional campaigns)
    public async Task SendPromotionalEmailsAsync(
        List<string> recipients,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken = default)
    {
        foreach (var recipient in recipients)
        {
            await _notifications.SendEmailAsync(
                recipient,
                subject,
                htmlBody,
                isHtml: true,
                cancellationToken: cancellationToken);

            // Add small delay to avoid rate limiting
            await Task.Delay(100, cancellationToken);
        }

        _logger.LogInformation("Sent promotional emails to {Count} recipients", recipients.Count);
    }
}
```

### Testing

```csharp
using NSubstitute;
using Pervaxis.Core.Abstractions.Genesis.Modules;
using Pervaxis.Genesis.Base.Result;

public class CustomerNotificationServiceTests
{
    [Fact]
    public async Task SendOrderConfirmationAsync_SendsEmail()
    {
        // Arrange
        var mockNotifications = Substitute.For<INotifications>();
        var mockLogger = Substitute.For<ILogger<CustomerNotificationService>>();
        
        mockNotifications.SendEmailAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(ProviderResult.Success());

        var service = new CustomerNotificationService(mockNotifications, mockLogger);
        var order = new Order { Id = "ORD-123", TotalAmount = 199.99m };

        // Act
        await service.SendOrderConfirmationAsync(order, "customer@example.com");

        // Assert
        await mockNotifications.Received(1).SendEmailAsync(
            "customer@example.com",
            Arg.Is<string>(s => s.Contains("ORD-123")),
            Arg.Any<string>(),
            true,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendShippingAlertAsync_SendsSms()
    {
        // Arrange
        var mockNotifications = Substitute.For<INotifications>();
        var mockLogger = Substitute.For<ILogger<CustomerNotificationService>>();
        
        mockNotifications.SendSmsAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(ProviderResult.Success());

        var service = new CustomerNotificationService(mockNotifications, mockLogger);

        // Act
        await service.SendShippingAlertAsync("+1234567890", "TRACK123");

        // Assert
        await mockNotifications.Received(1).SendSmsAsync(
            "+1234567890",
            Arg.Is<string>(s => s.Contains("TRACK123")),
            Arg.Any<CancellationToken>());
    }
}
```

---

## 6. Workflow (Step Functions)

**Use for:** Multi-step business processes, order fulfillment, approval workflows, long-running tasks.

### Configuration

```json
{
  "Genesis": {
    "Workflow": {
      "UseLocalStack": true,
      "LocalStackUrl": "http://localhost:4566",
      "Region": "us-east-1",
      "StateMachineArn": "arn:aws:states:us-east-1:000000000000:stateMachine:order-fulfillment",
      "EnableTenantIsolation": true,
      "ExecutionNamePrefix": "myservice"
    }
  }
}
```

### Dependency Injection

```csharp
// Program.cs
using Pervaxis.Genesis.Workflow.AWS.Extensions;

builder.Services.AddGenesisWorkflow(
    builder.Configuration.GetSection("Genesis:Workflow"));
```

### NuGet Package

```xml
<PackageReference Include="Pervaxis.Genesis.Workflow.AWS" Version="1.0.0" />
```

### Usage Example

```csharp
using Pervaxis.Core.Abstractions.Genesis.Modules;

public class OrderFulfillmentService
{
    private readonly IWorkflow _workflow;
    private readonly ILogger<OrderFulfillmentService> _logger;

    public OrderFulfillmentService(IWorkflow workflow, ILogger<OrderFulfillmentService> logger)
    {
        _workflow = workflow;
        _logger = logger;
    }

    // Start order fulfillment workflow
    public async Task<string> StartFulfillmentAsync(
        Order order,
        CancellationToken cancellationToken = default)
    {
        var workflowInput = new
        {
            OrderId = order.Id,
            CustomerId = order.CustomerId,
            TotalAmount = order.TotalAmount,
            Items = order.Items,
            ShippingAddress = order.ShippingAddress
        };

        var result = await _workflow.StartWorkflowAsync(
            "order-fulfillment",
            workflowInput,
            cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation(
                "Started fulfillment workflow {ExecutionId} for order {OrderId}",
                result.Data,
                order.Id);
            return result.Data;
        }
        else
        {
            _logger.LogError("Failed to start workflow: {Error}", result.ErrorMessage);
            throw new InvalidOperationException($"Failed to start workflow: {result.ErrorMessage}");
        }
    }

    // Check workflow status
    public async Task<WorkflowStatus> GetFulfillmentStatusAsync(
        string executionId,
        CancellationToken cancellationToken = default)
    {
        var result = await _workflow.GetWorkflowStatusAsync(executionId, cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Workflow {ExecutionId} status: {Status}", executionId, result.Data);
            return result.Data;
        }
        else
        {
            _logger.LogError("Failed to get workflow status: {Error}", result.ErrorMessage);
            throw new InvalidOperationException($"Failed to get workflow status: {result.ErrorMessage}");
        }
    }

    // Cancel workflow
    public async Task CancelFulfillmentAsync(
        string executionId,
        CancellationToken cancellationToken = default)
    {
        var result = await _workflow.StopWorkflowAsync(executionId, cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Cancelled workflow {ExecutionId}", executionId);
        }
        else
        {
            _logger.LogError("Failed to cancel workflow: {Error}", result.ErrorMessage);
        }
    }

    // List all running workflows
    public async Task<List<string>> GetRunningWorkflowsAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await _workflow.ListWorkflowsAsync(
            "RUNNING",
            maxResults: 100,
            cancellationToken: cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Found {Count} running workflows", result.Data.Count);
            return result.Data;
        }
        else
        {
            _logger.LogError("Failed to list workflows: {Error}", result.ErrorMessage);
            return new List<string>();
        }
    }
}

public record Order
{
    public string Id { get; init; } = string.Empty;
    public string CustomerId { get; init; } = string.Empty;
    public decimal TotalAmount { get; init; }
    public List<OrderItem> Items { get; init; } = new();
    public string ShippingAddress { get; init; } = string.Empty;
}

public record OrderItem
{
    public string ProductId { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public decimal Price { get; init; }
}
```

### Testing

```csharp
using NSubstitute;
using Pervaxis.Core.Abstractions.Genesis.Modules;
using Pervaxis.Genesis.Base.Result;

public class OrderFulfillmentServiceTests
{
    [Fact]
    public async Task StartFulfillmentAsync_StartsWorkflow()
    {
        // Arrange
        var mockWorkflow = Substitute.For<IWorkflow>();
        var mockLogger = Substitute.For<ILogger<OrderFulfillmentService>>();
        
        mockWorkflow.StartWorkflowAsync(
                Arg.Any<string>(),
                Arg.Any<object>(),
                Arg.Any<CancellationToken>())
            .Returns(ProviderResult<string>.Success("execution-123"));

        var service = new OrderFulfillmentService(mockWorkflow, mockLogger);
        var order = new Order
        {
            Id = "ORD-123",
            CustomerId = "CUST-456",
            TotalAmount = 199.99m,
            Items = new List<OrderItem>()
        };

        // Act
        var executionId = await service.StartFulfillmentAsync(order);

        // Assert
        Assert.Equal("execution-123", executionId);
        await mockWorkflow.Received(1).StartWorkflowAsync(
            "order-fulfillment",
            Arg.Any<object>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetFulfillmentStatusAsync_ReturnsStatus()
    {
        // Arrange
        var mockWorkflow = Substitute.For<IWorkflow>();
        var mockLogger = Substitute.For<ILogger<OrderFulfillmentService>>();
        
        mockWorkflow.GetWorkflowStatusAsync(
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(ProviderResult<WorkflowStatus>.Success(WorkflowStatus.Running));

        var service = new OrderFulfillmentService(mockWorkflow, mockLogger);

        // Act
        var status = await service.GetFulfillmentStatusAsync("execution-123");

        // Assert
        Assert.Equal(WorkflowStatus.Running, status);
    }
}
```

---

## 7. AI Assistance (Bedrock)

**Use for:** Content generation, chatbots, recommendations, image generation, data analysis.

### Configuration

```json
{
  "Genesis": {
    "AIAssistance": {
      "UseLocalStack": false,
      "Region": "us-east-1",
      "TextModelId": "anthropic.claude-3-sonnet-20240229-v1:0",
      "ImageModelId": "stability.stable-diffusion-xl-v1",
      "EnableTenantIsolation": true,
      "DefaultMaxTokens": 1024,
      "DefaultTemperature": 0.7
    }
  }
}
```

### Dependency Injection

```csharp
// Program.cs
using Pervaxis.Genesis.AIAssistance.AWS.Extensions;

builder.Services.AddGenesisAIAssistance(
    builder.Configuration.GetSection("Genesis:AIAssistance"));
```

### NuGet Package

```xml
<PackageReference Include="Pervaxis.Genesis.AIAssistance.AWS" Version="1.0.0" />
```

### Usage Example

```csharp
using Pervaxis.Core.Abstractions.Genesis.Modules;

public class ProductRecommendationService
{
    private readonly IAIAssistant _aiAssistant;
    private readonly ILogger<ProductRecommendationService> _logger;

    public ProductRecommendationService(
        IAIAssistant aiAssistant, 
        ILogger<ProductRecommendationService> logger)
    {
        _aiAssistant = aiAssistant;
        _logger = logger;
    }

    // Generate product description
    public async Task<string> GenerateProductDescriptionAsync(
        string productName,
        string category,
        List<string> features,
        CancellationToken cancellationToken = default)
    {
        var prompt = $@"
Generate a compelling product description for the following:

Product Name: {productName}
Category: {category}
Features:
{string.Join("\n", features.Select(f => $"- {f}"))}

Write a 2-3 paragraph description that highlights the key benefits and appeals to customers.
";

        var result = await _aiAssistant.GenerateTextAsync(
            prompt,
            maxTokens: 500,
            temperature: 0.7f,
            cancellationToken: cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Generated description for product {ProductName}", productName);
            return result.Data;
        }
        else
        {
            _logger.LogError("Failed to generate description: {Error}", result.ErrorMessage);
            return string.Empty;
        }
    }

    // Generate personalized recommendations
    public async Task<List<string>> GetPersonalizedRecommendationsAsync(
        string customerId,
        List<string> purchaseHistory,
        List<string> browsingHistory,
        CancellationToken cancellationToken = default)
    {
        var prompt = $@"
Based on the following customer data, recommend 5 products they might be interested in:

Purchase History:
{string.Join("\n", purchaseHistory.Select(p => $"- {p}"))}

Recently Viewed:
{string.Join("\n", browsingHistory.Select(b => $"- {b}"))}

Provide only the product names, one per line.
";

        var result = await _aiAssistant.GenerateTextAsync(
            prompt,
            maxTokens: 200,
            temperature: 0.5f,
            cancellationToken: cancellationToken);

        if (result.IsSuccess)
        {
            var recommendations = result.Data
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(r => r.Trim())
                .Take(5)
                .ToList();

            _logger.LogInformation(
                "Generated {Count} recommendations for customer {CustomerId}",
                recommendations.Count,
                customerId);

            return recommendations;
        }
        else
        {
            _logger.LogError("Failed to generate recommendations: {Error}", result.ErrorMessage);
            return new List<string>();
        }
    }

    // Generate product image
    public async Task<byte[]> GenerateProductImageAsync(
        string productDescription,
        CancellationToken cancellationToken = default)
    {
        var prompt = $"High quality product photography: {productDescription}";

        var result = await _aiAssistant.GenerateImageAsync(
            prompt,
            cancellationToken: cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Generated product image");
            return result.Data;
        }
        else
        {
            _logger.LogError("Failed to generate image: {Error}", result.ErrorMessage);
            return Array.Empty<byte>();
        }
    }

    // Customer support chatbot
    public async Task<string> AnswerCustomerQuestionAsync(
        string question,
        string orderContext,
        CancellationToken cancellationToken = default)
    {
        var prompt = $@"
You are a helpful customer support assistant. Answer the customer's question professionally and concisely.

Order Context: {orderContext}
Customer Question: {question}

Provide a helpful answer:
";

        var result = await _aiAssistant.GenerateTextAsync(
            prompt,
            maxTokens: 300,
            temperature: 0.5f,
            cancellationToken: cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Generated support response");
            return result.Data;
        }
        else
        {
            _logger.LogError("Failed to generate support response: {Error}", result.ErrorMessage);
            return "I apologize, but I'm unable to assist with that question right now. Please contact our support team.";
        }
    }
}
```

### Testing

```csharp
using NSubstitute;
using Pervaxis.Core.Abstractions.Genesis.Modules;
using Pervaxis.Genesis.Base.Result;

public class ProductRecommendationServiceTests
{
    [Fact]
    public async Task GenerateProductDescriptionAsync_ReturnsDescription()
    {
        // Arrange
        var mockAI = Substitute.For<IAIAssistant>();
        var mockLogger = Substitute.For<ILogger<ProductRecommendationService>>();
        
        mockAI.GenerateTextAsync(
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<float>(),
                Arg.Any<CancellationToken>())
            .Returns(ProviderResult<string>.Success(
                "This amazing product features cutting-edge technology..."));

        var service = new ProductRecommendationService(mockAI, mockLogger);

        // Act
        var description = await service.GenerateProductDescriptionAsync(
            "Smart Watch",
            "Electronics",
            new List<string> { "Heart rate monitor", "GPS", "Waterproof" });

        // Assert
        Assert.NotEmpty(description);
        Assert.Contains("product", description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GenerateProductImageAsync_ReturnsImageBytes()
    {
        // Arrange
        var mockAI = Substitute.For<IAIAssistant>();
        var mockLogger = Substitute.For<ILogger<ProductRecommendationService>>();
        
        var fakeImageBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }; // JPEG header
        mockAI.GenerateImageAsync(
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(ProviderResult<byte[]>.Success(fakeImageBytes));

        var service = new ProductRecommendationService(mockAI, mockLogger);

        // Act
        var imageBytes = await service.GenerateProductImageAsync("modern smartwatch");

        // Assert
        Assert.NotEmpty(imageBytes);
        Assert.Equal(4, imageBytes.Length);
    }
}
```

---

## 8. Reporting (Metabase)

**Use for:** Business dashboards, analytics, data visualization, scheduled reports.

### Configuration

```json
{
  "Genesis": {
    "Reporting": {
      "BaseUrl": "http://localhost:3000",
      "ApiKey": "your-metabase-api-key",
      "EnableTenantIsolation": true,
      "DefaultTimeout": 30
    }
  }
}
```

### Dependency Injection

```csharp
// Program.cs
using Pervaxis.Genesis.Reporting.AWS.Extensions;

builder.Services.AddGenesisReporting(
    builder.Configuration.GetSection("Genesis:Reporting"));
```

### NuGet Package

```xml
<PackageReference Include="Pervaxis.Genesis.Reporting.AWS" Version="1.0.0" />
```

### Usage Example

```csharp
using Pervaxis.Core.Abstractions.Genesis.Modules;

public class SalesAnalyticsService
{
    private readonly IReporting _reporting;
    private readonly ILogger<SalesAnalyticsService> _logger;

    public SalesAnalyticsService(IReporting reporting, ILogger<SalesAnalyticsService> logger)
    {
        _reporting = reporting;
        _logger = logger;
    }

    // Get dashboard data
    public async Task<Dashboard> GetSalesDashboardAsync(
        int dashboardId,
        CancellationToken cancellationToken = default)
    {
        var result = await _reporting.GetDashboardAsync(dashboardId, cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Retrieved dashboard {DashboardId}", dashboardId);
            return result.Data;
        }
        else
        {
            _logger.LogError("Failed to retrieve dashboard: {Error}", result.ErrorMessage);
            throw new InvalidOperationException($"Failed to retrieve dashboard: {result.ErrorMessage}");
        }
    }

    // Execute custom query
    public async Task<QueryResult> GetMonthlySalesAsync(
        int year,
        int month,
        CancellationToken cancellationToken = default)
    {
        var query = new
        {
            database = 1,
            type = "native",
            native = new
            {
                query = @"
                    SELECT 
                        DATE_TRUNC('day', order_date) as date,
                        SUM(total_amount) as total_sales,
                        COUNT(*) as order_count
                    FROM orders
                    WHERE EXTRACT(YEAR FROM order_date) = :year
                      AND EXTRACT(MONTH FROM order_date) = :month
                    GROUP BY DATE_TRUNC('day', order_date)
                    ORDER BY date",
                @params = new[] 
                {
                    new { name = "year", value = year },
                    new { name = "month", value = month }
                }
            }
        };

        var result = await _reporting.ExecuteQueryAsync(query, cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Executed monthly sales query for {Year}-{Month}", year, month);
            return result.Data;
        }
        else
        {
            _logger.LogError("Failed to execute query: {Error}", result.ErrorMessage);
            throw new InvalidOperationException($"Failed to execute query: {result.ErrorMessage}");
        }
    }

    // Export dashboard as PDF
    public async Task<byte[]> ExportDashboardToPdfAsync(
        int dashboardId,
        CancellationToken cancellationToken = default)
    {
        var result = await _reporting.ExportDashboardAsync(
            dashboardId,
            "pdf",
            cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Exported dashboard {DashboardId} as PDF", dashboardId);
            return result.Data;
        }
        else
        {
            _logger.LogError("Failed to export dashboard: {Error}", result.ErrorMessage);
            return Array.Empty<byte>();
        }
    }

    // Get specific report card
    public async Task<Card> GetReportCardAsync(
        int cardId,
        CancellationToken cancellationToken = default)
    {
        var result = await _reporting.GetCardAsync(cardId, cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Retrieved card {CardId}", cardId);
            return result.Data;
        }
        else
        {
            _logger.LogError("Failed to retrieve card: {Error}", result.ErrorMessage);
            throw new InvalidOperationException($"Failed to retrieve card: {result.ErrorMessage}");
        }
    }

    // Schedule email report
    public async Task ScheduleDailyReportAsync(
        int dashboardId,
        List<string> recipients,
        CancellationToken cancellationToken = default)
    {
        var schedule = new
        {
            dashboard_id = dashboardId,
            recipients = recipients,
            schedule_type = "daily",
            schedule_hour = 8, // 8 AM
            format = "pdf"
        };

        // Note: This uses Metabase's pulse/subscription API
        // Implementation depends on Metabase version and configuration
        _logger.LogInformation(
            "Scheduled daily report for dashboard {DashboardId} to {Count} recipients",
            dashboardId,
            recipients.Count);

        await Task.CompletedTask; // Placeholder for actual API call
    }
}
```

### Testing

```csharp
using NSubstitute;
using Pervaxis.Core.Abstractions.Genesis.Modules;
using Pervaxis.Genesis.Base.Result;

public class SalesAnalyticsServiceTests
{
    [Fact]
    public async Task GetSalesDashboardAsync_ReturnsDashboard()
    {
        // Arrange
        var mockReporting = Substitute.For<IReporting>();
        var mockLogger = Substitute.For<ILogger<SalesAnalyticsService>>();
        
        var dashboard = new Dashboard
        {
            Id = 1,
            Name = "Sales Dashboard",
            Cards = new List<Card>()
        };

        mockReporting.GetDashboardAsync(
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(ProviderResult<Dashboard>.Success(dashboard));

        var service = new SalesAnalyticsService(mockReporting, mockLogger);

        // Act
        var result = await service.GetSalesDashboardAsync(1);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Sales Dashboard", result.Name);
    }

    [Fact]
    public async Task ExportDashboardToPdfAsync_ReturnsBytes()
    {
        // Arrange
        var mockReporting = Substitute.For<IReporting>();
        var mockLogger = Substitute.For<ILogger<SalesAnalyticsService>>();
        
        var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46 }; // PDF header
        mockReporting.ExportDashboardAsync(
                Arg.Any<int>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(ProviderResult<byte[]>.Success(pdfBytes));

        var service = new SalesAnalyticsService(mockReporting, mockLogger);

        // Act
        var result = await service.ExportDashboardToPdfAsync(1);

        // Assert
        Assert.NotEmpty(result);
        Assert.Equal(4, result.Length);
    }
}
```

---

## General Best Practices

### 1. Always Use CancellationToken

```csharp
public async Task<Result> MyMethodAsync(CancellationToken cancellationToken = default)
{
    // Pass cancellationToken to all Genesis provider calls
    await _cache.GetAsync<string>("key", cancellationToken);
}
```

### 2. Handle ProviderResult Properly

```csharp
var result = await _cache.GetAsync<Product>("key");

if (result.IsSuccess)
{
    // Use result.Data
}
else
{
    // Log result.ErrorMessage
    // Decide: throw, return default, retry, etc.
}
```

### 3. Use Structured Logging

```csharp
_logger.LogInformation(
    "Processed order {OrderId} for customer {CustomerId}",
    orderId,
    customerId);
```

### 4. Multi-Tenancy

Genesis handles tenant isolation automatically when:
- `EnableTenantIsolation = true` in options
- `ITenantContext` is registered and resolved

Your code doesn't need to add tenant prefixes - Genesis does it.

### 5. LocalStack for Development

All providers support LocalStack. Set in appsettings.Development.json:

```json
{
  "Genesis": {
    "ProviderName": {
      "UseLocalStack": true,
      "LocalStackUrl": "http://localhost:4566"
    }
  }
}
```

### 6. Mock Genesis Providers in Tests

Use NSubstitute or Moq:

```csharp
var mockCache = Substitute.For<ICache>();
mockCache.GetAsync<string>(Arg.Any<string>())
    .Returns(ProviderResult<string>.Success("cached-value"));
```

### 7. Error Handling Strategy

```csharp
try
{
    var result = await _provider.OperationAsync(...);
    
    if (!result.IsSuccess)
    {
        // Genesis resilience (Polly) already retried
        // This is a real failure - handle accordingly
        _logger.LogError("Operation failed: {Error}", result.ErrorMessage);
        // Throw, return error result, use fallback, etc.
    }
}
catch (GenesisException ex)
{
    // Infrastructure failure (AWS connectivity, etc.)
    _logger.LogError(ex, "Genesis provider error");
}
```

---

## External Service Integration Examples

Genesis handles AWS services. For third-party services, integrate directly:

### Stripe (Payments)

```csharp
using Stripe;

public class PaymentService
{
    private readonly IConfiguration _configuration;

    public async Task<PaymentIntent> CreatePaymentIntentAsync(decimal amount)
    {
        StripeConfiguration.ApiKey = _configuration["Stripe:SecretKey"];
        
        var options = new PaymentIntentCreateOptions
        {
            Amount = (long)(amount * 100), // cents
            Currency = "usd"
        };

        var service = new PaymentIntentService();
        return await service.CreateAsync(options);
    }
}
```

### Twilio (SMS)

```csharp
using Twilio;
using Twilio.Rest.Api.V2010.Account;

public class SmsService
{
    public async Task SendSmsAsync(string to, string message)
    {
        TwilioClient.Init(accountSid, authToken);
        
        await MessageResource.CreateAsync(
            body: message,
            from: new Twilio.Types.PhoneNumber("+1234567890"),
            to: new Twilio.Types.PhoneNumber(to));
    }
}
```

---

## Summary

This guide covers all 8 Genesis providers with:
- ✅ Configuration examples
- ✅ DI registration
- ✅ Real usage patterns
- ✅ Testing strategies
- ✅ Best practices

**Genesis handles infrastructure. You focus on business logic.**

For questions or issues, see:
- Genesis repository: https://github.com/clarivex-tech/pervaxis-genesis
- Documentation: https://clarivex.tech/docs/genesis

---

*Genesis Provider Integration Guide*  
*Pervaxis Platform · Clarivex Technologies*
