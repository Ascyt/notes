namespace Notes.Features.Components.Assets;

/// <summary>
/// A text that only the server shows (to some users only, say admins), which <c>&lt;script-texts&gt;</c> leaves out: whatever is in that
/// block is on the page for everyone who sees it.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class NotForScriptsAttribute : Attribute;
