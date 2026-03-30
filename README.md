# ScmMoM — Source Code Monitor of Monitors

A cross-platform .NET 9 desktop application that monitors your repositories across **GitHub**, **GitLab**, and **Gitea** — all from a single dashboard. Built with [Avalonia UI](https://avaloniaui.net/) — runs on **Windows**, **macOS**, and **Linux**.

## Features

- **Multi-SCM Support** — Monitor GitHub, GitLab, and Gitea from one app
- **Multi-Account** — Connect multiple accounts simultaneously, each with its own provider/server
- **Review Requests** — See all PRs/MRs where you're a requested reviewer
- **Open Pull Requests / Merge Requests** — Track your authored PRs/MRs with status, click to view comments
- **CI Runs** — Monitor GitHub Actions, GitLab Pipelines, and Gitea Actions with status badges
- **Notifications** — Aggregate notifications/todos from all connected SCM platforms
- **Issues** — View assigned issues across all accounts
- **Sidebar Account Switcher** — Visual sidebar showing all accounts with health status dots
- **Clickable Links** — Open any PR/MR or CI run directly in your browser
- **Auto-Refresh** — Configurable polling interval (default: 5 minutes)
- **System Tray** — Minimizes to tray; right-click for quick actions
- **Desktop Notifications** — Alerts for new review requests and failed CI runs
- **Rate Limit Display** — Real-time API rate limit counter in the status bar
- **Compact Mode** — Small always-on-top overlay with count badges (📋🔀⚡🔔📌)
- **Light/Dark/System Theme** — Three-way theme toggle in Settings
- **Web Dashboard** — Embedded HTTP server with browser-based dashboard, PSK-authenticated API
- **Secure Token Storage** — Optional OS keyring storage (Windows Credential Manager, macOS Keychain, Linux libsecret)
- **Token Audit** — Warns about excessive token permissions on classic PATs

## Quick Start

### Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) or later
- A Personal Access Token for your SCM platform(s)

### Build & Run

```bash
git clone https://github.com/jday21sw/ScmMoM.git
cd ScmMoM

dotnet build ScmMoM.sln
dotnet run --project src/ScmMoM.UI
```

On first launch, add your accounts via the login window — select the provider type (GitHub/GitLab/Gitea), enter your username and token, then click **Connect & Launch**.

## Token Setup

### GitHub

**Required scopes (classic PAT):** `repo`, `workflow`, `read:org`

```bash
gh auth login --scopes repo,workflow,read:org
gh auth token
```

Or create a **fine-grained PAT** with `Pull requests: Read`, `Actions: Read`, `Metadata: Read`.

### GitLab

Create a Personal Access Token at `Settings → Access Tokens` with the `read_api` scope. For self-hosted GitLab, enter your server URL when adding the account.

### Gitea

Create a token at `Settings → Applications → Manage Access Tokens`. For self-hosted Gitea, enter your server URL when adding the account.

## Configuration

Use the in-app **Settings** dialog to configure per-account repositories and global settings. Settings are stored in `appsettings.json`:

```json
{
  "Accounts": [
    {
      "Id": "abc123",
      "ProviderType": "GitHub",
      "DisplayName": "Work GitHub",
      "Organization": "my-org",
      "Repositories": ["repo1", "repo2"],
      "RememberToken": true
    }
  ],
  "RefreshIntervalSeconds": 300,
  "NotificationsEnabled": true,
  "ThemeMode": "System",
  "WebServerEnabled": false,
  "WebServerPort": 5123,
  "ApiPsk": null
}
```

| Setting | Description | Default |
|---------|-------------|---------|
| `Accounts` | List of SCM account configurations | `[]` |
| `RefreshIntervalSeconds` | Auto-refresh interval (min: 30) | `300` |
| `NotificationsEnabled` | Desktop notification alerts | `true` |
| `ThemeMode` | `System`, `Light`, or `Dark` | `System` |
| `WebServerEnabled` | Enable embedded web dashboard | `false` |
| `WebServerPort` | Port for web dashboard | `5123` |
| `ApiPsk` | Pre-shared key for API authentication | `null` |

## Web Dashboard & Remote API

Enable in **Settings → Web Dashboard** to serve a browser-accessible dashboard at `http://localhost:5123`.

### API Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/status` | Dashboard status summary |
| GET | `/api/reviews` | Review requests |
| GET | `/api/pull-requests` | Open pull requests |
| GET | `/api/actions` | CI runs |
| GET | `/api/notifications` | Notifications |
| GET | `/api/issues` | Assigned issues |
| GET | `/api/accounts` | Connected accounts |
| POST | `/api/refresh` | Trigger a data refresh |

### PSK Authentication

Generate a Pre-Shared Key in **Settings → API Security**. Include it as `X-API-Key` header:

```bash
curl -H "X-API-Key: YOUR_PSK" http://localhost:5123/api/status
```

## Compact Mode

Click **▪ Compact** to switch to a minimal always-on-top overlay showing all count badges. Click **Expand** to return to full dashboard.

## System Tray

- **Close the window** → app minimizes to tray
- **Right-click tray icon** → Open Dashboard, Refresh Now, Quit
- **Click tray icon** → restore dashboard

## Architecture

- **ScmMoM.Core** — Class library: models, services, SCM providers (no UI dependency)
- **ScmMoM.UI** — Avalonia desktop app + embedded Kestrel web server
- **IScmProvider** — Unified interface for GitHub, GitLab, Gitea
- **AccountManager** — Multi-provider registry and factory
- **MVVM** with ReactiveUI
- **Octokit.NET** for GitHub, **NGitLab** for GitLab, **HttpClient** for Gitea
- **ASP.NET Core** (embedded) for web dashboard
- **DI** via `Microsoft.Extensions.DependencyInjection`

## Cross-Platform Publishing

```bash
# Windows
dotnet publish src/ScmMoM.UI -c Release -r win-x64 --self-contained -o publish/win-x64

# macOS (Apple Silicon)
dotnet publish src/ScmMoM.UI -c Release -r osx-arm64 --self-contained -o publish/osx-arm64

# macOS (Intel)
dotnet publish src/ScmMoM.UI -c Release -r osx-x64 --self-contained -o publish/osx-x64

# Linux
dotnet publish src/ScmMoM.UI -c Release -r linux-x64 --self-contained -o publish/linux-x64
```

## License

MIT
