using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using Wiretap.Maui.Core;

namespace Wiretap.Maui.Handler;

/// <summary>
/// HTTP message handler that intercepts and records all HTTP traffic for inspection.
/// Buffers only bodies with a known size within a fixed memory limit so inspection
/// never probes a live one-shot response stream.
/// </summary>
public class WiretapHandler : DelegatingHandler
{
    private const int MinimumBufferLimitBytes = 1_048_576;
    private readonly IWiretapStore _store;
    private readonly WiretapOptions _options;

    /// <summary>
    /// Creates a new WiretapHandler.
    /// </summary>
    /// <param name="store">The store for captured HTTP records.</param>
    /// <param name="options">Configuration options.</param>
    public WiretapHandler(IWiretapStore store, WiretapOptions options)
    {
        _store = store;
        _options = options;
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var record = new HttpRecord
        {
            Method = request.Method.Method,
            Url = request.RequestUri?.ToString() ?? string.Empty
        };

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Capture request details
            await CaptureRequestAsync(request, record, cancellationToken);

            // Send the request
            var response = await base.SendAsync(request, cancellationToken);

            stopwatch.Stop();
            record.Duration = stopwatch.Elapsed;

            // Capture response details
            await CaptureResponseAsync(response, record, cancellationToken);

            record.IsComplete = true;
            _store.Add(record);

            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            record.Duration = stopwatch.Elapsed;
            record.IsComplete = false;
            record.ErrorMessage = ex.Message;
            _store.Add(record);

            throw;
        }
    }

    private async Task CaptureRequestAsync(
        HttpRequestMessage request,
        HttpRecord record,
        CancellationToken cancellationToken)
    {
        if (_options.CaptureRequestHeaders)
        {
            record.RequestHeaders.Clear();
            CaptureHeaders(request.Headers, record.RequestHeaders);
            if (request.Content != null)
                CaptureHeaders(request.Content.Headers, record.RequestHeaders);
        }

        if (request.Content != null)
        {
            var (body, size, truncated) = await ReadContentForDisplayAsync(
                request.Content, _options.MaxBodySizeBytes, cancellationToken);
            record.RequestBody = body;
            record.RequestSize = size;
            record.RequestBodyTruncated = truncated;
            // ReadAsByteArrayAsync buffers internally, so downstream handlers
            // can still read the original content.
        }
    }

    private async Task CaptureResponseAsync(
        HttpResponseMessage response,
        HttpRecord record,
        CancellationToken cancellationToken)
    {
        record.StatusCode = (int)response.StatusCode;
        record.ReasonPhrase = response.ReasonPhrase;

        // Capture headers
        if (_options.CaptureResponseHeaders)
        {
            record.ResponseHeaders.Clear();
            CaptureHeaders(response.Headers, record.ResponseHeaders);

            if (response.Content != null)
            {
                CaptureHeaders(response.Content.Headers, record.ResponseHeaders);
            }
        }

        // Capture body
        if (response.Content != null)
        {
            var (body, size, truncated) = await ReadContentForDisplayAsync(
                response.Content, _options.MaxBodySizeBytes, cancellationToken);
            record.ResponseBody = body;
            record.ResponseSize = size;
            record.ResponseBodyTruncated = truncated;
            // No replacement — the buffered content remains readable by callers.
        }
    }

    private void CaptureHeaders(HttpHeaders headers, Dictionary<string, string[]> target)
    {
        foreach (var header in headers)
        {
            var values = header.Value.ToArray();

            // Mask sensitive headers if enabled
            if (_options.MaskSensitiveHeaders && IsSensitiveHeader(header.Key))
            {
                values = values.Select(_ => "[MASKED]").ToArray();
            }

            target[header.Key] = values;
        }
    }

    private bool IsSensitiveHeader(string headerName)
    {
        return _options.SensitiveHeaderPatterns.Any(pattern =>
            headerName.Equals(pattern, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<(string? Body, long Size, bool Truncated)> ReadContentForDisplayAsync(
        HttpContent content,
        int maxSize,
        CancellationToken cancellationToken)
    {
        try
        {
            var declaredSize = content.Headers.ContentLength;
            var bufferLimit = Math.Max(maxSize, MinimumBufferLimitBytes);
            if (declaredSize is null || declaredSize > bufferLimit || maxSize <= 0)
                return (null, declaredSize ?? 0, true);

            // ReadAsStreamAsync can hand out a one-shot native response stream on iOS.
            // Even checking CanSeek before the caller reads it can leave the caller
            // with an empty body. The byte-array API buffers the body for replay.
            var bytes = await content.ReadAsByteArrayAsync(cancellationToken);
            var captureSize = Math.Min(bytes.Length, maxSize);
            var encoding = GetEncoding(content.Headers.ContentType);
            return (encoding.GetString(bytes, 0, captureSize), bytes.Length, bytes.Length > captureSize);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return (null, 0, false);
        }
    }

    private static Encoding GetEncoding(MediaTypeHeaderValue? contentType)
    {
        if (contentType?.CharSet != null)
        {
            try
            {
                return Encoding.GetEncoding(contentType.CharSet);
            }
            catch
            {
                // Fall through to default
            }
        }

        return Encoding.UTF8;
    }
}
