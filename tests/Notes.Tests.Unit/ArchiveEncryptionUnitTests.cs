using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Notes.Features.Components.Archive;
using Notes.Features.Configuration;
using Notes.Features.ZippedFolders;

namespace Notes.Tests.Unit;

/// <summary>What encrypted archives rely on below the app: overwriting plaintext, zipping with a password, and the configuration's rules.</summary>
public sealed class ArchiveEncryptionUnitTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "notes-encryption", Guid.NewGuid().ToString("N"));

    public ArchiveEncryptionUnitTests() => _ = Directory.CreateDirectory(_root);

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

    [Fact]
    public void Overwriting_replaces_every_byte_and_keeps_the_length()
    {
        string file = Path.Combine(_root, "secret.md");
        byte[] text = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("the secret text ", 100_000)));
        File.WriteAllBytes(file, text);
        File.SetAttributes(file, FileAttributes.ReadOnly);

        Shredding.Overwrite(file, passes: 2);

        byte[] after = File.ReadAllBytes(file);
        Assert.Equal(text.Length, after.Length);
        Assert.DoesNotContain("the secret text", Encoding.UTF8.GetString(after));
    }

    [Fact]
    public async Task An_encrypted_zipping_takes_the_copy_away_and_needs_the_password_to_unzip()
    {
        ArchivedFolder folder = new(Path.Combine(_root, "notes", "foo", "_.7z"), Path.Combine(_root, "notes", "_", "temp", "unzipped", "foo"),
            Path.Combine(_root, "notes", "_", "temp", "zipping"));
        _ = Directory.CreateDirectory(Path.GetDirectoryName(folder.ArchivePath)!);
        _ = Directory.CreateDirectory(folder.UnzippedDirectory);
        await File.WriteAllTextAsync(Path.Combine(folder.UnzippedDirectory, "note.md"), "secret", TestContext.Current.CancellationToken);
        FolderArchiver archiver = new(NullLogger<FolderArchiver>.Instance, new ArchivesConfig { OverwritePasses = 1 });

        await archiver.ZipAsync(folder, new ArchiveOptions(ArchiveFormat.SevenZip, ArchiveCompression.Medium, "pässwörd"),
            TestContext.Current.CancellationToken);

        Assert.False(Directory.Exists(folder.UnzippedDirectory));
        Assert.Empty(Directory.EnumerateFiles(folder.WorkDirectory, "*", SearchOption.AllDirectories));
        await Assert.ThrowsAnyAsync<IOException>(() => archiver.UnzipAsync(folder, password: null, null, TestContext.Current.CancellationToken));
        await archiver.UnzipAsync(folder, "pässwörd", null, TestContext.Current.CancellationToken);
        Assert.Equal("secret", await File.ReadAllTextAsync(Path.Combine(folder.UnzippedDirectory, "note.md"), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("""{ "perms": { "inherit": true }, "gitignore": false, "archive": { "enabled": true, "encrypted": true }, "files": [] }""", false)]
    [InlineData("""{ "perms": { "inherit": true }, "gitignore": false, "password": "typed", "archive": { "enabled": true, "encrypted": true }, "files": [] }""", true)]
    [InlineData("""{ "perms": { "inherit": true }, "gitignore": false, "password": null, "archive": { "enabled": true, "encrypted": true }, "files": [] }""", false)]
    public void Encryption_needs_a_password_of_the_folder(string text, bool isValid)
    {
        ParseResult<FolderConfig> parsed = ConfigParser.ParseFolder(text, existingFiles: null, allowsPlainPasswords: true);

        Assert.Equal(isValid, parsed.IsValid);
        if (!isValid)
            Assert.Contains(parsed.Errors, error => error.StartsWith("archive.encrypted", StringComparison.Ordinal));
    }
}
