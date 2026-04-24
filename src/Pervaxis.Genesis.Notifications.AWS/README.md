# Pervaxis.Genesis.Notifications.AWS

AWS-based notification provider for the Pervaxis Genesis platform. Supports email (Amazon SES), SMS, and push notifications (Amazon SNS).

## Features

- ✅ **Email Notifications** - Send transactional emails via Amazon SES
- ✅ **Templated Emails** - Use SES email templates with dynamic data
- ✅ **SMS Messages** - Send SMS via Amazon SNS
- ✅ **Push Notifications** - Send mobile push notifications via SNS
- ✅ **LocalStack Support** - Test locally without AWS
- ✅ **Configuration Validation** - Validate options at startup
- ✅ **Structured Logging** - Full logging with Microsoft.Extensions.Logging
- ✅ **Async/Await** - Modern async patterns with CancellationToken support

## Installation

```bash
dotnet add package Pervaxis.Genesis.Notifications.AWS
```

## Configuration

### appsettings.json

```json
{
  "Notification": {
    "Region": "us-east-1",
    "FromEmail": "noreply@example.com",
    "FromName": "Pervaxis Platform",
    "ConfigurationSetName": "pervaxis-emails",
    "SmsTopicArn": "arn:aws:sns:us-east-1:123456789012:sms-notifications",
    "PushPlatformApplicationArn": "arn:aws:sns:us-east-1:123456789012:app/GCM/MyApp",
    "MaxRetries": 3,
    "RequestTimeoutSeconds": 30,
    "UseLocalEmulator": false,
    "LocalEmulatorUrl": "http://localhost:4566"
  }
}
```

### Configuration Options

| Option | Type | Required | Default | Description |
|--------|------|----------|---------|-------------|
| `Region` | string | Yes | - | AWS region (e.g., "us-east-1") |
| `FromEmail` | string | Yes | - | Verified sender email address |
| `FromName` | string | No | - | Display name for sender |
| `ConfigurationSetName` | string | No | - | SES configuration set for tracking |
| `SmsTopicArn` | string | No | - | SNS topic ARN for SMS delivery tracking |
| `PushPlatformApplicationArn` | string | No | - | SNS platform application ARN for push |
| `MaxRetries` | int | No | 3 | Maximum retry attempts |
| `RequestTimeoutSeconds` | int | No | 30 | Request timeout in seconds |
| `UseLocalEmulator` | bool | No | false | Use LocalStack for local testing |
| `LocalEmulatorUrl` | string | No | - | LocalStack endpoint URL |

## Usage

### 1. Register in Dependency Injection

#### Using Configuration

```csharp
using Pervaxis.Genesis.Notifications.AWS.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add notification services from appsettings.json
builder.Services.AddGenesisNotifications(
    builder.Configuration.GetSection("Notification"));

var app = builder.Build();
```

#### Using Action Configuration

```csharp
builder.Services.AddGenesisNotifications(options =>
{
    options.Region = "us-east-1";
    options.FromEmail = "noreply@example.com";
    options.FromName = "My App";
    options.UseLocalEmulator = true;
    options.LocalEmulatorUrl = "http://localhost:4566";
});
```

### 2. Send Email

```csharp
using Pervasis.Core.Abstractions.Genesis.Modules;

public class EmailService
{
    private readonly INotification _notification;

    public EmailService(INotification notification)
    {
        _notification = notification;
    }

    public async Task SendWelcomeEmailAsync(string userEmail, CancellationToken ct)
    {
        var result = await _notification.SendEmailAsync(
            to: userEmail,
            subject: "Welcome to Pervaxis!",
            body: "<h1>Welcome!</h1><p>Thanks for signing up.</p>",
            isHtml: true,
            cancellationToken: ct);

        if (result.IsSuccess)
        {
            Console.WriteLine("Email sent successfully!");
        }
        else
        {
            Console.WriteLine($"Failed to send email: {result.ErrorMessage}");
        }
    }
}
```

### 3. Send Templated Email

```csharp
public async Task SendOrderConfirmationAsync(string userEmail, string orderNumber, CancellationToken ct)
{
    var templateData = new Dictionary<string, string>
    {
        ["orderNumber"] = orderNumber,
        ["userName"] = "John Doe",
        ["orderTotal"] = "$99.99"
    };

    var result = await _notification.SendTemplatedEmailAsync(
        to: userEmail,
        templateId: "order-confirmation-template",
        templateData: templateData,
        cancellationToken: ct);

    if (!result.IsSuccess)
    {
        _logger.LogError("Failed to send order confirmation: {Error}", result.ErrorMessage);
    }
}
```

### 4. Send SMS

```csharp
public async Task SendVerificationCodeAsync(string phoneNumber, string code, CancellationToken ct)
{
    var message = $"Your verification code is: {code}. Valid for 5 minutes.";

    var result = await _notification.SendSmsAsync(
        phoneNumber: "+1234567890",
        message: message,
        cancellationToken: ct);

    if (!result.IsSuccess)
    {
        _logger.LogError("Failed to send SMS: {Error}", result.ErrorMessage);
    }
}
```

### 5. Send Push Notification

```csharp
public async Task SendPushNotificationAsync(string deviceToken, CancellationToken ct)
{
    var customData = new Dictionary<string, string>
    {
        ["orderId"] = "12345",
        ["action"] = "view_order"
    };

    var result = await _notification.SendPushAsync(
        deviceToken: deviceToken,
        title: "Order Update",
        message: "Your order has been shipped!",
        data: customData,
        cancellationToken: ct);

    if (!result.IsSuccess)
    {
        _logger.LogError("Failed to send push: {Error}", result.ErrorMessage);
    }
}
```

## IAM Permissions

### SES Email Permissions

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": [
        "ses:SendEmail",
        "ses:SendTemplatedEmail",
        "ses:SendRawEmail"
      ],
      "Resource": "*"
    }
  ]
}
```

### SNS SMS and Push Permissions

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": [
        "sns:Publish",
        "sns:CreatePlatformEndpoint",
        "sns:DeletePlatformEndpoint",
        "sns:GetEndpointAttributes",
        "sns:SetEndpointAttributes"
      ],
      "Resource": "*"
    }
  ]
}
```

## SES Setup

### 1. Verify Email Address or Domain

Before sending emails, you must verify the sender email address or domain in SES:

```bash
# Verify a single email address
aws ses verify-email-identity --email-address noreply@example.com

# Verify an entire domain (requires DNS TXT record)
aws ses verify-domain-identity --domain example.com
```

### 2. Create Email Template (Optional)

```bash
aws ses create-template --cli-input-json file://template.json
```

**template.json:**
```json
{
  "Template": {
    "TemplateName": "order-confirmation-template",
    "SubjectPart": "Order Confirmation #{{orderNumber}}",
    "HtmlPart": "<h1>Thanks {{userName}}!</h1><p>Order total: {{orderTotal}}</p>",
    "TextPart": "Thanks {{userName}}! Order total: {{orderTotal}}"
  }
}
```

### 3. Move Out of SES Sandbox

By default, SES is in sandbox mode (can only send to verified addresses). Request production access:

```bash
# Open AWS Console → SES → Account dashboard → Request production access
```

## SNS Setup

### 1. Create Platform Application for Push Notifications

```bash
# For Firebase Cloud Messaging (FCM/GCM)
aws sns create-platform-application \
  --name MyApp \
  --platform GCM \
  --attributes PlatformCredential=YOUR_FCM_SERVER_KEY

# For Apple Push Notification Service (APNS)
aws sns create-platform-application \
  --name MyApp \
  --platform APNS \
  --attributes PlatformCredential=YOUR_APNS_CERTIFICATE
```

### 2. Configure SMS Settings

```bash
# Set default SMS type (Transactional or Promotional)
aws sns set-sms-attributes \
  --attributes DefaultSMSType=Transactional

# Set spending limit (USD per month)
aws sns set-sms-attributes \
  --attributes MonthlySpendLimit=100
```

## LocalStack Configuration

For local development without AWS:

### docker-compose.yml

```yaml
services:
  localstack:
    image: localstack/localstack:latest
    ports:
      - "4566:4566"
    environment:
      - SERVICES=ses,sns
      - DEBUG=1
      - DATA_DIR=/tmp/localstack/data
    volumes:
      - "./localstack-data:/tmp/localstack"
```

### appsettings.Development.json

```json
{
  "Notification": {
    "Region": "us-east-1",
    "FromEmail": "test@example.com",
    "UseLocalEmulator": true,
    "LocalEmulatorUrl": "http://localhost:4566"
  }
}
```

### Verify LocalStack SES

```bash
# List verified email addresses
awslocal ses list-identities

# Send test email
awslocal ses send-email \
  --from test@example.com \
  --destination ToAddresses=user@example.com \
  --message Subject={Data="Test"},Body={Text={Data="Hello from LocalStack!"}}
```

## Troubleshooting

### 1. Email Not Sending

**Problem:** `MessageRejected: Email address is not verified`

**Solution:**
- Verify the sender email address in SES Console
- If in sandbox mode, also verify recipient addresses
- Request production access to send to any address

### 2. SMS Not Sending

**Problem:** `InvalidParameterException: Invalid phone number`

**Solution:**
- Use E.164 format: `+[country code][phone number]`
- Example: `+12345678901` (not `1-234-567-8901`)
- Ensure phone number doesn't have spaces or dashes

### 3. Push Notification Failing

**Problem:** `InvalidParameterException: Invalid device token`

**Solution:**
- Verify `PushPlatformApplicationArn` is configured
- Ensure device token is valid and registered
- Check FCM/APNS credentials in SNS platform application

### 4. High Latency

**Problem:** Email/SMS taking too long

**Solution:**
- Increase `RequestTimeoutSeconds` in configuration
- Ensure you're using the nearest AWS region
- Check AWS service health dashboard
- Consider async fire-and-forget pattern for non-critical notifications

### 5. Rate Limiting

**Problem:** `Throttling: Rate exceeded`

**Solution:**
- SES has sending quotas (e.g., 14 emails/second in sandbox)
- Request higher sending limits in SES Console
- Implement exponential backoff with `MaxRetries`
- For bulk emails, use SES bulk sending APIs

## Best Practices

1. **Email Verification**
   - Always verify sender email/domain in SES
   - Use domain verification for better deliverability
   - Set up SPF, DKIM, and DMARC records

2. **Template Management**
   - Use SES templates for consistent branding
   - Version your templates (e.g., `welcome-v1`, `welcome-v2`)
   - Test templates with sample data before production

3. **SMS Cost Management**
   - SMS can be expensive—set spending limits
   - Use `Transactional` type for OTP/alerts
   - Use `Promotional` type for marketing (cheaper but rate-limited)

4. **Push Notification Tokens**
   - Cache device tokens to avoid repeated endpoint creation
   - Clean up stale endpoints periodically
   - Handle token refresh in your mobile app

5. **Error Handling**
   - Always check `ProviderResult.IsSuccess`
   - Log errors with structured logging
   - Implement retry logic for transient failures

6. **Configuration Sets**
   - Use SES configuration sets to track email metrics
   - Monitor bounce and complaint rates
   - Set up SNS notifications for bounces/complaints

## Related Packages

- **Pervaxis.Genesis.Base** - Base abstractions and exceptions
- **Pervaxis.Core.Abstractions** - Core module interfaces (`INotification`)
- **Pervaxis.Genesis.Messaging.AWS** - For application-level messaging (SQS/SNS queuing)

## License

Copyright © 2026 Clarivex Technologies Private Limited. All rights reserved.
