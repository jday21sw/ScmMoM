# BPM — Browser Page Monitor

A cross-platform .NET 9 desktop application that monitors your GitHub repositories. Built with [Avalonia UI](https://avaloniaui.net/) — runs on **Windows**, **macOS**, and **Linux**.

## Features

- **Review Requests** — See all PRs where you're a requested reviewer across multiple repos
- **My Pull Requests** — Track your authored PRs with status (open/draft/merged/closed), click to view comments
- **GitHub Actions** — Monitor latest workflow runs with status badges, click to view annotations
- **Clickable Links** — Open any PR or Action run directly in your browser
- **Auto-Refresh** — Configurable polling interval (default: 5 minutes)
- **System Tray** — Minimizes to tray; right-click for quick actions
- **Desktop Notifications** — Alerts for new review requests and failed action runs
- **Rate Limit Display** — Real-time GitHub API rate limit counter in the status bar
- **Compact Mode** — Small always-on-top overlay window showing count badges (📋🔀⚡)
- **Light/Dark/System Theme** — Three-way theme toggle in Settings, applied immediately
- **Web Dashboard** — Embedded HTTP server that serves a browser-based dashboard at `http://localhost:5123`, auto-synced with the desktop app
- **Configurable** — Edit organization, repository list, refresh interval, theme, and web dashboard settings

## Quick Start

### Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) or later
- A GitHub Personal Access Token (see [Token Setup](#token-setup) below)

### Build & Run

```bash
# Clone the repo
git clone <this-repo-url>
cd BPM

# Build
dotnet build BPM.sln

# Run
dotnet run --project src/BPM.UI
```

On first launch, enter your **GitHub username** and **Personal Access Token** in the login window.

## Token Setup

BPM needs a GitHub Personal Access Token to read your repositories. The token is stored **in-memory only** — it is never written to disk. Choose one of the options below:

### Option A — `gh` CLI (Quickest)

If you have the [GitHub CLI](https://cli.github.com/) installed:

```bash
# Login with the required scopes
gh auth login --scopes repo,workflow,read:org

# Copy the token
gh auth token
```

Paste the output token into BPM at startup.

**Required classic scopes:**
| Scope | Purpose |
|-------|---------|
| `repo` | Read PRs, repo metadata (needed for private repos) |
| `workflow` | Read GitHub Actions workflow runs |
| `read:org` | Read organization membership |

### Option B — Fine-Grained PAT via Browser (Most Secure, Recommended)

Click this pre-filled link to create a fine-grained token with the correct permissions:

**[Create Fine-Grained PAT for BPM](https://github.com/settings/personal-access-tokens/new?name=BPM-Browser-Page-Monitor&description=Token+for+BPM+app+to+monitor+PRs+and+GitHub+Actions&target_name=21sw-us&pull_requests=read&actions=read&metadata=read)**

On the GitHub page:
1. Verify the pre-filled permissions: `Pull requests: Read`, `Actions: Read`, `Metadata: Read`
2. Under **Repository access**, select **All repositories** or choose the specific repos: `tsel`, `meta-21sw`, `meta-21sw-extras`, `LAVA-docker-compose`, `TSEL-GitHub-runner`
3. Set an **expiration** (e.g., 90 days)
4. Click **Generate token** and copy it

### Option C — Classic PAT via Browser (Manual)

1. Go to **GitHub → Settings → Developer settings → Personal access tokens → Tokens (classic)**
2. Click **Generate new token (classic)**
3. Check these scopes:
   - ✅ `repo`
   - ✅ `workflow`
   - ✅ `read:org`
4. Generate and copy the token

## Configuration

Edit `appsettings.json` (in the app's output directory) or use the in-app **Settings** dialog:

```json
{
  "Organization": "21sw-us",
  "Repositories": [
    "tsel",
    "meta-21sw",
    "meta-21sw-extras",
    "LAVA-docker-compose",
    "TSEL-GitHub-runner"
  ],
  "RefreshIntervalSeconds": 300,
  "NotificationsEnabled": true,
  "ThemeMode": "System",
  "WebServerEnabled": false,
  "WebServerPort": 5123
}
```

| Setting | Description | Default |
|---------|-------------|---------|
| `Organization` | GitHub organization to monitor | `21sw-us` |
| `Repositories` | List of repository names to query | 5 repos listed above |
| `RefreshIntervalSeconds` | Auto-refresh interval (min: 30) | `300` (5 min) |
| `NotificationsEnabled` | Desktop notification alerts | `true` |
| `ThemeMode` | UI theme: `System`, `Light`, or `Dark` | `System` |
| `WebServerEnabled` | Enable the embedded web dashboard server | `false` |
| `WebServerPort` | Port for the web dashboard | `5123` |

## Compact Mode

Click **▪ Compact** in the header bar (or select from the tray menu) to switch to a minimal always-on-top overlay showing review/PR/action counts. Click **Expand** to return to the full window.

## Web Dashboard

Enable the web dashboard in **Settings → Web Dashboard** to serve a browser-accessible version of BPM at `http://localhost:5123`. The web frontend polls the desktop app for data — no GitHub token is exposed to the browser.

- Automatically follows your browser's light/dark mode preference
- Tabs for Review Requests, Open Pull Requests, and GitHub Actions
- Click any row to view comments or annotations inline

## System Tray

- **Close the window** → app minimizes to the system tray
- **Right-click tray icon** → Open Dashboard, Refresh Now, Quit
- **Click tray icon** → restore the dashboard window

> **Linux note:** System tray requires `libappindicator3` or equivalent. Install via your package manager if the tray icon doesn't appear.

## Rate Limiting

BPM uses ~15 API calls per refresh (3 queries × 5 repos). At the default 5-minute interval, this is ~180 calls/hour — well under GitHub's 5,000/hour limit for authenticated requests. The status bar shows your current remaining calls and reset countdown.

| Color | Meaning |
|-------|---------|
| 🟢 Green | > 1,000 remaining |
| 🟡 Orange | 100–1,000 remaining |
| 🔴 Red | < 100 remaining |

## Architecture

- **BPM.Core** — Class library with models and services (no UI dependency)
- **BPM.UI** — Avalonia desktop application + embedded Kestrel web server
- **MVVM** pattern with ReactiveUI
- **Octokit.NET** for GitHub API interaction
- **ASP.NET Core** (embedded) for the web dashboard proxy
- **DI** via `Microsoft.Extensions.DependencyInjection`

## Cross-Platform Publishing

Build self-contained executables for each platform using `dotnet publish`. Each produces a single-directory deployment with all dependencies included.

### Windows

```bash
dotnet publish src/BPM.UI -c Release -r win-x64 --self-contained -o publish/win-x64
```

### macOS

```bash
# Intel Mac
dotnet publish src/BPM.UI -c Release -r osx-x64 --self-contained -o publish/osx-x64

# Apple Silicon (M1/M2/M3)
dotnet publish src/BPM.UI -c Release -r osx-arm64 --self-contained -o publish/osx-arm64
```

### Linux

```bash
dotnet publish src/BPM.UI -c Release -r linux-x64 --self-contained -o publish/linux-x64
```

### Single-File Executable (optional)

Add `-p:PublishSingleFile=true` to produce a single executable:

```bash
dotnet publish src/BPM.UI -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish/win-x64-single
```

> **Note:** The `wwwroot/` folder (web dashboard assets) is copied alongside the executable. For single-file builds, ensure the `wwwroot/` directory is distributed with the executable.

### Next Steps for Automated CI/CD

To automate builds for all platforms, add a GitHub Actions workflow (`.github/workflows/release.yml`) that:

1. Triggers on tag push (e.g., `v1.0.0`)
2. Runs `dotnet publish` in a matrix for `win-x64`, `osx-x64`, `osx-arm64`, `linux-x64`
3. Zips each output directory
4. Creates a GitHub Release with the zipped artifacts attached

For platform-specific packaging:
- **Windows** — Use [Inno Setup](https://jrsoftware.org/issetup.php) or MSIX for an installer
- **macOS** — Bundle as a `.app` using the [Avalonia packaging guide](https://docs.avaloniaui.net/docs/deployment/macOS)
- **Linux** — Create a `.deb`/`.rpm` package or distribute as an AppImage

## License

MIT
