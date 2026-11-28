using Microsoft.AspNetCore.Hosting;

namespace Notes.Tests.Common;

/// <summary>
/// The app with bridges (docs/administration/bridges.md): virtual directories kept in physical directories of their own, outside of the notes.
/// <code>
/// /hdd              → <see cref="HddDir"/>
/// /hdd/archive      → <see cref="ArchiveDir"/>   (a bridge inside a bridge)
/// /vault/deep       → <see cref="DeepDir"/>      (a bridge inside a directory alice may not read)
/// </code>
/// The directories the bridges are mounted in must be there when the app starts, so they are made here (and again by the tests' tree).
/// </summary>
public sealed class BridgedNotesAppFactory : NotesAppFactory
{
    public BridgedNotesAppFactory()
    {
        string bridges = NotesDir + "-bridges";
        HddDir = Directory.CreateDirectory(Path.Combine(bridges, "hdd-physical")).FullName;
        ArchiveDir = Directory.CreateDirectory(Path.Combine(bridges, "archive-physical")).FullName;
        DeepDir = Directory.CreateDirectory(Path.Combine(bridges, "deep-physical")).FullName;
        _ = Directory.CreateDirectory(Path.Combine(NotesDir, "vault"));
    }

    public string HddDir { get; }

    public string ArchiveDir { get; }

    public string DeepDir { get; }

    public NotesTree Hdd => new(HddDir, ConfigsChangedOnDisk);

    public NotesTree Archive => new(ArchiveDir, ConfigsChangedOnDisk);

    public NotesTree Deep => new(DeepDir, ConfigsChangedOnDisk);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Config:Bridges:0:Virtual", "/hdd");
        builder.UseSetting("Config:Bridges:0:Physical", HddDir);
        builder.UseSetting("Config:Bridges:1:Virtual", "/hdd/archive");
        builder.UseSetting("Config:Bridges:1:Physical", ArchiveDir);
        // Without a history of its own
        builder.UseSetting("Config:Bridges:1:History:Enabled", "false");
        builder.UseSetting("Config:Bridges:2:Virtual", "/vault/deep");
        builder.UseSetting("Config:Bridges:2:Physical", DeepDir);
    }

    /// <summary>Empties the notes and the physical directories of the bridges (also the app's data in them).</summary>
    public void ResetBridgedNotes()
    {
        ResetNotes();
        foreach (string bridge in new[] { HddDir, ArchiveDir, DeepDir })
        {
            foreach (string directory in Directory.EnumerateDirectories(bridge))
                TestDirectories.Delete(directory);
            foreach (string file in Directory.EnumerateFiles(bridge))
                File.Delete(file);
        }
        ConfigsChangedOnDisk();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try
        {
            TestDirectories.Delete(NotesDir + "-bridges");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A file still open; it is in the temp folder anyway
        }
    }
}
