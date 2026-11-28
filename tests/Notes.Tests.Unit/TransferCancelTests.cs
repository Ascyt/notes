using Microsoft.Extensions.Logging.Abstractions;
using Notes.Features.Components.Transfer;

namespace Notes.Tests.Unit;

/// <summary>Cancelling a transfer (from the list of operations) while it copies: nothing changes, and nothing of it is left behind.</summary>
public sealed class TransferCancelTests : IDisposable
{
    private readonly TempNotes _notes = new();

    public void Dispose() => _notes.Dispose();

    [Theory]
    [InlineData(TransferKind.Copy)]
    [InlineData(TransferKind.Move)]
    [InlineData(TransferKind.Delete)]
    public void Cancelled_while_copying_it_changes_nothing(TransferKind kind)
    {
        _ = _notes.Files.File("from/a.md", "a");
        _ = _notes.Files.File("from/b.md", "b");
        _ = _notes.Files.Dir("to");
        TransferExecutor executor = new(_notes.Locations, new TransferTexts(), NullLogger<TransferExecutor>.Instance);
        using CancellationTokenSource cancellation = new();
        List<TransferItem> items =
        [
            new(_notes.P("from/a.md"), "a.md", IsDirectory: false),
            new(_notes.P("from/b.md"), "b.md", IsDirectory: false)
        ];

        // Cancelled once the first file is copied
        Assert.ThrowsAny<OperationCanceledException>(() => executor.Execute(kind, _notes.P("to"), items, done =>
        {
            if (done > 0)
                cancellation.Cancel();
        }, kind == TransferKind.Delete ? new CancellationToken(canceled: true) : cancellation.Token));

        Assert.True(File.Exists(_notes.P("from/a.md")));
        Assert.True(File.Exists(_notes.P("from/b.md")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_notes.P("to")));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(_notes.Root, "_", "temp", "transfer"), "*", SearchOption.AllDirectories));
    }
}
