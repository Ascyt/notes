using Notes.Features.Bridges;
using Notes.Features.Passwords;

namespace Notes.Features.ZippedFolders;

public sealed partial class ArchivedFolders
{
    /// <summary>
    /// The password that an unzipped encrypted folder at or below the entries (physical paths, as requests have them) needs to be zipped and
    /// that the app doesn't know (nobody unlocked it since the app started, or since it was zipped), or null. Before what moves, deletes or
    /// renames them zips them: whoever asked is asked for it.
    /// </summary>
    public Protection? UnknownPasswordToZipAtOrBelow(IEnumerable<string> physicalPaths)
    {
        foreach (EntryLocation archive in locations.UnzippedArchives)
        {
            if (!physicalPaths.Any(path => locations.IsSameOrInside(archive.PhysicalPath, path)))
                continue;
            ResolvedArchive resolved = resolver.Resolve(archive.MountPath!);
            if (resolved.Settings is not { Encrypted: true })
                continue;
            string passwordFolder = resolved.SettingsFolder ?? Normalize(archive.MountPath!);
            if (passwords.PasswordOf(passwordFolder) is null && configs.Get(passwordFolder).Config?.PasswordHash is { } hash)
                return new Protection(passwordFolder, hash);
        }
        return null;
    }

    /// <summary>
    /// Unzips the folder (physical) and the folders below it whose archives are encrypted, with the passwords the app knows (see
    /// <see cref="ArchivePasswords"/>): before encryption is turned off, or the password changed, as those archives can't be opened any more
    /// once the configuration says otherwise. Also those that come to light in the ones it unzips. Whether all of them are unzipped now.
    /// </summary>
    public async Task<bool> UnzipEncryptedAsync(string folderPath)
    {
        for (int round = 0; round < 8; round++)
        {
            List<string> encrypted = [.. new[] { Normalize(folderPath) }.Concat(DirectoriesBelow(folderPath).Zipped)
                .Where(folder => MountedAt(folder) is null && ArchiveFileOf(folder) is { } archive
                                 && ArchiveReading.IsEncrypted(archive, FolderOf(folder).Format))];
            if (encrypted.Count == 0)
                return true;
            foreach (string folder in encrypted)
                if (await Unzip(folder).Finished is { } failure)
                {
                    logger.LogWarning("Could not unzip the encrypted folder {folder}: {failure}", folder, failure);
                    return false;
                }
        }
        return false;
    }

    /// <summary>
    /// Zips, in the background, the unzipped encrypted folders that the passwords of <paramref name="passwordFolders"/> encrypt (the lock
    /// button: what was unlocked is locked away, encrypted, at once). One that is in use is zipped once it isn't.
    /// </summary>
    public void ZipEncryptedInBackground(IReadOnlyCollection<string> passwordFolders)
    {
        List<string> folders = [.. UnzippedFolders().Where(folder => resolver.Resolve(folder) is { Settings.Encrypted: true } resolved
                                                                     && passwordFolders.Any(passwordFolder => IsSame(passwordFolder, resolved.SettingsFolder!)))];
        if (folders.Count == 0)
            return;
        _ = Task.Run(async () =>
        {
            foreach (string folder in folders)
            {
                try
                {
                    if (!await ZipAsync(folder, stopsWhenOpened: true))
                        logger.LogInformation("The encrypted folder {folder} stays unzipped for now: it is in use, or was opened", folder);
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Zipping the encrypted folder {folder} failed; it stays unzipped", folder);
                }
            }
        });
    }

    /// <summary>
    /// Unzips and zips again, now, in the background, the folder (physical) and the folders below whose settings say encrypted and that are
    /// zipped in an archive that isn't: after encryption was turned on, so that it doesn't stay on disk (it is overwritten when the new one
    /// takes its place). One that is unzipped is encrypted the next time it is zipped, its old archive overwritten then.
    /// </summary>
    public void ReencryptInBackground(string folderPath)
    {
        List<string> folders = [.. new[] { Normalize(folderPath) }.Concat(DirectoriesBelow(folderPath).Zipped)
            .Where(folder => resolver.SettingsOf(folder) is { Encrypted: true } && MountedAt(folder) is null
                             && ArchiveFileOf(folder) is { } archive && !ArchiveReading.IsEncrypted(archive, FolderOf(folder).Format))];
        if (folders.Count == 0)
            return;
        _ = Task.Run(async () =>
        {
            foreach (string folder in folders)
            {
                try
                {
                    if (await Unzip(folder).Finished is { } failure)
                    {
                        logger.LogWarning("Could not unzip {folder} to encrypt it: {failure}", folder, failure);
                        continue;
                    }
                    if (!await ZipAsync(folder, stopsWhenOpened: true))
                        logger.LogInformation("{folder} is encrypted the next time it is zipped: it is in use now", folder);
                }
                catch (Exception e)
                {
                    // (Nobody awaits this)
                    logger.LogError(e, "Encrypting {folder} failed; it is encrypted the next time it is zipped", folder);
                }
            }
        });
    }
}
