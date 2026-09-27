using System.Net;
using System.Text;
using Xunit;
using Wiretap.Maui.Core;
using Wiretap.Maui.Handler;

namespace Wiretap.Maui.Tests;

public class WiretapHandlerTests
{
    private static WiretapOptions CreateOptions(
        bool maskSensitiveHeaders = true,
        int maxBodySize = 1_048_576)
    {
        return new WiretapOptions
        {
            MaskSensitiveHeaders = maskSensitiveHeaders,
            MaxBodySizeBytes = maxBodySize,
            CaptureRequestHeaders = true,
            CaptureResponseHeaders = true
        };
    }

    private static WiretapStore CreateStore(int maxRecords = 500)
    {
        return new WiretapStore(new WiretapOptions { MaxStoredRequests = maxRecords });
    }

    /// <summary>
    /// Test handler that returns a configurable response.
    /// </summary>
    private class MockInnerHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

        public MockInnerHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        public MockInnerHandler(HttpResponseMessage response)
            : this(_ => Task.FromResult(response))
        {
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return _handler(request);
        }
    }

    [Fact]
    public async Task Handler_CapturesBasicRequestInfo()
    {
        // Arrange
        var store = CreateStore();
        var options = CreateOptions();
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"status\":\"ok\"}", Encoding.UTF8, "application/json")
        };

        var handler = new WiretapHandler(store, options)
        {
            InnerHandler = new MockInnerHandler(response)
        };

        var client = new HttpClient(handler);

        // Act
        await client.GetAsync("https://api.example.com/test");

        // Assert
        Assert.Equal(1, store.Count);
        var record = store.GetRecords()[0];
        Assert.Equal("GET", record.Method);
        Assert.Equal("https://api.example.com/test", record.Url);
        Assert.Equal(200, record.StatusCode);
        Assert.True(record.IsComplete);
    }

    [Fact]
    public async Task Handler_CapturesRequestBody()
    {
        // Arrange
        var store = CreateStore();
        var options = CreateOptions();
        var response = new HttpResponseMessage(HttpStatusCode.OK);

        var handler = new WiretapHandler(store, options)
        {
            InnerHandler = new MockInnerHandler(response)
        };

        var client = new HttpClient(handler);
        var requestBody = "{\"name\":\"test\",\"value\":123}";

        // Act
        await client.PostAsync(
            "https://api.example.com/create",
            new StringContent(requestBody, Encoding.UTF8, "application/json"));

        // Assert
        var record = store.GetRecords()[0];
        Assert.Equal("POST", record.Method);
        Assert.Equal(requestBody, record.RequestBody);
        Assert.Equal(requestBody.Length, record.RequestSize);
    }

    [Fact]
    public async Task Handler_CapturesResponseBody()
    {
        // Arrange
        var store = CreateStore();
        var options = CreateOptions();
        var responseBody = "{\"id\":1,\"name\":\"Test Item\"}";
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
        };

        var handler = new WiretapHandler(store, options)
        {
            InnerHandler = new MockInnerHandler(response)
        };

        var client = new HttpClient(handler);

        // Act
        var result = await client.GetAsync("https://api.example.com/item/1");
        var actualBody = await result.Content.ReadAsStringAsync();

        // Assert
        var record = store.GetRecords()[0];
        Assert.Equal(responseBody, record.ResponseBody);
        Assert.Equal(responseBody, actualBody); // Body should still be readable
    }

    [Fact]
    public async Task Handler_PreservesRequestBodyForDownstreamHandlers()
    {
        // Arrange
        var store = CreateStore();
        var options = CreateOptions();
        var requestBody = "{\"important\":\"data\"}";
        string? capturedBody = null;

        var innerHandler = new MockInnerHandler(async request =>
        {
            // Downstream handler reads the body
            if (request.Content != null)
            {
                capturedBody = await request.Content.ReadAsStringAsync();
            }
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var handler = new WiretapHandler(store, options)
        {
            InnerHandler = innerHandler
        };

        var client = new HttpClient(handler);

        // Act
        await client.PostAsync(
            "https://api.example.com/test",
            new StringContent(requestBody, Encoding.UTF8, "application/json"));

        // Assert
        Assert.Equal(requestBody, capturedBody); // Body preserved for downstream
    }

    [Fact]
    public async Task Handler_MasksSensitiveHeaders_WhenEnabled()
    {
        // Arrange
        var store = CreateStore();
        var options = CreateOptions(maskSensitiveHeaders: true);
        var response = new HttpResponseMessage(HttpStatusCode.OK);

        var handler = new WiretapHandler(store, options)
        {
            InnerHandler = new MockInnerHandler(response)
        };

        var client = new HttpClient(handler);
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "secret-token-12345");
        client.DefaultRequestHeaders.Add("X-Api-Key", "my-secret-api-key");

        // Act
        await client.GetAsync("https://api.example.com/secure");

        // Assert
        var record = store.GetRecords()[0];
        Assert.True(record.RequestHeaders.ContainsKey("Authorization"));
        Assert.Equal("[MASKED]", record.RequestHeaders["Authorization"][0]);
        Assert.True(record.RequestHeaders.ContainsKey("X-Api-Key"));
        Assert.Equal("[MASKED]", record.RequestHeaders["X-Api-Key"][0]);
    }

    [Fact]
    public async Task Handler_DoesNotMaskHeaders_WhenDisabled()
    {
        // Arrange
        var store = CreateStore();
        var options = CreateOptions(maskSensitiveHeaders: false);
        var response = new HttpResponseMessage(HttpStatusCode.OK);

        var handler = new WiretapHandler(store, options)
        {
            InnerHandler = new MockInnerHandler(response)
        };

        var client = new HttpClient(handler);
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "visible-token");

        // Act
        await client.GetAsync("https://api.example.com/test");

        // Assert
        var record = store.GetRecords()[0];
        Assert.Contains("Bearer visible-token", record.RequestHeaders["Authorization"][0]);
    }

    [Fact]
    public async Task Handler_CapturesDuration()
    {
        // Arrange
        var store = CreateStore();
        var options = CreateOptions();

        var innerHandler = new MockInnerHandler(async _ =>
        {
            await Task.Delay(50); // Simulate network latency
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var handler = new WiretapHandler(store, options)
        {
            InnerHandler = innerHandler
        };

        var client = new HttpClient(handler);

        // Act
        await client.GetAsync("https://api.example.com/slow");

        // Assert
        var record = store.GetRecords()[0];
        Assert.True(record.Duration.TotalMilliseconds >= 50);
    }

    [Fact]
    public async Task Handler_CapturesFailedRequests()
    {
        // Arrange
        var store = CreateStore();
        var options = CreateOptions();

        var innerHandler = new MockInnerHandler(_ =>
            throw new HttpRequestException("Connection refused"));

        var handler = new WiretapHandler(store, options)
        {
            InnerHandler = innerHandler
        };

        var client = new HttpClient(handler);

        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetAsync("https://api.example.com/fail"));

        // Verify the failed request was captured
        Assert.Equal(1, store.Count);
        var record = store.GetRecords()[0];
        Assert.False(record.IsComplete);
        Assert.Equal("Connection refused", record.ErrorMessage);
        Assert.True(record.Duration.TotalMilliseconds >= 0);
    }

    [Fact]
    public async Task Handler_TruncatesLargeBodies()
    {
        // Arrange
        var store = CreateStore();
        var options = CreateOptions(maxBodySize: 100);
        var largeBody = new string('X', 500);
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(largeBody, Encoding.UTF8, "text/plain")
        };

        var handler = new WiretapHandler(store, options)
        {
            InnerHandler = new MockInnerHandler(response)
        };

        var client = new HttpClient(handler);

        // Act
        await client.GetAsync("https://api.example.com/large");

        // Assert
        var record = store.GetRecords()[0];
        Assert.Equal(100, record.ResponseBody?.Length);
        Assert.Equal(500, record.ResponseSize); // Original size preserved
        Assert.True(record.ResponseBodyTruncated);
    }

    [Fact]
    public async Task Handler_DoesNotConsumeNonSeekableRequestBody()
    {
        var store = CreateStore();
        var payload = new string('X', 500);
        string? downstreamBody = null;
        var handler = new WiretapHandler(store, CreateOptions(maxBodySize: 100))
        {
            InnerHandler = new MockInnerHandler(async request =>
            {
                downstreamBody = await request.Content!.ReadAsStringAsync();
                return new HttpResponseMessage(HttpStatusCode.OK);
            })
        };
        using var client = new HttpClient(handler);
        using var content = new StreamContent(new NonSeekableReadStream(Encoding.UTF8.GetBytes(payload)));
        content.Headers.ContentLength = payload.Length;

        await client.PostAsync("https://api.example.com/upload", content);

        Assert.Equal(payload, downstreamBody);
        var record = Assert.Single(store.GetRecords());
        Assert.Null(record.RequestBody);
        Assert.Equal(payload.Length, record.RequestSize);
        Assert.True(record.RequestBodyTruncated);
    }

    [Fact]
    public async Task Handler_CapturesResponseHeaders()
    {
        // Arrange
        var store = CreateStore();
        var options = CreateOptions();
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("OK", Encoding.UTF8, "text/plain")
        };
        response.Headers.Add("X-Request-Id", "abc-123");

        var handler = new WiretapHandler(store, options)
        {
            InnerHandler = new MockInnerHandler(response)
        };

        var client = new HttpClient(handler);

        // Act
        await client.GetAsync("https://api.example.com/test");

        // Assert
        var record = store.GetRecords()[0];
        // Response should have captured headers (either from response.Headers or response.Content.Headers)
        Assert.True(record.ResponseHeaders.Count > 0, "Should have captured at least some response headers");
        // Content-Type should be captured from content headers
        Assert.True(record.ResponseHeaders.ContainsKey("Content-Type"));
    }

    [Fact]
    public async Task Handler_CapturesMultipleRequests()
    {
        // Arrange
        var store = CreateStore();
        var options = CreateOptions();
        var response = new HttpResponseMessage(HttpStatusCode.OK);

        var handler = new WiretapHandler(store, options)
        {
            InnerHandler = new MockInnerHandler(response)
        };

        var client = new HttpClient(handler);

        // Act
        await client.GetAsync("https://api.example.com/first");
        await client.GetAsync("https://api.example.com/second");
        await client.PostAsync("https://api.example.com/third",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        // Assert
        Assert.Equal(3, store.Count);
        var records = store.GetRecords();
        Assert.Contains(records, r => r.Url.EndsWith("/first"));
        Assert.Contains(records, r => r.Url.EndsWith("/second"));
        Assert.Contains(records, r => r.Url.EndsWith("/third") && r.Method == "POST");
    }

    [Fact]
    public async Task Handler_PreservesMultipartContentWithBoundary()
    {
        // Arrange
        var store = CreateStore();
        var options = CreateOptions();
        string? downstreamContentType = null;
        string? downstreamBody = null;

        var innerHandler = new MockInnerHandler(async request =>
        {
            downstreamContentType = request.Content?.Headers.ContentType?.ToString();
            if (request.Content != null)
                downstreamBody = await request.Content.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var handler = new WiretapHandler(store, options)
        {
            InnerHandler = innerHandler
        };

        var client = new HttpClient(handler);

        var multipart = new MultipartFormDataContent();
        var fileBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG magic bytes
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        multipart.Add(fileContent, "photo", "test.png");
        multipart.Add(new StringContent("caption text"), "caption");

        // Act
        await client.PostAsync("https://api.example.com/upload", multipart);

        // Assert — downstream handler received correct Content-Type with boundary
        Assert.NotNull(downstreamContentType);
        Assert.Contains("multipart/form-data", downstreamContentType);
        Assert.Contains("boundary=", downstreamContentType);

        // Assert — downstream handler could read the body with file content intact
        Assert.NotNull(downstreamBody);
        Assert.Contains("test.png", downstreamBody);
        Assert.Contains("caption text", downstreamBody);

        // Assert — Wiretap captured something for display
        var record = store.GetRecords()[0];
        Assert.Equal("POST", record.Method);
        Assert.True(record.RequestSize > 0);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Created)]
    [InlineData(HttpStatusCode.NoContent)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Handler_CapturesVariousStatusCodes(HttpStatusCode statusCode)
    {
        // Arrange
        var store = CreateStore();
        var options = CreateOptions();
        var response = new HttpResponseMessage(statusCode);

        var handler = new WiretapHandler(store, options)
        {
            InnerHandler = new MockInnerHandler(response)
        };

        var client = new HttpClient(handler);

        // Act
        await client.GetAsync("https://api.example.com/status");

        // Assert
        var record = store.GetRecords()[0];
        Assert.Equal((int)statusCode, record.StatusCode);
        Assert.True(record.IsComplete);
    }

    private sealed class NonSeekableReadStream(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(buffer, cancellationToken);
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
