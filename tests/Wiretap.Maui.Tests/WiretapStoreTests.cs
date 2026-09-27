using Xunit;
using Wiretap.Maui.Core;

namespace Wiretap.Maui.Tests;

public class WiretapStoreTests
{
    [Fact]
    public void ZeroCapacity_IsRejectedBeforeAddingRecords()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateStore(0));
    }

    private static WiretapStore CreateStore(int maxRecords = 500)
    {
        var options = new WiretapOptions { MaxStoredRequests = maxRecords };
        return new WiretapStore(options);
    }

    private static HttpRecord CreateRecord(string method = "GET", string url = "https://example.com/api")
    {
        return new HttpRecord
        {
            Method = method,
            Url = url,
            StatusCode = 200,
            IsComplete = true
        };
    }

    [Fact]
    public void Add_SingleRecord_IncreasesCount()
    {
        // Arrange
        var store = CreateStore();
        var record = CreateRecord();

        // Act
        store.Add(record);

        // Assert
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void Add_MultipleRecords_IncreasesCount()
    {
        // Arrange
        var store = CreateStore();

        // Act
        for (int i = 0; i < 10; i++)
        {
            store.Add(CreateRecord());
        }

        // Assert
        Assert.Equal(10, store.Count);
    }

    [Fact]
    public void Add_ExceedsCapacity_RemovesOldestRecords()
    {
        // Arrange
        var store = CreateStore(maxRecords: 5);
        var records = new List<HttpRecord>();

        // Act - Add 7 records to a store with capacity 5
        for (int i = 0; i < 7; i++)
        {
            var record = CreateRecord(url: $"https://example.com/api/{i}");
            records.Add(record);
            store.Add(record);
        }

        // Assert - Should only have 5 records
        Assert.Equal(5, store.Count);

        // First two records should be removed (oldest)
        var storedRecords = store.GetRecords();
        Assert.DoesNotContain(storedRecords, r => r.Url.EndsWith("/0"));
        Assert.DoesNotContain(storedRecords, r => r.Url.EndsWith("/1"));

        // Last five records should be present
        Assert.Contains(storedRecords, r => r.Url.EndsWith("/2"));
        Assert.Contains(storedRecords, r => r.Url.EndsWith("/6"));
    }

    [Fact]
    public void GetRecords_ReturnsNewestFirst()
    {
        // Arrange
        var store = CreateStore();

        // Act - Add records with slight delay to ensure different timestamps
        var record1 = new HttpRecord
        {
            Method = "GET",
            Url = "https://example.com/first",
            Timestamp = DateTime.UtcNow.AddSeconds(-2)
        };
        var record2 = new HttpRecord
        {
            Method = "GET",
            Url = "https://example.com/second",
            Timestamp = DateTime.UtcNow.AddSeconds(-1)
        };
        var record3 = new HttpRecord
        {
            Method = "GET",
            Url = "https://example.com/third",
            Timestamp = DateTime.UtcNow
        };

        store.Add(record1);
        store.Add(record2);
        store.Add(record3);

        // Assert
        var records = store.GetRecords();
        Assert.Equal(3, records.Count);
        Assert.Equal("https://example.com/third", records[0].Url);
        Assert.Equal("https://example.com/second", records[1].Url);
        Assert.Equal("https://example.com/first", records[2].Url);
    }

    [Fact]
    public void GetRecord_ExistingId_ReturnsRecord()
    {
        // Arrange
        var store = CreateStore();
        var record = CreateRecord();
        store.Add(record);

        // Act
        var retrieved = store.GetRecord(record.Id);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(record.Id, retrieved.Id);
        Assert.Equal(record.Url, retrieved.Url);
    }

    [Fact]
    public void GetRecord_NonExistingId_ReturnsNull()
    {
        // Arrange
        var store = CreateStore();
        store.Add(CreateRecord());

        // Act
        var retrieved = store.GetRecord(Guid.NewGuid());

        // Assert
        Assert.Null(retrieved);
    }

    [Fact]
    public void Clear_RemovesAllRecords()
    {
        // Arrange
        var store = CreateStore();
        for (int i = 0; i < 10; i++)
        {
            store.Add(CreateRecord());
        }
        Assert.Equal(10, store.Count);

        // Act
        store.Clear();

        // Assert
        Assert.Equal(0, store.Count);
        Assert.Empty(store.GetRecords());
    }

    [Fact]
    public void OnRecordAdded_EventFired_WhenRecordAdded()
    {
        // Arrange
        var store = CreateStore();
        HttpRecord? addedRecord = null;
        store.OnRecordAdded += r => addedRecord = r;

        var record = CreateRecord();

        // Act
        store.Add(record);

        // Assert
        Assert.NotNull(addedRecord);
        Assert.Equal(record.Id, addedRecord.Id);
    }

    [Fact]
    public void OnRecordsCleared_EventFired_WhenCleared()
    {
        // Arrange
        var store = CreateStore();
        store.Add(CreateRecord());
        var eventFired = false;
        store.OnRecordsCleared += () => eventFired = true;

        // Act
        store.Clear();

        // Assert
        Assert.True(eventFired);
    }

    [Fact]
    public void ThreadSafety_ConcurrentAdds_NoDataCorruption()
    {
        // Arrange
        var store = CreateStore(maxRecords: 1000);
        var tasks = new List<Task>();
        var recordCount = 100;
        var threadCount = 10;

        // Act - Add records from multiple threads concurrently
        for (int t = 0; t < threadCount; t++)
        {
            var threadId = t;
            tasks.Add(Task.Run(() =>
            {
                for (int i = 0; i < recordCount; i++)
                {
                    store.Add(CreateRecord(url: $"https://example.com/thread{threadId}/record{i}"));
                }
            }));
        }

        Task.WaitAll(tasks.ToArray());

        // Assert - Should have all records (threadCount * recordCount = 1000)
        Assert.Equal(threadCount * recordCount, store.Count);
    }

    [Fact]
    public void ThreadSafety_ConcurrentReadsAndWrites_NoExceptions()
    {
        // Arrange
        var store = CreateStore(maxRecords: 100);
        var cts = new CancellationTokenSource();
        var exceptions = new List<Exception>();

        // Act - Concurrent reads and writes
        var writeTask = Task.Run(() =>
        {
            try
            {
                for (int i = 0; i < 500 && !cts.Token.IsCancellationRequested; i++)
                {
                    store.Add(CreateRecord());
                }
            }
            catch (Exception ex)
            {
                lock (exceptions) exceptions.Add(ex);
            }
        });

        var readTask = Task.Run(() =>
        {
            try
            {
                for (int i = 0; i < 500 && !cts.Token.IsCancellationRequested; i++)
                {
                    var records = store.GetRecords();
                    _ = store.Count;
                }
            }
            catch (Exception ex)
            {
                lock (exceptions) exceptions.Add(ex);
            }
        });

        var clearTask = Task.Run(() =>
        {
            try
            {
                for (int i = 0; i < 10 && !cts.Token.IsCancellationRequested; i++)
                {
                    Thread.Sleep(10);
                    store.Clear();
                }
            }
            catch (Exception ex)
            {
                lock (exceptions) exceptions.Add(ex);
            }
        });

        Task.WaitAll(writeTask, readTask, clearTask);
        cts.Cancel();

        // Assert - No exceptions should have occurred
        Assert.Empty(exceptions);
    }

    [Fact]
    public void RingBuffer_MaintainsMaxCapacity_UnderLoad()
    {
        // Arrange
        var maxRecords = 50;
        var store = CreateStore(maxRecords: maxRecords);

        // Act - Add many more records than capacity
        for (int i = 0; i < 200; i++)
        {
            store.Add(CreateRecord(url: $"https://example.com/api/{i}"));
        }

        // Assert - Should never exceed max capacity
        Assert.Equal(maxRecords, store.Count);
    }
}
