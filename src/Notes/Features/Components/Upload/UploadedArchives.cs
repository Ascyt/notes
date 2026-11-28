using Notes.Features.Components.Archive;
using Notes.Features.Helpers;
using Notes.Features.ZippedFolders;

namespace Notes.Features.Components.Upload;

/// <summary>
/// Unzips an uploaded archive (.zip or .7z) into a folder that isn't anywhere yet (the staging folder), checking what is in it as an upload
/// is checked: every name as a new name, no more than <see cref="LimitsConfig.MaxUnzippedBytes"/> written (what the archive says about its
/// sizes isn't trusted) and <see cref="LimitsConfig.MaxUnzippedEntries"/> entries, nothing that leads out of it.
/// </summary>
public static class UploadedArchives
{
    /// <summary>The format of the archive by its name, or null if it is none that can be unzipped.</summary>
    public static ArchiveFormat? FormatOf(string name)
        => Path.GetExtension(name).ToLowerInvariant() switch
        {
            ".zip" => ArchiveFormat.Zip,
            ".7z" => ArchiveFormat.SevenZip,
            _ => null
        };

    /// <summary>
    /// Why it can't be unzipped before it is even tried, or null if it can be tried: one that is encrypted isn't unzipped here (who has its
    /// password unzips it themselves), and one that can't be read at all is damaged (or, a 7z, may have its index encrypted as well).
    /// </summary>
    public static string? WhyNotUnzippable(string archivePath, ArchiveFormat format, string name, UploadTexts texts)
        => ArchiveReading.IsEncryptedIfReadable(archivePath, format) switch
        {
            true => texts.ArchiveEncrypted(name),
            false => null,
            null => format == ArchiveFormat.SevenZip ? texts.ArchiveDamagedOrEncrypted(name) : texts.ArchiveDamaged(name)
        };

    /// <summary>
    /// How large its files say they are, unpacked, and how many entries it has, for telling how far unzipping it got (0 if it can't tell).
    /// </summary>
    public static (long Bytes, int Entries) DeclaredSize(string archivePath, ArchiveFormat format)
    {
        try
        {
            return (ArchiveReading.DeclaredSize(archivePath, format, password: null), ArchiveReading.CountEntries(archivePath, format, password: null));
        }
        catch (ArchiveUnreadableException)
        {
            return (0, 0);
        }
    }

    /// <summary>
    /// What in the staging folder becomes the new folder: the one folder that is all the archive holds (photos.zip with only photos/ in it),
    /// or the staging folder itself.
    /// </summary>
    public static string ContentsOf(string staging)
    {
        string[] entries = Directory.GetFileSystemEntries(staging);
        return entries.Length == 1 && Directory.Exists(entries[0]) ? entries[0] : staging;
    }

    /// <summary>
    /// Whether an entry of the archive (its path, by its parts) is left out: what can't be an entry anywhere (a configuration, the archive of an
    /// archived folder, the app's data folder), which a download by an admin may hold. Uploading is no way to set permissions or passwords.
    /// </summary>
    public static bool IsLeftOut(IEnumerable<string> parts)
        => parts.Any(part => part == PathService.ReservedName || string.Equals(part, PathService.ConfigFileName, StringComparison.OrdinalIgnoreCase)
                             || ArchivedFolder.IsArchiveFileName(part));

    /// <summary>
    /// Unzips it into <paramref name="staging"/> (which must not be there yet). Returns how many entries were left out. Throws an
    /// <see cref="UploadRefusedException"/> for what it holds that can't be uploaded, and an <see cref="ArchiveUnreadableException"/> if it
    /// can't be read (damaged).
    /// </summary>
    /// <param name="written">Told how many bytes were written so far, and how many entries were gone through, after each.</param>
    public static int Extract(string archivePath, ArchiveFormat format, string staging, string name, LimitsConfig limits,
        UploadTexts texts, NameTexts nameTexts, CancellationToken cancellationToken, Action<long, int>? written = null)
    {
        _ = Directory.CreateDirectory(staging);
        string root = Path.GetFullPath(staging) + Path.DirectorySeparatorChar;
        int entries = 0;
        int leftOut = 0;
        long total = 0;
        try
        {
            ForEach();
        }
        catch (ArchiveUnreadableException e) when (e.InnerException is UploadRefusedException refused)
        {
            // (The reader takes whatever is thrown while it reads for the archive being unreadable: a refusal is what it says)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(refused);
        }
        return leftOut;

        void ForEach() => ArchiveReading.ForEachEntry(archivePath, format, password: null, (entry, contents) =>
        {
            if (++entries > limits.MaxUnzippedEntries)
                throw new UploadRefusedException(StatusCodes.Status413PayloadTooLarge, texts.TooManyEntries(limits.MaxUnzippedEntries));

            string[] parts = entry.Path.Split('/');
            if (entry.Path.Length == 0 || parts.Any(part => part is "" or "." or ".."))
                throw new UploadRefusedException(StatusCodes.Status400BadRequest, texts.ArchiveDamaged(name));
            if (IsLeftOut(parts))
            {
                leftOut++;
                return;
            }

            // Each part as a new name is (normalized as one is stored)
            List<string> names = [];
            foreach (string part in parts)
            {
                if (NewNames.Check(part, out string normalized) is { } problem)
                    throw new UploadRefusedException(StatusCodes.Status400BadRequest, texts.BadEntryName(entry.Path, nameTexts.Problem(problem)));
                names.Add(normalized);
            }
            string target = Path.GetFullPath(Path.Combine([staging, .. names]));
            if (!target.StartsWith(root, StringComparison.Ordinal))
                throw new UploadRefusedException(StatusCodes.Status400BadRequest, texts.ArchiveDamaged(name));

            if (contents is null)
            {
                _ = Directory.CreateDirectory(target);
                return;
            }
            // (Twice in it, or twice but for case where the file system doesn't tell them apart: not a whole archive)
            if (Path.Exists(target))
                throw new UploadRefusedException(StatusCodes.Status400BadRequest, texts.ArchiveDamaged(name));
            _ = Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using (FileStream file = new(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920))
                total += CopyAtMost(contents, file, limits.MaxUnzippedBytes - total, limits, texts);
            written?.Invoke(total, entries);
            if (entry.LastWriteTimeUtc is { } time)
                File.SetLastWriteTimeUtc(target, time);
        }, cancellationToken);
    }

    /// <summary>Copies it, but never more than <paramref name="maxBytes"/>: more is refused. Returns how much was copied.</summary>
    private static long CopyAtMost(Stream source, Stream destination, long maxBytes, LimitsConfig limits, UploadTexts texts)
    {
        byte[] buffer = new byte[81920];
        long copied = 0;
        int read;
        while ((read = source.Read(buffer)) > 0)
        {
            copied += read;
            if (copied > maxBytes)
                throw new UploadRefusedException(StatusCodes.Status413PayloadTooLarge,
                    texts.UnzippedTooLarge(ByteSizes.Format(limits.MaxUnzippedBytes)));
            destination.Write(buffer, 0, read);
        }
        return copied;
    }
}
