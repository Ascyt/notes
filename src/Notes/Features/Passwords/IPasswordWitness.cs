namespace Notes.Features.Passwords;

/// <summary>
/// Told whenever a password was found to be the right one for a protection (typed into the dialog, or brought along in the header), for what
/// needs the password itself and not only that it is right: encrypted archives (<see cref="ZippedFolders.ArchivePasswords"/>).
/// </summary>
public interface IPasswordWitness
{
    void Verified(Protection protection, string password);
}
