using System.Net;
using System.Text;

namespace Wiretap.Maui.Handler;

/// <summary>Observes bytes only as the normal HTTP consumer transfers them.</summary>
internal sealed class BodyCaptureContent : HttpContent
{
    private readonly HttpContent _inner;
    private readonly BodyCapture _capture;

    internal BodyCaptureContent(HttpContent inner, int limit, Action<BodyCaptureResult> completed)
    {
        _inner = inner;
        _capture = new BodyCapture(limit, ContentEncoding(inner), completed);
        foreach (var header in inner.Headers)
            Headers.TryAddWithoutValidation(header.Key, header.Value);
    }

    protected override bool TryComputeLength(out long length)
    {
        var declared = _inner.Headers.ContentLength;
        length = declared.GetValueOrDefault();
        return declared.HasValue;
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        SerializeToStreamAsync(stream, context, CancellationToken.None);

    protected override void SerializeToStream(Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        using var observed = new CaptureWriteStream(stream, _capture);
        try
        {
            _inner.CopyTo(observed, context, cancellationToken);
            _capture.Finish(BodyTransfer.Completed);
        }
        catch (Exception ex)
        {
            _capture.Finish(new BodyTransfer.Failed(ex));
            throw;
        }
    }

    protected override async Task SerializeToStreamAsync(
        Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        using var observed = new CaptureWriteStream(stream, _capture);
        try
        {
            await _inner.CopyToAsync(observed, context, cancellationToken).ConfigureAwait(false);
            _capture.Finish(BodyTransfer.Completed);
        }
        catch (Exception ex)
        {
            _capture.Finish(new BodyTransfer.Failed(ex));
            throw;
        }
    }

    protected override Stream CreateContentReadStream(CancellationToken cancellationToken) =>
        new CaptureReadStream(_inner.ReadAsStream(cancellationToken), _capture);

    protected override Task<Stream> CreateContentReadStreamAsync() =>
        CreateContentReadStreamAsync(CancellationToken.None);

    protected override async Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken) =>
        new CaptureReadStream(await _inner.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), _capture);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _capture.Finish(BodyTransfer.Abandoned);
            _inner.Dispose();
        }
        base.Dispose(disposing);
    }

    private static Encoding ContentEncoding(HttpContent content)
    {
        try
        {
            var charset = content.Headers.ContentType?.CharSet?.Trim('"');
            return charset is null ? Encoding.UTF8 : Encoding.GetEncoding(charset);
        }
        catch (ArgumentException)
        {
            return Encoding.UTF8;
        }
    }
}

internal abstract class BodyTransfer
{
    private BodyTransfer() { }
    internal sealed class Complete : BodyTransfer;
    internal sealed class Abandon : BodyTransfer;
    internal sealed class Failed(Exception error) : BodyTransfer
    {
        internal Exception Error { get; } = error;
    }
    internal static BodyTransfer Completed { get; } = new Complete();
    internal static BodyTransfer Abandoned { get; } = new Abandon();
}

internal sealed record BodyCaptureResult(string Body, long Size, bool Truncated, BodyTransfer Transfer);

internal sealed class BodyCapture(int limit, Encoding encoding, Action<BodyCaptureResult> completed)
{
    private readonly object _gate = new();
    private readonly MemoryStream _preview = new(Math.Min(Math.Max(limit, 0), 4096));
    private long _size;
    private int _finished;

    internal void Append(ReadOnlySpan<byte> bytes)
    {
        lock (_gate)
        {
            if (_finished != 0)
                return;
            _size += bytes.Length;
            var remaining = (int)Math.Max(0, limit - _preview.Length);
            _preview.Write(bytes[..Math.Min(bytes.Length, remaining)]);
        }
    }

    internal void Finish(BodyTransfer transfer)
    {
        BodyCaptureResult result;
        lock (_gate)
        {
            if (_finished != 0)
                return;
            _finished = 1;
            var body = encoding.GetString(_preview.GetBuffer(), 0, (int)_preview.Length);
            result = new BodyCaptureResult(body, _size,
                _size > _preview.Length || transfer is not BodyTransfer.Complete, transfer);
            _preview.SetLength(0);
            _preview.Capacity = 0;
            _preview.Dispose();
        }
        completed(result);
    }
}

internal sealed class CaptureWriteStream(Stream inner, BodyCapture capture) : Stream
{
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => inner.CanWrite;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() => inner.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        inner.Write(buffer);
        capture.Append(buffer);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        capture.Append(buffer.Span);
    }
    // The content consumer owns the destination; disposing this observer must leave it open.
}

internal sealed class CaptureReadStream(Stream inner, BodyCapture capture) : Stream
{
    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() => inner.Flush();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        try
        {
            var count = inner.Read(buffer);
            Observe(buffer[..count], buffer.Length);
            return count;
        }
        catch (Exception ex)
        {
            capture.Finish(new BodyTransfer.Failed(ex));
            throw;
        }
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        try
        {
            var count = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            Observe(buffer.Span[..count], buffer.Length);
            return count;
        }
        catch (Exception ex)
        {
            capture.Finish(new BodyTransfer.Failed(ex));
            throw;
        }
    }

    private void Observe(ReadOnlySpan<byte> bytes, int requested)
    {
        capture.Append(bytes);
        if (bytes.IsEmpty && requested > 0)
            capture.Finish(BodyTransfer.Completed);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            capture.Finish(BodyTransfer.Abandoned);
            inner.Dispose();
        }
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        capture.Finish(BodyTransfer.Abandoned);
        await inner.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}
