# CI & SonarCloud Setup Guide — Pervaxis Platform

## Overview

This guide documents every lesson learned setting up GitHub Actions CI and SonarCloud on a .NET 9/10 solution. Follow this top-to-bottom on any new repo and the setup will be a first-run success.

---

## 1. Workflow Architecture — Two Files, Not One

Use **two separate workflow files**. Never combine `push` and `pull_request` triggers in a single file — it causes duplicate runs on every PR.

| File | Trigger | Purpose |
|---|---|---|
| `pr-check.yml` | `pull_request` targeting `main`, `develop` | Build + test + Sonar PR gate |
| `deploy.yml` | `push` to `main`, `develop` | Build + test + Sonar branch tracking |

### pr-check.yml skeleton

```yaml
on:
  pull_request:
    branches: [main, develop]
```

### deploy.yml skeleton

```yaml
on:
  push:
    branches: [main, develop]
  workflow_dispatch:   # keep this — needed to seed the baseline (see Section 6)
```

---

## 2. SonarCloud Free Plan — Hard Limits

**Know these before starting. Violating them causes mysterious failures.**

| Feature | Free Plan | Paid Plan |
|---|---|---|
| Branch analysis (`sonar.branch.name`) | ❌ NOT supported | ✅ |
| Multi-branch tracking | ❌ | ✅ |
| PR analysis | ✅ (PRs targeting default branch only) | ✅ |
| Default branch analysis | ✅ | ✅ |

### Critical rule: never use `sonar.branch.name`

Even `/d:sonar.branch.name="main"` breaks the free plan. The analysis uploads fine but the quality gate check returns `Project not found`. Remove it entirely — SonarCloud resolves the default branch automatically.

### Critical rule: only gate PRs targeting `main`

PRs targeting `develop` have no SonarCloud baseline (free plan doesn't store `develop`). Gate Sonar to `main` PRs only using `if: github.base_ref == 'main'` on all Sonar steps.

---

## 3. dotnet-sonarscanner vs Java sonarscanner

`dotnet sonarscanner` is **not** the Java sonarscanner. It does **not** read `sonar-project.properties`. That file is Java-only. Do not create it.

Every property must be passed inline as a `/d:` parameter:

```bash
dotnet sonarscanner begin \
  /k:"your-project-key" \
  /o:"your-org" \
  /d:sonar.host.url="https://sonarcloud.io" \
  /d:sonar.token="${{ secrets.SONAR_TOKEN }}" \
  /d:sonar.cs.opencover.reportsPaths="**/TestResults/**/coverage.opencover.xml" \
  /d:sonar.exclusions=".github/**,**/obj/**,**/*.g.cs" \
  /d:sonar.coverage.exclusions="**/obj/**,**/*.g.cs" \
  /d:sonar.qualitygate.wait=true
```

---

## 4. Required Exclusions

Always add these — without them SonarCloud will count YAML workflow files as source code with 0% coverage, failing the gate.

```
sonar.exclusions=.github/**,**/obj/**,**/*.g.cs
sonar.coverage.exclusions=**/obj/**,**/*.g.cs
```

---

## 5. PR Analysis Parameters

For `pr-check.yml`, pass these three PR params to enable diff-based analysis:

```bash
/d:sonar.pullrequest.key="${{ github.event.pull_request.number }}" \
/d:sonar.pullrequest.branch="${{ github.head_ref }}" \
/d:sonar.pullrequest.base="${{ github.base_ref }}" \
```

Do **not** add `sonar.branch.longLivedBranchesPattern` here. That's a paid-plan param and is not needed when using the PR analysis params correctly.

---

## 6. The Bootstrap Problem — First Merge to Main

**This catches everyone the first time.** The sequence:

1. Repo is new → `main` has no code, no SonarCloud baseline
2. First PR (`develop → main`) runs the PR Check
3. No baseline exists → SonarCloud treats ALL code as new code
4. Every pre-existing issue counts as a new issue → quality gate fails

### Solution sequence

**Step 1** — Temporarily set `sonar.qualitygate.wait=false` in `pr-check.yml` so the first PR can merge without being blocked.

**Step 2** — Merge the PR. The push to `main` triggers `deploy.yml`, which runs Sonar and seeds the baseline.

**Step 3** — Open a follow-up PR that flips `pr-check.yml` back to `sonar.qualitygate.wait=true`.

After Step 2, all future PRs gate correctly against the real baseline.

---

## 7. SonarCloud Branch Name — master vs main

SonarCloud creates the default branch as `master` regardless of what your repo uses. Fix this immediately after the first successful analysis:

1. SonarCloud → project → **Administration** → **Branches and Pull Requests**
2. If both `master` and `main` exist (from bootstrap attempts with `sonar.branch.name`): **delete** `main` first
3. Rename `master` → `main`

If you see `Could not find ref: master in refs/heads` in the logs, this rename hasn't been done yet.

---

## 8. Quality Gate Strategy — Track vs Block

Use a split strategy:

| Workflow | `qualitygate.wait` | Rationale |
|---|---|---|
| `deploy.yml` (push to main) | `false` | Tracks health on dashboard, never blocks CI |
| `pr-check.yml` (PR to main) | `true` | Enforces quality on new code before merge |

The 80% coverage condition in the quality gate applies to **new code lines in the PR only**, not the overall codebase. This means:
- Overall coverage of 44% on main does not fail future PRs
- Each PR only needs 80% coverage on the lines it introduces
- Overall coverage climbs incrementally as you write tests for new features

---

## 9. Code Quality Rules to Fix Before First PR

These Sonar rules will appear as new issues on the first full analysis. Fix them in the codebase before setting up Sonar, or they will accumulate.

| Rule | Description | Fix |
|---|---|---|
| **S107** | Method has more than 7 parameters | Introduce a parameter object/record |
| **S3246** | Generic type parameter should be `in`/`out` | Add variance keyword to interface |
| **S2326** | Unused generic type parameter | Remove or use the type param |
| **S1133** | Deprecated code not removed | Add `// NOSONAR` or remove the code |
| **S4144** | Method implementation identical to another | Extract shared private method |
| **S1192** | String literal repeated 4+ times | Extract to a constant |
| **S1075** | Hardcoded URI/path | Move to config/options |
| **CA1062** | Public method parameter not null-checked | Add `ArgumentNullException.ThrowIfNull(param)` |

---

## 10. Coverage Reporting Setup

In `deploy.yml` and `pr-check.yml`:

```yaml
- name: Test with coverage
  run: |
    dotnet test Pervaxis.sln \
      --no-build \
      --configuration Release \
      --collect:"XPlat Code Coverage" \
      --results-directory TestResults \
      -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=opencover
```

Pass the report path to SonarScanner:

```bash
/d:sonar.cs.opencover.reportsPaths="**/TestResults/**/coverage.opencover.xml"
```

---

## 11. Java Requirement

`dotnet-sonarscanner` requires Java to run the scanner engine. Always add this step **before** the sonarscanner install:

```yaml
- name: Setup Java 17
  if: github.base_ref == 'main'   # or github.ref_name == 'main'
  uses: actions/setup-java@v4
  with:
    distribution: temurin
    java-version: "17"
```

Gate it behind the same condition as the Sonar steps so it doesn't slow down develop-only runs.

---

## 12. SonarCloud GitHub App — Avoid Duplicate Runs

If the SonarCloud GitHub App is installed on the repo, it triggers its own analysis in addition to the workflow scanner — resulting in 3 checks on every PR. Remove the repo from the SonarCloud GitHub App's scope (SonarCloud → Organisation → GitHub App settings → exclude the repo).

---

## 13. Complete Reference — pr-check.yml

```yaml
name: PR Check

on:
  pull_request:
    branches: [main, develop]

env:
  DOTNET_NOLOGO: true
  DOTNET_SKIP_FIRST_TIME_EXPERIENCE: true
  DOTNET_CLI_TELEMETRY_OPTOUT: true

jobs:
  build-test-scan:
    name: Build · Test · SonarQube
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v5
        with:
          fetch-depth: 0

      - uses: actions/setup-dotnet@v5
        with:
          dotnet-version: "9.x"

      - name: Setup Java 17
        if: github.base_ref == 'main'
        uses: actions/setup-java@v4
        with:
          distribution: temurin
          java-version: "17"

      - uses: actions/cache@v5
        with:
          path: ~/.nuget/packages
          key: ${{ runner.os }}-nuget-${{ hashFiles('**/*.csproj') }}
          restore-keys: ${{ runner.os }}-nuget-

      - name: Install SonarScanner
        if: github.base_ref == 'main'
        run: dotnet tool install --global dotnet-sonarscanner

      - run: dotnet restore YourSolution.sln

      - name: SonarQube begin
        if: github.base_ref == 'main'
        env:
          SONAR_TOKEN: ${{ secrets.SONAR_TOKEN }}
        run: |
          dotnet sonarscanner begin \
            /k:"your-project-key" /o:"your-org" \
            /d:sonar.host.url="https://sonarcloud.io" \
            /d:sonar.token="${{ secrets.SONAR_TOKEN }}" \
            /d:sonar.cs.opencover.reportsPaths="**/TestResults/**/coverage.opencover.xml" \
            /d:sonar.exclusions=".github/**,**/obj/**,**/*.g.cs" \
            /d:sonar.coverage.exclusions="**/obj/**,**/*.g.cs" \
            /d:sonar.pullrequest.key="${{ github.event.pull_request.number }}" \
            /d:sonar.pullrequest.branch="${{ github.head_ref }}" \
            /d:sonar.pullrequest.base="${{ github.base_ref }}" \
            /d:sonar.qualitygate.wait=true

      - run: dotnet build YourSolution.sln --no-restore --configuration Release

      - name: Test with coverage
        run: |
          dotnet test YourSolution.sln --no-build --configuration Release \
            --collect:"XPlat Code Coverage" --results-directory TestResults \
            -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=opencover

      - name: SonarQube end
        if: github.base_ref == 'main'
        env:
          SONAR_TOKEN: ${{ secrets.SONAR_TOKEN }}
        run: dotnet sonarscanner end /d:sonar.token="${{ secrets.SONAR_TOKEN }}"

      - uses: actions/upload-artifact@v5
        if: always()
        with:
          name: pr-test-results-${{ github.run_id }}
          path: TestResults/
          retention-days: 7
```

---

## 14. Complete Reference — deploy.yml

```yaml
name: Deploy

on:
  push:
    branches: [main, develop]
  workflow_dispatch:

env:
  DOTNET_NOLOGO: true
  DOTNET_SKIP_FIRST_TIME_EXPERIENCE: true
  DOTNET_CLI_TELEMETRY_OPTOUT: true

jobs:
  build-test-scan:
    name: Build · Test · SonarQube
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v5
        with:
          fetch-depth: 0

      - uses: actions/setup-dotnet@v5
        with:
          dotnet-version: "9.x"

      - name: Setup Java 17
        if: github.ref_name == 'main' || github.event_name == 'workflow_dispatch'
        uses: actions/setup-java@v4
        with:
          distribution: temurin
          java-version: "17"

      - uses: actions/cache@v5
        with:
          path: ~/.nuget/packages
          key: ${{ runner.os }}-nuget-${{ hashFiles('**/*.csproj') }}
          restore-keys: ${{ runner.os }}-nuget-

      - name: Install SonarScanner
        if: github.ref_name == 'main' || github.event_name == 'workflow_dispatch'
        run: dotnet tool install --global dotnet-sonarscanner

      - run: dotnet restore YourSolution.sln

      - name: SonarQube begin
        if: github.ref_name == 'main' || github.event_name == 'workflow_dispatch'
        env:
          SONAR_TOKEN: ${{ secrets.SONAR_TOKEN }}
        run: |
          dotnet sonarscanner begin \
            /k:"your-project-key" /o:"your-org" \
            /d:sonar.host.url="https://sonarcloud.io" \
            /d:sonar.token="${{ secrets.SONAR_TOKEN }}" \
            /d:sonar.cs.opencover.reportsPaths="**/TestResults/**/coverage.opencover.xml" \
            /d:sonar.exclusions=".github/**,**/obj/**,**/*.g.cs" \
            /d:sonar.coverage.exclusions="**/obj/**,**/*.g.cs" \
            /d:sonar.qualitygate.wait=false

      - run: dotnet build YourSolution.sln --no-restore --configuration Release

      - name: Test with coverage
        run: |
          dotnet test YourSolution.sln --no-build --configuration Release \
            --collect:"XPlat Code Coverage" --results-directory TestResults \
            -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=opencover

      - name: SonarQube end
        if: github.ref_name == 'main' || github.event_name == 'workflow_dispatch'
        env:
          SONAR_TOKEN: ${{ secrets.SONAR_TOKEN }}
        run: dotnet sonarscanner end /d:sonar.token="${{ secrets.SONAR_TOKEN }}"

      - uses: actions/upload-artifact@v5
        if: always()
        with:
          name: deploy-test-results-${{ github.run_id }}
          path: TestResults/
          retention-days: 7
```

---

## 15. Setup Checklist for a New Repo

```
[ ] Create SonarCloud project — note the project key and org slug
[ ] Add SONAR_TOKEN as a GitHub Actions secret
[ ] Remove repo from SonarCloud GitHub App scope (prevents triple runs)
[ ] Add pr-check.yml and deploy.yml using the templates above
[ ] Fix any S107 / CA1062 / S3246 issues before first push
[ ] Push to develop — Deploy runs build+test only (no Sonar on develop, free plan)
[ ] Open develop → main PR — PR Check runs Sonar with wait=false (bootstrap)
[ ] Merge the PR — Deploy on main seeds the SonarCloud baseline
[ ] In SonarCloud UI: rename master → main (Administration → Branches)
[ ] Open follow-up PR: flip pr-check.yml back to qualitygate.wait=true
[ ] Done — all future PRs to main are quality gated on new code
```
