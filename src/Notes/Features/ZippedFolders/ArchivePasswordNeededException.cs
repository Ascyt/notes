using Notes.Features.Passwords;

namespace Notes.Features.ZippedFolders;

/// <summary>
/// An unzipped encrypted folder can't be zipped (for a move, delete or rename) because the app doesn't know its password: see
/// <see cref="ArchivedFolders.UnknownPasswordToZipAtOrBelow"/>. Answered with the password prompt (<see cref="ArchivePasswordPrompt"/>).
/// </summary>
public sealed class ArchivePasswordNeededException(Protection protection)
    : IOException($"The password of {protection.PhysicalPath} isn't known: it has to be unlocked to zip what it encrypts.")
{
    public Protection Protection { get; } = protection;
}
