using System.Buffers;

namespace notes.Features.Components.Download;

/// <summary>
/// A write-only, non-seekable stream for a response body (which refuses synchronous writes) that is handed to code that
/// still writes synchronously now and then. <see cref="ZipArchiveEntry"/> does: closing an entry flushes the last bytes of its
/// compressor synchronously, even when the entry is disposed asynchronously. Those few bytes are kept back and sent in front of the
/// next asynchronous write (or by <see cref="FlushDeferredAsync"/>), so no thread ever waits on the client.
/// </summary>
/// <remarks>Only meant for the occasional small synchronous write: it throws if more than <see cref="MaxDeferredBytes"/> pile up, instead of quietly buffering a whole file.</remarks>
public sealed class DeferredWriteStream(Stream inner) : Stream
{
    public const int MaxDeferredBytes = 4 * 1024 * 1024;

    private readonly ArrayBufferWriter<byte> _deferred = new();

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
        => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (_deferred.WrittenCount + buffer.Length > MaxDeferredBytes)
            throw new InvalidOperationException("Too much was written synchronously to be held back; it would have to be written asynchronously.");

        _deferred.Write(buffer);
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await FlushDeferredAsync(cancellationToken);
        await inner.WriteAsync(buffer, cancellationToken);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <summary>Sends what was written synchronously so far.</summary>
    public async ValueTask FlushDeferredAsync(CancellationToken cancellationToken = default)
    {
        if (_deferred.WrittenCount == 0)
            return;

        await inner.WriteAsync(_deferred.WrittenMemory, cancellationToken);
        _deferred.Clear();
    }

    /// <summary>Nothing to do: a synchronous flush can't send anything, see <see cref="FlushDeferredAsync"/>.</summary>
    public override void Flush()
    {
    }

    public override async Task FlushAsync(CancellationToken cancellationToken)
    {
        await FlushDeferredAsync(cancellationToken);
        await inner.FlushAsync(cancellationToken);
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
