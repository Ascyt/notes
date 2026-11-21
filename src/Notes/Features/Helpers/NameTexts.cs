namespace Notes.Features.Helpers;

/// <summary>
/// Why a name, or a path, can't be that of a new entry (<see cref="NewNames"/>, <see cref="LimitsConfig.MaxPathBytes"/>, the addresses of the
/// app). A site with texts of its own derives from this class and registers it in place of this one, as for
/// <see cref="Components.Toolbar.ToolbarTexts"/>.
/// </summary>
public class NameTexts
{
    public virtual string Problem(NameProblem problem) => problem switch
    {
        NameProblem.InvalidCharacters => "The name contains characters that are not valid.",
        NameProblem.NotAName => "This name can't be used: it must not be empty, begin or end with a space, end with a dot, or contain / or \\, "
            + "and some names are reserved.",
        NameProblem.TooLong => $"The name is too long (at most {NewNames.MaxNameBytes} bytes; most characters take one, others up to four).",
        NameProblem.WindowsCharacters => $"Names can't contain any of {NewNames.WindowsInvalidCharacters}.",
        NameProblem.Invisible => "Names can't contain control characters or invisible formatting characters (such as those that reverse the "
            + "direction of the text).",
        NameProblem.DeviceName => "This name is reserved for devices on Windows, also with an extension.",
        _ => "This name can't be used."
    };

    /// <summary>Naming the entry as the user knows it (its virtual path).</summary>
    public virtual string PathTooLong(string display, int maxPathBytes)
        => $"'{display}' would have too long a path (at most {maxPathBytes} bytes, from the root). A shorter name, or a place nearer the root, "
            + "is needed.";

    /// <summary>A name at the root that an address of the app takes (<see cref="Addresses.AppAddresses"/>).</summary>
    public virtual string TakenByTheApp(string name)
        => $"'{name}' is the address of a page of the app, so nothing at the root can have this name.";
}
