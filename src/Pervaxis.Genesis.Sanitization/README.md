# Pervaxis.Genesis.Sanitization

Server-side input sanitization module for the Pervaxis Genesis platform. Prevents stored/reflected XSS at the service layer using whitelist-based HTML sanitization.

## Features

- **ISanitizer interface** — `StripAll`, `SanitizeHtml`, and profile-based `Sanitize` methods
- **Three built-in profiles** — PlainText, SafeHtml, Markdown
- **Custom profiles** — Define additional profiles via configuration
- **[Sanitize] attribute** — Declarative sanitization on DTO string properties at model binding time
- **FluentValidation extensions** — `.Sanitized()` (transform) and `.MustBeSanitized()` (reject)
- **Global middleware** — Optional auto-sanitization of POST/PUT/PATCH request bodies (off by default)
- **Observability** — Metrics (operations, threats detected, duration) and structured logging

## Quick Start

```csharp
// Program.cs
builder.Services.AddGenesisSanitization(builder.Configuration.GetSection("Genesis:Sanitization"));
app.UseGenesisSanitization();
```

```json
// appsettings.json
{
  "Genesis": {
    "Sanitization": {
      "DefaultProfile": "PlainText",
      "AllowCustomProfiles": true,
      "MaxInputLength": 1000000,
      "EnableMiddleware": false
    }
  }
}
```

## Usage

### Explicit in service layer

```csharp
public class CommentService
{
    private readonly ISanitizer _sanitizer;

    public async Task<Comment> CreateAsync(CreateCommentRequest request)
    {
        var safeContent = _sanitizer.SanitizeHtml(request.Body);
        // ... store safeContent
    }
}
```

### Declarative on DTO

```csharp
public record CreateCommentRequest
{
    [Sanitize(Profile = "SafeHtml")]
    public required string Body { get; init; }
}
```

### FluentValidation

```csharp
public class CreateCommentValidator : AbstractValidator<CreateCommentRequest>
{
    public CreateCommentValidator(ISanitizer sanitizer)
    {
        RuleFor(x => x.Body)
            .Sanitized(sanitizer, "SafeHtml")
            .NotEmpty();
    }
}
```

## Implementation

Uses [HtmlSanitizer](https://github.com/mganss/HtmlSanitizer) (Ganss.Xss) — a mature, battle-tested .NET library with whitelist-based sanitization that handles edge cases including nested encoding, unicode tricks, and attribute injection.
