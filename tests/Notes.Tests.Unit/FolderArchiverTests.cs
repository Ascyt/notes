using Notes.Features.Helpers;
using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Notes.Features.Components.Archive;
using Notes.Features.ZippedFolders;

namespace Notes.Tests.Unit;

/// <summary>
/// Zipping and unzipping a folder's archive, and above all that stopping at any point of it (a crash, a power cut) loses nothing: what is on
/// disk at each <see cref="ArchiveStep"/> is copied away, recovered as at the next start, and must still hold all of the folder.
/// </summary>
public sealed class FolderArchiverTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "notes-archiver", Guid.NewGuid().ToString("N"));
    private readonly FolderArchiver _archiver = new(NullLogger<FolderArchiver>.Instance);

    public FolderArchiverTests() => _ = Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            TestDirectories.Delete(_root);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    public static TheoryData<string, string?> Formats => new()
    {
        { "7z", null },
        { "7z", "pässwörd" },
        { "zip", null },
        { "zip", "pässwörd" }
    };

    private static ArchiveOptions Options(string format, string? password)
        => format == "7z"
            ? new ArchiveOptions(ArchiveFormat.SevenZip, ArchiveCompression.Medium, password)
            : new ArchiveOptions(ArchiveFormat.Zip, ArchiveCompression.Medium, password);

    /// <summary>The zipped folder "foo" under a root, as the app lays it out.</summary>
    private static ArchivedFolder FolderUnder(string root, ArchiveFormat format)
        => new(Path.Combine(root, "notes", "foo", ArchivedFolder.ArchiveFileName(format)),
            Path.Combine(root, "notes", "_", "temp", "unzipped", "foo"),
            Path.Combine(root, "notes", "_", "temp", "zipping"));

    private ArchivedFolder Folder(ArchiveOptions options)
    {
        ArchivedFolder folder = FolderUnder(_root, options.Format);
        _ = Directory.CreateDirectory(Path.GetDirectoryName(folder.ArchivePath)!);
        return folder;
    }

    // ---- What a folder holds

    /// <summary>Every file (by its path, with "/") with its text, and every empty directory (with a "/" at the end).</summary>
    private static SortedDictionary<string, string> Read(string directory)
    {
        SortedDictionary<string, string> tree = new(StringComparer.Ordinal);
        foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            tree[Path.GetRelativePath(directory, file).Replace('\\', '/')] = File.ReadAllText(file);
        foreach (string inner in Directory.EnumerateDirectories(directory, "*", SearchOption.AllDirectories))
            if (!Directory.EnumerateFileSystemEntries(inner).Any())
                tree[Path.GetRelativePath(directory, inner).Replace('\\', '/') + "/"] = "";
        return tree;
    }

    private static void Write(string directory, IDictionary<string, string> tree)
    {
        foreach ((string path, string text) in tree)
        {
            string target = Path.Combine(directory, path);
            if (path.EndsWith('/'))
                _ = Directory.CreateDirectory(target);
            else
            {
                _ = Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllText(target, text);
            }
        }
    }

    private static readonly Dictionary<string, string> Original = new()
    {
        ["note.md"] = "# Note\nhello",
        ["empty.md"] = "",
        ["sub/deeper/äöü 🔑.md"] = "unicode",
        ["empty folder/"] = "",
        ["_/history/objects/ab"] = "the folder's own history goes in as well",
        [".hidden"] = "hidden too"
    };

    /// <summary>The original, changed as a user would while it is unzipped: one file changed, one added, one deleted.</summary>
    private static SortedDictionary<string, string> Changed()
    {
        SortedDictionary<string, string> changed = new(Original, StringComparer.Ordinal)
        {
            ["note.md"] = "# Note\nchanged",
            ["sub/new.md"] = "added"
        };
        _ = changed.Remove(".hidden");
        return changed;
    }

    /// <summary>What the archive holds, unzipped into a directory of its own.</summary>
    private async Task<SortedDictionary<string, string>> ContentsOfArchive(ArchivedFolder folder, string? password)
    {
        string elsewhere = Path.Combine(_root, "check", Guid.NewGuid().ToString("N"));
        ArchivedFolder copy = folder with { UnzippedDirectory = Path.Combine(elsewhere, "unzipped"), WorkDirectory = Path.Combine(elsewhere, "work") };
        await new FolderArchiver(NullLogger<FolderArchiver>.Instance).UnzipAsync(copy, password, null, TestContext.Current.CancellationToken);
        return Read(copy.UnzippedDirectory);
    }

    /// <summary>A folder whose archive holds <see cref="Original"/>, and nothing unzipped.</summary>
    private async Task<ArchivedFolder> ZippedOriginal(ArchiveOptions options)
    {
        ArchivedFolder folder = Folder(options);
        Write(folder.UnzippedDirectory, Original);
        await _archiver.ZipAsync(folder, options, TestContext.Current.CancellationToken);
        return folder;
    }

    // ---- Zipping and unzipping

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task What_is_zipped_is_what_is_unzipped_again(string format, string? password)
    {
        ArchiveOptions options = Options(format, password);
        ArchivedFolder folder = await ZippedOriginal(options);

        Assert.True(File.Exists(folder.ArchivePath));
        Assert.False(Directory.Exists(folder.UnzippedDirectory));
        Assert.False(File.Exists(folder.NewArchivePath));

        await _archiver.UnzipAsync(folder, password, null, TestContext.Current.CancellationToken);
        Assert.Equal(new SortedDictionary<string, string>(Original, StringComparer.Ordinal), Read(folder.UnzippedDirectory));
        // Nothing is left behind in the work directory
        Assert.Empty(Directory.EnumerateFiles(folder.WorkDirectory, "*", SearchOption.AllDirectories));
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task Zipping_again_replaces_the_archive_with_what_was_changed(string format, string? password)
    {
        ArchiveOptions options = Options(format, password);
        ArchivedFolder folder = await ZippedOriginal(options);
        await _archiver.UnzipAsync(folder, password, null, TestContext.Current.CancellationToken);
        TestDirectories.Delete(folder.UnzippedDirectory);
        Write(folder.UnzippedDirectory, Changed());

        await _archiver.ZipAsync(folder, options, TestContext.Current.CancellationToken);

        Assert.Equal(Changed(), await ContentsOfArchive(folder, password));
    }

    [Fact]
    public async Task The_times_of_the_files_are_kept()
    {
        ArchiveOptions options = Options("7z", null);
        ArchivedFolder folder = Folder(options);
        Write(folder.UnzippedDirectory, Original);
        DateTime then = new(2020, 5, 17, 10, 30, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(Path.Combine(folder.UnzippedDirectory, "note.md"), then);
        await _archiver.ZipAsync(folder, options, TestContext.Current.CancellationToken);

        await _archiver.UnzipAsync(folder, null, null, TestContext.Current.CancellationToken);

        Assert.Equal(then, File.GetLastWriteTimeUtc(Path.Combine(folder.UnzippedDirectory, "note.md")), TimeSpan.FromSeconds(2));
    }

    [Theory]
    [InlineData("7z")]
    [InlineData("zip")]
    public async Task An_empty_folder_can_be_zipped(string format)
    {
        ArchiveOptions options = Options(format, null);
        ArchivedFolder folder = Folder(options);
        _ = Directory.CreateDirectory(folder.UnzippedDirectory);

        await _archiver.ZipAsync(folder, options, TestContext.Current.CancellationToken);

        Assert.Empty(await ContentsOfArchive(folder, null));
    }

    [Fact]
    public async Task Unzipping_tells_how_far_it_got()
    {
        ArchivedFolder folder = await ZippedOriginal(Options("7z", null));
        List<double> told = [];

        await _archiver.UnzipAsync(folder, null, new SynchronousProgress(told.Add), TestContext.Current.CancellationToken);

        Assert.NotEmpty(told);
        Assert.Equal(1, told[^1]);
        Assert.Equal(told.Order(), told);
    }

    [Theory]
    [InlineData("7z")]
    [InlineData("zip")]
    public async Task Zipping_tells_how_far_it_got_through_writing_and_checking(string format)
    {
        ArchiveOptions options = Options(format, null);
        ArchivedFolder folder = Folder(options);
        Write(folder.UnzippedDirectory, Original);
        List<double> told = [];

        await _archiver.ZipAsync(folder, options, TestContext.Current.CancellationToken, new SynchronousProgress(told.Add));

        Assert.NotEmpty(told);
        Assert.Equal(told.Order(), told);
        // Writing is the first 70 %, reading it back the rest; it never says it is done before the caller does
        Assert.Contains(told, value => value < 0.7);
        Assert.Contains(told, value => value > 0.7);
        Assert.True(told[^1] < 1);
    }

    [Theory]
    [InlineData("7z")]
    [InlineData("zip")]
    public async Task Stopping_zipping_before_the_archive_is_replaced_leaves_everything_as_it_was(string format)
    {
        ArchiveOptions options = Options(format, null);
        ArchivedFolder folder = await ZippedOriginal(options);
        await _archiver.UnzipAsync(folder, null, null, TestContext.Current.CancellationToken);
        File.WriteAllText(Path.Combine(folder.UnzippedDirectory, "note.md"), "changed");
        // As when the folder is opened while it is zipped: stopped once the new archive is written, before it is checked
        using CancellationTokenSource stop = new();
        _archiver.StepReached = step =>
        {
            if (step == ArchiveStep.ArchiveWritten)
                stop.Cancel();
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _archiver.ZipAsync(folder, options, stop.Token));

        Assert.False(File.Exists(folder.NewArchivePath));
        Assert.Equal("changed", File.ReadAllText(Path.Combine(folder.UnzippedDirectory, "note.md")));
        _archiver.StepReached = null;
        TestDirectories.Delete(folder.UnzippedDirectory);
        Assert.Equal(new SortedDictionary<string, string>(Original, StringComparer.Ordinal), await ContentsOfArchive(folder, null));
    }

    [Theory]
    [InlineData("7z", null)]
    [InlineData("zip", "secret")]
    public async Task Zipping_and_unzipping_say_what_they_are_doing_step_by_step(string format, string? password)
    {
        ArchiveOptions options = Options(format, password);
        ArchivedFolder folder = Folder(options);
        Write(folder.UnzippedDirectory, Original);
        List<Notes.Features.Helpers.WorkStatus> zipping = [];

        await _archiver.ZipAsync(folder, options, TestContext.Current.CancellationToken, status: zipping.Add);

        string writing = password is null ? WorkPhases.Writing : WorkPhases.Encrypting;
        string deleting = password is null ? WorkPhases.Deleting : WorkPhases.Overwriting;
        Assert.Equal([WorkPhases.Measuring, writing, WorkPhases.Checking, WorkPhases.Replacing, deleting],
            zipping.Select(status => status.Phase).Distinct());
        // Each counts up to all of the files (and writing to all of the bytes); the empty folder is no file
        int files = Original.Keys.Count(key => !key.EndsWith('/'));
        Assert.All(zipping.Where(status => status.Phase != WorkPhases.Measuring && status.Phase != WorkPhases.Replacing),
            status => Assert.Equal(files, status.TotalFiles));
        Assert.Equal(files, zipping.Last(status => status.Phase == WorkPhases.Checking).Files);
        Assert.Equal(files, zipping.Last(status => status.Phase == deleting).Files);
        Notes.Features.Helpers.WorkStatus written = zipping.Last(status => status.Phase == writing);
        Assert.Equal(written.TotalBytes, written.Bytes);
        Assert.True(written.TotalBytes > 0);

        List<Notes.Features.Helpers.WorkStatus> unzipping = [];
        await _archiver.UnzipAsync(folder, password, null, TestContext.Current.CancellationToken, unzipping.Add);

        Assert.Equal([WorkPhases.Unzipping, WorkPhases.Placing], unzipping.Select(status => status.Phase).Distinct());
        Notes.Features.Helpers.WorkStatus unzipped = unzipping.Last(status => status.Phase == WorkPhases.Unzipping);
        Assert.Equal(unzipped.TotalFiles, unzipped.Files);
        Assert.True(unzipped.TotalFiles >= files);
    }

    private sealed class SynchronousProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }

    [Theory]
    [InlineData("7z")]
    [InlineData("zip")]
    public async Task A_new_archive_that_doesnt_hold_what_was_zipped_replaces_nothing(string format)
    {
        ArchiveOptions options = Options(format, null);
        ArchivedFolder folder = await ZippedOriginal(options);
        await _archiver.UnzipAsync(folder, null, null, TestContext.Current.CancellationToken);
        File.WriteAllText(Path.Combine(folder.UnzippedDirectory, "note.md"), "changed");
        // As a disk could damage it: once written, part of it is overwritten
        _archiver.StepReached = step =>
        {
            if (step != ArchiveStep.ArchiveWritten)
                return;
            using FileStream archive = new(folder.NewArchivePath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
            archive.Position = archive.Length / 3;
            archive.Write(new byte[64]);
        };

        await Assert.ThrowsAnyAsync<IOException>(() => _archiver.ZipAsync(folder, options, TestContext.Current.CancellationToken));

        // The old archive and the copy (with the change) are as they were
        Assert.False(File.Exists(folder.NewArchivePath));
        Assert.Equal("changed", File.ReadAllText(Path.Combine(folder.UnzippedDirectory, "note.md")));
        _archiver.StepReached = null;
        TestDirectories.Delete(folder.UnzippedDirectory);
        Assert.Equal(new SortedDictionary<string, string>(Original, StringComparer.Ordinal), await ContentsOfArchive(folder, null));
    }

    // ---- What can't be unzipped

    [Theory]
    [InlineData("7z")]
    [InlineData("zip")]
    public async Task A_wrong_password_unzips_nothing(string format)
    {
        ArchivedFolder folder = await ZippedOriginal(Options(format, "right"));

        await Assert.ThrowsAsync<ArchiveUnreadableException>(
            () => _archiver.UnzipAsync(folder, "wrong", null, TestContext.Current.CancellationToken));

        Assert.False(Directory.Exists(folder.UnzippedDirectory));
        Assert.Empty(Directory.EnumerateFiles(folder.WorkDirectory, "*", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("7z")]
    [InlineData("zip")]
    public async Task A_damaged_archive_unzips_nothing(string format)
    {
        ArchivedFolder folder = await ZippedOriginal(Options(format, null));
        byte[] bytes = File.ReadAllBytes(folder.ArchivePath);
        File.WriteAllBytes(folder.ArchivePath, bytes[..(bytes.Length / 2)]);

        await Assert.ThrowsAnyAsync<IOException>(() => _archiver.UnzipAsync(folder, null, null, TestContext.Current.CancellationToken));

        Assert.False(Directory.Exists(folder.UnzippedDirectory));
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("sub/../../outside.txt")]
    public async Task An_entry_that_would_lead_out_of_the_folder_unzips_nothing(string entryName)
    {
        ArchivedFolder folder = Folder(Options("zip", null));
        using (ZipArchive zip = ZipFile.Open(folder.ArchivePath, ZipArchiveMode.Create))
        {
            using (StreamWriter writer = new(zip.CreateEntry("fine.txt").Open()))
                writer.Write("fine");
            using (StreamWriter writer = new(zip.CreateEntry(entryName).Open()))
                writer.Write("evil");
        }

        await Assert.ThrowsAnyAsync<IOException>(() => _archiver.UnzipAsync(folder, null, null, TestContext.Current.CancellationToken));

        Assert.False(Directory.Exists(folder.UnzippedDirectory));
        Assert.Empty(Directory.EnumerateFiles(_root, "outside.txt", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task An_archive_with_an_entry_twice_unzips_nothing()
    {
        ArchivedFolder folder = Folder(Options("zip", null));
        using (ZipArchive zip = ZipFile.Open(folder.ArchivePath, ZipArchiveMode.Create))
            foreach (string text in new[] { "first", "second" })
                using (StreamWriter writer = new(zip.CreateEntry("same.txt").Open()))
                    writer.Write(text);

        await Assert.ThrowsAnyAsync<IOException>(() => _archiver.UnzipAsync(folder, null, null, TestContext.Current.CancellationToken));

        Assert.False(Directory.Exists(folder.UnzippedDirectory));
    }

    // ---- Stopping at any point

    public static TheoryData<ArchiveStep, string> ZippingSteps => StepsWithFormats(
        ArchiveStep.WritingArchive, ArchiveStep.ArchiveWritten, ArchiveStep.ArchiveChecked, ArchiveStep.ArchiveReplaced,
        ArchiveStep.UnzippedMovedAway, ArchiveStep.DeletingUnzipped);

    public static TheoryData<ArchiveStep, string> UnzippingSteps => StepsWithFormats(
        ArchiveStep.Unzipping, ArchiveStep.Unzipped, ArchiveStep.UnzippedInPlace);

    private static TheoryData<ArchiveStep, string> StepsWithFormats(params ArchiveStep[] steps)
    {
        TheoryData<ArchiveStep, string> data = [];
        foreach (ArchiveStep step in steps)
            foreach (string format in new[] { "7z", "zip" })
                data.Add(step, format);
        return data;
    }

    [Theory]
    [MemberData(nameof(ZippingSteps))]
    public async Task Stopping_while_zipping_loses_nothing(ArchiveStep step, string format)
    {
        ArchiveOptions options = Options(format, "secret");
        ArchivedFolder folder = await ZippedOriginal(options);
        await _archiver.UnzipAsync(folder, "secret", null, TestContext.Current.CancellationToken);
        TestDirectories.Delete(folder.UnzippedDirectory);
        Write(folder.UnzippedDirectory, Changed());

        string snapshot = await SnapshotAt(step, () => _archiver.ZipAsync(folder, options, TestContext.Current.CancellationToken));

        Assert.Equal(Changed(), await RecoveredContents(snapshot, options, "secret"));
    }

    [Theory]
    [MemberData(nameof(UnzippingSteps))]
    public async Task Stopping_while_unzipping_loses_nothing(ArchiveStep step, string format)
    {
        ArchiveOptions options = Options(format, "secret");
        ArchivedFolder folder = await ZippedOriginal(options);

        string snapshot = await SnapshotAt(step, () => _archiver.UnzipAsync(folder, "secret", null, TestContext.Current.CancellationToken));

        Assert.Equal(new SortedDictionary<string, string>(Original, StringComparer.Ordinal), await RecoveredContents(snapshot, options, "secret"));
    }

    [Fact]
    public async Task Every_step_is_reached()
    {
        ArchiveOptions options = Options("7z", null);
        ArchivedFolder folder = await ZippedOriginal(options);
        List<ArchiveStep> reached = [];
        _archiver.StepReached = reached.Add;

        await _archiver.UnzipAsync(folder, null, null, TestContext.Current.CancellationToken);
        await _archiver.ZipAsync(folder, options, TestContext.Current.CancellationToken);

        Assert.Equal(Enum.GetValues<ArchiveStep>().Order(), reached.Order());
    }

    /// <summary>
    /// Runs <paramref name="work"/>, copying what is on disk under the root as it is when <paramref name="step"/> is reached (as a power cut
    /// would leave it), and returns where the copy is. The work itself goes on to the end.
    /// </summary>
    private async Task<string> SnapshotAt(ArchiveStep step, Func<Task> work)
    {
        string snapshot = Path.Combine(Path.GetTempPath(), "notes-archiver", Guid.NewGuid().ToString("N"));
        bool taken = false;
        _archiver.StepReached = reached =>
        {
            if (reached != step)
                return;
            CopyTree(Path.Combine(_root, "notes"), Path.Combine(snapshot, "notes"));
            taken = true;
        };
        await work();
        _archiver.StepReached = null;
        Assert.True(taken, $"{step} was reached");
        return snapshot;
    }

    /// <summary>
    /// Recovers the copy as the next start would, and returns what the folder holds then: the unzipped copy if there is one, otherwise what
    /// the archive holds. Whatever is left, the archive must be one that can be read, and the work directory empty.
    /// </summary>
    private async Task<SortedDictionary<string, string>> RecoveredContents(string snapshot, ArchiveOptions options, string? password)
    {
        try
        {
            ArchivedFolder folder = FolderUnder(snapshot, options.Format);
            FolderArchiver archiver = new(NullLogger<FolderArchiver>.Instance);
            ArchivedFolderState state = archiver.Recover(folder);
            archiver.DeleteLeftovers(archiver.FindLeftovers(folder.WorkDirectory), progress: null, status: null, CancellationToken.None);

            Assert.False(File.Exists(folder.NewArchivePath));
            Assert.Empty(Directory.EnumerateFiles(folder.WorkDirectory, "*", SearchOption.AllDirectories));
            Assert.NotEqual(ArchivedFolderState.Missing, state);
            if (File.Exists(folder.ArchivePath))
                _ = await ContentsOfArchive(folder, password);

            return state == ArchivedFolderState.Unzipped ? Read(folder.UnzippedDirectory) : await ContentsOfArchive(folder, password);
        }
        finally
        {
            TestDirectories.Delete(snapshot);
        }
    }

    /// <summary>Copies everything, also a file that is being written at the moment (as far as it is written).</summary>
    private static void CopyTree(string from, string to)
    {
        _ = Directory.CreateDirectory(to);
        foreach (string directory in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
            _ = Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, directory)));
        foreach (string file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            using FileStream source = new(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using FileStream target = File.Create(Path.Combine(to, Path.GetRelativePath(from, file)));
            source.CopyTo(target);
        }
    }
}
