# ScmMoM Architecture

## Overview

ScmMoM is a layered desktop application built around a provider abstraction that normalizes activity from GitHub, GitLab, and Gitea. The desktop process owns the complete runtime: configuration, secure token retrieval, provider instances, refresh orchestration, UI state, and the optional embedded web server.

At a high level the system is split into two projects:

- `src/ScmMoM.Core`: domain models, configuration, secure token storage, notification logic, and provider implementations.
- `src/ScmMoM.UI`: Avalonia application shell, MVVM view models, windows, compact mode, and the embedded HTTP server and web assets.

## Architectural Goals

- Present a single dashboard view across different SCM vendors.
- Support multiple accounts concurrently, including mixed providers.
- Keep provider-specific code isolated behind one interface.
- Preserve a desktop-first workflow while exposing a lightweight browser dashboard from the same process.
- Store tokens outside plain text configuration when the user opts in.

## Runtime Composition

### Process Startup

`App.axaml.cs` is the runtime composition root.

Startup sequence:

1. Build a small dependency injection container with `ConfigService`, `AccountManager`, `NotificationService`, and `ITokenStore`.
2. Load persisted settings from `appsettings.json` in the application output directory.
3. Apply the saved theme variant before opening any main UI.
4. Present `LoginWindow` with `LoginViewModel` as the initial interaction surface.

After authentication succeeds, the app:

1. Creates `DashboardViewModel`.
2. Loads active providers from `AccountManager`.
3. Evaluates token scope warnings across all connected accounts.
4. Opens `MainWindow`.
5. Creates tray integration and starts timed refresh.
6. Starts the embedded web server if enabled in config.

### Dependency Injection Scope

The app uses `Microsoft.Extensions.DependencyInjection` as a simple service locator for singleton services. There is no nested request scope because the application is effectively one long-lived desktop session.

## Core Domain Layer

### Configuration

`ConfigService` owns load, save, and migration behavior for `AppConfig`.

`AppConfig` contains:

- `Accounts`: list of provider account definitions.
- `RefreshIntervalSeconds`
- `NotificationsEnabled`
- `ThemeMode`
- `WebServerEnabled`
- `WebServerPort`
- `ApiPsk`

Configuration migration currently handles the transition from the original flat single-account format into the newer `Accounts` list.

### Account Definition

`ScmAccountConfig` is the canonical persisted account record. It includes:

- Provider type
- Display name
- Optional server URL for self-hosted GitLab or Gitea, and optionally GitHub Enterprise style hosts
- Username
- Organization or group
- Repository whitelist
- Remember-token preference

The repository whitelist is now treated as an authoritative filter when dashboard data is aggregated.

### Provider Abstraction

`IScmProvider` is the core seam that keeps vendor-specific logic out of the UI layer. Each provider must implement methods for:

- Token initialization and validation
- Review request retrieval
- Open PR or MR retrieval
- Recent CI retrieval
- Notifications retrieval
- Assigned issue retrieval
- Run annotation lookup
- PR or MR comment lookup
- Rate-limit reporting
- Token scope auditing

Because the interface returns normalized models, the UI does not need to know whether a result originated from Octokit, NGitLab, or direct REST calls.

### Provider Implementations

#### GitHubProvider

GitHub integration uses Octokit for authenticated API access. The provider covers:

- Pull requests and review requests
- Actions workflow runs and annotations
- Notifications
- Assigned issues
- PR comments
- Rate-limit tracking from Octokit `ApiInfo`
- Scope analysis for classic tokens

GitHub’s current-user endpoints can return account-wide results, so the dashboard layer applies the configured repository whitelist before showing aggregated data.

#### GitLabProvider

GitLab integration uses NGitLab plus direct REST fallbacks where the SDK does not provide a convenient endpoint. It covers:

- Merge requests where the user is a reviewer
- Open merge requests for configured projects
- Pipelines
- Todos as notification equivalents
- Assigned issues
- MR comments
- PAT scope checks through GitLab token metadata

#### GiteaProvider

Gitea integration is based on REST calls with `HttpClient`. It covers:

- Pull requests
- Action runs where supported
- Notifications
- Assigned issues using a per-repository scan
- PR comments
- Server URL handling for self-hosted instances

### AccountManager

`AccountManager` is a small provider registry and factory. Its responsibilities are intentionally limited:

- Build the correct provider implementation for an account.
- Store active provider instances by account id.
- Expose the currently active providers for refresh and drill-down operations.

This keeps provider lifecycle concerns out of the view models.

### Secure Token Storage

`CredentialStoreService` implements `ITokenStore` using Git Credential Manager. Tokens are keyed per account under a URI-like namespace. This gives cross-platform secure storage support through the platform’s credential backend instead of plain text config.

Behavior:

- Tokens are optional and user-controlled via `Remember token`.
- Config stores account metadata but not the secret itself.
- Legacy credential keys are still read and removed for backward compatibility.

### NotificationService

`NotificationService` is responsible for detecting new review requests and failing CI runs between refreshes. The UI consumes its derived events to raise desktop notifications without hard-wiring that logic into provider or window code.

## UI Layer

### MVVM and ReactiveUI

The Avalonia client uses ReactiveUI-based MVVM. View models own state, commands, and transformations. Views remain mostly declarative in XAML, with limited code-behind for UI-specific behavior.

Primary view models:

- `LoginViewModel`: account onboarding, inline token save, connection orchestration.
- `DashboardViewModel`: refresh orchestration, aggregate collections, account filtering, tray tooltip, rate-limit state, and detail panel state.
- `SettingsViewModel`: account editing, refresh and theme settings, web server configuration, and PSK generation.

### Login Flow

`LoginViewModel` performs three distinct jobs:

- Bootstrap from existing config and credential store entries.
- Create new accounts and validate their tokens before persisting.
- Allow inline token entry for accounts that exist in config but do not have a saved token.

This gives the application a recoverable startup path when account metadata exists but credentials do not.

### Dashboard Aggregation

`DashboardViewModel` is the most important orchestration layer in the app.

Refresh behavior:

1. Collect active providers from `AccountManager`.
2. Request reviews, PRs, CI runs, notifications, and issues from every provider in parallel.
3. Tag each result with account id and account display name.
4. Apply repository whitelist filtering per account before aggregation.
5. Store full in-memory collections for later account filtering in the desktop UI.
6. Update rate-limit status, tray tooltip, and notification detections.

This design keeps provider implementations focused on data retrieval while letting the dashboard own cross-provider normalization and presentation filtering.

### Drill-Down Panels

The desktop dashboard supports secondary detail views for:

- CI annotations per run
- PR or MR comments

These rely on the active account context, because drill-down APIs are provider-specific and require the corresponding provider instance.

### Tray and Compact Mode

The desktop shell supports a tray-first workflow:

- Closing the dashboard hides it instead of shutting down.
- Tray menu exposes open, compact mode, refresh, and quit actions.
- Compact mode reuses the same dashboard view model but renders a reduced window for quick-glance monitoring.

## Embedded Web Server

`ScmWebServer` hosts a slim ASP.NET Core application inside the desktop process.

Responsibilities:

- Serve static assets from `wwwroot`.
- Expose JSON endpoints backed directly by `DashboardViewModel` collections.
- Gate `/api/*` requests behind an optional pre-shared key.
- Trigger desktop refresh operations through `POST /api/refresh`.

Important constraint:

- The web server reads already-aggregated dashboard state. It is not a separate backend and does not own provider refresh behavior itself.

This keeps the deployment model simple at the cost of tighter coupling to the in-process desktop session.

## Data Flow

### Normal Refresh Cycle

1. Timer or user action invokes dashboard refresh.
2. Dashboard asks each provider for normalized collections.
3. Dashboard filters and tags records.
4. Observable collections update the Avalonia UI.
5. The same collections become available to the embedded web API.

### Authentication Cycle

1. User enters account metadata and token.
2. `LoginViewModel` creates a provider through `AccountManager`.
3. Provider validates token against the vendor API.
4. Successful accounts are persisted to config.
5. Optional token persistence writes to the credential store.
6. Connected providers are held in memory for the session.

## Platform and Packaging Notes

- Target framework: `net9.0`
- Desktop UI: Avalonia 11
- Data grid support: `Avalonia.Controls.DataGrid`
- Embedded server: `Microsoft.AspNetCore.App`
- Linux packaging support includes a `.desktop` asset and application icon resources.

## Extension Points

ScmMoM is structured so the following additions are straightforward:

- New SCM vendor support by implementing `IScmProvider`.
- Additional dashboard tabs through new normalized models and provider methods.
- Background refresh or headless worker mode by extracting the current dashboard orchestration into a service.
- Richer web API behavior if the current in-process view-model-backed API is replaced with a dedicated aggregation service.

## Current Tradeoffs

- The embedded web API depends on desktop view-model state rather than a service-oriented backend.
- Provider capabilities are normalized to a common denominator, so some vendor-specific richness is intentionally hidden.
- Most runtime state is held in memory for simplicity, which is appropriate for a desktop monitor but limits background or distributed use cases.
- The current DI setup is intentionally lightweight and optimized for a single-process desktop app, not a large modular host.

## Summary

Release `0.1` delivers a pragmatic architecture: a thin domain core around a provider abstraction, a ReactiveUI desktop shell for orchestration and presentation, and an optional in-process web surface for remote visibility. It is small enough to stay maintainable, but already structured around the right seams for additional providers, richer notifications, and a more service-oriented backend if the product grows.