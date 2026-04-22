# RFC-XXXX — [Short Title]

> **RFC** (Request for Comments) is required for any change that is **breaking**, **cross-cutting**, or **significantly alters** a platform contract. Small, additive changes do not require an RFC — use a standard PR instead.

| Field | Value |
|-------|-------|
| **Status** | Draft / Under Review / Accepted / Rejected / Withdrawn |
| **RFC Number** | RFC-XXXX |
| **Date** | YYYY-MM-DD |
| **Author(s)** | Name, Name |
| **Target Version** | X.Y.0 (minor) or X.0.0 (major) |
| **Breaking Change** | Yes / No |
| **Spec Sections** | Section XX, Section XX |

---

## Summary

_One paragraph. What is being proposed and why? Who does it affect?_

---

## Motivation

_What problem does this RFC solve? What is the current limitation, pain point, or capability gap? Link to GitHub Issues, incidents, or spec requirements._

---

## Detailed Design

_The full technical specification. Be precise enough that an engineer who has not been in any prior discussions could implement this from this document alone._

### Interface / Contract Changes

```csharp
// Before
public interface ISomeContract
{
    // existing signature
}

// After
public interface ISomeContract
{
    // new signature
}
```

### Configuration Changes

_Document any changes to `IOptions<T>` shapes, appsettings keys, or environment variables._

### Behavioral Changes

_Document any changes to runtime behavior — what was true before, what will be true after._

### Migration Path

_How do existing consumers migrate? Step-by-step. Must be completable within the deprecation window (minimum two minor versions)._

---

## Drawbacks

_Why should we NOT do this? What are the costs, risks, or downsides? Be honest._

---

## Alternatives Considered

_What other approaches were evaluated? Why were they rejected?_

| Alternative | Why Not Selected |
|-------------|-----------------|
| Option A | Reason |
| Option B | Reason |

---

## Rollout Plan

### Deprecation Window

| Version | Action |
|---------|--------|
| vX.Y.0 | Add `[Obsolete(..., error: false)]` on old API. Add new API. Announce in CHANGELOG. |
| vX.Y+1.0 | Strengthen deprecation warning. Update migration guide. |
| vX+1.0.0 | Upgrade to `[Obsolete(..., error: true)]`. Old API becomes compile error. |
| vX+2.0.0 | Remove old API entirely. |

### Communication

- [ ] CHANGELOG.md entry drafted
- [ ] Migration guide updated at `docs/migrations/`
- [ ] Product teams notified (list them)
- [ ] Platform changelog PR linked: #XXX

---

## Open Questions

_Questions that are still unresolved at the time of submission. Assign them to specific people._

1. _Question 1_ — @assignee
2. _Question 2_ — @assignee

---

## Acknowledgements

_Who contributed to the design discussion?_

---

_Pervaxis Platform · Clarivex Technologies_
