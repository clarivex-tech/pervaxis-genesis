# Pervaxis.Genesis.AIAssistance.AWS

AWS Bedrock-based AI assistance provider for the Pervaxis Genesis platform. Supports text generation, embeddings, and image generation using Claude, Titan, and Stable Diffusion models.

## Features

- **Text Generation**: Claude 3.5 Sonnet and Titan text models
- **Embeddings**: Titan Embeddings for vector generation
- **Image Generation**: Stable Diffusion XL for AI-generated images
- **Model Flexibility**: Easy switching between Claude and Titan models
- **LocalStack Support**: Full local development support
- **Type-Safe**: Strongly-typed configuration with validation
- **Async/Await**: Modern async API throughout
- **Comprehensive Logging**: Structured logging with Microsoft.Extensions.Logging
- **Resource Management**: Proper IDisposable implementation

## Installation

```bash
dotnet add package Pervaxis.Genesis.AIAssistance.AWS
```

## Configuration

### appsettings.json

```json
{
  "AIAssistance": {
    "Region": "us-east-1",
    "TextModelId": "anthropic.claude-3-5-sonnet-20241022-v2:0",
    "EmbeddingModelId": "amazon.titan-embed-text-v1",
    "ImageModelId": "stability.stable-diffusion-xl-v1",
    "Temperature": 0.7,
    "MaxTokens": 1024,
    "MaxRetries": 3,
    "RequestTimeoutSeconds": 60
  }
}
```

### Dependency Injection

```csharp
using Pervaxis.Genesis.AIAssistance.AWS.Extensions;

// Option 1: From configuration
services.AddGenesisAIAssistance(configuration.GetSection("AIAssistance"));

// Option 2: Inline configuration
services.AddGenesisAIAssistance(options =>
{
    options.Region = "us-east-1";
    options.TextModelId = "anthropic.claude-3-5-sonnet-20241022-v2:0";
    options.Temperature = 0.7;
    options.MaxTokens = 2048;
});
```

## Usage

### Text Generation

```csharp
using Pervaxis.Core.Abstractions.Genesis.Modules;

public class ContentService
{
    private readonly IAIAssistant _aiAssistant;

    public ContentService(IAIAssistant aiAssistant)
    {
        _aiAssistant = aiAssistant;
    }

    public async Task<string> GenerateBlogPostAsync(string topic)
    {
        var prompt = $"Write a detailed blog post about: {topic}";
        var content = await _aiAssistant.GenerateTextAsync(prompt);
        return content;
    }

    public async Task<string> AnswerQuestionAsync(string question)
    {
        var prompt = $"Answer this question concisely: {question}";
        var answer = await _aiAssistant.GenerateTextAsync(prompt);
        return answer;
    }
}
```

### Embeddings Generation

```csharp
public class SearchService
{
    private readonly IAIAssistant _aiAssistant;

    public SearchService(IAIAssistant aiAssistant)
    {
        _aiAssistant = aiAssistant;
    }

    public async Task<float[]> GenerateQueryEmbeddingAsync(string query)
    {
        var embedding = await _aiAssistant.GenerateEmbeddingAsync(query);
        // Use embedding for similarity search, clustering, etc.
        return embedding;
    }

    public async Task<Dictionary<string, float[]>> IndexDocumentsAsync(List<string> documents)
    {
        var embeddings = new Dictionary<string, float[]>();
        
        foreach (var doc in documents)
        {
            var embedding = await _aiAssistant.GenerateEmbeddingAsync(doc);
            embeddings[doc] = embedding;
        }
        
        return embeddings;
    }
}
```

### Image Generation

```csharp
public class ImageService
{
    private readonly IAIAssistant _aiAssistant;

    public ImageService(IAIAssistant aiAssistant)
    {
        _aiAssistant = aiAssistant;
    }

    public async Task<byte[]> GenerateMarketingImageAsync(string description)
    {
        var prompt = $"Professional marketing image: {description}";
        var imageData = await _aiAssistant.GenerateImageAsync(prompt);
        
        // Save to file
        await File.WriteAllBytesAsync("marketing-image.png", imageData);
        
        return imageData;
    }

    public async Task<byte[]> CreateIllustrationAsync(string concept)
    {
        var prompt = $"Artistic illustration of: {concept}, vibrant colors, high detail";
        var imageData = await _aiAssistant.GenerateImageAsync(prompt);
        return imageData;
    }
}
```

## Model Selection

### Text Generation Models

#### Claude 3.5 Sonnet (Recommended)
```json
"TextModelId": "anthropic.claude-3-5-sonnet-20241022-v2:0"
```
- **Best for**: Complex reasoning, analysis, content generation
- **Context**: 200K tokens
- **Cost**: $3 per 1M input tokens, $15 per 1M output tokens

#### Claude 3 Opus
```json
"TextModelId": "anthropic.claude-3-opus-20240229"
```
- **Best for**: Highest quality outputs, complex tasks
- **Context**: 200K tokens
- **Cost**: $15 per 1M input tokens, $75 per 1M output tokens

#### Titan Text Express
```json
"TextModelId": "amazon.titan-text-express-v1"
```
- **Best for**: Cost-effective text generation
- **Context**: 8K tokens
- **Cost**: $0.80 per 1M input tokens, $1.60 per 1M output tokens

### Embedding Models

#### Titan Embeddings G1 (Default)
```json
"EmbeddingModelId": "amazon.titan-embed-text-v1"
```
- **Dimensions**: 1,536
- **Max input**: 8,192 tokens
- **Cost**: $0.10 per 1M tokens

#### Titan Embeddings V2
```json
"EmbeddingModelId": "amazon.titan-embed-text-v2:0"
```
- **Dimensions**: 1,024 (default) or configurable
- **Max input**: 8,192 tokens
- **Cost**: $0.02 per 1M tokens

### Image Generation Models

#### Stable Diffusion XL (Default)
```json
"ImageModelId": "stability.stable-diffusion-xl-v1"
```
- **Resolution**: 1024x1024
- **Cost**: $0.04 per image

## Configuration Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `Region` | string | (required) | AWS region (e.g., "us-east-1") |
| `TextModelId` | string | Claude 3.5 Sonnet | Model for text generation |
| `EmbeddingModelId` | string | Titan Embeddings G1 | Model for embeddings |
| `ImageModelId` | string | Stable Diffusion XL | Model for images |
| `Temperature` | double | 0.7 | Randomness (0.0-1.0) |
| `MaxTokens` | int | 1024 | Maximum output tokens |
| `MaxRetries` | int | 3 | Retry attempts |
| `RequestTimeoutSeconds` | int | 60 | Request timeout |
| `UseLocalEmulator` | bool | false | Use LocalStack |
| `LocalEmulatorUrl` | Uri | null | LocalStack endpoint |

## IAM Permissions

### Minimum Required Permissions

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": [
        "bedrock:InvokeModel"
      ],
      "Resource": [
        "arn:aws:bedrock:*::foundation-model/anthropic.claude-*",
        "arn:aws:bedrock:*::foundation-model/amazon.titan-*",
        "arn:aws:bedrock:*::foundation-model/stability.stable-diffusion-*"
      ]
    }
  ]
}
```

### Model-Specific Permissions

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": "bedrock:InvokeModel",
      "Resource": [
        "arn:aws:bedrock:us-east-1::foundation-model/anthropic.claude-3-5-sonnet-20241022-v2:0",
        "arn:aws:bedrock:us-east-1::foundation-model/amazon.titan-embed-text-v1",
        "arn:aws:bedrock:us-east-1::foundation-model/stability.stable-diffusion-xl-v1"
      ]
    }
  ]
}
```

## LocalStack Support

### Configuration

```json
{
  "AIAssistance": {
    "Region": "us-east-1",
    "UseLocalEmulator": true,
    "LocalEmulatorUrl": "http://localhost:4566",
    "TextModelId": "anthropic.claude-3-5-sonnet-20241022-v2:0"
  }
}
```

### Docker Compose

```yaml
version: '3.8'
services:
  localstack:
    image: localstack/localstack:latest
    ports:
      - "4566:4566"
    environment:
      - SERVICES=bedrock
      - DEBUG=1
    volumes:
      - "./localstack:/var/lib/localstack"
```

**Note**: LocalStack's Bedrock support is limited. For full local testing, consider using mock implementations.

## Error Handling

All operations throw `GenesisException` on errors:

```csharp
try
{
    var text = await _aiAssistant.GenerateTextAsync(prompt);
}
catch (GenesisException ex)
{
    _logger.LogError(ex, "AI generation failed: {Message}", ex.Message);
    // Handle error appropriately
}
```

## Cost Estimation

### Text Generation Example
- Claude 3.5 Sonnet
- 1,000 requests/day
- 500 input tokens per request
- 1,000 output tokens per request

**Monthly Cost**:
- Input: 30,000 requests × 500 tokens = 15M tokens × $3 = $45
- Output: 30,000 requests × 1,000 tokens = 30M tokens × $15 = $450
- **Total: $495/month**

### Embeddings Example
- Titan Embeddings G1
- 10,000 documents indexed
- 500 tokens per document

**One-time Cost**:
- 10,000 × 500 = 5M tokens × $0.10 = $0.50

### Image Generation Example
- Stable Diffusion XL
- 100 images/day

**Monthly Cost**:
- 3,000 images × $0.04 = $120/month

## Best Practices

### 1. Temperature Settings
- **Creative tasks**: 0.7-1.0 (blog posts, stories)
- **Factual tasks**: 0.0-0.3 (summaries, Q&A)
- **Balanced tasks**: 0.4-0.6 (general content)

### 2. Token Management
- Set `MaxTokens` based on use case
- Short responses: 256-512 tokens
- Medium responses: 1024-2048 tokens
- Long responses: 4096+ tokens

### 3. Prompt Engineering
```csharp
// Good: Clear, specific prompt
var prompt = "Write a 3-paragraph product description for a smart thermostat. " +
             "Focus on energy savings and ease of use. " +
             "Target audience: homeowners aged 30-50.";

// Poor: Vague prompt
var prompt = "Write about thermostats";
```

### 4. Caching Embeddings
```csharp
// Cache embeddings to avoid redundant API calls
var cacheKey = $"embedding:{Hash(text)}";
if (!_cache.TryGetValue(cacheKey, out float[] embedding))
{
    embedding = await _aiAssistant.GenerateEmbeddingAsync(text);
    _cache.Set(cacheKey, embedding, TimeSpan.FromDays(30));
}
```

### 5. Batch Processing
```csharp
// Process in batches to manage rate limits
var batches = documents.Chunk(10);
foreach (var batch in batches)
{
    var tasks = batch.Select(doc => _aiAssistant.GenerateEmbeddingAsync(doc));
    var embeddings = await Task.WhenAll(tasks);
    await Task.Delay(TimeSpan.FromSeconds(1)); // Rate limiting
}
```

## Troubleshooting

### Model Not Found
**Error**: `ResourceNotFoundException`
**Solution**: Verify model access in AWS Bedrock console. Some models require manual enablement.

### Throttling Errors
**Error**: `ThrottlingException`
**Solution**: Implement exponential backoff and increase `MaxRetries`.

### Timeout Errors
**Error**: `TimeoutException`
**Solution**: Increase `RequestTimeoutSeconds` for image generation (can take 30+ seconds).

### Invalid Response Format
**Error**: `GenesisException: Invalid response format`
**Solution**: Check model ID matches the expected format (Claude vs Titan).

## License

Copyright (C) 2026 Clarivex Technologies Private Limited. All Rights Reserved.

## Support

- **Documentation**: https://clarivex.tech/docs/genesis/aiassistance
- **Issues**: https://github.com/clarivex/pervaxis-genesis/issues
- **Email**: support@clarivex.tech
