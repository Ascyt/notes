namespace Notes.Features.ZippedFolders;

/// <summary>
/// An archived folder being zipped (from when zipping takes its gate, waiting for what uses it included): how far it got, and the stop that
/// opening it pulls, where it may.
/// </summary>
internal sealed class ZipJob(bool stopsWhenOpened, CancellationToken outer) : IProgress<double>, IDisposable
{
    private readonly CancellationTokenSource _stop = CancellationTokenSource.CreateLinkedTokenSource(outer);
    private double _progress;
    private Helpers.WorkStatus _status = new(Helpers.WorkPhases.Waiting);

    /// <summary>Told how far it got (from the thread that zips), and passed on to who else waits for it.</summary>
    public Action<double>? AlsoReport { get; init; }

    public double Progress => Volatile.Read(ref _progress);

    /// <summary>What it is doing now, for the text under its bar (waiting for what uses the folder, at first).</summary>
    public Helpers.WorkStatus Status => Volatile.Read(ref _status);

    public void SetStatus(Helpers.WorkStatus status)
    {
        Volatile.Write(ref _status, status);
        AlsoStatus?.Invoke(status);
    }

    /// <summary>Told what it is doing, as <see cref="AlsoReport"/> is told how far it got.</summary>
    public Action<Helpers.WorkStatus>? AlsoStatus { get; init; }

    public CancellationToken Token => _stop.Token;

    public void Report(double value)
    {
        Volatile.Write(ref _progress, value);
        AlsoReport?.Invoke(value);
    }

    /// <summary>
    /// Stops it, whatever waits for it (asked for in the list of operations: a move or a delete that waits for it then fails as "in use"). Past
    /// the point where the new archive took the old one's place this changes nothing: it finishes.
    /// </summary>
    public void Cancel()
    {
        try
        {
            _stop.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Ended meanwhile
        }
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
