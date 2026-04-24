# Core.Abstractions Interfaces Needed for Genesis

This document tracks which Genesis module interfaces need to be added to `Pervaxis.Core.Abstractions` before implementation can proceed.

---

## Status Overview

| Interface | Status | Genesis Provider | Priority | Target Core Version |
|---|---|---|---|---|
| ✅ `ICache` | Available | Caching.AWS | - | v1.0.0 |
| ✅ `IMessaging` | Available | Messaging.AWS | - | v1.0.0 |
| ❌ `IFileStorage` | **MISSING** | FileStorage.AWS | HIGH | v1.1.0 |
| ❌ `ISearch` | **MISSING** | Search.AWS | MEDIUM | v1.1.0 |
| ❌ `INotification` | **MISSING** | Notifications.AWS | MEDIUM | v1.1.0 |
| ❌ `IWorkflow` | **MISSING** | Workflow.AWS | MEDIUM | v1.2.0 |
| ❌ `IAIAssistant` | **MISSING** | AIAssistance.AWS | LOW | v1.2.0 |
| ❌ `IReporting` | **MISSING** | Reporting.AWS | LOW | v1.2.0 |

---

## 1. IFileStorage (Priority: HIGH)

**Namespace**: `Pervaxis.Core.Abstractions.Genesis.Modules`  
**File Location**: `src/Pervaxis.Core.Abstractions/Genesis/Modules/IFileStorage.cs`  
**Used By**: `Pervaxis.Genesis.FileStorage.AWS` (S3 implementation)

### Interface Definition

```csharp
namespace Pervaxis.Core.Abstractions.Genesis.Modules;

/// <summary>
/// Abstraction for file storage operations (upload, download, delete, list).
/// Implemented by cloud-specific providers (S3, Azure Blob, GCS).
/// </summary>
public interface IFileStorage
{
    /// <summary>
    /// Uploads a file to storage.
    /// </summary>
    /// <param name="key">Unique file key/path.</param>
    /// <param name="content">File content stream.</param>
    /// <param name="contentType">MIME content type (optional).</param>
    /// <param name="metadata">Custom metadata key-value pairs (optional).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The full storage key for the uploaded file.</returns>
    Task<string> UploadAsync(
        string key,
        Stream content,
        string? contentType = null,
        IDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads a file from storage.
    /// </summary>
    /// <param name="key">File key/path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>File content stream, or null if not found.</returns>
    Task<Stream?> DownloadAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a file from storage.
    /// </summary>
    /// <param name="key">File key/path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if deleted successfully.</returns>
    Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a file exists in storage.
    /// </summary>
    /// <param name="key">File key/path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if file exists.</returns>
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a presigned URL for temporary file access.
    /// </summary>
    /// <param name="key">File key/path.</param>
    /// <param name="expiry">URL expiration timespan.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Presigned URL string.</returns>
    Task<string> GetPresignedUrlAsync(
        string key,
        TimeSpan expiry,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets metadata for a file.
    /// </summary>
    /// <param name="key">File key/path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Dictionary of metadata key-value pairs.</returns>
    Task<IDictionary<string, string>> GetMetadataAsync(
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists files matching a prefix.
    /// </summary>
    /// <param name="prefix">Optional prefix filter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Collection of file keys.</returns>
    Task<IEnumerable<string>> ListAsync(
        string? prefix = null,
        CancellationToken cancellationToken = default);
}
```

---

## 2. ISearch (Priority: MEDIUM)

**Namespace**: `Pervaxis.Core.Abstractions.Genesis.Modules`  
**File Location**: `src/Pervaxis.Core.Abstractions/Genesis/Modules/ISearch.cs`  
**Used By**: `Pervaxis.Genesis.Search.AWS` (OpenSearch implementation)

### Interface Definition

```csharp
namespace Pervaxis.Core.Abstractions.Genesis.Modules;

/// <summary>
/// Abstraction for full-text search operations (index, search, delete).
/// Implemented by cloud-specific providers (OpenSearch, Azure Cognitive Search, GCS).
/// </summary>
public interface ISearch
{
    /// <summary>
    /// Indexes a document for searching.
    /// </summary>
    /// <typeparam name="T">Document type.</typeparam>
    /// <param name="index">Index name.</param>
    /// <param name="id">Document ID.</param>
    /// <param name="document">Document to index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if indexed successfully.</returns>
    Task<bool> IndexAsync<T>(
        string index,
        string id,
        T document,
        CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Searches documents matching a query.
    /// </summary>
    /// <typeparam name="T">Document type.</typeparam>
    /// <param name="index">Index name.</param>
    /// <param name="query">Search query string.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Collection of matching documents.</returns>
    Task<IEnumerable<T>> SearchAsync<T>(
        string index,
        string query,
        CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Deletes a document from the index.
    /// </summary>
    /// <param name="index">Index name.</param>
    /// <param name="id">Document ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if deleted successfully.</returns>
    Task<bool> DeleteAsync(
        string index,
        string id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bulk indexes multiple documents.
    /// </summary>
    /// <typeparam name="T">Document type.</typeparam>
    /// <param name="index">Index name.</param>
    /// <param name="documents">Documents to index (with IDs).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of documents indexed successfully.</returns>
    Task<int> BulkIndexAsync<T>(
        string index,
        IDictionary<string, T> documents,
        CancellationToken cancellationToken = default) where T : class;
}
```

---

## 3. INotification (Priority: MEDIUM)

**Namespace**: `Pervaxis.Core.Abstractions.Genesis.Modules`  
**File Location**: `src/Pervaxis.Core.Abstractions/Genesis/Modules/INotification.cs`  
**Used By**: `Pervaxis.Genesis.Notifications.AWS` (SES + SNS implementation)

### Interface Definition

```csharp
namespace Pervaxis.Core.Abstractions.Genesis.Modules;

/// <summary>
/// Abstraction for notification operations (email, SMS, push).
/// Implemented by cloud-specific providers (SES+SNS, SendGrid, Twilio).
/// </summary>
public interface INotification
{
    /// <summary>
    /// Sends an email notification.
    /// </summary>
    /// <param name="to">Recipient email address.</param>
    /// <param name="subject">Email subject.</param>
    /// <param name="body">Email body (HTML or plain text).</param>
    /// <param name="isHtml">Whether body is HTML.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Message ID if sent successfully.</returns>
    Task<string> SendEmailAsync(
        string to,
        string subject,
        string body,
        bool isHtml = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a templated email notification.
    /// </summary>
    /// <param name="to">Recipient email address.</param>
    /// <param name="templateId">Template identifier.</param>
    /// <param name="templateData">Data to merge into template.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Message ID if sent successfully.</returns>
    Task<string> SendTemplatedEmailAsync(
        string to,
        string templateId,
        IDictionary<string, string> templateData,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends an SMS notification.
    /// </summary>
    /// <param name="phoneNumber">Recipient phone number (E.164 format).</param>
    /// <param name="message">SMS message text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Message ID if sent successfully.</returns>
    Task<string> SendSmsAsync(
        string phoneNumber,
        string message,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a push notification.
    /// </summary>
    /// <param name="deviceToken">Device token or endpoint ARN.</param>
    /// <param name="title">Notification title.</param>
    /// <param name="message">Notification message.</param>
    /// <param name="data">Additional data payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Message ID if sent successfully.</returns>
    Task<string> SendPushAsync(
        string deviceToken,
        string title,
        string message,
        IDictionary<string, string>? data = null,
        CancellationToken cancellationToken = default);
}
```

---

## 4. IWorkflow (Priority: MEDIUM)

**Namespace**: `Pervaxis.Core.Abstractions.Genesis.Modules`  
**File Location**: `src/Pervaxis.Core.Abstractions/Genesis/Modules/IWorkflow.cs`  
**Used By**: `Pervaxis.Genesis.Workflow.AWS` (Step Functions implementation)

### Interface Definition

```csharp
namespace Pervaxis.Core.Abstractions.Genesis.Modules;

/// <summary>
/// Abstraction for workflow execution operations.
/// Implemented by cloud-specific providers (Step Functions, Azure Logic Apps, Cloud Workflows).
/// </summary>
public interface IWorkflow
{
    /// <summary>
    /// Starts a workflow execution.
    /// </summary>
    /// <param name="workflowName">Workflow/state machine name.</param>
    /// <param name="input">Input data for the workflow.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Execution ARN or ID.</returns>
    Task<string> StartExecutionAsync(
        string workflowName,
        object input,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the status of a workflow execution.
    /// </summary>
    /// <param name="executionId">Execution ARN or ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Execution status (Running, Succeeded, Failed, etc.).</returns>
    Task<string> GetExecutionStatusAsync(
        string executionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the output of a completed workflow execution.
    /// </summary>
    /// <typeparam name="T">Output type.</typeparam>
    /// <param name="executionId">Execution ARN or ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Workflow output, or null if not completed.</returns>
    Task<T?> GetExecutionOutputAsync<T>(
        string executionId,
        CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Stops a running workflow execution.
    /// </summary>
    /// <param name="executionId">Execution ARN or ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if stopped successfully.</returns>
    Task<bool> StopExecutionAsync(
        string executionId,
        CancellationToken cancellationToken = default);
}
```

---

## 5. IAIAssistant (Priority: LOW)

**Namespace**: `Pervaxis.Core.Abstractions.Genesis.Modules`  
**File Location**: `src/Pervaxis.Core.Abstractions/Genesis/Modules/IAIAssistant.cs`  
**Used By**: `Pervaxis.Genesis.AIAssistance.AWS` (Bedrock implementation)

### Interface Definition

```csharp
namespace Pervaxis.Core.Abstractions.Genesis.Modules;

/// <summary>
/// Abstraction for AI assistance operations (text generation, embeddings).
/// Implemented by cloud-specific providers (Bedrock, Azure OpenAI, Vertex AI).
/// </summary>
public interface IAIAssistant
{
    /// <summary>
    /// Generates text from a prompt.
    /// </summary>
    /// <param name="prompt">Input prompt.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Generated text.</returns>
    Task<string> GenerateTextAsync(
        string prompt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates embeddings for text.
    /// </summary>
    /// <param name="text">Input text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Embedding vector.</returns>
    Task<float[]> GenerateEmbeddingAsync(
        string text,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates an image from a prompt.
    /// </summary>
    /// <param name="prompt">Image description prompt.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Image data as byte array.</returns>
    Task<byte[]> GenerateImageAsync(
        string prompt,
        CancellationToken cancellationToken = default);
}
```

---

## 6. IReporting (Priority: LOW)

**Namespace**: `Pervaxis.Core.Abstractions.Genesis.Modules`  
**File Location**: `src/Pervaxis.Core.Abstractions/Genesis/Modules/IReporting.cs`  
**Used By**: `Pervaxis.Genesis.Reporting.AWS` (Metabase REST API implementation)

### Interface Definition

```csharp
namespace Pervaxis.Core.Abstractions.Genesis.Modules;

/// <summary>
/// Abstraction for reporting and analytics operations.
/// Implemented by reporting tool integrations (Metabase, Power BI, Tableau).
/// </summary>
public interface IReporting
{
    /// <summary>
    /// Executes a query and returns results.
    /// </summary>
    /// <typeparam name="T">Result row type.</typeparam>
    /// <param name="query">SQL or query string.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Query results.</returns>
    Task<IEnumerable<T>> ExecuteQueryAsync<T>(
        string query,
        CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Gets a dashboard by ID.
    /// </summary>
    /// <param name="dashboardId">Dashboard identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Dashboard metadata and structure.</returns>
    Task<object> GetDashboardAsync(
        string dashboardId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new dashboard.
    /// </summary>
    /// <param name="name">Dashboard name.</param>
    /// <param name="definition">Dashboard definition object.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Created dashboard ID.</returns>
    Task<string> CreateDashboardAsync(
        string name,
        object definition,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Exports a report to a file format.
    /// </summary>
    /// <param name="reportId">Report identifier.</param>
    /// <param name="format">Export format (PDF, CSV, Excel).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Exported report data.</returns>
    Task<byte[]> ExportReportAsync(
        string reportId,
        string format,
        CancellationToken cancellationToken = default);
}
```

---

## Next Steps

### For Core.Abstractions Team

1. Review interface definitions above
2. Add interfaces to `Pervaxis.Core.Abstractions` project
3. Publish updated NuGet package (v1.1.0 for high priority, v1.2.0 for low priority)
4. Notify Genesis team when available

### For Genesis Team

1. **Block Task 2.3+** until Core interfaces available
2. Focus on Caching/Messaging provider improvements
3. Write tests, documentation, and samples for completed providers
4. Update `Directory.Build.props` when new Core versions published

---

*Document created: 2026-04-22*  
*Last updated: 2026-04-22*  
*Maintainer: Pervaxis Platform Team*
