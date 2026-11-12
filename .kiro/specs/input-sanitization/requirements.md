# Requirements Document

## Introduction

The Genesis Input Sanitization module provides server-side input sanitization for the Pervaxis platform, closing the last meaningful security gap against stored/reflected XSS at the service layer. Security headers alone only cover browser-side rendering — this module prevents dangerous content from ever being persisted. The module exposes an `ISanitizer` interface with built-in profiles (PlainText, SafeHtml, Markdown) and supports custom profiles via configuration. It integrates with FluentValidation, ASP.NET Core model binding, and optionally as global middleware. The module follows existing Genesis patterns for DI registration, options validation, and observability.

## Glossary

- **Sanitization_Module**: The Genesis abstraction and implementation library (`Pervaxis.Genesis.Sanitization`) that defines the contracts, profiles, options, default implementation, attribute, FluentValidation extensions, middleware, and diagnostics for input sanitization.
- **ISanitizer**: The primary abstraction interface for performing sanitization operations against input strings.
- **SanitizationProfile**: A configuration object that defines which HTML tags, attributes, CSS properties, and URL schemes are allowed. Includes three built-in static profiles and supports custom profiles defined via configuration.
- **PlainText_Profile**: A built-in profile that strips ALL HTML tags and returns plain text only.
- **SafeHtml_Profile**: A built-in profile that allows safe structural/formatting HTML (`<b>`, `<i>`, `<a>`, `<ul>`, `<ol>`, `<li>`, `<p>`, `<br>`) while stripping scripts, iframes, event handlers, `javascript:` URLs, `<style>`, `<object>`, and `<embed>`.
- **Markdown_Profile**: A built-in profile that allows the SafeHtml set plus Markdown-rendered elements (`<code>`, `<pre>`, `<h1>`–`<h6>`, `<blockquote>`, `<table>`, `<thead>`, `<tbody>`, `<tr>`, `<th>`, `<td>`, `<img>` with `src`/`alt`).
- **HtmlSanitizer**: The underlying third-party library (`Ganss.Xss.HtmlSanitizer`) used for whitelist-based HTML sanitization. Battle-tested, handles nested encoding, unicode tricks, and attribute injection.
- **SanitizeAttribute**: An ASP.NET Core model binding attribute (`[Sanitize]`) that sanitizes string properties at binding time before validation runs.
- **Sanitization_Middleware**: An optional ASP.NET Core middleware that auto-sanitizes all string fields in POST/PUT/PATCH request bodies. Off by default, opt-in via configuration.
- **Threat_Detection**: When sanitization actually strips dangerous content from an input (input ≠ output), indicating a potential attack attempt.
- **PervaxisMeter**: The static metrics factory from `Pervaxis.Core.Observability.Metrics` used to create counters and histograms.
- **Forge**: The code generation engine that auto-wires Genesis module registration into every generated service.

## Requirements

### Requirement 1: Module Registration

**User Story:** As a platform engineer, I want to register the Sanitization module using a standard Genesis extension method, so that it integrates consistently with other Genesis modules.

#### Acceptance Criteria

1. THE Sanitization_Module SHALL provide an `AddGenesisSanitization` extension method on `IServiceCollection` that accepts an `IConfiguration` parameter and returns `IServiceCollection` for method chaining.
2. THE Sanitization_Module SHALL provide an `AddGenesisSanitization` extension method on `IServiceCollection` that accepts an `Action<SanitizationOptions>` parameter and returns `IServiceCollection` for method chaining.
3. WHEN `AddGenesisSanitization` is called, THE Sanitization_Module SHALL register `ISanitizer` in the dependency injection container as a singleton.
4. WHEN `AddGenesisSanitization` is called with an `IConfiguration` parameter, THE Sanitization_Module SHALL bind options from the `Genesis:Sanitization` configuration section.
5. WHEN `AddGenesisSanitization` is called with an `Action<SanitizationOptions>` parameter, THE Sanitization_Module SHALL apply the action delegate to configure the options instance.
6. IF `AddGenesisSanitization` is called with a null `IServiceCollection`, null `IConfiguration`, or null `Action<SanitizationOptions>` parameter, THEN THE Sanitization_Module SHALL throw an `ArgumentNullException` identifying the null parameter by name.
7. IF `AddGenesisSanitization` is called multiple times on the same `IServiceCollection`, THEN THE Sanitization_Module SHALL register services using the TryAdd pattern, ensuring idempotent registration without duplicate service descriptors.
8. WHEN `AddGenesisSanitization` is called, THE Sanitization_Module SHALL register all built-in profiles (PlainText, SafeHtml, Markdown) in the profile registry and create their corresponding pre-configured `HtmlSanitizer` instances at startup.

### Requirement 2: Options Configuration

**User Story:** As a platform engineer, I want to configure the Sanitization module through a validated options class, so that misconfiguration is caught early at startup.

#### Acceptance Criteria

1. THE SanitizationOptions SHALL extend `GenesisOptionsBase`.
2. THE SanitizationOptions SHALL include a `DefaultProfile` property of type string for specifying the default sanitization profile name, with a default value of `"PlainText"`.
3. THE SanitizationOptions SHALL include an `AllowCustomProfiles` property of type boolean with a default value of `true`, controlling whether custom profiles defined in configuration are loaded.
4. THE SanitizationOptions SHALL include a `CustomProfiles` property of type `Dictionary<string, CustomProfileDefinition>` for defining additional profiles via configuration.
5. THE SanitizationOptions SHALL include a `MaxInputLength` property of type integer with a default value of `1_000_000` (1MB of characters) and a valid range of 1 to 10_000_000, controlling the maximum string length accepted for sanitization.
6. THE SanitizationOptions SHALL include an `EnableMiddleware` property of type boolean with a default value of `false`, controlling whether the global sanitization middleware is active.
7. THE SanitizationOptions SHALL include a `MiddlewareExcludedRoutes` property of type `List<string>` for specifying route patterns excluded from middleware sanitization.
8. THE SanitizationOptions `Validate()` method SHALL return false when `base.Validate()` returns false.
9. THE SanitizationOptions `Validate()` method SHALL return false when `DefaultProfile` is null or empty.
10. THE SanitizationOptions `Validate()` method SHALL return false when `DefaultProfile` does not match any built-in profile name or configured custom profile name.
11. THE SanitizationOptions `Validate()` method SHALL return false when `MaxInputLength` is less than 1 or greater than 10_000_000.
12. THE SanitizationOptions `Validate()` method SHALL return false when any `CustomProfileDefinition` has a null or empty `Name` property or a `Name` that conflicts with a built-in profile name.

### Requirement 3: ISanitizer Interface

**User Story:** As a domain developer, I want a clean sanitizer interface with multiple operation modes, so that I can sanitize input appropriately for my context.

#### Acceptance Criteria

1. THE Sanitization_Module SHALL define an `ISanitizer` interface with a `StripAll(string input)` method that removes all HTML tags and returns plain text only.
2. THE Sanitization_Module SHALL define an `ISanitizer` interface with a `SanitizeHtml(string input)` method that allows safe HTML (SafeHtml_Profile) and strips dangerous elements.
3. THE Sanitization_Module SHALL define an `ISanitizer` interface with a `Sanitize(string input, SanitizationProfile profile)` method that applies the specified profile's rules.
4. THE Sanitization_Module SHALL define an `ISanitizer` interface with a `Sanitize(string input, string profileName)` method that resolves the named profile from the registry and applies its rules.
5. WHEN `StripAll` is called with a null or empty input, THE ISanitizer SHALL return the input unchanged (null returns null, empty returns empty).
6. WHEN `SanitizeHtml` is called with a null or empty input, THE ISanitizer SHALL return the input unchanged.
7. WHEN `Sanitize` is called with a null or empty input, THE ISanitizer SHALL return the input unchanged.
8. WHEN `Sanitize` is called with a `profileName` that does not exist in the registry, THE ISanitizer SHALL throw an `ArgumentException` identifying the invalid profile name.
9. WHEN any sanitization method is called with an input exceeding `SanitizationOptions.MaxInputLength`, THE ISanitizer SHALL throw an `ArgumentException` indicating the input exceeds the maximum allowed length.
10. THE ISanitizer implementation SHALL be thread-safe for concurrent calls.

### Requirement 4: Built-In Sanitization Profiles

**User Story:** As a domain developer, I want pre-configured profiles for common sanitization scenarios, so that I don't need to define tag allowlists myself.

#### Acceptance Criteria

1. THE PlainText_Profile SHALL strip ALL HTML tags, attributes, and content, returning only the text content of the input.
2. THE PlainText_Profile SHALL decode HTML entities (e.g., `&amp;` → `&`, `&lt;` → `<`) in the output text.
3. THE SafeHtml_Profile SHALL allow the following tags: `<b>`, `<i>`, `<strong>`, `<em>`, `<a>`, `<ul>`, `<ol>`, `<li>`, `<p>`, `<br>`, `<span>`.
4. THE SafeHtml_Profile SHALL allow the `href` attribute on `<a>` tags, restricted to `http:`, `https:`, and `mailto:` URL schemes only.
5. THE SafeHtml_Profile SHALL strip the following dangerous elements entirely (including content): `<script>`, `<iframe>`, `<object>`, `<embed>`, `<style>`, `<link>`, `<meta>`, `<base>`, `<form>`, `<input>`, `<textarea>`, `<select>`, `<button>`.
6. THE SafeHtml_Profile SHALL strip all event handler attributes (`on*` — e.g., `onclick`, `onerror`, `onload`, `onmouseover`).
7. THE SafeHtml_Profile SHALL strip `javascript:`, `vbscript:`, and `data:` URL schemes from all attributes that accept URLs.
8. THE Markdown_Profile SHALL allow all tags permitted by SafeHtml_Profile plus: `<code>`, `<pre>`, `<h1>` through `<h6>`, `<blockquote>`, `<table>`, `<thead>`, `<tbody>`, `<tr>`, `<th>`, `<td>`, `<img>`, `<hr>`, `<dl>`, `<dt>`, `<dd>`.
9. THE Markdown_Profile SHALL allow `src` and `alt` attributes on `<img>` tags, with `src` restricted to `http:` and `https:` URL schemes only.
10. THE Markdown_Profile SHALL allow `class` attribute on `<code>` and `<pre>` tags (for syntax highlighting CSS classes).

### Requirement 5: Custom Profiles

**User Story:** As a platform engineer, I want to define custom sanitization profiles via configuration, so that teams can create domain-specific sanitization rules without code changes.

#### Acceptance Criteria

1. WHEN `AllowCustomProfiles` is true, THE Sanitization_Module SHALL load custom profiles from the `CustomProfiles` section of `SanitizationOptions` at startup.
2. EACH custom profile SHALL define: a unique `Name`, an `AllowedTags` list, an `AllowedAttributes` dictionary (tag → allowed attribute names), and an `AllowedUrlSchemes` list.
3. WHEN a custom profile is loaded, THE Sanitization_Module SHALL create a configured `HtmlSanitizer` instance for that profile and register it in the profile registry.
4. IF `AllowCustomProfiles` is false, THE Sanitization_Module SHALL ignore any `CustomProfiles` entries in configuration and only use built-in profiles.
5. IF a custom profile `Name` conflicts with a built-in profile name (PlainText, SafeHtml, Markdown), THE Sanitization_Module SHALL throw an `InvalidOperationException` at startup identifying the conflicting name.
6. WHEN a custom profile's `AllowedTags` list is empty, THE Sanitization_Module SHALL treat it as equivalent to PlainText (strip all HTML).

### Requirement 6: Sanitize Attribute (Model Binding)

**User Story:** As a domain developer, I want to declaratively sanitize DTO string properties using an attribute, so that inputs are clean before validation runs.

#### Acceptance Criteria

1. THE Sanitization_Module SHALL provide a `[Sanitize]` attribute decorated with `[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]` that can be applied to string properties on request DTOs.
2. THE `[Sanitize]` attribute SHALL accept an optional `Profile` parameter of type string with a default value of null, where null means "use the `SanitizationOptions.DefaultProfile`" and a non-null value specifies the profile name to use.
3. WHEN model binding completes for a request DTO containing properties decorated with `[Sanitize]`, THE Sanitization_Module SHALL sanitize those property values using the specified profile BEFORE any validation (FluentValidation or DataAnnotations) executes.
4. IF the `[Sanitize]` attribute specifies a `Profile` that does not exist in the registry, THE Sanitization_Module SHALL throw an `InvalidOperationException` at the time of sanitization, resulting in an HTTP 500 response.
5. THE `[Sanitize]` attribute processing SHALL handle null property values as no-ops (null in, null out).
6. THE `[Sanitize]` attribute SHALL be processed by an ASP.NET Core action filter registered with an order that guarantees execution before FluentValidation's filter.

### Requirement 7: FluentValidation Extension

**User Story:** As a domain developer, I want FluentValidation extensions for sanitization, so that I can compose sanitization with other validation rules in a fluent pipeline.

#### Acceptance Criteria

1. THE Sanitization_Module SHALL provide a `.Sanitized()` extension method on `IRuleBuilder<T, string>` that transforms the property value by sanitizing it with the default profile before subsequent validation rules execute.
2. THE Sanitization_Module SHALL provide a `.Sanitized(string profileName)` extension method on `IRuleBuilder<T, string>` that transforms the property value by sanitizing it with the named profile.
3. THE Sanitization_Module SHALL provide a `.Sanitized(SanitizationProfile profile)` extension method on `IRuleBuilder<T, string>` that transforms the property value by sanitizing it with the provided profile instance.
4. THE Sanitization_Module SHALL provide a `.MustBeSanitized()` extension method on `IRuleBuilder<T, string>` that VALIDATES (does not transform) — failing validation if the input contains content that would be stripped by the default profile.
5. THE Sanitization_Module SHALL provide a `.MustBeSanitized(string profileName)` extension method on `IRuleBuilder<T, string>` that validates against the named profile.
6. WHEN `.Sanitized()` transforms a value, THE transformation SHALL occur inline in the FluentValidation pipeline using FluentValidation's `Transform()` API, ensuring subsequent rules (e.g., `.NotEmpty()`, `.MaximumLength()`) operate on the sanitized value.
7. WHEN `.MustBeSanitized()` detects content that would be stripped, THE validation failure message SHALL be: `"'{PropertyName}' contains disallowed content that would be removed by sanitization."`.

### Requirement 8: Global Sanitization Middleware

**User Story:** As a platform engineer, I want optional global middleware that auto-sanitizes request body strings, so that I can apply blanket protection without decorating every DTO.

#### Acceptance Criteria

1. THE Sanitization_Module SHALL provide a `UseGenesisSanitization` extension method on `IApplicationBuilder` that registers the Sanitization_Middleware in the request pipeline.
2. WHEN `SanitizationOptions.EnableMiddleware` is true, THE Sanitization_Middleware SHALL intercept POST, PUT, and PATCH requests with JSON bodies and sanitize all string properties in the deserialized request body using the `DefaultProfile`.
3. WHEN `SanitizationOptions.EnableMiddleware` is false, THE Sanitization_Middleware SHALL pass all requests through without modification.
4. THE Sanitization_Middleware SHALL NOT modify request bodies for routes matching any pattern in `SanitizationOptions.MiddlewareExcludedRoutes`.
5. THE Sanitization_Middleware SHALL re-serialize the sanitized body and replace the request body stream so downstream components receive the sanitized content.
6. THE Sanitization_Middleware SHALL NOT sanitize non-string properties (numbers, booleans, arrays of non-strings, nested objects are traversed but only string leaves are sanitized).
7. IF the request body cannot be deserialized as JSON (invalid JSON or non-JSON Content-Type), THE Sanitization_Middleware SHALL pass the request through unmodified without returning an error.
8. THE Sanitization_Middleware SHALL log at Information level when it sanitizes a request, including the route and the count of fields that were modified.
9. IF `UseGenesisSanitization` is called with a null `IApplicationBuilder`, THEN THE Sanitization_Module SHALL throw an `ArgumentNullException`.

### Requirement 9: Observability — Metrics

**User Story:** As an SRE, I want metrics on sanitization operations, so that I can monitor usage patterns and detect potential attack attempts.

#### Acceptance Criteria

1. WHEN a sanitization operation completes, THE Sanitization_Module SHALL increment the `genesis.sanitization.operations` counter metric by 1, tagged with `profile` (the profile name used) and `source` (values: `explicit`, `attribute`, `middleware`, `fluentvalidation`).
2. WHEN a sanitization operation detects and strips dangerous content (input ≠ output after sanitization), THE Sanitization_Module SHALL increment the `genesis.sanitization.threats_detected` counter metric by 1, tagged with `profile` and `source`.
3. WHEN a sanitization operation completes, THE Sanitization_Module SHALL record the elapsed time in the `genesis.sanitization.duration_ms` histogram metric in milliseconds, tagged with `profile`.
4. THE Sanitization_Module SHALL create all metrics as `static readonly` fields using `PervaxisMeter.CreateCounter<long>` and `PervaxisMeter.CreateHistogram<double>` with the unit parameter set to `"1"` for counters and `"ms"` for histograms.
5. IF metric emission fails for any reason, THEN THE Sanitization_Module SHALL suppress the failure silently without affecting the sanitization outcome.

### Requirement 10: Observability — Logging

**User Story:** As an SRE, I want structured logging for sanitization events, so that I can detect and investigate potential XSS attack attempts.

#### Acceptance Criteria

1. WHEN a sanitization operation strips dangerous content from input (threat detected), THE Sanitization_Module SHALL emit a structured log at Warning level containing: the profile name, the source (explicit/attribute/middleware/fluentvalidation), the length of the original input, the length of the sanitized output, and the count of elements removed.
2. WHEN a sanitization operation completes without stripping any content (clean input), THE Sanitization_Module SHALL emit a structured log at Debug level containing: the profile name and source.
3. WHEN a custom profile is loaded from configuration at startup, THE Sanitization_Module SHALL emit a structured log at Information level containing: the profile name, the count of allowed tags, and the count of allowed attributes.
4. WHEN the middleware sanitizes a request body, THE Sanitization_Module SHALL emit a structured log at Information level containing: the route, HTTP method, and count of string fields modified.
5. THE Sanitization_Module SHALL emit all structured logs using `ILogger<T>` with compile-time source-generated `LoggerMessage` methods.

### Requirement 11: Security — XSS Prevention

**User Story:** As a security engineer, I want the module to handle known XSS bypass techniques, so that the sanitization cannot be trivially evaded.

#### Acceptance Criteria

1. THE Sanitization_Module SHALL handle nested/double encoding attacks (e.g., `%253Cscript%253E`, `&#x3C;script&#x3E;`) by decoding all HTML entities before applying sanitization rules.
2. THE Sanitization_Module SHALL handle case-variation attacks (e.g., `<ScRiPt>`, `<SCRIPT>`) by performing case-insensitive tag matching.
3. THE Sanitization_Module SHALL handle null byte injection (e.g., `<scr\0ipt>`) by stripping null bytes before processing.
4. THE Sanitization_Module SHALL handle attribute injection via backticks, quotes, and expression syntax (e.g., `` style=`background:url(javascript:...)` ``).
5. THE Sanitization_Module SHALL handle SVG/MathML-based XSS vectors by stripping `<svg>` and `<math>` elements and their children unless explicitly allowed in the profile.
6. THE Sanitization_Module SHALL handle CSS expression attacks (e.g., `expression()`, `url(javascript:)`) by stripping style attributes unless explicitly allowed in the profile.
7. THE Sanitization_Module SHALL pass at minimum the OWASP XSS Filter Evasion Cheat Sheet vectors without allowing any script execution or dangerous attribute through.

### Requirement 12: Forge Integration

**User Story:** As a platform engineer, I want the Sanitization module to be selectable in Forge when generating service prints, so that generated services automatically include input sanitization.

#### Acceptance Criteria

1. THE Sanitization_Module SHALL be registered as an optional module in the Forge module catalog with the identifier `"Sanitization"` and category `"Security"`.
2. WHEN a user selects the Sanitization module in the Forge UI, THE generated service print SHALL include the `Pervaxis.Genesis.Sanitization` NuGet package reference in the service project file.
3. WHEN a user selects the Sanitization module in the Forge UI, THE generated service print SHALL include `AddGenesisSanitization` registration in `Program.cs` with configuration binding to the `Genesis:Sanitization` section.
4. WHEN a user selects the Sanitization module in the Forge UI, THE generated service print SHALL include a default `Genesis:Sanitization` configuration section in `appsettings.json` containing: `DefaultProfile` set to `"PlainText"`, `AllowCustomProfiles` set to `true`, `MaxInputLength` set to `1000000`, and `EnableMiddleware` set to `false`.
5. WHEN a user does not select the Sanitization module in the Forge UI, THE generated service print SHALL not contain any sanitization-related code, packages, or configuration.
