using notes.Features.Helpers;

namespace notes.Features.Components.Transfer;

/// <summary>
/// The second stage of a transfer: does what the <see cref="TransferPlanner"/> found out, as a transaction.
/// <list type="number">
/// <item>Copy: everything is copied into a staging folder next to the notes (same volume), so a failure here only means deleting the staging folder.</item>
/// <item>Commit: the copies are renamed into the target directory. Only then, for a move, are the originals renamed into the staging folder, so
/// there is always a complete copy of everything and a failure can be rolled back by renames. The staging folder is deleted last.</item>
/// </list>
/// A delete skips the copy and the commit: renaming the originals into the staging folder makes them all disappear at once, and can still be undone.
/// Failures are thrown as <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/>, after everything has been undone.
/// </summary>
public sealed class TransferExecutor(Config config, ILogger<TransferExecutor> logger)
{
    public void Execute(TransferKind kind, string physicalTarget, List<TransferItem> items)
    {
        string staging = Path.Combine(Path.GetFullPath(config.Dir), PathService.ReservedName, "temp", "transfer", Guid.NewGuid().ToString("N"));
        string stagedCopies = Path.Combine(staging, "copies");
        string trash = Path.Combine(staging, "originals");
        bool keepStaging = false;

        try
        {
            _ = Directory.CreateDirectory(stagedCopies);
            _ = Directory.CreateDirectory(trash);

            if (kind == TransferKind.Delete)
                TrashOriginals(items, trash, kind);
            else
                CopyAndCommit(kind, physicalTarget, items, stagedCopies, trash);
        }
        catch (TransferException e) when (e.KeepsStaging)
        {
            keepStaging = true;
            throw;
        }
        finally
        {
            // What is left is the staging copies after a failure, or the originals after a move or delete. Failing to remove them doesn't fail the transfer.
            if (!keepStaging)
                DeleteStaging(staging);
        }
    }

    private void CopyAndCommit(TransferKind kind, string physicalTarget, List<TransferItem> items, string stagedCopies, string trash)
    {
        for (int i = 0; i < items.Count; i++)
            CopyEntry(items[i], Path.Combine(stagedCopies, i.ToString()));

        // Put the copies in place...
        List<string> placed = [];
        try
        {
            for (int i = 0; i < items.Count; i++)
            {
                string destination = Path.Combine(physicalTarget, items[i].Name);
                MoveEntry(Path.Combine(stagedCopies, i.ToString()), destination, items[i].IsDirectory);
                placed.Add(destination);
            }

            // ...and, for a move, take the originals away
            if (kind == TransferKind.Move)
                TrashOriginals(items, trash, kind);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The originals are back where they were by now (TrashOriginals puts them back itself), so the copies are all that is left to remove
            foreach (string destination in placed)
                DeleteEntry(destination);

            if (e is IOException && placed.Count < items.Count && Path.Exists(Path.Combine(physicalTarget, items[placed.Count].Name)))
                throw new TransferException(StatusCodes.Status409Conflict, $"'{items[placed.Count].Name}' was created in this directory in the meantime. Nothing was changed.");
            throw;
        }
    }

    /// <summary>Renames the originals into the trash folder. If one can't be (e.g. it is open elsewhere), the ones already renamed are put back and the exception is rethrown.</summary>
    private void TrashOriginals(List<TransferItem> items, string trash, TransferKind kind)
    {
        List<int> trashed = [];
        try
        {
            for (int i = 0; i < items.Count; i++)
            {
                MoveEntry(items[i].Source, Path.Combine(trash, i.ToString()), items[i].IsDirectory);
                trashed.Add(i);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            foreach (int i in trashed)
            {
                try
                {
                    MoveEntry(Path.Combine(trash, i.ToString()), items[i].Source, items[i].IsDirectory);
                }
                catch (Exception restoreError) when (restoreError is IOException or UnauthorizedAccessException)
                {
                    // Extremely unlikely (a rename just worked the other way round). For a move the copies stay in place, so nothing is lost
                    // either way, and the staging folder is kept for whoever has to sort it out.
                    logger.LogCritical(restoreError, "Could not put {source} back from {trash}", items[i].Source, trash);
                    throw new TransferException(StatusCodes.Status500InternalServerError,
                        kind == TransferKind.Delete
                            ? $"The deletion failed and could not be fully undone. Entries that could not be put back are kept on the server in {trash}."
                            : $"The move failed and could not be fully undone. Nothing was lost: copies of everything are in this directory, and originals that could not be put back are kept on the server in {trash}.",
                        keepsStaging: true);
                }
            }

            throw;
        }
    }

    private void DeleteStaging(string staging)
    {
        try
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(e, "Could not delete the temporary folder {staging} of a transfer", staging);
        }
    }

    /// <summary>Copies the entry, and of a directory exactly the contents the planner checked (nothing hidden from the user, nothing that came later).</summary>
    private static void CopyEntry(TransferItem item, string destination)
    {
        if (!item.IsDirectory)
        {
            File.Copy(item.Source, destination);
            return;
        }

        _ = Directory.CreateDirectory(destination);
        foreach (InsideEntry entry in item.Contents)
        {
            string entryDestination = Path.Combine(destination, Path.GetRelativePath(item.Source, entry.Path));
            if (entry.IsDirectory)
                _ = Directory.CreateDirectory(entryDestination);
            else
                File.Copy(entry.Path, entryDestination);
        }
    }

    private static void MoveEntry(string source, string destination, bool isDirectory)
    {
        if (isDirectory)
            Directory.Move(source, destination);
        else
            File.Move(source, destination);
    }

    private void DeleteEntry(string physical)
    {
        try
        {
            if (Directory.Exists(physical))
                Directory.Delete(physical, recursive: true);
            else
                File.Delete(physical);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogError(e, "Could not remove {path} while undoing a transfer", physical);
        }
    }
}
