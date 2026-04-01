# ScmMoM

ScmMoM, short for Source Code Monitor of Monitors, is a .NET 9 desktop monitor for teams and individual contributors who work across multiple source control platforms and multiple accounts. It combines GitHub, GitLab, and Gitea activity into one Avalonia desktop app and an optional browser-accessible dashboard served from the same process.

Release `0.1` establishes the first complete multi-SCM, multi-account version of the app.

## Release 0.1 Highlights

- Unified monitoring for GitHub, GitLab, and Gitea through a shared provider abstraction.
- Multi-account login, storage, and dashboard aggregation with per-account filtering.
- Review requests, pull requests or merge requests, CI runs, notifications, and assigned issues in one place.
- Inline token entry for accounts that exist in config but do not yet have saved credentials.
- Secure optional token persistence through Git Credential Manager backed OS storage.
- Permission auditing for tokens with an in-app warning banner when scopes appear broader than necessary.
- Embedded web dashboard and JSON API protected with an optional pre-shared key.
- Compact always-on-top window, tray behavior, rate-limit display, and theme switching.
- Updated branding, application icon assets, login artwork, and Linux desktop integration assets.

## Features

- Multi-SCM support for GitHub, GitLab, and Gitea.
- Multiple named accounts active at the same time.
- Per-account repository whitelists so the dashboard only surfaces configured repositories.
- Review request monitoring for PR and MR workflows.
- Open pull request and merge request tracking with recent comment drill-down.
- Recent CI runs with provider-specific status and annotation details.
- Notifications and assigned issues aggregated across connected accounts.
- Sidebar account switcher with provider icons and health status dots.
- Web dashboard with account selector and tabbed views for reviews, PRs, CI, notifications, and issues.
- Optional desktop notifications for new review requests and failing CI.
- Configurable refresh interval, theme mode, and web server settings.

## Quick Start

### Prerequisites

- .NET 9 SDK
- A personal access token for each provider you want to connect

### Build and Run

```bash
git clone https://github.com/jday21sw/ScmMoM.git
cd ScmMoM
dotnet build ScmMoM.sln
dotnet run --project src/ScmMoM.UI
```

On first launch:

1. Add an account in the login window.
2. Choose GitHub, GitLab, or Gitea.
3. Enter display name, username, organization or group, and the repository list to monitor.
4. Enter a token and optionally enable Remember token.
5. Select Connect & Launch.

## Windows Installation (MSIX)

Download the `.msix` package from the [latest release](https://github.com/jday21sw/ScmMoM/releases). The package is signed with a self-signed development certificate.

### Installation Methods

#### Option 1: Enable Developer Mode (Recommended for Development)
1. Open **Settings** → **System** → **For developers**
2. Enable **Developer Mode**
3. Run the downloaded `.msix` file

#### Option 2: Trust the Self-Signed Certificate
If you don't want to enable Developer Mode:

Open PowerShell **as Administrator** and run:
```powershell
# Install the certificate from the release asset first
$certPath = "C:\Path\To\ScmMoM-win-x64-signing.cer"
Import-Certificate -FilePath $certPath -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
Import-Certificate -FilePath $certPath -CertStoreLocation Cert:\LocalMachine\Root | Out-Null

# Then install the MSIX package
$msixPath = "C:\Path\To\ScmMoM-win-x64.msix"
Add-AppxPackage -Path $msixPath
```
Download both files from the same release tag:
- `ScmMoM-win-x64.msix`
- `ScmMoM-win-x64-signing.cer`

## Provider Token Guidance

### GitHub

Classic personal access tokens should include `repo`, `workflow`, and `read:org`.

Fine-grained tokens should allow read access for pull requests, actions, and repository metadata.

### GitLab

Use a personal access token with at least `read_api`. Self-hosted GitLab accounts should provide the server URL when the account is created.

### Gitea

Create a token through the applications or access tokens settings for the server. Self-hosted Gitea accounts must provide the server URL.

## Configuration Model

Settings are stored in `appsettings.json` under `%LOCALAPPDATA%\\ScmMoM` (for example `C:\\Users\\<you>\\AppData\\Local\\ScmMoM\\appsettings.json`). The current configuration shape is account-based:

```json
{
  "Accounts": [
    {
      "Id": "abc12345",
      "ProviderType": 0,
      "DisplayName": "Work GitHub",
      "ServerUrl": "",
      "Username": "octocat",
      "Organization": "my-org",
      "Repositories": ["repo-a", "repo-b"],
      "RememberToken": true
    }
  ],
  "RefreshIntervalSeconds": 300,
  "NotificationsEnabled": true,
  "ThemeMode": "System",
  "WebServerEnabled": false,
  "WebServerPort": 5123,
  "ApiPsk": ""
}
```

Legacy single-account configuration is migrated automatically into the `Accounts` list on load.

## Desktop Workflow

- The login window handles account creation, inline token entry, and startup connection.
- The main dashboard aggregates all connected providers and supports account-scoped filtering from the sidebar.
- The settings window edits account metadata, repository lists, refresh behavior, theme mode, and web server security.
- Closing the main window hides the app to tray instead of exiting; explicit quit remains available from the header and tray menu.

## Web Dashboard and API

When enabled, the embedded server publishes static files and JSON endpoints from the desktop process on `http://localhost:<port>`.

Available endpoints:

- `GET /api/status`
- `GET /api/reviews`
- `GET /api/pull-requests`
- `GET /api/actions`
- `GET /api/notifications`
- `GET /api/issues`
- `GET /api/accounts`
- `GET /api/actions/{repo}/{checkSuiteId}/annotations`
- `GET /api/pull-requests/{repo}/{number}/comments`
- `POST /api/refresh`

If an API PSK is configured, callers must send `X-API-Key`.

## Architecture

High-level summary:

- `ScmMoM.Core` contains models, configuration services, token storage, provider implementations, and the account manager.
- `ScmMoM.UI` contains the Avalonia application shell, windows, view models, and the embedded ASP.NET Core web server.
- ReactiveUI drives the desktop MVVM layer.
- Provider libraries are Octokit for GitHub, NGitLab plus REST calls for GitLab, and HttpClient-based REST integration for Gitea.

For a fuller design walkthrough, see `ARCHITECTURE.md`.

## Publishing

```bash
dotnet publish src/ScmMoM.UI -c Release -r win-x64 --self-contained -o publish/win-x64
dotnet publish src/ScmMoM.UI -c Release -r osx-arm64 --self-contained -o publish/osx-arm64
dotnet publish src/ScmMoM.UI -c Release -r osx-x64 --self-contained -o publish/osx-x64
dotnet publish src/ScmMoM.UI -c Release -r linux-x64 --self-contained -o publish/linux-x64
```

## Roadmap

Planned follow-on work is tracked in `TODO.md`.

## License

MIT
