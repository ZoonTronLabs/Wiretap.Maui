using System.Globalization;
using Wiretap.Maui.Core;
using Wiretap.Maui.UI;

namespace Wiretap.Maui.Services;

/// <summary>
/// Manages the Wiretap entry point (notification-based on supported platforms).
/// </summary>
public sealed partial class WiretapEntryPointService : IDisposable
{
    private const int MaxPreviewLines = 5;
    private static readonly TimeSpan UpdateDelay = TimeSpan.FromMilliseconds(500);
    private readonly IWiretapStore _store;
    private readonly WiretapOptions _options;
    private readonly object _sync = new();
    private readonly Timer _updateTimer;
    private bool _isVisible;
    private bool _disposed;
    private bool _updateScheduled;

    public WiretapEntryPointService(IWiretapStore store, WiretapOptions options)
    {
        _store = store;
        _options = options;
        _updateTimer = new Timer(_ => UpdateCount(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        _store.OnRecordAdded += OnRecordAdded;
        _store.OnRecordsCleared += OnRecordsCleared;
    }

    /// <summary>
    /// Shows the entry point (notification on Android, badge/notification on iOS).
    /// </summary>
    public void Show()
    {
        if (!_options.ShowFloatingButton)
            return;

        lock (_sync)
        {
            if (_disposed)
                return;

            _isVisible = true;
            if (!_updateScheduled)
            {
                _updateScheduled = true;
                _updateTimer.Change(TimeSpan.Zero, Timeout.InfiniteTimeSpan);
            }
        }
    }

    /// <summary>
    /// Hides the entry point.
    /// </summary>
    public void Hide()
    {
        lock (_sync)
        {
            if (_disposed)
                return;

            _isVisible = false;
            _updateScheduled = false;
            _updateTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            HidePlatform();
        }
    }

    private void OnRecordAdded(HttpRecord _)
    {
        ScheduleUpdate();
    }

    private void OnRecordsCleared()
    {
        ScheduleUpdate();
    }

    private void ScheduleUpdate()
    {
        lock (_sync)
        {
            if (_isVisible && !_disposed && !_updateScheduled)
            {
                _updateScheduled = true;
                _updateTimer.Change(UpdateDelay, Timeout.InfiniteTimeSpan);
            }
        }
    }

    private void UpdateCount()
    {
        lock (_sync)
        {
            _updateScheduled = false;
            if (!_isVisible || _disposed)
                return;

            ShowPlatform(_store.Count);
        }
    }

    private bool IsVisible
    {
        get { lock (_sync) return _isVisible && !_disposed; }
    }

    partial void ShowPlatform(int count);
    partial void HidePlatform();

    private IReadOnlyList<string> BuildPreviewLines()
    {
        var lines = new List<string>(MaxPreviewLines);
        var records = _store.GetRecords();

        foreach (var record in records.Take(MaxPreviewLines))
        {
            var line = FormatPreviewLine(record);
            if (!string.IsNullOrWhiteSpace(line))
                lines.Add(line);
        }

        return lines;
    }

    private static string FormatPreviewLine(HttpRecord record)
    {
        var status = record.IsComplete
            ? record.StatusCode.ToString(CultureInfo.InvariantCulture)
            : record.IsFailed ? "ERR" : "PEND";

        var method = string.IsNullOrWhiteSpace(record.Method)
            ? "?"
            : record.Method.ToUpperInvariant();

        var path = ExtractPath(record.Url);
        if (string.IsNullOrWhiteSpace(path))
            path = record.DisplayUrl;

        if (string.IsNullOrWhiteSpace(path))
            return $"{status} {method}";

        return $"{status} {method} {Truncate(path, 48)}";
    }

    private static string ExtractPath(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return string.Empty;

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            if (!string.IsNullOrWhiteSpace(uri.AbsolutePath))
                return uri.AbsolutePath;
            if (!string.IsNullOrWhiteSpace(uri.Host))
                return uri.Host;
        }

        return url;
    }

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
            return value;

        if (maxLength <= 3)
            return value[..maxLength];

        return value[..(maxLength - 3)] + "...";
    }

    public void Dispose()
    {
        _store.OnRecordAdded -= OnRecordAdded;
        _store.OnRecordsCleared -= OnRecordsCleared;
        Hide();
        lock (_sync)
        {
            _disposed = true;
            _updateTimer.Dispose();
        }
    }

    internal static void OpenInspectorFromNotification()
    {
        var services = WiretapServiceLocator.GetServices();
        services?.GetService<WiretapNavigator>()?.Open();
    }
}
