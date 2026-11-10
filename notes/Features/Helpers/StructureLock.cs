namespace notes.Features.Helpers;

/// <summary>
/// Held by every operation that adds, removes or renames entries in the notes (create, upload, rename, copy, move, delete), for all users,
/// for as long as it checks permissions and then acts on them. So nothing can appear in a directory between the moment a move or delete
/// checked everything inside it and the moment it moves or deletes it, and no two of these operations can see each other half done.
/// Saving a file changes no entries and doesn't need it. Changes made to the files outside of the app are not covered.
/// </summary>
public sealed class StructureLock
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Waits for the lock; dispose the result to release it. Not cancellable on purpose: once an operation started waiting it is
    /// carried out, even if the request that asked for it went away.
    /// </summary>
    public async Task<IDisposable> EnterAsync()
    {
        await _gate.WaitAsync(CancellationToken.None);
        return new Releaser(_gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                _ = gate.Release();
        }
    }
}
