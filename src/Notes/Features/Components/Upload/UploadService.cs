using System.Security.Cryptography;
using System.Text;
using Notes.Features.Auth;
using Notes.Features.Bridges;
using Notes.Features.Configuration;
using Notes.Features.Helpers;

namespace Notes.Features.Components.Upload;

public interface IUploadService
{
    /// <summary>
    /// Starts an upload for the user and returns its id, which only that user can continue. Throws an <see cref="UploadRefusedException"/> if
    /// the user has as many unfinished uploads as they may have (<see cref="LimitsConfig.MaxUploadsInProgressPerUser"/>).
    /// </summary>
    string StartUpload(string userId);

    /// <summary>
    /// Writes one chunk of the user's upload at <paramref name="offset"/>. Returns null if the user has no such upload or the
    /// chunk doesn't continue where the previous one ended, otherwise "" or, after the last chunk, the virtual path of the
    /// finished file (renamed to "name (n).ext" if the name was taken). <paramref name="mayCreate"/> is asked for the physical path the
    /// finished file is about to get; if it says no, the upload is discarded and an <see cref="AccessDeniedException"/> thrown. What breaks the
    /// limits (<see cref="LimitsConfig.MaxUploadBytes"/>, the length of the name and path the file gets) discards the upload too, and throws an
    /// <see cref="UploadRefusedException"/>, as does a chunk sent while another one of the same upload is still being written.
    /// </summary>
    /// <remarks>
    /// The file goes into <paramref name="folders"/> inside <paramref name="physicalDirectory"/> (none: right into it). Those that aren't there
    /// are made when the last chunk has arrived, each asked of <paramref name="mayCreate"/> first; those that are there are asked of
    /// <paramref name="mayEnter"/>. If either says no, the upload is discarded and an <see cref="AccessDeniedException"/> thrown.
    /// </remarks>
    Task<string?> UploadChunkAsync(string userId, Guid uploadId, string physicalDirectory, IReadOnlyList<string> folders, string name, long offset,
        bool isLast, Stream body, Func<string, bool> mayCreate, Func<string, bool> mayEnter);

    /// <summary>
    /// Where a file put into <paramref name="folders"/> inside <paramref name="physicalDirectory"/> ends up: the folders that are there as
    /// they are found (also through a bridge), the others where they would be made.
    /// </summary>
    string TargetDirectory(string physicalDirectory, IReadOnlyList<string> folders);

    /// <summary>Discards what has been uploaded so far for the user's <paramref name="uploadId"/>.</summary>
    void AbortUpload(string userId, Guid uploadId);

    /// <summary>
    /// Deletes unfinished uploads of all users that were last written to more than <paramref name="maxAge"/> ago and returns how many.
    /// </summary>
    int DeleteStaleUploads(TimeSpan maxAge);
}

/// <summary>Why an upload can't go on, with the status and the message for the user.</summary>
public sealed class UploadRefusedException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

/// <param name="archives">The archived folders (the app always has them; the tests of uploads alone go without).</param>
public sealed class UploadService(EntryLocations locations, IPathService path, StructureLock structureLock, FolderConfigStore configs,
    LimitsConfig limits, TimeProvider time, UploadTexts texts, NameTexts nameTexts, ZippedFolders.ArchivedFolders? archives = null)
    : IUploadService
{
    public string StartUpload(string userId)
    {
        // The id is made here and only exists as a file in the user's own folder, so a chunk can only ever be
        // written to an upload that this user started (a made-up or someone else's id finds nothing)
        string userDirectory = UserTempDirectory(userId);
        _ = Directory.CreateDirectory(userDirectory);
        // Each one is a file on disk until it is finished or cleared as stale. (Two requests at once may get one more in: a bound, not a quota.)
        if (Directory.EnumerateFiles(userDirectory).Count() >= limits.MaxUploadsInProgressPerUser)
            throw new UploadRefusedException(StatusCodes.Status429TooManyRequests,
                texts.TooManyUnfinished(limits.MaxUploadsInProgressPerUser));

        Guid uploadId = Guid.NewGuid();
        using (FileStream temp = new(Path.Combine(userDirectory, TempName(uploadId)), FileMode.CreateNew, FileAccess.Write, FileShare.None))
            SetLastWriteTimeToNow(temp);
        return uploadId.ToString("N");
    }

    public async Task<string?> UploadChunkAsync(string userId, Guid uploadId, string physicalDirectory, IReadOnlyList<string> folders, string name,
        long offset, bool isLast, Stream body, Func<string, bool> mayCreate, Func<string, bool> mayEnter)
    {
        // Chunks pile up in a temp file outside the entries that only gets its real name once the last chunk has arrived
        string tempPath = Path.Combine(UserTempDirectory(userId), TempName(uploadId));
        FileStream temp;
        try
        {
            temp = new FileStream(tempPath, FileMode.Open, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            // Never started, or cleared as stale (see DeleteStaleUploads)
            return null;
        }
        catch (IOException) when (File.Exists(tempPath))
        {
            // Open for another chunk of it, which is still being written: chunks come one after the other
            throw new UploadRefusedException(StatusCodes.Status409Conflict, texts.PartInProgress);
        }

        await using (temp)
        {
            // A retried chunk may overwrite what a failed attempt left behind, but there must be no gap
            if (temp.Length < offset)
                return null;

            temp.SetLength(offset);
            temp.Position = offset;
            bool fits = await CopyAtMostAsync(body, temp, limits.MaxUploadBytes - offset);
            await temp.FlushAsync();
            SetLastWriteTimeToNow(temp);
            if (!fits)
            {
                temp.SetLength(0);
                await temp.DisposeAsync();
                File.Delete(tempPath);
                throw new UploadRefusedException(StatusCodes.Status413PayloadTooLarge,
                    texts.TooLarge(ByteSizes.Format(limits.MaxUploadBytes)));
            }
        }

        if (!isLast)
            return "";

        // Never overwrite: pick the next free "name (n).ext"
        string baseName = Path.GetFileNameWithoutExtension(name);
        string extension = Path.GetExtension(name);
        string finalName = name;
        using (await structureLock.EnterAsync())
        {
            // While this waited for the lock, the upload may have been cleared as stale: then it is gone like any other cleared upload
            if (!File.Exists(tempPath))
                return null;
            // The directory may have been made an archived folder meanwhile: then it is in its unzipped copy now
            if (archives is not null)
                physicalDirectory = archives.WhereItIsNow(physicalDirectory);

            // Checked again now that nothing can change it: the directory may have gone while the chunks arrived
            if (!Directory.Exists(physicalDirectory))
            {
                File.Delete(tempPath);
                throw new UploadRefusedException(StatusCodes.Status404NotFound,
                    texts.DirectoryGone);
            }

            try
            {
                physicalDirectory = EnterOrMakeFolders(physicalDirectory, folders, mayCreate, mayEnter);
            }
            catch (Exception e) when (e is AccessDeniedException or UploadRefusedException)
            {
                File.Delete(tempPath);
                throw;
            }

            for (int n = 1; ; n++)
            {
                string finalPath = Path.Combine(physicalDirectory, finalName);
                // The number that makes it free may make it too long (the name the user gave was checked before)
                string? tooLong = Encoding.UTF8.GetByteCount(finalName) > NewNames.MaxNameBytes
                    ? texts.NumberedNameTooLong(name, NewNames.MaxNameBytes)
                    : limits.IsTooLong(locations.ToVirtualPath(finalPath))
                        ? nameTexts.PathTooLong(path.ToVirtualPath(finalPath), limits.MaxPathBytes)
                        : null;
                if (tooLong is not null)
                {
                    File.Delete(tempPath);
                    throw new UploadRefusedException(StatusCodes.Status400BadRequest, tooLong);
                }

                if (locations.IsFree(finalPath))
                {
                    if (!mayCreate(finalPath))
                    {
                        File.Delete(tempPath);
                        throw new AccessDeniedException(texts.CantCreate(finalName));
                    }

                    try
                    {
                        tempPath = MoveToVolumeOf(tempPath, physicalDirectory);
                        File.Move(tempPath, finalPath);
                        configs.FileAdded(physicalDirectory, finalName);
                        string virtualPath = path.ToVirtualPath(finalPath);
                        // Below an onLowestLevel folder, a directory that holds a file now is archived right away
                        if (archives is not null)
                            await archives.ArchiveLowestLevelAfterChangeAsync(physicalDirectory);
                        return virtualPath;
                    }
                    catch (FileNotFoundException) when (!File.Exists(tempPath))
                    {
                        // Cleared as stale just now (the job doesn't take the lock)
                        return null;
                    }
                    catch (IOException) when (Path.Exists(finalPath))
                    {
                        // Made outside of the app in the meantime: on to the next name
                    }
                }

                finalName = $"{baseName} ({n}){extension}";
            }
        }
    }

    public string TargetDirectory(string physicalDirectory, IReadOnlyList<string> folders)
    {
        foreach (string folder in folders)
            physicalDirectory = Existing(physicalDirectory, folder) ?? Path.Combine(physicalDirectory, folder);
        return physicalDirectory;
    }

    /// <summary>
    /// Goes down the folders from the directory, making those that aren't there (under the lock, which the caller holds), and returns the last.
    /// </summary>
    private string EnterOrMakeFolders(string physicalDirectory, IReadOnlyList<string> folders, Func<string, bool> mayCreate,
        Func<string, bool> mayEnter)
    {
        foreach (string folder in folders)
        {
            // One that is there, as the address would find it (in any case, or a bridge)
            if (Existing(physicalDirectory, folder) is { } existing)
            {
                if (!Directory.Exists(existing))
                    throw new UploadRefusedException(StatusCodes.Status409Conflict, texts.NotAFolder(folder));
                if (!mayEnter(existing))
                    throw new AccessDeniedException(texts.CantCreate(folder));
                physicalDirectory = existing;
                continue;
            }

            // Not there, but not free either: hidden by a bridge, or a link
            string made = Path.Combine(physicalDirectory, folder);
            if (!locations.IsFree(made))
                throw new UploadRefusedException(StatusCodes.Status409Conflict, texts.NotAFolder(folder));
            if (!mayCreate(made))
                throw new AccessDeniedException(texts.CantCreate(folder));
            _ = Directory.CreateDirectory(made);
            physicalDirectory = made;
        }

        return physicalDirectory;
    }

    /// <summary>The entry of the name in the directory, as an address finds it, or null if there is none.</summary>
    private string? Existing(string physicalDirectory, string name)
    {
        string directory = locations.ToVirtualPath(physicalDirectory);
        return path.ToPhysicalPath(directory == "" ? name : directory + "/" + name);
    }

    public void AbortUpload(string userId, Guid uploadId)
    {
        try
        {
            File.Delete(Path.Combine(UserTempDirectory(userId), TempName(uploadId)));
        }
        catch (DirectoryNotFoundException)
        {
            // The user has no uploads at all (never had, or all cleared as stale): there is nothing to abort
        }
    }

    public int DeleteStaleUploads(TimeSpan maxAge)
    {
        string usersDirectory = Path.Combine(GlobalDirectory, "temp", "user");
        if (!Directory.Exists(usersDirectory))
            return 0;

        DateTime cutoff = time.GetUtcNow().UtcDateTime - maxAge;
        int deleted = 0;
        foreach (string userDirectory in Directory.EnumerateDirectories(usersDirectory))
        {
            foreach (string file in Directory.EnumerateFiles(userDirectory))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) >= cutoff)
                        continue;

                    File.Delete(file);
                    deleted++;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // In use or already gone; the next run tries again
                }
            }

            // Only succeeds when nothing is left in it
            try
            {
                Directory.Delete(userDirectory);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }

        return deleted;
    }

    /// <summary>
    /// Marks the upload as written to now, by the app's clock: <see cref="DeleteStaleUploads"/> compares this time with the clock, so both
    /// must come from the same one (the file system's own time would be another clock, as far as the tests are concerned).
    /// </summary>
    private void SetLastWriteTimeToNow(FileStream temp)
        => File.SetLastWriteTimeUtc(temp.SafeFileHandle, time.GetUtcNow().UtcDateTime);

    /// <summary>
    /// The upload moved next to the directory it goes into, if that is in a bridge (which may be on another volume): there it can be renamed
    /// into place, so that it appears at once and complete, not while it is copied. Returns where it is now.
    /// </summary>
    private string MoveToVolumeOf(string tempPath, string physicalDirectory)
    {
        // (Already there when a name was taken in the meantime and the next one is tried)
        string targetTemp = locations.TempDirectoryFor(physicalDirectory, "upload");
        if (locations.LocationOf(physicalDirectory) == locations.Root
            || string.Equals(Path.GetDirectoryName(tempPath), targetTemp, StringComparison.OrdinalIgnoreCase))
            return tempPath;

        _ = Directory.CreateDirectory(targetTemp);
        string moved = Path.Combine(targetTemp, Path.GetFileName(tempPath));
        File.Move(tempPath, moved);
        return moved;
    }

    /// <summary>The folder for the app's own data at the root, never shown or reachable through it.</summary>
    private string GlobalDirectory => locations.Root.DataDirectory;

    private string UserTempDirectory(string userId)
        => Path.Combine(GlobalDirectory, "temp", "user", UserFolderName(userId));

    /// <summary>The user id itself if it is safe to use as a folder name (Keycloak ids are), otherwise a hash of it.</summary>
    private static string UserFolderName(string userId)
        => userId.Length is > 0 and <= 64 && userId.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')
            ? userId
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userId)));

    /// <summary>Copies at most <paramref name="maxBytes"/>; false if the source has more (what was copied is then of no use).</summary>
    private static async Task<bool> CopyAtMostAsync(Stream source, Stream destination, long maxBytes)
    {
        byte[] buffer = new byte[81920];
        long copied = 0;
        int read;
        while ((read = await source.ReadAsync(buffer)) > 0)
        {
            copied += read;
            if (copied > maxBytes)
                return false;
            await destination.WriteAsync(buffer.AsMemory(0, read));
        }

        return true;
    }

    private static string TempName(Guid uploadId)
        => uploadId.ToString("N") + ".part";
}
