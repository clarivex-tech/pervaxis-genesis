# GitHub Secrets Setup Guide

This guide explains how to configure secrets for the Pervaxis Genesis repository.

## Important: Two Different Names!

⚠️ **GitHub Restrictions:** GitHub reserves the `GITHUB_` prefix, so we use different names:

| Location | Secret/Variable Name | Why |
|----------|---------------------|-----|
| **GitHub Repository Secret** | `GH_PACKAGES_PAT` | For local/dev fallback or external automation |
| **Local Environment Variable** | `GITHUB_PACKAGES_PAT` | Works fine locally, used by `nuget.config` |

**Summary:** 
- GitHub Actions uses `GH_PACKAGES_PAT` for GitHub Packages restore and publish access.
- Local env var: `GITHUB_PACKAGES_PAT` (for your machine)
- The PAT must be valid and have access to the `clarivex-tech` GitHub Packages feed.

---

## Required Secrets

### 1. `GH_PACKAGES_PAT` (GitHub Personal Access Token, optional)

**Purpose:** Authenticate with GitHub Packages for local development or external automation.

**Scopes Required:**
- `read:packages` - Download packages from GitHub Package Registry

**Setup Steps:**

#### For Repository (optional):
1. Go to repository **Settings** → **Secrets and variables** → **Actions**
2. Click **New repository secret**
3. Name: `GH_PACKAGES_PAT` (Note: Cannot use `GITHUB_` prefix - reserved by GitHub)
4. Value: Your GitHub Personal Access Token with `read:packages` scope
5. Click **Add secret**

#### For Local Development:

**Windows (PowerShell):**
```powershell
[Environment]::SetEnvironmentVariable("GITHUB_PACKAGES_PAT", "ghp_YOUR_TOKEN_HERE", "User")
```

**Windows (CMD):**
```cmd
setx GITHUB_PACKAGES_PAT "ghp_YOUR_TOKEN_HERE"
```

**Linux/macOS:**
```bash
echo 'export GITHUB_PACKAGES_PAT="ghp_YOUR_TOKEN_HERE"' >> ~/.bashrc
source ~/.bashrc
```

**Restart your terminal after setting the environment variable.**

---

### 2. `NUGET_AUTH_TOKEN` (Optional - for NuGet.org)

**Purpose:** Publish packages to NuGet.org (only needed for public releases).

**Setup:** Same as above, but use your NuGet.org API key.

---

### 3. `SONAR_TOKEN` (SonarCloud Integration)

**Purpose:** Code quality analysis with SonarCloud.

**Setup:**
1. Generate token at https://sonarcloud.io/account/security
2. Add to GitHub repository secrets as `SONAR_TOKEN`

---

## How to Generate a GitHub Personal Access Token

1. Go to **GitHub** → **Settings** → **Developer settings** → **Personal access tokens** → **Tokens (classic)**
2. Click **Generate new token** → **Generate new token (classic)**
3. Set:
   - **Note:** `Pervaxis Genesis - Package Registry`
   - **Expiration:** Choose appropriate duration (90 days, 1 year, or custom)
   - **Scopes:** Check `read:packages`
4. Click **Generate token**
5. **Copy the token immediately** (you won't be able to see it again!)

---

## Verification

After setting up secrets, verify they work:

### Local Development:
```bash
# Check environment variable is set
echo $env:GITHUB_PACKAGES_PAT  # PowerShell
echo %GITHUB_PACKAGES_PAT%     # CMD
echo $GITHUB_PACKAGES_PAT      # Linux/macOS

# Test package restore
dotnet restore Pervaxis.Genesis.slnx
```

### GitHub Actions:
Check workflow runs at: https://github.com/clarivex-tech/pervaxis-genesis/actions
GitHub Actions restore/publish should use the workflow `github.token`, so package access no longer depends on `GH_PACKAGES_PAT`.

---

## Security Best Practices

1. ✅ **Never commit tokens to source control**
2. ✅ Use repository secrets for CI/CD
3. ✅ Use environment variables for local development
4. ✅ Rotate tokens regularly (every 90 days recommended)
5. ✅ Use minimum required scopes (`read:packages` not `write:packages`)
6. ✅ Revoke tokens immediately if compromised

---

## Troubleshooting

### "Unable to load the service index for source" error:
- Verify `GITHUB_PACKAGES_PAT` environment variable is set locally
- Restart your terminal/IDE after setting the variable
- Check token hasn't expired
- Verify token has `read:packages` scope

### GitHub Actions workflow fails at restore:
- Check `GH_PACKAGES_PAT` is configured and not expired
- Verify the token has `read:packages` scope
- Verify the token owner can read the `clarivex-tech` GitHub Packages feed

### Package not found:
- Verify package exists at https://github.com/orgs/clarivex-tech/packages
- Check you have access to the `clarivex-tech` organization
- Verify package name and version in `.csproj` files

---

## Support

For issues with secrets or authentication, contact:
- **DevOps Team:** devops@clarivex.tech
- **Repository Owner:** Anand Jayaseelan
