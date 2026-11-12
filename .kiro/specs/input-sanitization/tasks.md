# Implementation Plan: Input Sanitization

## Overview

This plan implements the Genesis Input Sanitization module as a single project: `Pervaxis.Genesis.Sanitization`. Tasks are ordered to build foundational types first (profiles, interfaces, options), then the core sanitizer implementation, followed by integration points (attribute, FluentValidation, middleware), observability, and finally tests.

## Status: ✅ IMPLEMENTED

**Branch:** `feature/genesis-input-sanitization`  
**Tests:** 96 passing (sanitizer core, options validation, DI registration, profile registry, XSS bypass vectors)  
**Build:** 0 warnings, 0 errors

## Tasks

- [x] 1. Set up project structure and core abstractions ✅
  - [x] 1.1 Create project files and directory structure ✅
    - Created `src/Pervaxis.Genesis.Sanitization/Pervaxis.Genesis.Sanitization.csproj` targeting net10.0 with FrameworkReference to Microsoft.AspNetCore.App, ProjectReference to Pervaxis.Genesis.Base, PackageReference to HtmlSanitizer (8.1.870) and FluentValidation (11.11.0)
    - Created `tests/Pervaxis.Genesis.Sanitization.Tests/Pervaxis.Genesis.Sanitization.Tests.csproj` with xUnit, NSubstitute, FluentAssertions, Microsoft.AspNetCore.Mvc.Testing
    - Created all subdirectory folders per the design project structure
    - Added all project references to `Pervaxis.Genesis.slnx`
    - _Requirements: 1.1, 1.2_

  - [x] 1.2 Define `ISanitizer` interface ✅
    - Created `src/Pervaxis.Genesis.Sanitization/Abstractions/ISanitizer.cs` with methods: `StripAll`, `SanitizeHtml`, `Sanitize(string, SanitizationProfile)`, `Sanitize(string, string)`
    - _Requirements: 3.1, 3.2, 3.3, 3.4_

  - [x] 1.3 Define `SanitizationProfile` class with built-in profiles ✅
    - Created `src/Pervaxis.Genesis.Sanitization/Abstractions/SanitizationProfile.cs` with static readonly PlainText, SafeHtml, Markdown instances
    - Includes AllowedTags, AllowedAttributes, AllowedUrlSchemes as immutable collections
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 4.7, 4.8, 4.9, 4.10_

- [x] 2. Implement options and configuration ✅
  - [x] 2.1 Implement `SanitizationOptions` with validation ✅
    - Created `src/Pervaxis.Genesis.Sanitization/Options/SanitizationOptions.cs` extending `GenesisOptionsBase`
    - Includes DefaultProfile, AllowCustomProfiles, CustomProfiles, MaxInputLength, EnableMiddleware, MiddlewareExcludedRoutes
    - Implements `Validate()` method
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 2.7, 2.8, 2.9, 2.10, 2.11, 2.12_

  - [x] 2.2 Implement `CustomProfileDefinition` ✅
    - Created `src/Pervaxis.Genesis.Sanitization/Options/CustomProfileDefinition.cs`
    - _Requirements: 5.1, 5.2_

- [x] 3. Implement profile registry and core sanitizer ✅
  - [x] 3.1 Implement `ProfileRegistry` ✅
    - Created `src/Pervaxis.Genesis.Sanitization/Services/ProfileRegistry.cs`
    - Registers built-in profiles at construction, loads custom profiles from options
    - Creates pre-configured HtmlSanitizer instance per profile (immutable after startup)
    - _Requirements: 1.8, 5.1, 5.2, 5.3, 5.4, 5.5, 5.6_

  - [x] 3.2 Implement `GenesisSanitizer` ✅
    - Created `src/Pervaxis.Genesis.Sanitization/Services/GenesisSanitizer.cs` implementing ISanitizer
    - StripAll → uses regex-based HTML stripping with null byte removal and entity decoding
    - SanitizeHtml → uses SafeHtml HtmlSanitizer instance
    - Sanitize(input, profile) → lookup in registry by profile.Name
    - Sanitize(input, profileName) → lookup in registry by name
    - Null/empty passthrough, MaxInputLength check, thread-safe (all state is immutable)
    - _Requirements: 3.1–3.10, 11.1–11.7_

- [x] 4. Implement DI registration extensions ✅
  - [x] 4.1 Implement `SanitizationServiceCollectionExtensions` ✅
    - Created `src/Pervaxis.Genesis.Sanitization/Extensions/SanitizationServiceCollectionExtensions.cs`
    - Both IConfiguration and Action<Options> overloads, TryAdd pattern, null guards
    - Registers ProfileRegistry, GenesisSanitizer/ISanitizer as singletons, SanitizeActionFilter
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 1.8_

  - [x] 4.2 Implement `SanitizationApplicationBuilderExtensions` ✅
    - Created `src/Pervaxis.Genesis.Sanitization/Extensions/SanitizationApplicationBuilderExtensions.cs`
    - `UseGenesisSanitization()` extension method
    - _Requirements: 8.1, 8.9_

- [x] 5. Implement the [Sanitize] attribute and action filter ✅
  - [x] 5.1 Implement `SanitizeAttribute` ✅
    - Created `src/Pervaxis.Genesis.Sanitization/Filters/SanitizeAttribute.cs`
    - AttributeUsage(Property), optional Profile property
    - _Requirements: 6.1, 6.2_

  - [x] 5.2 Implement `SanitizeActionFilter` ✅
    - Created `src/Pervaxis.Genesis.Sanitization/Filters/SanitizeActionFilter.cs`
    - IAsyncActionFilter that scans action arguments for properties with [Sanitize]
    - Sanitizes before validation, handles null values as no-op
    - Ordered before FluentValidation's filter (order: -100)
    - _Requirements: 6.3, 6.4, 6.5, 6.6_

- [x] 6. Implement FluentValidation extensions ✅
  - [x] 6.1 Implement `SanitizationRuleBuilderExtensions` ✅
    - Created `src/Pervaxis.Genesis.Sanitization/Extensions/SanitizationRuleBuilderExtensions.cs`
    - `.Sanitized(sanitizer)` — transforms using default profile, sets property via reflection
    - `.Sanitized(sanitizer, profileName)` — transforms using named profile
    - `.Sanitized(sanitizer, profile)` — transforms using profile instance
    - `.MustBeSanitized(sanitizer)` — validates (fails if content would be stripped)
    - `.MustBeSanitized(sanitizer, profileName)` — validates against named profile
    - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5, 7.6, 7.7_

- [x] 7. Implement global sanitization middleware ✅
  - [x] 7.1 Implement `SanitizationMiddleware` ✅
    - Created `src/Pervaxis.Genesis.Sanitization/Middleware/SanitizationMiddleware.cs`
    - Intercepts POST/PUT/PATCH with JSON bodies when EnableMiddleware is true
    - Deserializes body, recursively sanitizes string leaves, re-serializes and replaces stream
    - Route exclusion matching (wildcard and exact), pass-through on invalid JSON or non-JSON Content-Type
    - Logs at Information level with route and modified field count
    - _Requirements: 8.1, 8.2, 8.3, 8.4, 8.5, 8.6, 8.7, 8.8_

- [x] 8. Implement observability (metrics and logging) ✅
  - [x] 8.1 Implement `SanitizationMetrics` ✅
    - Created `src/Pervaxis.Genesis.Sanitization/Diagnostics/SanitizationMetrics.cs`
    - Counters: operations (tagged: profile, source), threats_detected (tagged: profile, source)
    - Histogram: duration_ms (tagged: profile)
    - _Requirements: 9.1, 9.2, 9.3, 9.4, 9.5_

  - [x] 8.2 Implement `SanitizationLogMessages` ✅
    - Created `src/Pervaxis.Genesis.Sanitization/Diagnostics/SanitizationLogMessages.cs`
    - Source-generated LoggerMessage attributes for zero-allocation logging
    - Warning: threat detected (profile, source, original length, output length)
    - Debug: clean input (profile, source)
    - Information: custom profile loaded, middleware sanitized request
    - _Requirements: 10.1, 10.2, 10.3, 10.4, 10.5_

- [x] 9. Unit tests — core functionality ✅
  - [x] 9.1 Unit tests for DI registration ✅
    - Null guards, service resolution, idempotent registration, singleton lifetime
    - _Requirements: 1.1–1.8_

  - [x] 9.2 Unit tests for options validation ✅
    - Boundary cases for MaxInputLength, DefaultProfile, custom profile name conflicts
    - _Requirements: 2.8–2.12_

  - [x] 9.3 Unit tests for GenesisSanitizer ✅
    - StripAll removes all HTML, SanitizeHtml allows safe tags, Sanitize by profile/name
    - Null/empty passthrough, MaxInputLength enforcement, unknown profile exception, thread safety
    - _Requirements: 3.1–3.10_

  - [x] 9.4 Unit tests for built-in profiles ✅
    - Covered within GenesisSanitizerTests — PlainText strips everything, SafeHtml allows/strips correct elements, Markdown extends SafeHtml
    - _Requirements: 4.1–4.10_

  - [x] 9.5 Unit tests for custom profiles ✅
    - Load from config, name conflict detection, empty AllowedTags = PlainText behavior, disabled ignores customs
    - _Requirements: 5.1–5.6_

- [x] 10. Security tests — XSS bypass resistance ✅
  - [x] 10.1 OWASP XSS Filter Evasion Cheat Sheet vectors ✅
    - Script tag variations (5 cases), event handlers (6 cases), JavaScript URLs (5 cases)
    - Iframe/object/embed (4 cases), CSS injection (3 cases), SVG/MathML (3 cases)
    - Case variation (3 cases), data URIs (2 cases), encoded attacks (2 cases)
    - Null byte injection, form injection (3 cases), meta/base/link (3 cases)
    - Complex nested attack, idempotence verification
    - Total: 42 security-focused test cases
    - _Requirements: 11.1, 11.2, 11.3, 11.4, 11.5, 11.6, 11.7_

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1"] },
    { "id": 1, "tasks": ["1.2", "1.3"] },
    { "id": 2, "tasks": ["2.1", "2.2"] },
    { "id": 3, "tasks": ["3.1", "3.2"] },
    { "id": 4, "tasks": ["4.1", "4.2"] },
    { "id": 5, "tasks": ["5.1", "5.2", "6.1"] },
    { "id": 6, "tasks": ["7.1", "8.1", "8.2"] },
    { "id": 7, "tasks": ["9.1", "9.2", "9.3", "9.4", "9.5", "9.6", "9.7", "9.8"] },
    { "id": 8, "tasks": ["10.1"] }
  ]
}
```

## Notes

- Single project (no AWS split needed — sanitization is in-memory, no external store)
- Uses HtmlSanitizer (Ganss.Xss v8.1.870) for SafeHtml/Markdown profiles
- PlainText uses regex-based stripping with null byte removal + entity decoding for robustness
- One immutable HtmlSanitizer instance per profile created at startup for thread safety
- Middleware is opt-in (off by default) — can break legitimate inputs if applied blindly
- FluentValidation offers BOTH `.Sanitized()` (transform) and `.MustBeSanitized()` (reject)
- No fail-open needed — no external dependencies, all in-memory
- Full solution builds with 0 warnings, 0 errors
- All solution tests pass (including 96 new sanitization tests)
