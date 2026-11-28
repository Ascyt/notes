using System.Collections.Concurrent;
using Notes.Features.Bridges;
using Notes.Features.Components.Archive;
using Notes.Features.Configuration;
using Notes.Features.Helpers;
using Notes.Features.History;

namespace Notes.Features.ZippedFolders;

/// <summary>An archived folder being unzipped: how far it got, and how it ended.</summary>
public sealed class UnzipJob(CancellationToken stopping = default)
{
    private readonly CancellationTokenSource _cancellation = CancellationTokenSource.CreateLinkedTokenSource(stopping);

    /// <summary>Stops it when the app stops, or when it is cancelled (in the list of operations): nothing is unzipped then.</summary>
    public CancellationToken Token => _cancellation.Token;

    public void Cancel()
    {
        try
        {
            _cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private double _progress;
    private WorkStatus _status = new(WorkPhases.Waiting);

    /// <summary>How far it got, 0 to 1.</summary>
    public double Progress => Volatile.Read(ref _progress);

    internal void Report(double progress) => Volatile.Write(ref _progress, progress);

    /// <summary>What it is doing now, for the text under its bar (waiting, until it may begin).</summary>
    public WorkStatus Status => Volatile.Read(ref _status);

    internal void SetStatus(WorkStatus status) => Volatile.Write(ref _status, status);

    /// <summary>Done once it is unzipped (and mounted). Null if it worked, otherwise why not (for the log; the user is told less).</summary>
    public Task<string?> Finished { get; internal set; } = Task.FromResult<string?>(null);

    /// <summary>
    /// The folder whose password it needs, if it ended because its archive is encrypted and the password isn't known (nobody unlocked it
    /// since the app started, or it was changed): whoever opens it is asked for it.
    /// </summary>
    public string? NeedsPasswordOf { get; internal set; }
}
