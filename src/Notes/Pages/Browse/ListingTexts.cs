namespace Notes.Pages.Browse;

/// <summary>
/// The texts of the listing of a directory (<see cref="DirectoryView"/>) and its parts: the groups of directories and files
/// (<see cref="EntryTable._EntryGroup"/>) and the grid of media (<see cref="MediaGrid._MediaGroup"/>). A site with texts of its own derives
/// from this class and registers it in place of this one, as for <see cref="Features.Components.Toolbar.ToolbarTexts"/>.
/// </summary>
public class ListingTexts
{
    /// <summary>In place of the listing, for a directory with nothing in it (and nothing to add).</summary>
    public virtual string Empty => "Empty";

    public virtual string Directories => "Directories";
    public virtual string Files => "Files";
    public virtual string Media => "Media";

    public virtual string SelectAllDirectories => "Select all directories";
    public virtual string SelectAllFiles => "Select all files";
    public virtual string SelectAllMedia => "Select all media";

    /// <summary>The box of an entry, read out.</summary>
    public virtual string Select(string name) => $"Select {name}";

    /// <summary>The box of an entry that can't be opened, which is there only to keep the names in line with the others.</summary>
    public virtual string CantBeSelected(string name) => $"{name}: can't be opened or selected";

    /// <summary>Why an entry has a lock: the user may not read it.</summary>
    public virtual string NoReadAccess(bool isDirectory)
        => isDirectory ? "No read access to this directory. Its contents are hidden." : "No read access to this file.";

    /// <summary>Why an entry of an earlier state can't be opened in it, and whether it is still there now (where it then leads).</summary>
    public virtual string CantBeSeenAsItWas(bool isGoneNow)
        => isGoneNow
            ? "Insufficient permissions to see this entry as it was. It is not there any more."
            : "Insufficient permissions to see this entry as it was.";

    /// <summary>The root, for someone logged in who may not read it.</summary>
    public virtual string RootUnreadable => "No read access to this directory.";

    /// <summary>The root, for someone who is not logged in, with <c>{login}</c> where the link to the login goes (<see cref="LoginLink"/>).</summary>
    public virtual string RootNeedsLogin => "This directory can only be browsed after {login}.";

    public virtual string LoginLink => "logging in";

    /// <summary>The mark on the picture of a video, which tells it from an image.</summary>
    public virtual string Video => "Video";
}
