# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [2.0.0] - 2026-09-27

### Changed
- SQLite persistence now uses `sqlite-net-base` and the host's initialized SQLitePCLRaw provider. The package no longer brings `bundle_green`, its native SQLite library, or a competing provider. Hosts using persistence must initialize their own provider before Wiretap; hosts without one can set `EnablePersistence = false`.
- Updated CommunityToolkit.Maui to 15.0.1 and the MAUI Controls minimum to 10.0.90.
- Capture reads only a bounded preview from seekable HTTP content and leaves non-seekable streams untouched. Non-seekable body previews are omitted.
- Background persistence uses a bounded queue; under sustained overload, the oldest queued captures may be dropped.

### Fixed
- iOS Wiretap notifications stay passive in the notification list without foreground banners or sound; app notifications keep their existing delegate behavior.
- Wiretap no longer writes or clears the host app's icon badge.
- Android keeps its low-importance ongoing entry point and updates it without repeated alerts.
- Notification count updates are coalesced and no longer copy and sort the entire store on every request.
- Clearing persisted records cannot reinsert older queued captures; opening a persisted record from the inspector no longer blocks the UI thread.
- Invalid store capacities fail immediately instead of looping while adding records.

## [1.0.0] - 2026-02-05

### Added
- HTTP traffic interception via `DelegatingHandler`
- In-memory ring buffer storage with configurable capacity
- Request list UI with method, URL, status, duration display
- Request detail view with headers and body tabs
- JSON pretty-printing for request/response bodies
- Sensitive header masking (Authorization, API keys, cookies)
- Search and filter by method, status code, or text
- Export to cURL format
- Export to PDF format with professional styling
- SQLite persistent storage option
- Notifications on Android and iOS for quick access
- Dark mode support
- Shell navigation integration (`WiretapPage` route)
- Localized timestamp display

### Supported Platforms
- iOS 15.0+
- Mac Catalyst 15.0+
- Android API 24+ (Android 7.0)
