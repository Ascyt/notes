using System.Collections.Concurrent;
using System.Diagnostics;
using Notes.Features.Bridges;
using Notes.Features.Components.Archive;
using Notes.Features.Configuration;
using Notes.Features.Helpers;
using Notes.Features.History;

namespace Notes.Features.ZippedFolders;

public sealed partial class ArchivedFolders
{
    // ---- After a crash, when the app starts

    /// <summary>
    /// Finds what zipping, unzipping or turning archiving on left when the app stopped, in every location, and puts it right: half done work
    /// is deleted, copies that are there are mounted (they hold everything), and a folder that was being turned into an archived one gets its
    /// configuration back. Folders whose archiving is on but that weren't converted yet are converted. Runs before the first request; but
    /// deleting what is left in the work directories (overwriting each file, which takes long with many) runs behind the start, as an
    /// operation of the list (<see cref="DeleteLeftoversInBackground"/>).
    /// </summary>
    public async Task RecoverAsync()
    {
        List<string> leftovers = [];
        List<EntryLocation> roots = [.. locations.All.Where(location => !location.IsArchive)];
        foreach (EntryLocation location in roots)
            await RecoverInAsync(location, leftovers);
        DeleteLeftoversInBackground(leftovers);

        // What the configurations say now, also where they were changed while the app didn't run (or it stopped right after one was saved)
        await ReconcileEverywhereAsync();
    }

    /// <summary>
    /// Overwrites and deletes what zipping and unzipping left (found by <see cref="FolderArchiver.FindLeftovers"/>) without keeping the start
    /// waiting: nothing of it is used (the archive or the copy hold everything), so nothing needs it gone first. In the list of operations
    /// while it runs, for admins; stopped when the app stops, what is left is found again at the next start.
    /// </summary>
    private void DeleteLeftoversInBackground(List<string> leftovers)
    {
        if (leftovers.Count == 0)
            return;

        LeftoverDeletionJob job = new(lifetime.ApplicationStopping);
        IDisposable? operation = operations?.Begin(Operations.OperationKind.DeletingLeftovers, locations.Root.PhysicalPath, () => job.Progress,
            () => job.Status, job.Cancel, adminsOnly: true);
        logger.LogInformation("Deleting what zipping and unzipping left ({count} folder(s)) in the background; the app starts meanwhile", leftovers.Count);
        _ = Task.Run(() =>
        {
            try
            {
                archiver.DeleteLeftovers(leftovers, job.Report, job.SetStatus, job.Token);
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation("Stopped deleting what zipping and unzipping left; what is left is deleted at the next start");
            }
            catch (Exception e)
            {
                logger.LogError(e, "Could not delete what zipping and unzipping left; it is deleted at the next start");
            }
            finally
            {
                operation?.Dispose();
            }
        });
    }

    private async Task RecoverInAsync(EntryLocation location, List<string> leftovers)
    {
        string data = location.DataDirectory;
        logger.LogInformation("Recovering archived folders in {location}: looking at what zipping and unzipping left", location.PhysicalPath);
        leftovers.AddRange(archiver.FindLeftovers(Path.Combine(data, "temp", ZippingKind)));
        RecoverTurningOff(location);

        // Copies whose folder is gone: the app stopped while a folder was turned into an archived one
        string unzippedRoot = Path.Combine(data, "temp", UnzippedKind);
        if (Directory.Exists(unzippedRoot))
            foreach (string copy in CopyRootsIn(location, unzippedRoot))
            {
                string folder = Path.Combine(location.PhysicalPath, Path.GetRelativePath(unzippedRoot, copy));
                if (!Directory.Exists(folder))
                {
                    logger.LogWarning("{copy} was being made an archived folder when the app stopped; it is put back at {folder}", copy, folder);
                    PutBackConfiguration(folder, copy);
                }
            }

        // (Walks every directory of the location, which takes a while with many)
        logger.LogInformation("Recovering archived folders in {location}: looking for archived folders", location.PhysicalPath);
        Stopwatch stopwatch = Stopwatch.StartNew();
        List<string> archivedFolders = ArchivedFoldersIn(location.PhysicalPath);
        logger.LogInformation("Found {count} archived folder(s) in {location} after {seconds:0.0} s", archivedFolders.Count, location.PhysicalPath,
            stopwatch.Elapsed.TotalSeconds);

        foreach (string folder in archivedFolders)
        {
            ArchivedFolder archived = FolderOf(folder);
            switch (archiver.Recover(archived))
            {
                case ArchivedFolderState.Unzipped:
                    logger.LogWarning("The archived folder {folder} was unzipped when the app stopped; its copy counts, and is zipped later", folder);
                    EntryLocation mounted = Mount(folder, archived.UnzippedDirectory, StateOf(folder));
                    await RecoverInAsync(mounted, leftovers);
                    break;
                case ArchivedFolderState.Missing:
                    // Turned on, but not converted yet (the app stopped right after the configuration was saved)
                    await ConvertAsync(folder);
                    break;
            }
        }
    }

    /// <summary>
    /// The unzipped copies in the folder of copies: the directories whose folder is archived, or holds nothing but its configuration and archive
    /// (what is left of a folder whose contents are its copy: one turned off, or archived by an <c>onLowestLevel</c> folder above), or is gone
    /// (but for its copy). Any other directory there is one on the way to copies, whose folder holds the folders they are of.
    /// </summary>
    private IEnumerable<string> CopyRootsIn(EntryLocation location, string unzippedRoot)
    {
        Stack<string> pending = new([unzippedRoot]);
        while (pending.TryPop(out string? directory))
        {
            foreach (string inner in Directory.EnumerateDirectories(directory))
            {
                string folder = Path.Combine(location.PhysicalPath, Path.GetRelativePath(unzippedRoot, inner));
                bool isCopy = Directory.Exists(folder)
                    ? IsArchived(folder) || ArchiveSettingsResolver.HoldsOnlyItsOwn(folder)
                    : File.Exists(Path.Combine(inner, PathService.ConfigFileName));
                if (isCopy)
                    yield return inner;
                else if (Directory.Exists(folder))
                    pending.Push(inner);
            }
        }
    }

    /// <summary>The archived folders in the directory (not its own data, nor what a bridge hides or what is inside archived ones).</summary>
    private List<string> ArchivedFoldersIn(string root)
    {
        List<string> found = [];
        Stack<string> pending = new([root]);
        EnumerationOptions options = new() { AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true };
        while (pending.TryPop(out string? directory))
        {
            foreach (string inner in Directory.EnumerateDirectories(directory, "*", options))
            {
                // (An archived folder that is unzipped is hidden by its copy too: it is found all the same, its copy is looked into by
                // the callers that need it)
                if ((IsSame(directory, root) && Path.GetFileName(inner) == PathService.ReservedName)
                    || (locations.IsHiddenByBridge(inner) && MountedAt(inner) is null))
                    continue;
                if (IsArchived(inner))
                    found.Add(inner);
                else
                    pending.Push(inner);
            }
        }
        return found;
    }
}
