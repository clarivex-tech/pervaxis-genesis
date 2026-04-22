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
| **Components** | Pervaxis.Genesis.<Component> |

---

## Summary

_One paragraph. What is being proposed and why? Who does it affect?_

---

## Motivation

_What problem does this RFC solve? What is the current limitation, pain point, or capability gap? Link to GitHub Issues or incident reports._

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

**Before:**
```json
{
  "Pervaxis": {
    "Genesis": {
      "Component": {
        "OldKey": "value"
      }
    }
  }
}
```

**After:**
```json
{
  "Pervaxis": {
    "Genesis": {
      "Component": {
        "NewKey": "value"
      }
    }
  }
}
```

### Behavioral Changes

_Document any changes to runtime behavior — what was true before, what will be true after._

### Migration Path

_How do existing consumers migrate? Step-by-step. Must be completable within the deprecation window (minimum two minor versions)._

1. Upgrade to version X.Y.0
2. Replace `OldApi()` calls with `NewApi()`
3. Update configuration keys in `appsettings.json`
4. Test changes in local environment
5. Deploy to UAT/staging before production

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

## AWS Impact

### Service Changes
_List any AWS service configuration changes required._

### IAM Permission Changes
_Document any new IAM permissions required._

```json
{
  "Effect": "Allow",
  "Action": [
    "service:NewAction"
  ],
  "Resource": "arn:aws:service:region:account:resource/*"
}
```

### Cost Impact
_Estimate any AWS cost implications (API calls, data transfer, storage)._

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
- [ ] Migration guide created at `docs/migrations/`
- [ ] Consuming teams notified (list them)
- [ ] Platform changelog PR linked: #XXX

---

## Testing Strategy

_How will this change be tested?_

- [ ] Unit tests cover new behavior
- [ ] Integration tests with LocalStack / AWS dev account
- [ ] Performance regression tests (if applicable)
- [ ] Security review (if applicable)

---

## Open Questions

_Questions that are still unresolved at the time of submission. Assign them to specific people._

1. _Question 1_ — @assignee
2. _Question 2_ — @assignee

---

## Acknowledgements

_Who contributed to the design discussion?_

---

_Pervaxis Platform · Clarivex Technologies · Genesis Edition_
