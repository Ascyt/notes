using Notes.Features.Helpers;

namespace Notes.Features.ZippedFolders;

/// <summary>
/// Deleting what zipping and unzipping left when the app stopped (<see cref="FolderArchiver.DeleteLeftovers"/>), which runs behind the start:
/// how far it got, and how to stop it (in the list of operations).
/// </summary>
public sealed class LeftoverDeletionJob(CancellationToken stopping)
{
    private readonly CancellationTokenSource _cancellation = CancellationTokenSource.CreateLinkedTokenSource(stopping);
    private double _progress;
    private WorkStatus _status = new(WorkPhases.Measuring);

    /// <summary>Stops it when the app stops, or when it is cancelled: what is left is deleted at the next start.</summary>
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

    /// <summary>How far it got, 0 to 1.</summary>
    public double Progress => Volatile.Read(ref _progress);

    internal void Report(double progress) => Volatile.Write(ref _progress, progress);

    /// <summary>What it is doing now, for the text under its bar.</summary>
    public WorkStatus Status => Volatile.Read(ref _status);

    internal void SetStatus(WorkStatus status) => Volatile.Write(ref _status, status);
}
