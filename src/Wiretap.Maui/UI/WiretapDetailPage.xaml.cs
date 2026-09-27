using System.Text;
using System.Text.Json;
using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using Wiretap.Maui.Core;
using Wiretap.Maui.Services;

namespace Wiretap.Maui.UI;

/// <summary>
/// Page displaying detailed information about a single HTTP request/response.
/// </summary>
public partial class WiretapDetailPage : ContentPage, IQueryAttributable
{
    private readonly IWiretapStore _store;
    private readonly WiretapOptions _options;
    private HttpRecord? _currentRecord;
    private bool _isRequestTabActive = true;

    // Raw content for copy operations
    private string _requestHeadersRaw = string.Empty;
    private string _requestBodyRaw = string.Empty;
    private string _responseHeadersRaw = string.Empty;
    private string _responseBodyRaw = string.Empty;

    public WiretapDetailPage(IWiretapStore store, WiretapOptions? options = null)
    {
        InitializeComponent();
        _store = store;
        _options = options ?? new WiretapOptions();
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (!query.TryGetValue("RecordId", out var value))
            return;

        Guid recordId;
        if (value is Guid guid)
            recordId = guid;
        else if (Guid.TryParse(value?.ToString(), out var parsed))
            recordId = parsed;
        else
            return;

        if (recordId != Guid.Empty)
            _ = LoadRecordAsync(recordId);
    }

    /// <summary>
    /// Load and display a specific HTTP record.
    /// </summary>
    public void LoadRecord(Guid recordId)
    {
        var record = _store.GetRecord(recordId);
        if (record == null)
        {
            MethodLabel.Text = "Not Found";
            UrlLabel.Text = "Record not found";
            return;
        }

        _currentRecord = record;
        DisplayRecord(record);
    }

    private async Task LoadRecordAsync(Guid recordId)
    {
        try
        {
            var record = await _store.GetRecordAsync(recordId);
            if (record is null)
            {
                MethodLabel.Text = "Not Found";
                UrlLabel.Text = "Record not found";
                return;
            }

            _currentRecord = record;
            DisplayRecord(record);
        }
        catch (Exception)
        {
            MethodLabel.Text = "Unavailable";
            UrlLabel.Text = "Could not load this record";
        }
    }

    private void DisplayRecord(HttpRecord record)
    {
        // Title
        Title = $"{record.Method} {(record.IsComplete ? record.StatusCode.ToString() : "Failed")}";

        // Method badge
        MethodLabel.Text = record.Method;
        MethodBadge.BackgroundColor = GetMethodColor(record.Method);

        // Status badge
        if (record.IsComplete)
        {
            StatusBadge.IsVisible = true;
            StatusLabel.Text = $"{record.StatusCode} {record.ReasonPhrase}";
            StatusBadge.BackgroundColor = GetStatusColor(record.StatusCode);
            ErrorIndicator.IsVisible = false;
        }
        else
        {
            StatusBadge.IsVisible = false;
            ErrorIndicator.IsVisible = true;
        }

        // URL
        UrlLabel.Text = record.Url;

        // Metadata
        TimestampLabel.Text = $"📅 {record.LocalTimestamp:HH:mm:ss.fff}";
        DurationLabel.Text = $"⏱️ {record.DurationDisplay}";
        SizeLabel.Text = $"📦 {record.TotalSize:N0} bytes";

        // Error message
        if (!record.IsComplete && !string.IsNullOrEmpty(record.ErrorMessage))
        {
            ErrorMessageLabel.Text = $"Error: {record.ErrorMessage}";
            ErrorMessageLabel.IsVisible = true;
        }
        else
        {
            ErrorMessageLabel.IsVisible = false;
        }

        // Request content
        _requestHeadersRaw = FormatHeadersRaw(record.RequestHeaders);
        RequestHeadersLabel.Text = string.IsNullOrEmpty(_requestHeadersRaw) ? "(no headers)" : _requestHeadersRaw;

        _requestBodyRaw = record.RequestBody ?? string.Empty;
        RequestBodyEditor.Text = FormatBody(record.RequestBody, record.RequestBodyTruncated);
        RequestTruncatedLabel.IsVisible = record.RequestBodyTruncated;

        // Response content
        _responseHeadersRaw = FormatHeadersRaw(record.ResponseHeaders);
        ResponseHeadersLabel.Text = string.IsNullOrEmpty(_responseHeadersRaw) ? "(no headers)" : _responseHeadersRaw;

        _responseBodyRaw = record.ResponseBody ?? string.Empty;
        ResponseBodyEditor.Text = FormatBody(record.ResponseBody, record.ResponseBodyTruncated);
        ResponseTruncatedLabel.IsVisible = record.ResponseBodyTruncated;

        // Set initial tab state
        SetActiveTab(true);
    }

    private static string FormatHeadersRaw(Dictionary<string, string[]> headers)
    {
        if (headers.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        foreach (var header in headers.OrderBy(h => h.Key))
        {
            foreach (var value in header.Value)
            {
                sb.AppendLine($"{header.Key}: {value}");
            }
        }
        return sb.ToString().TrimEnd();
    }

    // Reusable JSON options for pretty-printing with proper Unicode support
    private static readonly JsonSerializerOptions PrettyPrintOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private string FormatBody(string? body, bool truncated)
    {
        if (string.IsNullOrEmpty(body))
            return "(no body)";

        // Detect binary content (high ratio of non-printable chars)
        if (IsBinaryContent(body))
            return $"(binary content, {body.Length:N0} bytes)";

        // Cap display size to prevent UI freeze
        const int maxDisplayChars = 32_768;
        var displayBody = body;
        var displayTruncated = truncated;
        if (body.Length > maxDisplayChars)
        {
            displayBody = body[..maxDisplayChars];
            displayTruncated = true;
        }

        var result = displayBody;

        // Try to pretty-print JSON
        if (_options.PrettyPrintJson)
        {
            try
            {
                var json = JsonSerializer.Deserialize<JsonElement>(displayBody);
                result = JsonSerializer.Serialize(json, PrettyPrintOptions);
            }
            catch
            {
                // Not JSON, use as-is
            }
        }

        if (displayTruncated)
        {
            result += "\n\n... (truncated)";
        }

        return result;
    }

    private static bool IsBinaryContent(string text)
    {
        const int sampleSize = 512;
        var sample = text.Length > sampleSize ? text[..sampleSize] : text;
        var nonPrintable = sample.Count(c => char.IsControl(c) && c != '\n' && c != '\r' && c != '\t');
        return nonPrintable > sample.Length * 0.1;
    }

    private static Color GetMethodColor(string method)
    {
        return method.ToUpperInvariant() switch
        {
            "GET" => Color.FromArgb("#2196F3"),     // Blue
            "POST" => Color.FromArgb("#4CAF50"),    // Green
            "PUT" => Color.FromArgb("#FF9800"),     // Orange
            "PATCH" => Color.FromArgb("#9C27B0"),   // Purple
            "DELETE" => Color.FromArgb("#F44336"), // Red
            "HEAD" => Color.FromArgb("#607D8B"),    // Blue-grey
            "OPTIONS" => Color.FromArgb("#795548"), // Brown
            _ => Color.FromArgb("#9E9E9E")          // Grey
        };
    }

    private static Color GetStatusColor(int statusCode)
    {
        return statusCode switch
        {
            >= 200 and < 300 => Color.FromArgb("#4CAF50"), // Green - success
            >= 300 and < 400 => Color.FromArgb("#2196F3"), // Blue - redirect
            >= 400 and < 500 => Color.FromArgb("#FF9800"), // Orange - client error
            >= 500 => Color.FromArgb("#F44336"),           // Red - server error
            _ => Color.FromArgb("#9E9E9E")                 // Grey - unknown
        };
    }

    private void SetActiveTab(bool isRequestTab)
    {
        _isRequestTabActive = isRequestTab;

        // Update button styles
        if (isRequestTab)
        {
            RequestTabButton.BackgroundColor = Application.Current?.RequestedTheme == AppTheme.Dark
                ? Color.FromArgb("#1976D2")
                : Color.FromArgb("#2196F3");
            RequestTabButton.TextColor = Colors.White;

            ResponseTabButton.BackgroundColor = Application.Current?.RequestedTheme == AppTheme.Dark
                ? Color.FromArgb("#3D3D3D")
                : Color.FromArgb("#E0E0E0");
            ResponseTabButton.TextColor = Application.Current?.RequestedTheme == AppTheme.Dark
                ? Color.FromArgb("#AAAAAA")
                : Color.FromArgb("#666666");
        }
        else
        {
            ResponseTabButton.BackgroundColor = Application.Current?.RequestedTheme == AppTheme.Dark
                ? Color.FromArgb("#1976D2")
                : Color.FromArgb("#2196F3");
            ResponseTabButton.TextColor = Colors.White;

            RequestTabButton.BackgroundColor = Application.Current?.RequestedTheme == AppTheme.Dark
                ? Color.FromArgb("#3D3D3D")
                : Color.FromArgb("#E0E0E0");
            RequestTabButton.TextColor = Application.Current?.RequestedTheme == AppTheme.Dark
                ? Color.FromArgb("#AAAAAA")
                : Color.FromArgb("#666666");
        }

        // Show/hide content
        RequestContent.IsVisible = isRequestTab;
        ResponseContent.IsVisible = !isRequestTab;
    }

    private void OnRequestTabClicked(object? sender, EventArgs e)
    {
        SetActiveTab(true);
    }

    private void OnResponseTabClicked(object? sender, EventArgs e)
    {
        SetActiveTab(false);
    }

    private async void OnCopyRequestHeadersClicked(object? sender, EventArgs e)
    {
        await CopyToClipboardAsync(_requestHeadersRaw, "Request headers");
    }

    private async void OnCopyRequestBodyClicked(object? sender, EventArgs e)
    {
        await CopyToClipboardAsync(_requestBodyRaw, "Request body");
    }

    private async void OnCopyResponseHeadersClicked(object? sender, EventArgs e)
    {
        await CopyToClipboardAsync(_responseHeadersRaw, "Response headers");
    }

    private async void OnCopyResponseBodyClicked(object? sender, EventArgs e)
    {
        await CopyToClipboardAsync(_responseBodyRaw, "Response body");
    }

    private async Task CopyToClipboardAsync(string content, string description)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            await ShowToastAsync($"{description} is empty.");
            return;
        }

        await Clipboard.Default.SetTextAsync(content);

        await ShowToastAsync($"{description} copied to clipboard.");
    }

    private async void OnExportPdfClicked(object? sender, EventArgs e)
    {
        if (_currentRecord == null)
        {
            await DisplayAlertAsync("Error", "No record loaded.", "OK");
            return;
        }

        try
        {
            // Generate PDF
            var pdfBytes = PdfExporter.ToPdf(_currentRecord);
            var fileName = PdfExporter.GenerateFileName(_currentRecord);

            // Save to cache directory for sharing
            var cacheDir = FileSystem.CacheDirectory;
            var filePath = Path.Combine(cacheDir, fileName);
            await File.WriteAllBytesAsync(filePath, pdfBytes);

            // Share the PDF file
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = $"HTTP {_currentRecord.Method} Request Report",
                File = new ShareFile(filePath, "application/pdf")
            });
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", $"Failed to export PDF: {ex.Message}", "OK");
        }
    }

    private async void OnCopyCurlClicked(object? sender, EventArgs e)
    {
        if (_currentRecord == null)
        {
            await ShowToastAsync("No record loaded.");
            return;
        }

        await CurlExporter.CopyToClipboardAsync(_currentRecord);
        await ShowToastAsync("cURL command copied to clipboard.");
    }

    private static Task ShowToastAsync(string message)
    {
        var toast = Toast.Make(message, ToastDuration.Short, 14);
        return toast.Show(CancellationToken.None);
    }
}
