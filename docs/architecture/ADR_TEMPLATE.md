# ADR-XXXX — [Short Title]

| Field | Value |
|-------|-------|
| **Status** | Proposed / Accepted / Deprecated / Superseded by ADR-XXXX |
| **Date** | YYYY-MM-DD |
| **Authors** | Name, Name |
| **Reviewers** | Name, Name |
| **Component** | Pervaxis.Genesis.<Component> |

---

## Context

_What is the situation or problem that prompted this decision? Include the relevant constraints, forces, and requirements that shaped the solution space. Be specific — reference GitHub Issues, spec sections, or incident reports where applicable._

---

## Decision

_State the decision clearly and directly. One or two sentences. This is the decision, not the reasoning._

> **We will [do / use / adopt] [X] because [one-line reason].**

---

## Rationale

_Why was this option chosen over the alternatives? What trade-offs were accepted? Reference the evaluation criteria and how each option scored._

### Options considered

| Option | Pros | Cons | Decision |
|--------|------|------|----------|
| Option A | ... | ... | **Selected** |
| Option B | ... | ... | Rejected — reason |
| Option C | ... | ... | Rejected — reason |

### Key factors

- _Factor 1: ..._
- _Factor 2: ..._
- _Factor 3: ..._

---

## Consequences

### Positive
- _What becomes easier or better as a result of this decision?_

### Negative
- _What becomes harder or worse? What debt is accepted?_

### Risks
- _What could go wrong? What monitoring or mitigation is in place?_

---

## Implementation Notes

_Optional. Any specific implementation details, migration steps, or gotchas that future maintainers need to know._

**AWS Service Configuration:**
```json
{
  "Pervaxis": {
    "Genesis": {
      "<Component>": {
        "Region": "ap-south-1",
        // Component-specific settings
      }
    }
  }
}
```

**IAM Permissions Required:**
```json
{
  "Effect": "Allow",
  "Action": [
    "service:Action1",
    "service:Action2"
  ],
  "Resource": "arn:aws:service:region:account:resource/*"
}
```

---

## Related

- ADR-XXXX — [Related decision]
- GitHub Issue #XXX — [Related issue]
- AWS Service Documentation: [link]

---

_Pervaxis Platform · Clarivex Technologies · Genesis Edition_
