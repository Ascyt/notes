using Notes.Features.Bridges;
using Notes.Features.Helpers;

namespace Notes.Features.Components.Archive;

/// <summary>
/// Tells how far the writing of an archive got (0 to 1): the bytes of the files read so far, against what was measured before
/// (<see cref="Measure"/>). Each file also counts as a few bytes by itself, so that many small files move it too. What was measured may be
/// more than goes in (entries the user may not read are measured, but left out), so it is never told as done before it is.
/// </summary>
public sealed class ArchiveProgress
{
    /// <summary>What a file counts as besides its bytes (opening it, its entry in the archive).</summary>
    private const long BytesPerFile = 4096;

    private readonly long _total;
    private readonly Action<double> _report;
    private long _done;
    private long _lastReportedPerMille = -1;

    /// <param name="report">Told the new value whenever it moved by at least a thousandth (from the thread that is writing).</param>
    public ArchiveProgress(long bytes, int files, Action<double> report)
    {
        _total = Math.Max(1, bytes + files * BytesPerFile);
        _report = report;
    }

    /// <summary>
    /// What the files of the entries (files, and everything below directories, bridges included) have together, by what is on disk now. Links
    /// and the app's own data aren't looked into, as the archive leaves them out. What can't be read is skipped: the writer reports it.
    /// </summary>
    public static (long Bytes, int Files) Measure(EntryLocations locations, IEnumerable<string> physicalPaths)
    {
        long bytes = 0;
        int files = 0;
        EnumerationOptions options = new() { AttributesToSkip = 0, IgnoreInaccessible = true };
        Stack<DirectoryInfo> pending = new();
        foreach (string path in physicalPaths)
        {
            if (File.Exists(path))
            {
                bytes += new FileInfo(path).Length;
                files++;
            }
            else if (Directory.Exists(path))
                pending.Push(new DirectoryInfo(path));
        }

        while (pending.TryPop(out DirectoryInfo? directory))
        {
            try
            {
                foreach (ChildEntry child in locations.ListDirectory(directory, options))
                {
                    if (PathService.IsLink(child.Entry))
                        continue;
                    if (child.Entry is DirectoryInfo inner)
                    {
                        if (child.Name != PathService.ReservedName)
                            pending.Push(inner);
                        continue;
                    }
                    bytes += ((FileInfo)child.Entry).Length;
                    files++;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Only for telling how far it got: the writer itself says what it can't read
            }
        }

        return (bytes, files);
    }

    /// <summary>The source of a file, counted as it is read.</summary>
    public Stream Counting(Stream source) => new CountingReadStream(source, this);

    private void Add(long bytes)
    {
        long done = Interlocked.Add(ref _done, bytes);
        // Never done before the writer is (what was measured may be more than goes in, or less if files grew)
        long perMille = Math.Min(999, done * 1000 / _total);
        if (perMille > Interlocked.Exchange(ref _lastReportedPerMille, perMille))
            _report(perMille / 1000.0);
    }

    /// <summary>Reads through, counting the bytes, and the file itself once it is closed.</summary>
    private sealed class CountingReadStream(Stream inner, ArchiveProgress progress) : Stream
    {
        private bool _closed;

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
            progress.Add(read);
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int read = await inner.ReadAsync(buffer, cancellationToken);
            progress.Add(read);
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
            if (disposing && !_closed)
            {
                _closed = true;
                progress.Add(BytesPerFile);
                inner.Dispose();
            }
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            if (!_closed)
            {
                _closed = true;
                progress.Add(BytesPerFile);
                await inner.DisposeAsync();
            }
            await base.DisposeAsync();
        }
    }
}
