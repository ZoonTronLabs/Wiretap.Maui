# Wiretap.Maui

In-app HTTP traffic inspector for .NET MAUI - debug network requests like [Chucker](https://github.com/ChuckerTeam/chucker) for Android.

[![NuGet](https://img.shields.io/nuget/v/Wiretap.Maui.svg)](https://www.nuget.org/packages/Wiretap.Maui/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

## Screenshots

<p align="center">
  <img src="screenshots/list-ios.jpg" width="250" alt="Request List" />
  <img src="screenshots/detail-ios.jpg" width="250" alt="Request Detail" />
  <img src="screenshots/notification-android.jpg" width="300" alt="Android Notification" />
</p>

## Features

- 📡 **Intercept HTTP traffic** - Captures all requests/responses from your HttpClient
- 🎯 **Simple setup** - Add the handler and choose in-memory or host-provided SQLite storage
- 📋 **Request list** - View all captured requests with method, status, duration, size
- 🔍 **Detail view** - Full request/response headers and bodies with tabs
- 🔎 **Search & Filter** - Filter by method (GET, POST, etc.), status code (2xx, 4xx, 5xx), or search text
- 🎨 **JSON formatting** - Pretty-printed JSON bodies for easy reading
- 🔒 **Sensitive data masking** - Auto-masks Authorization headers, API keys, cookies
- 📤 **Export to cURL** - Copy request as cURL command
- 📄 **Export to PDF** - Generate professional PDF reports
- 📋 **Copy to clipboard** - Copy headers or body with one tap
- 💾 **SQLite persistence** - Optional persistent storage across app restarts
- 🔔 **Quiet entry point** - Persistent Android notification or passive iOS notification-list item
- 🌙 **Dark mode support** - Adapts to system theme
- ⚡ **Debug-only** - Easily exclude from release builds with `#if DEBUG`
- 🔄 **Bounded storage** - Configurable memory limit and bounded background write queue

## Installation

```bash
dotnet add package Wiretap.Maui
```

Or via NuGet Package Manager:
```
Install-Package Wiretap.Maui
```

## Quick Start

### Step 1: Add Wiretap to your MauiProgram.cs

```csharp
using Wiretap.Maui;

public static MauiApp CreateMauiApp()
{
    var builder = MauiApp.CreateBuilder();
    builder
        .UseMauiApp<App>()
#if DEBUG
        .UseWiretap(options => options.EnablePersistence = false)
#endif
        ;

    return builder.Build();
}
```

### Step 2: Add the handler to your HttpClients

```csharp
builder.Services.AddHttpClient("MyApi", client =>
{
    client.BaseAddress = new Uri("https://api.example.com/");
})
#if DEBUG
.AddWiretapHandler()  // Add this line!
#endif
;
```

That's it for in-memory capture. To keep records across launches, see the SQLite setup below.

### SQLite persistence (optional)

Wiretap 2.0 no longer bundles a native SQLite library. The host app chooses one provider and initializes it **before** Wiretap opens its database. For example:

```xml
<PackageReference Include="SQLite3MC.PCLRaw.bundle" Version="2.4.0" />
```

```csharp
public static MauiApp CreateMauiApp()
{
    SQLitePCL.Batteries_V2.Init();
    var builder = MauiApp.CreateBuilder();
    builder.UseMauiApp<App>().UseWiretap(); // persistence is enabled by default
    return builder.Build();
}
```

If the host already initializes a SQLitePCLRaw provider, Wiretap uses that same provider. It does not call `Batteries_V2.Init()` or bring a second native SQLite implementation. Without a provider, set `EnablePersistence = false`; otherwise initialization reports the missing setup.

## ⚠️ Important Notes

### Handler Order Matters

Always add `WiretapHandler` **AFTER** your auth handlers so it captures requests WITH authorization headers:

```csharp
builder.Services.AddHttpClient<ApiService>(...)
    .AddHttpMessageHandler<AuthTokenHandler>()  // First: adds auth token
#if DEBUG
    .AddWiretapHandler()                         // Second: captures request with token
#endif
    .AddStandardResilienceHandler();
```

### Accessing the Inspector

The recommended way to access the HTTP inspector is via manual navigation (add a button in your Settings or Debug menu):

```csharp
// Add to your Settings page ViewModel
[RelayCommand]
private async Task OpenHttpInspectorAsync()
{
    await Shell.Current.GoToAsync("WiretapPage");
}
```

## Configuration

Customize Wiretap behavior with options:

```csharp
.UseWiretap(options =>
{
    options.MaxStoredRequests = 500;        // Max requests to keep (default: 500)
    options.ShowFloatingButton = true;      // Show notification entry point (default: true)
    options.PrettyPrintJson = true;         // Format JSON bodies (default: true)
    options.MaskSensitiveHeaders = true;    // Mask auth headers (default: true)
    options.CaptureRequestHeaders = true;   // Capture request headers (default: true)
    options.CaptureResponseHeaders = true;  // Capture response headers (default: true)
    options.MaxBodySizeBytes = 1_048_576;   // Max captured body bytes (default: 1MB)
    options.EnablePersistence = false;      // In-memory mode without SQLite provider

    // Custom sensitive header patterns
    options.SensitiveHeaderPatterns = new[]
    {
        "Authorization", "X-Api-Key", "Cookie", "Set-Cookie", "X-Auth-Token"
    };
})
```

## Show/Hide the Notification Entry Point

Android keeps one low-importance ongoing notification that opens the inspector. iOS keeps a passive item in the notification list: Wiretap shows no foreground banner or sound and never changes the app icon badge. Updates are coalesced. The host app remains responsible for notification permissions.

```csharp
// In your App.xaml.cs or anywhere with access to Application

#if DEBUG
this.ShowWiretapEntryPoint();

this.HideWiretapEntryPoint();
#endif

// Or using IServiceProvider
serviceProvider.ShowWiretapEntryPoint();
```

## Navigate to Inspector Directly (Recommended)

Add a button to your Settings or Profile page for easy access:

**ViewModel:**
```csharp
public partial class SettingsViewModel : ObservableObject
{
#if DEBUG
    public bool IsDebugMode => true;

    [RelayCommand]
    private async Task OpenHttpInspectorAsync()
    {
        await Shell.Current.GoToAsync("WiretapPage");
    }
#else
    public bool IsDebugMode => false;
#endif
}
```

**XAML:**
```xml
<!-- Developer Tools section (only visible in DEBUG) -->
<VerticalStackLayout IsVisible="{Binding IsDebugMode}">
    <Label Text="DEVELOPER TOOLS" Style="{StaticResource SectionHeaderStyle}" />

    <Button Text="📡 HTTP Inspector"
            Command="{Binding OpenHttpInspectorCommand}"
            BackgroundColor="#2196F3"
            TextColor="White" />
</VerticalStackLayout>
```

**Programmatic navigation:**
```csharp
// Using Shell navigation (routes are auto-registered)
await Shell.Current.GoToAsync("WiretapPage");

// Or push as modal
var store = serviceProvider.GetService<IWiretapStore>();
var page = new WiretapPage(store, Application.Current.Dispatcher);
await Navigation.PushModalAsync(new NavigationPage(page));
```

## Supported Platforms

| Platform | Minimum Version |
|----------|-----------------|
| iOS | 15.0+ |
| Mac Catalyst | 15.0+ |
| Android | API 24+ (Android 7.0) |

## How It Works

Wiretap uses a `DelegatingHandler` to intercept HTTP traffic in the `HttpClient` pipeline:

```
Your App → WiretapHandler → AuthHandler → Network
              ↓
         WiretapStore (ring buffer)
              ↓
         WiretapPage (UI)
```

**Key points:**
- Seekable request/response bodies are read up to `MaxBodySizeBytes`, then rewound so callers receive the original content. Non-seekable bodies are left untouched and omitted from the preview.
- Records are stored in memory with a configurable limit (oldest removed when full)
- The quiet notification entry point provides quick access to the inspector UI
- Sensitive headers are masked by default to protect credentials

## Architecture

```
┌─────────────────────────────────────────────────────┐
│                   Your MAUI App                      │
│  ┌─────────────┐    ┌──────────────────┐            │
│  │ MauiProgram │───▶│ .UseWiretap()    │            │
│  └─────────────┘    └──────────────────┘            │
│         │                                            │
│         ▼                                            │
│  ┌─────────────────────────────────────┐            │
│  │         HttpClient Pipeline         │            │
│  │  ┌───────────────┐ ┌─────────────┐  │            │
│  │  │WiretapHandler │▶│ YourHandler │  │            │
│  │  └───────────────┘ └─────────────┘  │            │
│  └─────────────────────────────────────┘            │
└─────────────────────────────────────────────────────┘
                    │
                    ▼
┌─────────────────────────────────────────────────────┐
│               Wiretap.Maui Package                   │
│                                                      │
│  ┌──────────────────┐     ┌─────────────────────┐   │
│  │  WiretapHandler  │────▶│   IWiretapStore     │   │
│  │ (DelegatingHandler)    │   (Ring Buffer)     │   │
│  └──────────────────┘     └──────────┬──────────┘   │
│                                      │              │
│  ┌──────────────────┐     ┌──────────▼──────────┐   │
│  │ Entry Point      │────▶│   WiretapPage       │   │
│  │ (Notification)   │     │   (Request List)    │   │
│  └──────────────────┘     └──────────┬──────────┘   │
│                                      │              │
│                           ┌──────────▼──────────┐   │
│                           │ WiretapDetailPage   │   │
│                           │ (Headers + Body)    │   │
│                           └─────────────────────┘   │
└─────────────────────────────────────────────────────┘
```

## Debug-Only Best Practice

Always wrap Wiretap integration in `#if DEBUG` to ensure it's excluded from release builds:

```csharp
#if DEBUG
    .UseWiretap()
#endif

// and

#if DEBUG
    .AddWiretapHandler()
#endif
```

## Sample App

See the [samples/Wiretap.Maui.Sample](samples/Wiretap.Maui.Sample) folder for a complete working example. It initializes its own SQLite3MC provider before Wiretap.

Run it with:
```bash
cd samples/Wiretap.Maui.Sample
dotnet build -t:Run -f net10.0-ios
```

## API Reference

### WiretapExtensions

| Method | Description |
|--------|-------------|
| `UseWiretap(options?)` | Adds Wiretap services to the MAUI app |
| `AddWiretapHandler()` | Adds the HTTP interception handler to HttpClient |
| `ShowWiretapEntryPoint()` | Shows the notification entry point |
| `HideWiretapEntryPoint()` | Hides the notification entry point |
| `InitializeWiretapAsync()` | Loads persisted records when SQLite is enabled |

### WiretapOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MaxStoredRequests` | `int` | 500 | Maximum number of requests to keep |
| `ShowFloatingButton` | `bool` | true | Whether to show the notification entry point |
| `PrettyPrintJson` | `bool` | true | Format JSON in detail view |
| `MaskSensitiveHeaders` | `bool` | true | Mask sensitive header values |
| `CaptureRequestHeaders` | `bool` | true | Capture request headers |
| `CaptureResponseHeaders` | `bool` | true | Capture response headers |
| `MaxBodySizeBytes` | `int` | 1MB | Max body bytes to capture from seekable content |
| `EnablePersistence` | `bool` | true | Store records with the host's SQLite provider |
| `MaxPersistedRequests` | `int` | 1000 | Maximum records retained on disk |
| `SensitiveHeaderPatterns` | `string[]` | See below | Headers to mask |

**Default sensitive headers:** `Authorization`, `X-Api-Key`, `Cookie`, `Set-Cookie`

### IWiretapStore

For programmatic access to captured requests:

```csharp
public interface IWiretapStore
{
    IReadOnlyList<HttpRecord> GetRecords();
    HttpRecord? GetRecord(Guid id);
    void Clear();
    event Action<HttpRecord>? OnRecordAdded;
    event Action? OnRecordsCleared;
}
```

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Acknowledgments

- Inspired by [Chucker](https://github.com/ChuckerTeam/chucker) for Android
- Project structure based on [Plugin.Maui.Feature](https://github.com/jfversluis/Plugin.Maui.Feature) template by Gerald Versluis

## Roadmap

- [x] HTTP traffic interception
- [x] Request list and detail UI
- [x] JSON pretty-printing
- [x] Sensitive header masking
- [x] Export to cURL format
- [x] Export to PDF format
- [x] Request filtering and search
- [x] SQLite persistent storage
- [x] Notifications (Android/iOS)
- [x] Dark mode support
