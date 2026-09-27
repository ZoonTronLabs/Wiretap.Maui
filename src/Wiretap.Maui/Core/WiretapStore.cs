namespace Wiretap.Maui.Core;

/// <summary>
/// Thread-safe in-memory store for HTTP records using a ring buffer.
/// When the maximum capacity is reached, oldest records are removed.
/// </summary>
public class WiretapStore : IWiretapStore
{
    private readonly object _lock = new();
    private readonly List<HttpRecord> _records;
    private readonly int _maxRecords;

    /// <inheritdoc />
    public event Action<HttpRecord>? OnRecordAdded;

    /// <inheritdoc />
    public event Action? OnRecordsCleared;

    /// <summary>
    /// Creates a new WiretapStore with the specified maximum capacity.
    /// </summary>
    /// <param name="options">Wiretap configuration options.</param>
    public WiretapStore(WiretapOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxStoredRequests);
        _maxRecords = options.MaxStoredRequests;
        _records = new List<HttpRecord>(_maxRecords);
    }

    /// <inheritdoc />
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _records.Count;
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<HttpRecord> GetRecords()
    {
        lock (_lock)
        {
            // Return newest first
            return _records.OrderByDescending(r => r.Timestamp).ToList();
        }
    }

    /// <inheritdoc />
    public HttpRecord? GetRecord(Guid id)
    {
        lock (_lock)
        {
            return _records.FirstOrDefault(r => r.Id == id);
        }
    }

    /// <inheritdoc />
    public void Add(HttpRecord record)
    {
        lock (_lock)
        {
            // Ring buffer: remove oldest if at capacity
            while (_records.Count >= _maxRecords)
            {
                _records.RemoveAt(0);
            }

            _records.Add(record);
        }

        // Raise event outside lock to prevent deadlocks
        OnRecordAdded?.Invoke(record);
    }

    /// <inheritdoc />
    public void Clear()
    {
        lock (_lock)
        {
            _records.Clear();
        }

        OnRecordsCleared?.Invoke();
    }
}
