using System.Security.Cryptography;
using System.Text;
using notes.Features.Auth;
using notes.Features.Helpers;
using notes.Features.Permissions;

namespace notes.Features.Components.Upload;

public interface IUploadService
{
    /// <summary>Starts an upload for the user and returns its id, which only that user can continue.</summary>
    string StartUpload(string userId);

    /// <summary>
    /// Writes one chunk of the user's upload at <paramref name="offset"/>. Returns null if the user has no such upload or the
    /// chunk doesn't continue where the previous one ended, otherwise "" or, after the last chunk, the virtual path of the
    /// finished file (renamed to "name (n).ext" if the name was taken). <paramref name="mayCreate"/> is asked for the physical path the
    /// finished file is about to get; if it says no, the upload is discarded and an <see cref="AccessDeniedException"/> thrown.
    /// </summary>
    Task<string?> UploadChunkAsync(string userId, Guid uploadId, string physicalDirectory, string name, long offset, bool isLast, Stream body, Func<string, bool> mayCreate);

    /// <summary>Discards what has been uploaded so far for the user's <paramref name="uploadId"/>.</summary>
    void AbortUpload(string userId, Guid uploadId);

    /// <summary>Deletes unfinished uploads of all users that were last written to more than <paramref name="maxAge"/> ago and returns how many.</summary>
    int DeleteStaleUploads(TimeSpan maxAge);
}

public sealed class UploadService(Config config, IPathService path, StructureLock structureLock, FolderConfigStore configs, TimeProvider time) : IUploadService
{
    public string StartUpload(string userId)
    {
        // The id is made here and only exists as a file in the user's own folder, so a chunk can only ever be
        // written to an upload that this user started (a made-up or someone else's id finds nothing)
        string userDirectory = UserTempDirectory(userId);
        _ = Directory.CreateDirectory(userDirectory);

        Guid uploadId = Guid.NewGuid();
        using (FileStream temp = new(Path.Combine(userDirectory, TempName(uploadId)), FileMode.CreateNew, FileAccess.Write, FileShare.None))
            SetLastWriteTimeToNow(temp);
        return uploadId.ToString("N");
    }

    public async Task<string?> UploadChunkAsync(string userId, Guid uploadId, string physicalDirectory, string name, long offset, bool isLast, Stream body, Func<string, bool> mayCreate)
    {
        // Chunks pile up in a temp file outside the notes that only gets its real name once the last chunk has arrived
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

        await using (temp)
        {
            // A retried chunk may overwrite what a failed attempt left behind, but there must be no gap
            if (temp.Length < offset)
                return null;

            temp.SetLength(offset);
            temp.Position = offset;
            await body.CopyToAsync(temp);
            await temp.FlushAsync();
            SetLastWriteTimeToNow(temp);
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

            for (int n = 1; ; n++)
            {
                string finalPath = Path.Combine(physicalDirectory, finalName);
                if (!Path.Exists(finalPath))
                {
                    if (!mayCreate(finalPath))
                    {
                        File.Delete(tempPath);
                        throw new AccessDeniedException($"Insufficient permissions to create '{finalName}' here.");
                    }

                    try
                    {
                        File.Move(tempPath, finalPath);
                        configs.FileAdded(physicalDirectory, finalName);
                        return path.ToVirtualPath(config.Dir, finalPath);
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

    /// <summary>The folder for globally saved data, inside the notes directory but never shown or reachable through it.</summary>
    private string GlobalDirectory => Path.Combine(Path.GetFullPath(config.Dir), PathService.ReservedName);

    private string UserTempDirectory(string userId)
        => Path.Combine(GlobalDirectory, "temp", "user", UserFolderName(userId));

    /// <summary>The user id itself if it is safe to use as a folder name (Keycloak ids are), otherwise a hash of it.</summary>
    private static string UserFolderName(string userId)
        => userId.Length is > 0 and <= 64 && userId.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')
            ? userId
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userId)));

    private static string TempName(Guid uploadId)
        => uploadId.ToString("N") + ".part";
}
