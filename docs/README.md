# Pervaxis Genesis — Documentation

> Reference documentation extracted from Pervaxis.Core standards

---

## Quick Links

### Getting Started
- **[Solution Setup](SOLUTION_SETUP.md)** — ⭐ START HERE: What was created, build verification, and implementation roadmap

### Development Guides
- **[Pervaxis Standards](PERVAXIS_STANDARDS.md)** — Code standards, conventions, and best practices
- **[Project Structure Guide](PROJECT_STRUCTURE_GUIDE.md)** — How to structure Genesis provider projects
- **[Dependency Management](DEPENDENCY_MANAGEMENT.md)** — Guidelines for adding and managing NuGet packages

### Templates
- **[ADR Template](architecture/ADR_TEMPLATE.md)** — Architecture Decision Record template
- **[RFC Template](governance/RFC_TEMPLATE.md)** — Request for Comments template

---

## Documentation Structure

```
docs/
├── README.md                           # This file
├── SOLUTION_SETUP.md                   # Initial setup summary and roadmap
├── PERVASIS_STANDARDS.md               # Code standards and conventions
├── PROJECT_STRUCTURE_GUIDE.md          # Project structure patterns
├── DEPENDENCY_MANAGEMENT.md            # Dependency guidelines
│
├── architecture/
│   ├── ADR_TEMPLATE.md                 # Template for new ADRs
│   └── ADR-XXXX-*.md                   # Individual ADRs
│
└── governance/
    └── RFC_TEMPLATE.md                 # Template for breaking changes
```

---

## When to Use Each Document

### SOLUTION_SETUP.md
**Use when:**
- First time working with Genesis solution
- Need to understand what was created
- Planning implementation phases
- Verifying build configuration
- Looking for next steps

**Contains:**
- Complete solution inventory (18 projects)
- Build verification results
- Phase-by-phase implementation roadmap
- Code templates for each component
- AWS SDK version matrix
- Useful commands reference

### PERVAXIS_STANDARDS.md
**Use when:**
- Writing new code
- Reviewing PRs
- Onboarding new team members
- Setting up IDE/editor

**Contains:**
- Code style checklist
- Naming conventions
- .NET 10 requirements
- AWS SDK best practices
- Testing requirements

### PROJECT_STRUCTURE_GUIDE.md
**Use when:**
- Creating a new Genesis provider
- Adding folders/files to existing providers
- Understanding solution layout

**Contains:**
- Folder structure patterns
- File naming conventions
- Namespace conventions
- .csproj templates
- README.md templates

### DEPENDENCY_MANAGEMENT.md
**Use when:**
- Adding a new NuGet package
- Upgrading AWS SDK versions
- Resolving dependency conflicts
- Reviewing package vulnerabilities

**Contains:**
- Approval process
- Version pinning rules
- Justification format
- AWS SDK version matrix
- Security scanning

### ADR Template
**Use when:**
- Making an architectural decision
- Choosing between AWS services
- Evaluating implementation approaches

**Format:** `ADR-XXXX-short-title.md`

### RFC Template
**Use when:**
- Proposing a breaking change
- Changing public APIs
- Deprecating features

**Format:** `RFC-XXXX-short-title.md`

---

## Quick Start Checklist

Before creating a new Genesis provider:

1. ✅ Read [PERVAXIS_STANDARDS.md](PERVAXIS_STANDARDS.md)
2. ✅ Review [PROJECT_STRUCTURE_GUIDE.md](PROJECT_STRUCTURE_GUIDE.md)
3. ✅ Check [DEPENDENCY_MANAGEMENT.md](DEPENDENCY_MANAGEMENT.md) for AWS SDK versions
4. ✅ Copy project structure template from guide
5. ✅ Add dependency justifications to `.csproj`
6. ✅ Create README.md from template
7. ✅ Write tests (90% coverage target)

---

## Contributing

### Adding Documentation

New documentation should:
- Use Markdown format (`.md`)
- Follow GitHub-flavored Markdown syntax
- Include frontmatter metadata (where applicable)
- Be linked from this README

### Updating Standards

When updating standards:
1. Open a GitHub Issue describing the change
2. Get team approval
3. Update relevant documentation
4. Update CHANGELOG.md
5. Notify team in #platform-engineering

---

## Version History

| Date | Change | Author |
|------|--------|--------|
| 2026-04-21 | Initial documentation created from Pervaxis.Core standards | [Your Name] |

---

*Pervaxis Platform · Clarivex Technologies · Genesis Edition*
