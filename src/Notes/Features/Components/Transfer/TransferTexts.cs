namespace Notes.Features.Components.Transfer;

/// <summary>What a copy, move, delete or rename is, in the messages about it.</summary>
public enum WhatIsDone
{
    Copy,
    Move,
    Delete,
    Rename
}

/// <summary>
/// The messages of copying, moving, deleting and renaming: why they can't be done, and what went wrong. A site with texts of its own derives
/// from this class and registers it in place of this one, as for <see cref="Toolbar.ToolbarTexts"/>. Entries are named as the user knows them
/// (their virtual paths).
/// </summary>
public class TransferTexts
{
    /// <summary>"copy", "move", "delete", "rename", in the sentences below.</summary>
    protected virtual string Verb(WhatIsDone what) => what.ToString().ToLowerInvariant();

    /// <summary>"copied", "moved", "deleted", "renamed".</summary>
    protected virtual string Done(WhatIsDone what) => what == WhatIsDone.Copy ? "copied" : Verb(what) + "d";

    public virtual string TargetMissing => "The target directory does not exist.";
    public virtual string NoReadAccessToTarget => "No read access to the target directory.";
    public virtual string IntoItself(WhatIsDone what, string name) => $"Cannot {Verb(what)} '{name}' into itself.";
    public virtual string AlreadyHere => "Everything selected is already in this directory.";

    public virtual string ArchivedFolderUnzipFailed
        => "An archived folder in what was selected could not be unzipped, so what is in it couldn't be checked. Nothing was changed.";
    public virtual string SameNameTwice(string name) => $"More than one of the selected entries is called '{name}'.";
    public virtual string NoReadAccess(string display, WhatIsDone what) => $"No read access to '{display}', so it can't be {Done(what)}.";

    public virtual string NoReadAccessInside(string inner, string display, WhatIsDone what)
        => $"No read access to '{inner}', so '{display}' can't be {Done(what)}.";

    public virtual string ServerCantRead(string display, WhatIsDone what)
        => $"Part of '{display}' can't be read by the server, so it can't be {Done(what)}.";

    public virtual string IsALink(string display, WhatIsDone what) => $"'{display}' is a link, which can't be {Done(what)}.";
    public virtual string CantDo(WhatIsDone what, string display) => $"Insufficient permissions to {Verb(what)} '{display}'.";
    public virtual string CantCreate(string display) => $"Insufficient permissions to create '{display}'.";

    // While it is done
    public virtual string CreatedInTheMeantime(string name) => $"'{name}' was created in this directory in the meantime. Nothing was changed.";

    public virtual string DeleteNotUndone
        => "The deletion failed and could not be fully undone. Entries that could not be put back are kept on the server (an admin finds "
            + "where in its log).";

    public virtual string MoveNotUndone
        => "The move failed and could not be fully undone. Nothing was lost: copies of everything are in this directory, and originals that "
            + "could not be put back are kept on the server (an admin finds where in its log).";

    public virtual string FailedAndUndone(WhatIsDone what)
        => $"The {Verb(what)} failed and was undone: the server could not change one of the entries (it may be in use). Retrying in a moment "
            + "may help.";

    // The requests
    public virtual string LoginRequired => "Login required to copy, move or delete.";
    public virtual string AlreadyInProgress => "A copy or move is already in progress.";
    public virtual string SelectionChanged => "The selection was changed in the meantime (in another tab?). Nothing was done.";
    public virtual string NothingSelected => "Nothing is selected.";
    public virtual string FailedUnexpectedly => "The transfer failed unexpectedly.";
}
