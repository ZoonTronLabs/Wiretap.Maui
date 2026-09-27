namespace Wiretap.Maui;

/// <summary>
/// Configuration options for Wiretap HTTP inspector.
/// </summary>
public class WiretapOptions
{
    /// <summary>
    /// Maximum number of HTTP records to store in memory.
    /// Older records are removed when this limit is exceeded.
    /// Default: 500
    /// </summary>
    public int MaxStoredRequests { get; set; } = 500;

    /// <summary>
    /// Whether to show the Wiretap entry point (notification on Android).
    /// Default: true
    /// </summary>
    public bool ShowFloatingButton { get; set; } = true;

    /// <summary>
    /// Whether to pretty-print JSON request/response bodies.
    /// Default: true
    /// </summary>
    public bool PrettyPrintJson { get; set; } = true;

    /// <summary>
    /// Whether to capture request headers.
    /// Default: true
    /// </summary>
    public bool CaptureRequestHeaders { get; set; } = true;

    /// <summary>
    /// Whether to capture response headers.
    /// Default: true
    /// </summary>
    public bool CaptureResponseHeaders { get; set; } = true;

    /// <summary>
    /// Whether to mask sensitive header values (e.g., Authorization, API keys).
    /// Default: true
    /// </summary>
    public bool MaskSensitiveHeaders { get; set; } = true;

    /// <summary>
    /// Header name patterns to mask. Values matching these patterns will show as [MASKED].
    /// Default: Authorization, X-Api-Key, Cookie, Set-Cookie
    /// </summary>
    public string[] SensitiveHeaderPatterns { get; set; } =
    [
        "Authorization",
        "X-Api-Key",
        "Api-Key",
        "X-Auth-Token",
        "Cookie",
        "Set-Cookie"
    ];

    /// <summary>
    /// Maximum body size to capture in bytes. Larger bodies will be truncated.
    /// Default: 1MB (1_048_576 bytes)
    /// </summary>
    public int MaxBodySizeBytes { get; set; } = 1_048_576;

    // ==================== Persistence Options ====================

    /// <summary>
    /// Whether to enable SQLite persistence for HTTP records.
    /// When enabled, records are written to SQLite in the background.
    /// Requires the host to initialize a SQLitePCLRaw provider before Wiretap starts.
    /// Default: true
    /// </summary>
    public bool EnablePersistence { get; set; } = true;

    /// <summary>
    /// Number of days to retain persisted records.
    /// Records older than this are automatically deleted.
    /// Default: 7 days
    /// </summary>
    public int RetentionDays { get; set; } = 7;

    /// <summary>
    /// Number of recent records to keep in memory cache for fast UI access.
    /// This is separate from MaxStoredRequests which limits the in-memory store.
    /// Default: 50
    /// </summary>
    public int MemoryCacheSize { get; set; } = 50;

    /// <summary>
    /// Maximum number of records to persist in the SQLite database.
    /// When exceeded, oldest records are automatically deleted.
    /// Default: 1000
    /// </summary>
    public int MaxPersistedRequests { get; set; } = 1000;

    /// <summary>
    /// Custom path for the SQLite database file.
    /// If null, uses the default path in the app's local data directory.
    /// </summary>
    public string? DatabasePath { get; set; }
}
