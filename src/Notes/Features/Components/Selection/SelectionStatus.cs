using Notes.Features.Helpers;
using Notes.Features.Passwords;
using Notes.Features.ZippedFolders;

namespace Notes.Features.Components.Selection;

/// <summary>
/// What the selected entries are like for this browser now, which the selection itself (the user's, for all their tabs) can't say: password
/// unlocks are per browser, and an archived folder is zipped and unzipped as it is used. Made for every answer about the selection.
/// </summary>
/// <param name="Locked">The selected entries a password locks for this browser (they are selected, but nothing can be done with them).</param>
/// <param name="Unlock">The addresses whose passwords unlock them, outermost first, each once (a folder above may lock several).</param>
/// <param name="Zipped">The addresses of the selected archived folders that are zipped now (and not locked).</param>
/// <param name="Unzipped">The addresses of the selected archived folders that are unzipped now (and not locked).</param>
/// <param name="Unlocked">The addresses of the selected entries whose own password is unlocked now.</param>
public sealed record SelectionStatus(IReadOnlyList<string> Locked, IReadOnlyList<string> Unlock, IReadOnlyList<string> Zipped,
    IReadOnlyList<string> Unzipped, IReadOnlyList<string> Unlocked)
{
    public static SelectionStatus None { get; } = new([], [], [], [], []);
}

/// <summary>The selection as the pages get it: the user's (<see cref="SelectionSnapshot"/>), with what it is like for this browser.</summary>
public sealed record SelectionView(long Version, bool Busy, IReadOnlyList<string> Paths, int DirectoryCount, bool CanDelete, double? Progress,
    SelectionStatus Status)
{
    public static SelectionView Of(SelectionSnapshot snapshot, SelectionStatus status)
        => new(snapshot.Version, snapshot.Busy, snapshot.Paths, snapshot.DirectoryCount, snapshot.CanDelete, snapshot.Progress, status);
}

/// <summary>Tells the <see cref="SelectionStatus"/> of selected entries, inside a request (the unlocks are those of its browser).</summary>
public sealed class SelectionStatusReader(IPathService path, PasswordLocks passwordLocks, PasswordGate gate, ArchivedFolders archives)
{
    /// <summary>Of the selected entries (virtual paths). Those that are gone are left out.</summary>
    public SelectionStatus Of(IReadOnlyList<string> virtualPaths)
    {
        if (virtualPaths.Count == 0)
            return SelectionStatus.None;

        // (Mostly in the same folders: their configurations are read once)
        using IDisposable scope = Configuration.ConfigReadScope.Begin();
        List<string> locked = [], zipped = [], unzipped = [], unlocked = [];
        List<string> unlock = [];
        HashSet<string> asked = new(StringComparer.OrdinalIgnoreCase);
        foreach (string virtualPath in virtualPaths)
        {
            if (path.ToPhysicalPath(virtualPath) is not { } physical)
                continue;
            bool isDirectory = Directory.Exists(physical);
            if (!isDirectory && !File.Exists(physical))
                continue;

            string address = VirtualPaths.ToAddress(virtualPath);
            if (passwordLocks.FirstLocked(physical) is { } protection)
            {
                locked.Add(virtualPath);
                string unlockAddress = gate.AddressOf(protection, physical);
                if (asked.Add(unlockAddress))
                    unlock.Add(unlockAddress);
                continue;
            }

            if (passwordLocks.HasOwnPassword(physical, isDirectory))
                unlocked.Add(address);
            // (The entry's own archive only: an archived folder inside a selected one is unzipped with it when it is used)
            if (isDirectory)
                switch (archives.ArchiveStateOf(physical))
                {
                    case ArchiveState.Zipped:
                        zipped.Add(address);
                        break;
                    case ArchiveState.Unzipped:
                        unzipped.Add(address);
                        break;
                }
        }

        // Outermost first: unlocking a folder above may unlock what is below it too
        unlock.Sort((a, b) => a.Length.CompareTo(b.Length));
        return new SelectionStatus(locked, unlock, zipped, unzipped, unlocked);
    }
}
