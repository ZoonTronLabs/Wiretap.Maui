using SQLite;
using Wiretap.Maui.Core;

namespace Wiretap.Maui.Database;

/// <summary>
/// SQLite database for persisting HTTP records.
/// Provides async operations and connection pooling for performance.
/// </summary>
public class WiretapDatabase : IAsyncDisposable
{
    private readonly SQLiteAsyncConnection _connection;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    /// <summary>
    /// Default database file name.
    /// </summary>
    public const string DatabaseFileName = "wiretap.db3";

    /// <summary>
    /// Creates a new WiretapDatabase with the default path in the app data directory.
    /// </summary>
    public WiretapDatabase() : this(GetDefaultDatabasePath())
    {
    }

    /// <summary>
    /// Creates a new WiretapDatabase with a custom database path.
    /// </summary>
    /// <param name="databasePath">Full path to the database file.</param>
    public WiretapDatabase(string databasePath)
    {
        DatabasePath = databasePath;

        // Configure connection with connection pooling and WAL mode for performance
        var flags = SQLiteOpenFlags.ReadWrite |
                    SQLiteOpenFlags.Create |
                    SQLiteOpenFlags.SharedCache;

        _connection = new SQLiteAsyncConnection(databasePath, flags);
    }

    /// <summary>
    /// Gets the path to the database file.
    /// </summary>
    public string DatabasePath { get; }

    /// <summary>
    /// Gets the default database path in the app data directory.
    /// </summary>
    public static string GetDefaultDatabasePath()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appDataPath, DatabaseFileName);
    }

    /// <summary>
    /// Ensures the database is initialized with the required tables and indexes.
    /// Thread-safe and idempotent.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_initialized)
            return;

        await _initLock.WaitAsync();
        try
        {
            if (_initialized)
                return;

            // The package supplies no native SQLite implementation. The host owns its provider.
            try
            {
                _ = SQLitePCL.raw.sqlite3_libversion();
            }
            catch (Exception ex) when (ex is InvalidOperationException or NullReferenceException)
            {
                throw new InvalidOperationException(
                    "Wiretap SQLite persistence requires a host SQLitePCLRaw provider. " +
                    "Install a SQLitePCLRaw bundle and call SQLitePCL.Batteries_V2.Init() " +
                    "before using Wiretap, or set EnablePersistence = false.", ex);
            }

            // Create table if it doesn't exist
            await _connection.CreateTableAsync<HttpRecordEntity>();

            // Enable WAL mode for better concurrent read/write performance
            // Note: We use EnableWriteAheadLoggingAsync which handles WAL mode properly
            await _connection.EnableWriteAheadLoggingAsync();

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// Inserts a new HTTP record into the database.
    /// </summary>
    /// <param name="record">The record to insert.</param>
    public async Task InsertAsync(HttpRecord record)
    {
        await EnsureInitializedAsync();
        var entity = HttpRecordEntity.FromRecord(record);
        await _connection.InsertAsync(entity);
    }

    /// <summary>
    /// Updates an existing HTTP record in the database.
    /// </summary>
    /// <param name="record">The record to update.</param>
    public async Task UpdateAsync(HttpRecord record)
    {
        await EnsureInitializedAsync();
        var entity = HttpRecordEntity.FromRecord(record);
        await _connection.UpdateAsync(entity);
    }

    /// <summary>
    /// Inserts or updates an HTTP record (upsert).
    /// </summary>
    /// <param name="record">The record to insert or update.</param>
    public async Task InsertOrReplaceAsync(HttpRecord record)
    {
        await EnsureInitializedAsync();
        var entity = HttpRecordEntity.FromRecord(record);
        await _connection.InsertOrReplaceAsync(entity);
    }

    /// <summary>
    /// Gets all records ordered by timestamp (newest first).
    /// </summary>
    /// <param name="limit">Maximum number of records to return. 0 for unlimited.</param>
    /// <returns>List of HTTP records.</returns>
    public async Task<List<HttpRecord>> GetRecordsAsync(int limit = 0)
    {
        await EnsureInitializedAsync();

        var query = _connection.Table<HttpRecordEntity>()
            .OrderByDescending(r => r.Timestamp);

        List<HttpRecordEntity> entities;
        if (limit > 0)
            entities = await query.Take(limit).ToListAsync();
        else
            entities = await query.ToListAsync();

        return entities.Select(e => e.ToRecord()).ToList();
    }

    /// <summary>
    /// Gets a specific record by ID.
    /// </summary>
    /// <param name="id">The record ID.</param>
    /// <returns>The record, or null if not found.</returns>
    public async Task<HttpRecord?> GetRecordAsync(Guid id)
    {
        await EnsureInitializedAsync();
        var entity = await _connection.FindAsync<HttpRecordEntity>(id.ToString());
        return entity?.ToRecord();
    }

    /// <summary>
    /// Deletes a specific record by ID.
    /// </summary>
    /// <param name="id">The record ID.</param>
    /// <returns>Number of records deleted (0 or 1).</returns>
    public async Task<int> DeleteAsync(Guid id)
    {
        await EnsureInitializedAsync();
        return await _connection.DeleteAsync<HttpRecordEntity>(id.ToString());
    }

    /// <summary>
    /// Deletes all records from the database.
    /// </summary>
    /// <returns>Number of records deleted.</returns>
    public async Task<int> DeleteAllAsync()
    {
        await EnsureInitializedAsync();
        return await _connection.DeleteAllAsync<HttpRecordEntity>();
    }

    /// <summary>
    /// Gets the total number of records in the database.
    /// </summary>
    public async Task<int> CountAsync()
    {
        await EnsureInitializedAsync();
        return await _connection.Table<HttpRecordEntity>().CountAsync();
    }

    /// <summary>
    /// Deletes records older than the specified date.
    /// </summary>
    /// <param name="olderThan">Delete records with timestamps before this date.</param>
    /// <returns>Number of records deleted.</returns>
    public async Task<int> DeleteOlderThanAsync(DateTime olderThan)
    {
        await EnsureInitializedAsync();
        return await _connection.ExecuteAsync(
            "DELETE FROM HttpRecords WHERE Timestamp < ?",
            olderThan);
    }

    /// <summary>
    /// Keeps only the most recent N records, deleting older ones.
    /// </summary>
    /// <param name="keepCount">Number of records to keep.</param>
    /// <returns>Number of records deleted.</returns>
    public async Task<int> TrimToCountAsync(int keepCount)
    {
        await EnsureInitializedAsync();

        // Get the IDs of records to keep (most recent N)
        var recordsToKeep = await _connection.Table<HttpRecordEntity>()
            .OrderByDescending(r => r.Timestamp)
            .Take(keepCount)
            .ToListAsync();

        if (recordsToKeep.Count == 0)
            return 0;

        var idsToKeep = recordsToKeep.Select(r => r.Id).ToList();

        // Count how many will be deleted
        var totalCount = await _connection.Table<HttpRecordEntity>().CountAsync();
        if (totalCount <= keepCount)
            return 0;

        // Delete all records not in the keep list using parameterized query
        var placeholders = string.Join(",", idsToKeep.Select((_, i) => $"?{i + 1}"));
        var deleteQuery = $"DELETE FROM HttpRecords WHERE Id NOT IN ({placeholders})";

        return await _connection.ExecuteAsync(deleteQuery, idsToKeep.Cast<object>().ToArray());
    }

    /// <summary>
    /// Searches records by URL containing the specified text.
    /// </summary>
    /// <param name="searchText">Text to search for in URLs.</param>
    /// <param name="limit">Maximum number of results.</param>
    /// <returns>Matching records.</returns>
    public async Task<List<HttpRecord>> SearchByUrlAsync(string searchText, int limit = 100)
    {
        await EnsureInitializedAsync();

        var entities = await _connection.QueryAsync<HttpRecordEntity>(
            "SELECT * FROM HttpRecords WHERE Url LIKE ? ORDER BY Timestamp DESC LIMIT ?",
            $"%{searchText}%",
            limit);

        return entities.Select(e => e.ToRecord()).ToList();
    }

    /// <summary>
    /// Gets records filtered by HTTP method.
    /// </summary>
    /// <param name="method">HTTP method to filter by (e.g., "GET", "POST").</param>
    /// <param name="limit">Maximum number of results.</param>
    /// <returns>Matching records.</returns>
    public async Task<List<HttpRecord>> GetByMethodAsync(string method, int limit = 100)
    {
        await EnsureInitializedAsync();

        var entities = await _connection.Table<HttpRecordEntity>()
            .Where(r => r.Method == method)
            .OrderByDescending(r => r.Timestamp)
            .Take(limit)
            .ToListAsync();

        return entities.Select(e => e.ToRecord()).ToList();
    }

    /// <summary>
    /// Gets records filtered by status code range.
    /// </summary>
    /// <param name="minStatus">Minimum status code (inclusive).</param>
    /// <param name="maxStatus">Maximum status code (exclusive).</param>
    /// <param name="limit">Maximum number of results.</param>
    /// <returns>Matching records.</returns>
    public async Task<List<HttpRecord>> GetByStatusRangeAsync(int minStatus, int maxStatus, int limit = 100)
    {
        await EnsureInitializedAsync();

        var entities = await _connection.Table<HttpRecordEntity>()
            .Where(r => r.StatusCode >= minStatus && r.StatusCode < maxStatus)
            .OrderByDescending(r => r.Timestamp)
            .Take(limit)
            .ToListAsync();

        return entities.Select(e => e.ToRecord()).ToList();
    }

    private async Task EnsureInitializedAsync()
    {
        if (!_initialized)
            await InitializeAsync();
    }

    /// <summary>
    /// Closes the database connection.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await _connection.CloseAsync();
        _initLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
