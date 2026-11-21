using System.Globalization;
using System.Text;

namespace Notes.Features.Helpers;

/// <summary>Which rule a new name breaks (<see cref="NewNames.Check"/>).</summary>
public enum NameProblem
{
    /// <summary>Half of a character (a lone surrogate).</summary>
    InvalidCharacters,

    /// <summary>One that no entry can have (<see cref="PathService.IsValid"/>).</summary>
    NotAName,

    TooLong,

    /// <summary>A character Windows doesn't allow.</summary>
    WindowsCharacters,

    /// <summary>A control character, or one that changes how the text around it is shown.</summary>
    Invisible,

    /// <summary>A name Windows keeps for a device.</summary>
    DeviceName
}

/// <summary>
/// The rules for a name a user gives a new entry (an upload, a rename), on top of <see cref="PathService.IsValid"/>, which every name that is
/// looked up must keep. They are stricter so that a new name means the same on every system the entries may be kept on, can be shown without
/// fooling anyone, and fits on disk. Names that are already there and break them (made outside the app) stay usable.
/// </summary>
public static class NewNames
{
    /// <summary>The longest name, in UTF-8 bytes: what Linux file systems allow (Windows allows 255 UTF-16 units, which is never less).</summary>
    public const int MaxNameBytes = 255;

    /// <summary>
    /// Characters Windows doesn't allow in names, refused everywhere so that the entries can be moved there (or zipped and unpacked there).
    /// </summary>
    public const string WindowsInvalidCharacters = "<>:\"|?*";

    /// <summary>Names Windows keeps for devices, also with any extension ("nul.txt" is the device too).</summary>
    private static readonly HashSet<string> WindowsDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM¹", "COM²", "COM³",
        "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT¹", "LPT²", "LPT³"
    };

    /// <summary>
    /// Checks the name. Returns null if it may be used, with the name as it is to be stored (<paramref name="normalized"/>: in Unicode's composed
    /// form, so that "é" typed on one system is the same name as on another); otherwise which rule it breaks (told by <see cref="NameTexts"/>).
    /// </summary>
    public static NameProblem? Check(string name, out string normalized)
    {
        normalized = "";
        try
        {
            normalized = name.Normalize(NormalizationForm.FormC);
        }
        catch (ArgumentException)
        {
            // Half of a character (a lone surrogate)
            return NameProblem.InvalidCharacters;
        }

        if (!PathService.IsValid(normalized))
            return NameProblem.NotAName;
        if (Encoding.UTF8.GetByteCount(normalized) > MaxNameBytes)
            return NameProblem.TooLong;
        if (normalized.IndexOfAny(WindowsInvalidCharacters.ToCharArray()) >= 0)
            return NameProblem.WindowsCharacters;
        if (normalized.Any(IsInvisibleOrControl))
            return NameProblem.Invisible;
        if (WindowsDeviceNames.Contains(normalized.Split('.')[0].TrimEnd()))
            return NameProblem.DeviceName;

        return null;
    }

    /// <summary>
    /// Control characters (line breaks, tabs, …) and characters that change how text around them is shown without being seen themselves:
    /// marks that reverse the direction of the text ("txt.exe" shown as "exe.txt"), zero-width spaces, line and paragraph separators. The
    /// zero-width joiner is allowed, as emoji are made of it.
    /// </summary>
    private static bool IsInvisibleOrControl(char c)
        => c != '\u200D' && CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.Control or UnicodeCategory.Format
            or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator;
}
