namespace Wiretap.Maui.Core;

/// <summary>
/// Interface for storing and retrieving captured HTTP records.
/// </summary>
public interface IWiretapStore
{
    /// <summary>
    /// Searches records by URL. For persistent stores, this queries the database.
    /// </summary>
    /// <param name="searchText">Text to search for in URLs.</param>
    /// <param name="limit">Maximum number of results.</param>
    /// <returns>Matching records.</returns>
    Task<List<HttpRecord>> SearchByUrlAsync(string searchText, int limit = 100) =>
        Task.FromResult(GetRecords().Where(r => r.Url.Contains(searchText, StringComparison.OrdinalIgnoreCase)).Take(limit).ToList());

    /// <summary>
    /// Gets records filtered by HTTP method. For persistent stores, this queries the database.
    /// </summary>
    /// <param name="method">HTTP method to filter by.</param>
    /// <param name="limit">Maximum number of results.</param>
    /// <returns>Matching records.</returns>
    Task<List<HttpRecord>> GetByMethodAsync(string method, int limit = 100) =>
        Task.FromResult(GetRecords().Where(r => r.Method == method).Take(limit).ToList());

    /// <summary>
    /// Gets records filtered by status code range. For persistent stores, this queries the database.
    /// </summary>
    /// <param name="minStatus">Minimum status code (inclusive).</param>
    /// <param name="maxStatus">Maximum status code (exclusive).</param>
    /// <param name="limit">Maximum number of results.</param>
    /// <returns>Matching records.</returns>
    Task<List<HttpRecord>> GetByStatusRangeAsync(int minStatus, int maxStatus, int limit = 100) =>
        Task.FromResult(GetRecords().Where(r => r.StatusCode >= minStatus && r.StatusCode < maxStatus).Take(limit).ToList());


    /// <summary>
    /// Gets all stored HTTP records, ordered by timestamp (newest first).
    /// </summary>
    /// <returns>Read-only list of HTTP records.</returns>
    IReadOnlyList<HttpRecord> GetRecords();

    /// <summary>
    /// Gets a specific HTTP record by its ID.
    /// </summary>
    /// <param name="id">The record ID.</param>
    /// <returns>The HTTP record, or null if not found.</returns>
    HttpRecord? GetRecord(Guid id);

    /// <summary>
    /// Gets a record without blocking the caller while a persistent store reads it.
    /// </summary>
    Task<HttpRecord?> GetRecordAsync(Guid id) => Task.FromResult(GetRecord(id));

    /// <summary>
    /// Adds a new HTTP record to the store.
    /// </summary>
    /// <param name="record">The record to add.</param>
    void Add(HttpRecord record);

    /// <summary>
    /// Removes all stored HTTP records.
    /// </summary>
    void Clear();

    /// <summary>
    /// Gets the current number of stored records.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Event raised when a new record is added.
    /// </summary>
    event Action<HttpRecord>? OnRecordAdded;

    /// <summary>
    /// Event raised when records are cleared.
    /// </summary>
    event Action? OnRecordsCleared;
}
