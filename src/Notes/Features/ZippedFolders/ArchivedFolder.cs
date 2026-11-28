using Notes.Features.Components.Archive;

namespace Notes.Features.ZippedFolders;

/// <summary>
/// A folder that is kept as an archive, and where it is unzipped to while it is used.
/// </summary>
/// <param name="ArchivePath">The archive, in the folder's own directory (<c>_.7z</c> or <c>_.zip</c>).</param>
/// <param name="UnzippedDirectory">Where its contents are while it is unzipped (in <c>_/temp/unzipped</c> of its location).</param>
/// <param name="WorkDirectory">
/// The app's working folder for zipping in the same location (<c>_/temp/zipping</c>), so on the same volume as the other two: what is in it
/// is only ever half done, and can always be deleted (see <see cref="FolderArchiver.RecoverWorkDirectory"/>).
/// </param>
public sealed record ArchivedFolder(string ArchivePath, string UnzippedDirectory, string WorkDirectory)
{
    /// <summary>The name of the archive in the folder's directory, without its extension.</summary>
    public const string ArchiveName = "_";

    /// <summary>Where the archive is written to before it takes the old one's place (next to it: a rename stays within the directory).</summary>
    public string NewArchivePath => ArchivePath + ".new";

    public ArchiveFormat Format => ArchivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? ArchiveFormat.Zip : ArchiveFormat.SevenZip;

    /// <summary>
    /// The archive of the other format that zipping replaces (the folder's <c>filetype</c> changed), or null: deleted once the new one took
    /// its place, before the unzipped copy is taken away.
    /// </summary>
    public string? FormerArchivePath { get; init; }

    /// <summary>
    /// Whether the archives the new one replaces are overwritten before they go (they weren't encrypted, the new one is): a file that is only
    /// deleted stays readable on the disk until something else is written there.
    /// </summary>
    public bool ShredsReplacedArchives { get; init; }

    /// <summary>Both archive files the folder could have (<c>_.7z</c>, <c>_.zip</c>).</summary>
    public static string[] ArchivePathsIn(string folderPath)
        => [Path.Combine(folderPath, ArchiveFileName(ArchiveFormat.SevenZip)), Path.Combine(folderPath, ArchiveFileName(ArchiveFormat.Zip))];

    /// <summary>The archive's name for the format: <c>_.7z</c> or <c>_.zip</c>.</summary>
    public static string ArchiveFileName(ArchiveFormat format) => ArchiveName + (format == ArchiveFormat.SevenZip ? ".7z" : ".zip");

    /// <summary>Whether the name is that of an archive of an archived folder, or of one being written (<c>.new</c>).</summary>
    public static bool IsArchiveFileName(string name)
        => name.Equals("_.7z", StringComparison.OrdinalIgnoreCase) || name.Equals("_.zip", StringComparison.OrdinalIgnoreCase)
           || name.Equals("_.7z.new", StringComparison.OrdinalIgnoreCase) || name.Equals("_.zip.new", StringComparison.OrdinalIgnoreCase);
}
