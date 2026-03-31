# TODO

## Product Improvements

- Add saved account health diagnostics so users can see which API call failed and why without enabling a debugger.
- Support account groups, favorites, or custom dashboard presets for large multi-account setups.
- Add provider-specific deep links for notifications and issues where the current API response lacks a direct HTML URL.
- Allow repository discovery from the remote provider instead of requiring manual comma-separated entry.
- Persist selected account and selected tab across restarts.

## UX Improvements

- Add onboarding hints for required token scopes per provider directly in the login form.
- Improve token error messages so 401, connectivity, and scope issues are clearly distinguished.
- Add search and column sorting presets across all desktop and web tables.
- Add manual retry actions for a single account instead of always refreshing every provider.
- Add export or copy actions for failing CI annotations and PR comments.

## Architecture Improvements

- Extract dashboard refresh orchestration into an application service so the web server does not depend directly on view-model state.
- Add structured logging with log levels and a small in-app log viewer.
- Add cancellation and timeout policies around provider refresh operations.
- Introduce provider capability flags for features that are not uniformly supported across SCM vendors.
- Add automated integration tests around provider normalization and config migration.

## Web Dashboard Improvements

- Add account-scoped detail endpoints instead of relying on the active provider.
- Add authentication status and connection health indicators in the web UI.
- Add server-sent events or websockets for live refresh feedback.
- Add pagination or virtualization for large issue and notification lists.
- Add a mobile-specific table presentation for narrow screens.

## Packaging and Release Improvements

- Add packaged installers for Windows, macOS, and Linux instead of publish-folder distribution only.
- Add a reproducible release checklist and version stamping in the application UI.
- Publish sample configuration and token setup guidance for GitHub Enterprise, self-hosted GitLab, and self-hosted Gitea.
- Add screenshot automation for release notes and documentation refreshes.