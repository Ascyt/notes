namespace Notes.Features.ZippedFolders;

/// <summary>
/// An archived folder being zipped (from when zipping takes its gate, waiting for what uses it included): how far it got, and the stop that
/// opening it pulls, where it may.
/// </summary>
internal sealed class ZipJob(bool stopsWhenOpened, CancellationToken outer) : IProgress<double>, IDisposable
{
    private readonly CancellationTokenSource _stop = CancellationTokenSource.CreateLinkedTokenSource(outer);
    private double _progress;

    /// <summary>Told how far it got (from the thread that zips), and passed on to who else waits for it.</summary>
    public Action<double>? AlsoReport { get; init; }

    public double Progress => Volatile.Read(ref _progress);

    public CancellationToken Token => _stop.Token;

    public void Report(double value)
    {
        Volatile.Write(ref _progress, value);
        AlsoReport?.Invoke(value);
    }

    /// <summary>
    /// Stops it, if it may be stopped for that (not a zipping that a move or a delete waits for). Past the point where the new archive took
    /// the old one's place this changes nothing: it finishes.
    /// </summary>
    public void StopBecauseOpened()
    {
        if (!stopsWhenOpened || _stop.IsCancellationRequested)
            return;
        try
        {
            _stop.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Ended meanwhile
        }
    }

    public void Dispose() => _stop.Dispose();
}
