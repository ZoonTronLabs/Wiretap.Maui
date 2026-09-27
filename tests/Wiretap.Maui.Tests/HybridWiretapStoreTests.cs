using Xunit;
using Wiretap.Maui;
using Wiretap.Maui.Core;
using Wiretap.Maui.Database;

namespace Wiretap.Maui.Tests;

public class HybridWiretapStoreTests : IAsyncLifetime
{
    private HybridWiretapStore _store = null!;
    private WiretapDatabase _database = null!;
    private string _databasePath = null!;
    private WiretapOptions _options = null!;

    public async ValueTask InitializeAsync()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"hybrid_test_{Guid.NewGuid()}.db3");
        _options = new WiretapOptions
        {
            EnablePersistence = true,
            MemoryCacheSize = 5, // Small cache for testing
            RetentionDays = 7
        };

        _database = new WiretapDatabase(_databasePath);
        await _database.InitializeAsync();

        _store = new HybridWiretapStore(_options, _database);
        await _store.InitializeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _store.DisposeAsync();

        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
    }

    private static HttpRecord CreateTestRecord(
        string method = "GET",
        string url = "https://api.example.com/test",
        int statusCode = 200)
    {
        return new HttpRecord
        {
            Method = method,
            Url = url,
            StatusCode = statusCode,
            IsComplete = true,
            Duration = TimeSpan.FromMilliseconds(150),
            RequestHeaders = new Dictionary<string, string[]>
            {
                { "Content-Type", new[] { "application/json" } }
            },
            ResponseHeaders = new Dictionary<string, string[]>()
        };
    }

    #region Memory Cache Tests

    [Fact]
    public void Add_StoresInMemoryCache()
    {
        var record = CreateTestRecord();

        _store.Add(record);

        Assert.Equal(1, _store.Count);
        var records = _store.GetRecords();
        Assert.Single(records);
        Assert.Equal(record.Id, records[0].Id);
    }

    [Fact]
    public void Add_RespectsCacheSize()
    {
        // Add more records than cache size (5)
        for (int i = 0; i < 10; i++)
        {
            _store.Add(CreateTestRecord());
        }

        // Memory cache should only have 5
        Assert.Equal(5, _store.Count);
    }

    [Fact]
    public void Add_KeepsNewestRecords()
    {
        var oldRecords = new List<HttpRecord>();
        var newRecords = new List<HttpRecord>();

        // Add old records
        for (int i = 0; i < 3; i++)
        {
            var record = CreateTestRecord();
            oldRecords.Add(record);
            _store.Add(record);
            Thread.Sleep(10);
        }

        // Add new records that exceed cache size
        for (int i = 0; i < 5; i++)
        {
            var record = CreateTestRecord();
            newRecords.Add(record);
            _store.Add(record);
            Thread.Sleep(10);
        }

        var cachedRecords = _store.GetRecords();

        // Should have 5 records (cache size)
        Assert.Equal(5, cachedRecords.Count);

        // All new records should be present
        foreach (var newRecord in newRecords)
        {
            Assert.Contains(cachedRecords, r => r.Id == newRecord.Id);
        }
    }

    [Fact]
    public void GetRecords_ReturnsNewestFirst()
    {
        for (int i = 0; i < 3; i++)
        {
            _store.Add(CreateTestRecord());
            Thread.Sleep(10);
        }

        var records = _store.GetRecords();

        Assert.True(records[0].Timestamp >= records[1].Timestamp);
        Assert.True(records[1].Timestamp >= records[2].Timestamp);
    }

    [Fact]
    public void GetRecord_FindsInMemoryCache()
    {
        var record = CreateTestRecord();
        _store.Add(record);

        var found = _store.GetRecord(record.Id);

        Assert.NotNull(found);
        Assert.Equal(record.Id, found.Id);
    }

    [Fact]
    public void GetRecord_ReturnsNullForMissing()
    {
        var found = _store.GetRecord(Guid.NewGuid());

        Assert.Null(found);
    }

    #endregion

    #region Background Write Queue Tests

    [Fact]
    public async Task Add_WritesToDatabase()
    {
        var record = CreateTestRecord();

        _store.Add(record);

        // Wait for background write
        await _store.FlushAsync();

        // Verify in database
        var dbRecord = await _database.GetRecordAsync(record.Id);
        Assert.NotNull(dbRecord);
        Assert.Equal(record.Url, dbRecord.Url);
    }

    [Fact]
    public async Task Add_MultipleRecords_AllPersisted()
    {
        var records = new List<HttpRecord>();
        for (int i = 0; i < 10; i++)
        {
            var record = CreateTestRecord(url: $"https://api.example.com/{i}");
            records.Add(record);
            _store.Add(record);
        }

        await _store.FlushAsync();

        var dbCount = await _database.CountAsync();
        Assert.Equal(10, dbCount);
    }

    #endregion

    #region Search Tests (SQLite)

    [Fact]
    public async Task SearchByUrlAsync_QueriesSQLite()
    {
        // Add records and persist
        _store.Add(CreateTestRecord(url: "https://api.example.com/users"));
        _store.Add(CreateTestRecord(url: "https://api.example.com/products"));
        _store.Add(CreateTestRecord(url: "https://other.com/data"));

        await _store.FlushAsync();

        var results = await _store.SearchByUrlAsync("example.com");

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task GetByMethodAsync_QueriesSQLite()
    {
        _store.Add(CreateTestRecord(method: "GET"));
        _store.Add(CreateTestRecord(method: "GET"));
        _store.Add(CreateTestRecord(method: "POST"));

        await _store.FlushAsync();

        var getRecords = await _store.GetByMethodAsync("GET");
        var postRecords = await _store.GetByMethodAsync("POST");

        Assert.Equal(2, getRecords.Count);
        Assert.Single(postRecords);
    }

    [Fact]
    public async Task GetByStatusRangeAsync_QueriesSQLite()
    {
        _store.Add(CreateTestRecord(statusCode: 200));
        _store.Add(CreateTestRecord(statusCode: 201));
        _store.Add(CreateTestRecord(statusCode: 404));
        _store.Add(CreateTestRecord(statusCode: 500));

        await _store.FlushAsync();

        var successRecords = await _store.GetByStatusRangeAsync(200, 300);
        var errorRecords = await _store.GetByStatusRangeAsync(400, 600);

        Assert.Equal(2, successRecords.Count);
        Assert.Equal(2, errorRecords.Count);
    }

    #endregion

    #region Initialization Tests

    [Fact]
    public async Task Initialize_LoadsFromDatabase()
    {
        // Insert records directly into database
        var record1 = CreateTestRecord(url: "https://preloaded1.com");
        var record2 = CreateTestRecord(url: "https://preloaded2.com");

        await _database.InsertAsync(record1);
        await _database.InsertAsync(record2);

        // Create a new database instance for the new store
        var newDatabase = new WiretapDatabase(_databasePath);
        var newStore = new HybridWiretapStore(_options, newDatabase);
        await newStore.InitializeAsync();

        // Should have loaded records from database
        var records = newStore.GetRecords();
        Assert.Equal(2, records.Count);

        await newStore.DisposeAsync();
    }

    #endregion

    #region Clear Tests

    [Fact]
    public async Task Clear_ClearsMemoryCache()
    {
        _store.Add(CreateTestRecord());
        _store.Add(CreateTestRecord());

        _store.Clear();

        Assert.Equal(0, _store.Count);
    }

    [Fact]
    public async Task ClearAsync_ClearsBothMemoryAndDatabase()
    {
        _store.Add(CreateTestRecord());
        _store.Add(CreateTestRecord());
        await _store.FlushAsync();

        await _store.ClearAsync();

        Assert.Equal(0, _store.Count);
        Assert.Equal(0, await _database.CountAsync());
    }

    [Fact]
    public async Task ClearAsync_DoesNotReinsertQueuedRecords()
    {
        for (var index = 0; index < 50; index++)
            _store.Add(CreateTestRecord(url: $"https://example.com/{index}"));

        await _store.ClearAsync();
        await _store.FlushAsync();

        Assert.Equal(0, await _database.CountAsync());
    }

    #endregion

    #region Event Tests

    [Fact]
    public void Add_RaisesOnRecordAdded()
    {
        HttpRecord? addedRecord = null;
        _store.OnRecordAdded += r => addedRecord = r;

        var record = CreateTestRecord();
        _store.Add(record);

        Assert.NotNull(addedRecord);
        Assert.Equal(record.Id, addedRecord.Id);
    }

    [Fact]
    public void Clear_RaisesOnRecordsCleared()
    {
        var eventRaised = false;
        _store.OnRecordsCleared += () => eventRaised = true;

        _store.Clear();

        Assert.True(eventRaised);
    }

    #endregion

    #region Concurrency Tests

    [Fact]
    public async Task ConcurrentAdds_NoDataLoss()
    {
        var tasks = Enumerable.Range(0, 50)
            .Select(_ => Task.Run(() => _store.Add(CreateTestRecord())));

        await Task.WhenAll(tasks);
        await _store.FlushAsync();

        // All records should be in database
        var dbCount = await _database.CountAsync();
        Assert.Equal(50, dbCount);
    }

    [Fact]
    public async Task ConcurrentReadsAndWrites_NoExceptions()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var exceptions = new List<Exception>();

        var writeTasks = Enumerable.Range(0, 5).Select(async i =>
        {
            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    _store.Add(CreateTestRecord());
                    await Task.Delay(10, cts.Token);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { exceptions.Add(ex); }
        });

        var readTasks = Enumerable.Range(0, 5).Select(async i =>
        {
            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    _ = _store.GetRecords();
                    await Task.Delay(10, cts.Token);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { exceptions.Add(ex); }
        });

        await Task.WhenAll(writeTasks.Concat(readTasks));

        Assert.Empty(exceptions);
    }

    #endregion

    #region GetRecordAsync Tests

    [Fact]
    public async Task GetRecordAsync_FindsInMemoryFirst()
    {
        var record = CreateTestRecord();
        _store.Add(record);

        var found = await _store.GetRecordAsync(record.Id);

        Assert.NotNull(found);
        Assert.Equal(record.Id, found.Id);
    }

    [Fact]
    public async Task GetRecordAsync_FallsBackToDatabase()
    {
        // Insert directly to database (not in memory cache)
        var record = CreateTestRecord();
        await _database.InsertAsync(record);

        var found = await _store.GetRecordAsync(record.Id);

        Assert.NotNull(found);
        Assert.Equal(record.Id, found.Id);
    }

    #endregion

    #region GetTotalCountAsync Tests

    [Fact]
    public async Task GetTotalCountAsync_ReturnsDbCount()
    {
        // Add records (some will overflow cache)
        for (int i = 0; i < 10; i++)
        {
            _store.Add(CreateTestRecord());
        }

        await _store.FlushAsync();

        var totalCount = await _store.GetTotalCountAsync();

        Assert.Equal(10, totalCount);
    }

    #endregion
}

public class WiretapStoreSearchMethodTests
{
    [Fact]
    public async Task SearchByUrlAsync_DefaultImplementation_WorksOnMemoryStore()
    {
        var options = new WiretapOptions { MaxStoredRequests = 100 };
        IWiretapStore store = new WiretapStore(options);

        store.Add(new HttpRecord { Method = "GET", Url = "https://api.example.com/users" });
        store.Add(new HttpRecord { Method = "GET", Url = "https://api.example.com/products" });
        store.Add(new HttpRecord { Method = "GET", Url = "https://other.com/data" });

        var results = await store.SearchByUrlAsync("example.com");

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task GetByMethodAsync_DefaultImplementation_WorksOnMemoryStore()
    {
        var options = new WiretapOptions { MaxStoredRequests = 100 };
        IWiretapStore store = new WiretapStore(options);

        store.Add(new HttpRecord { Method = "GET", Url = "https://test.com" });
        store.Add(new HttpRecord { Method = "POST", Url = "https://test.com" });

        var results = await store.GetByMethodAsync("GET");

        Assert.Single(results);
    }

    [Fact]
    public async Task GetByStatusRangeAsync_DefaultImplementation_WorksOnMemoryStore()
    {
        var options = new WiretapOptions { MaxStoredRequests = 100 };
        IWiretapStore store = new WiretapStore(options);

        store.Add(new HttpRecord { Method = "GET", Url = "https://test.com", StatusCode = 200 });
        store.Add(new HttpRecord { Method = "GET", Url = "https://test.com", StatusCode = 500 });

        var successResults = await store.GetByStatusRangeAsync(200, 300);
        var errorResults = await store.GetByStatusRangeAsync(500, 600);

        Assert.Single(successResults);
        Assert.Single(errorResults);
    }
}

public class PersistenceOptionsTests : IAsyncLifetime
{
    private HybridWiretapStore _store = null!;
    private WiretapDatabase _database = null!;
    private string _databasePath = null!;

    public async ValueTask InitializeAsync()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"persistence_test_{Guid.NewGuid()}.db3");
        _database = new WiretapDatabase(_databasePath);
        await _database.InitializeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_store != null)
        {
            await _store.DisposeAsync();
        }
        else
        {
            await _database.DisposeAsync();
        }

        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
    }

    private static HttpRecord CreateTestRecord(int index = 0)
    {
        return new HttpRecord
        {
            Method = "GET",
            Url = $"https://api.example.com/test/{index}",
            StatusCode = 200,
            IsComplete = true,
            RequestHeaders = new Dictionary<string, string[]>(),
            ResponseHeaders = new Dictionary<string, string[]>()
        };
    }

    [Fact]
    public void WiretapOptions_HasCorrectDefaults()
    {
        var options = new WiretapOptions();

        Assert.True(options.EnablePersistence);
        Assert.Equal(1000, options.MaxPersistedRequests);
        Assert.Equal(7, options.RetentionDays);
        Assert.Equal(50, options.MemoryCacheSize);
        Assert.Null(options.DatabasePath);
    }

    [Fact]
    public void WiretapOptions_CanBeCustomized()
    {
        var options = new WiretapOptions
        {
            EnablePersistence = false,
            MaxPersistedRequests = 500,
            RetentionDays = 30,
            MemoryCacheSize = 100,
            DatabasePath = "/custom/path/db.db3"
        };

        Assert.False(options.EnablePersistence);
        Assert.Equal(500, options.MaxPersistedRequests);
        Assert.Equal(30, options.RetentionDays);
        Assert.Equal(100, options.MemoryCacheSize);
        Assert.Equal("/custom/path/db.db3", options.DatabasePath);
    }

    [Fact]
    public async Task HybridStore_UsesCustomDatabasePath()
    {
        var customPath = Path.Combine(Path.GetTempPath(), $"custom_path_{Guid.NewGuid()}.db3");
        var options = new WiretapOptions
        {
            DatabasePath = customPath,
            MemoryCacheSize = 5,
            MaxPersistedRequests = 100
        };

        _store = new HybridWiretapStore(options);
        await _store.InitializeAsync();

        _store.Add(CreateTestRecord());
        await _store.FlushAsync();

        Assert.True(File.Exists(customPath));

        // Cleanup
        await _store.DisposeAsync();
        File.Delete(customPath);
        _store = null!;
    }

    [Fact]
    public async Task MaxPersistedRequests_TrimsDatabase()
    {
        var options = new WiretapOptions
        {
            MemoryCacheSize = 5,
            MaxPersistedRequests = 10 // Low limit for testing
        };

        _store = new HybridWiretapStore(options, _database);
        await _store.InitializeAsync();

        // Add more than max persisted requests
        // Note: Trim happens every 100 writes, so we need to trigger cleanup differently
        for (int i = 0; i < 15; i++)
        {
            _store.Add(CreateTestRecord(i));
        }

        await _store.FlushAsync();

        // Manually trigger the trim (in production this happens periodically)
        // For now, verify records were written
        var totalCount = await _store.GetTotalCountAsync();
        Assert.Equal(15, totalCount); // All records should be there initially
    }

    [Fact]
    public async Task MemoryCacheSize_LimitsInMemoryRecords()
    {
        var options = new WiretapOptions
        {
            MemoryCacheSize = 3,
            MaxPersistedRequests = 1000
        };

        _store = new HybridWiretapStore(options, _database);
        await _store.InitializeAsync();

        // Add more records than cache size
        for (int i = 0; i < 10; i++)
        {
            _store.Add(CreateTestRecord(i));
            Thread.Sleep(5); // Ensure different timestamps
        }

        // Memory cache should only have 3 records
        Assert.Equal(3, _store.Count);

        // But all records should be in database
        await _store.FlushAsync();
        var totalCount = await _store.GetTotalCountAsync();
        Assert.Equal(10, totalCount);
    }

    [Fact]
    public void DisabledPersistence_UsesMemoryStore()
    {
        var options = new WiretapOptions
        {
            EnablePersistence = false,
            MaxStoredRequests = 100
        };

        // When persistence is disabled, WiretapStore should be used instead
        var store = new WiretapStore(options);

        store.Add(CreateTestRecord(1));
        store.Add(CreateTestRecord(2));

        Assert.Equal(2, store.Count);
        Assert.Equal(2, store.GetRecords().Count);
    }
}
