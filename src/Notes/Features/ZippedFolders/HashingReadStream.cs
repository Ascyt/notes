using System.Security.Cryptography;

namespace Notes.Features.ZippedFolders;

/// <summary>
/// Reads a stream and works out the SHA-256 of what was read. On disposing, it reads what is left first, so the hash is that of all of it even
/// where whoever read it stopped early (an archive that then holds less fails the check against it).
/// </summary>
internal sealed class HashingReadStream(Stream inner, Action<byte[]> hashed) : Stream
{
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private bool _done;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => inner.Length;

    public override long Position
    {
        get => inner.Position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        int read = inner.Read(buffer);
        _hash.AppendData(buffer[..read]);
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        int read = await inner.ReadAsync(buffer, cancellationToken);
        _hash.AppendData(buffer.Span[..read]);
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_done)
        {
            _done = true;
            byte[] rest = new byte[81920];
            while (Read(rest) > 0) { }
            hashed(_hash.GetHashAndReset());
            _hash.Dispose();
            inner.Dispose();
        }
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (!_done)
        {
            _done = true;
            byte[] rest = new byte[81920];
            while (await ReadAsync(rest) > 0) { }
            hashed(_hash.GetHashAndReset());
            _hash.Dispose();
            await inner.DisposeAsync();
        }
        await base.DisposeAsync();
    }
}
