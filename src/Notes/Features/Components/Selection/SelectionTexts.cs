namespace Notes.Features.Components.Selection;

/// <summary>
/// The texts of the selection: the card at the bottom of a listing and the confirmation of a deletion (<see cref="_SelectionBar"/>), and
/// what its script says. A site with texts of its own derives from this class and registers it in place of this one, as for
/// <see cref="Toolbar.ToolbarTexts"/>. The script's texts are properties, with what changes in braces (<c>{count}</c>); counts have one
/// text for one and one for more.
/// </summary>
public class SelectionTexts
{
    public virtual string CopyHere => "Copy here";
    public virtual string MoveHere => "Move here";
    public virtual string Delete => "Delete";
    public virtual string Download => "Download";
    public virtual string Working => "Working";
    public virtual string Progress => "Progress";
    public virtual string ClearSelection => "Clear selection";
    public virtual string ConfirmDeletion => "Confirm deletion";
    public virtual string Close => "Close";
    public virtual string Cancel => "Cancel";
    public virtual string DeleteSelected => "Delete selected";

    /// <summary>The confirmation, with <c>{what}</c> where the script puts what is selected (<see cref="DirectoriesAndFiles"/>).</summary>
    public virtual string WillBeDeleted => "{what} will be deleted.";

    /// <summary>The server's, where the selection can't be changed while it is copied or moved.</summary>
    public virtual string TransferInProgress => "A copy or move is in progress.";

    // The script's

    public virtual string OneSelected => "1 element selected";
    public virtual string ManySelected => "{count} elements selected";
    public virtual string Busy => "A copy, move or delete of the selection is in progress.";
    public virtual string NothingToSelect => "There is nothing to select.";
    public virtual string CantDeleteOrMove => "Insufficient permissions to delete or move some of the selected entries.";

    public virtual string OneElement => "1 element";
    public virtual string ManyElements => "{count} elements";
    public virtual string CopiedHere => "Copied {elements} here{except}.";
    public virtual string MovedHere => "Moved {elements} here{except}.";
    public virtual string Deleted => "Deleted {elements}{except}.";

    /// <summary>Added to what was done where entries the user may not read were left out of it.</summary>
    public virtual string ExceptUnreadable => ", except entries without read access";

    /// <summary>Where the server says nothing about why.</summary>
    public virtual string Failed => "Failed ({status}).";

    public virtual string CopyConnectionLost => "The connection was lost. The copy may still be running on the server or may have finished; the selection shows which.";
    public virtual string MoveConnectionLost => "The connection was lost. The move may still be running on the server or may have finished; the selection shows which.";
    public virtual string DeleteConnectionLost => "The connection was lost. The deletion may still be running on the server or may have finished; the selection shows which.";

    // What a deletion is of, in its confirmation: "2 directories and 1 file", ", including everything inside the selected directories,"
    public virtual string OneDirectory => "1 directory";
    public virtual string ManyDirectories => "{count} directories";
    public virtual string OneFile => "1 file";
    public virtual string ManyFiles => "{count} files";
    public virtual string DirectoriesAndFiles => "{directories} and {files}";
    public virtual string InsideOneDirectory => ", including everything inside the selected directory,";
    public virtual string InsideManyDirectories => ", including everything inside the selected directories,";
}
