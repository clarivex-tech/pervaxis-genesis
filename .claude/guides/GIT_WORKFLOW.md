# Git Workflow - Pervaxis Genesis

## Branch Strategy

Pervaxis Genesis follows a **three-tier branching strategy** inspired by GitFlow:

```
feature/xxx → develop → main
```

### Branch Purposes

| Branch | Purpose | Protection | Deployment |
|--------|---------|------------|------------|
| **main** | Production-ready code | Protected, requires PR + reviews | Production releases |
| **develop** | Integration branch for features | Protected, requires PR | Development environment |
| **feature/*** | Individual feature development | Not protected | Local/feature preview |

---

## Standard Workflow

### 1. Create Feature Branch

**Always branch from `develop`:**

```bash
# Update develop first
git checkout develop
git pull origin develop

# Create feature branch
git checkout -b feature/your-feature-name

# Examples:
git checkout -b feature/add-azure-providers
git checkout -b feature/performance-improvements
git checkout -b fix/cache-timeout-issue
```

**Naming Convention:**
- **feature/xxx** - New features or enhancements
- **fix/xxx** - Bug fixes
- **docs/xxx** - Documentation only
- **refactor/xxx** - Code refactoring (no functional changes)
- **test/xxx** - Adding or updating tests

### 2. Develop Your Feature

```bash
# Make changes
git add .
git commit -m "feat(scope): description"

# Push to remote
git push origin feature/your-feature-name
```

**Commit Message Format (Conventional Commits):**
```
<type>(scope): <description>

[optional body]

[optional footer]

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>
```

**Types:**
- `feat:` - New feature
- `fix:` - Bug fix
- `docs:` - Documentation changes
- `style:` - Formatting, missing semicolons, etc.
- `refactor:` - Code refactoring
- `test:` - Adding tests
- `chore:` - Maintenance tasks

### 3. Create Pull Request to `develop`

**⚠️ CRITICAL: Always PR to `develop` first, NOT to `main`**

```bash
# On GitHub, create PR:
# Base: develop ← Compare: feature/your-feature-name
```

**PR Title Format:**
```
feat(caching): add distributed locking support
fix(messaging): resolve SQS batch size limit
docs(readme): update resilience configuration examples
```

**PR Description Template:**
```markdown
## Summary
Brief description of what this PR does.

## Changes
- Change 1
- Change 2
- Change 3

## Testing
- [ ] Unit tests passing (390/390)
- [ ] Build succeeds (0 warnings, 0 errors)
- [ ] Manually tested feature

## Checklist
- [ ] Code follows Genesis coding standards
- [ ] XML documentation added for public APIs
- [ ] README updated (if needed)
- [ ] TASKS.md updated (if completing a task)
- [ ] No breaking changes (or documented in PR)
```

### 4. Review and Merge to `develop`

**After PR approval:**
1. ✅ CI/CD checks pass (build, tests, SonarCloud)
2. ✅ Code review approved
3. ✅ Merge PR to `develop` (squash or merge commit)
4. ✅ Delete feature branch after merge

### 5. Promote `develop` to `main`

**When ready for release (weekly or on-demand):**

```bash
# Create PR from develop to main
# Base: main ← Compare: develop
```

**PR Title:**
```
Release v1.x.x - [Brief summary of changes]
```

**After merge:**
```bash
# Tag the release
git checkout main
git pull origin main
git tag -a v1.0.0 -m "Release v1.0.0 - Genesis AWS Providers

- Multi-tenancy support
- Observability integration
- Resilience policies
- 8 providers, 390 tests passing"

git push origin v1.0.0
```

---

## ⚠️ Common Mistakes to Avoid

### ❌ DON'T: Create PR directly to `main`

```bash
# WRONG:
Base: main ← Compare: feature/your-feature
```

**Why?** Bypasses integration testing in `develop` and breaks the workflow.

**Exception:** Hotfixes for production issues (use `hotfix/xxx` branch).

### ❌ DON'T: Merge `main` into `develop` manually

Let the PR workflow handle this. If `develop` gets behind `main`, it will be synchronized via merge commit.

### ❌ DON'T: Force push to `develop` or `main`

```bash
# NEVER DO THIS:
git push --force origin develop
git push --force origin main
```

**Why?** Rewrites history and breaks collaboration.

### ❌ DON'T: Commit directly to `develop` or `main`

Always use pull requests for traceability and review.

---

## Special Workflows

### Hotfix Workflow (Production Emergency)

When a critical bug needs immediate fix in production:

```bash
# Branch from main (not develop)
git checkout main
git pull origin main
git checkout -b hotfix/critical-cache-issue

# Fix the issue
git add .
git commit -m "fix(caching): resolve connection leak in ElastiCache provider"
git push origin hotfix/critical-cache-issue

# Create TWO PRs:
# 1. hotfix/xxx → main (immediate fix)
# 2. hotfix/xxx → develop (keep develop in sync)
```

**After merging hotfix to main:**
```bash
# Tag the hotfix release
git checkout main
git pull origin main
git tag -a v1.0.1 -m "Hotfix v1.0.1 - Critical cache connection fix"
git push origin v1.0.1

# Merge to develop
git checkout develop
git merge main
git push origin develop
```

### Long-Running Feature Branches

For large features that take multiple days:

```bash
# Keep your feature branch up-to-date with develop
git checkout feature/long-running-feature
git fetch origin
git merge origin/develop

# Resolve conflicts if any
git push origin feature/long-running-feature
```

### Syncing Branches After Accidental Merge

If a feature PR is accidentally merged to `main`:

```bash
# Merge develop into main (brings all develop changes)
git checkout main
git merge develop -m "Merge develop into main - synchronize branches"
git push origin main

# Fast-forward develop to match main
git checkout develop
git merge main --ff-only
git push origin develop
```

---

## Branch Protection Rules

### `main` Branch Protection

**Settings on GitHub:**
- ✅ Require pull request before merging
- ✅ Require approvals: 1 (or more for team)
- ✅ Require status checks to pass:
  - Build (pr-check workflow)
  - Tests (390/390 passing)
  - SonarCloud quality gate
- ✅ Require branches to be up to date
- ✅ Include administrators (enforce for everyone)
- ❌ Allow force pushes: **Disabled**
- ❌ Allow deletions: **Disabled**

### `develop` Branch Protection

**Settings on GitHub:**
- ✅ Require pull request before merging
- ✅ Require status checks to pass:
  - Build (pr-check workflow)
  - Tests (390/390 passing)
- ✅ Require branches to be up to date
- ❌ Allow force pushes: **Disabled**
- ❌ Allow deletions: **Disabled**

---

## CI/CD Integration

### GitHub Actions Workflows

**pr-check.yml** - Runs on every PR:
```yaml
on:
  pull_request:
    branches: [ develop, main ]
```

Checks:
- ✅ Build (0 warnings, 0 errors)
- ✅ Tests (all passing)
- ✅ SonarCloud quality gate
- ✅ Security scan (no vulnerabilities)

**deploy.yml** - Runs on merge to `develop` or `main`:
```yaml
on:
  push:
    branches: [ develop, main ]
```

Actions:
- Build and test
- Track code quality
- Update deployment status

**publish.yml** - Runs on version tags:
```yaml
on:
  push:
    tags: [ 'v*.*.*' ]
```

Actions:
- Build release packages
- Publish to GitHub Packages
- Create GitHub Release with notes

---

## Quick Reference Commands

### Daily Workflow

```bash
# Start new feature
git checkout develop && git pull origin develop
git checkout -b feature/my-feature

# Work and commit
git add .
git commit -m "feat(scope): description"
git push origin feature/my-feature

# Create PR on GitHub: feature/my-feature → develop
```

### Keep Feature Branch Updated

```bash
git checkout feature/my-feature
git fetch origin
git merge origin/develop
git push origin feature/my-feature
```

### Check Branch Status

```bash
# See all branches
git branch -a

# See commits in feature not in develop
git log develop..feature/my-feature --oneline

# See commits in develop not in main
git log main..develop --oneline
```

### Clean Up Old Branches

```bash
# Delete local branch
git branch -d feature/old-feature

# Delete remote branch
git push origin --delete feature/old-feature

# Prune deleted remote branches
git fetch --prune
```

---

## Release Process

### Version Numbering (SemVer)

```
v<major>.<minor>.<patch>

Examples:
v1.0.0 - Initial release
v1.1.0 - New feature (backward compatible)
v1.1.1 - Bug fix (backward compatible)
v2.0.0 - Breaking change
```

### Release Checklist

**Before creating release PR (`develop → main`):**

- [ ] All feature PRs merged to `develop`
- [ ] All tests passing (390/390)
- [ ] Build succeeds (0 warnings, 0 errors)
- [ ] TASKS.md updated with completed tasks
- [ ] CHANGELOG.md updated with release notes
- [ ] Version bumped in `Directory.Build.props`
- [ ] Documentation reviewed and updated

**After merging to `main`:**

- [ ] Tag release: `git tag -a v1.x.x -m "..."`
- [ ] Push tag: `git push origin v1.x.x`
- [ ] Verify publish workflow runs successfully
- [ ] Verify packages published to GitHub Packages
- [ ] Create GitHub Release with notes
- [ ] Announce release (if needed)

---

## Troubleshooting

### "Your branch has diverged from origin"

```bash
# If you haven't pushed local commits yet
git fetch origin
git rebase origin/your-branch

# If you've pushed and need to sync
git fetch origin
git merge origin/your-branch
```

### "Merge conflict in X file"

```bash
# Update your branch
git fetch origin
git merge origin/develop

# Fix conflicts manually in your editor
# Then:
git add <resolved-files>
git commit -m "Merge develop into feature/xxx - resolve conflicts"
git push origin feature/xxx
```

### "Need to sync develop and main"

```bash
# Merge develop into main
git checkout main
git merge develop
git push origin main

# Fast-forward develop
git checkout develop
git merge main --ff-only
git push origin develop
```

---

## Best Practices

### ✅ DO:
- Always branch from `develop` for new features
- Keep feature branches short-lived (< 1 week)
- Write clear commit messages (Conventional Commits)
- Update TASKS.md when completing tasks
- Add tests for new features
- Keep PRs focused (one feature per PR)
- Review PRs promptly
- Delete feature branches after merge

### ❌ DON'T:
- Don't commit directly to `develop` or `main`
- Don't create PR directly to `main` (use `develop` first)
- Don't force push to protected branches
- Don't merge without CI/CD passing
- Don't leave feature branches stale for weeks
- Don't commit secrets or credentials
- Don't skip code review

---

## Getting Help

**Questions about workflow?**
- Check this document: `.claude/guides/GIT_WORKFLOW.md`
- Review CLAUDE.md: `.claude/CLAUDE.md`
- Check GitHub PRs for examples

**Common workflows:**
- Standard feature: See "Standard Workflow" section above
- Hotfix: See "Hotfix Workflow" section above
- Release: See "Release Process" section above

---

*Last Updated: 2026-04-26*  
*Pervaxis Platform · Clarivex Technologies · Genesis Edition*
