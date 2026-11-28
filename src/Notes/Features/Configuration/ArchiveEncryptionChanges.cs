using Notes.Features.Passwords;
using Notes.Features.ZippedFolders;

namespace Notes.Features.Configuration;

/// <summary>What saving a folder's configuration does about encrypted archives (see <see cref="ArchiveEncryptionChanges"/>).</summary>
/// <param name="Refusal">Why it can't be saved (with <paramref name="Status"/>), or null.</param>
/// <param name="KeptHash">The hash that the typed <c>password</c> is saved as (the one it had: encryption off isn't a new password).</param>
/// <param name="NewPassword">The password the archives are encrypted with from now on, kept in memory once it is saved.</param>
/// <param name="Reencrypt">Whether the archives are zipped again, encrypted, right away.</param>
/// <param name="ForgetPassword">Whether the password is forgotten once it is saved (nothing needs it any more).</param>
public sealed record EncryptionChange(string? Refusal = null, int Status = 0, string? KeptHash = null, string? NewPassword = null,
    bool Reencrypt = false, bool ForgetPassword = false)
{
    public static EncryptionChange None { get; } = new();
}

/// <summary>
/// Encryption of archives, when a folder's configuration is saved (<c>"archive": { "encrypted": true }</c>; for an <c>onLowestLevel</c> folder,
/// of the folders it archives, with its password):
/// <list type="bullet">
/// <item>On: the password has to be known, typed into the configuration or known since the folder was unlocked. The archives are zipped
/// again, encrypted, right away.</item>
/// <item>Off (or archiving off): the password has to be typed into the configuration, and must be the folder's. The encrypted archives are
/// unzipped with it, and it is saved as the hash it had.</item>
/// <item>A new password (typed) while it is on: the archives encrypted with the old one (known: changing it needs the folder unlocked) are
/// unzipped first, and zipped with the new one from then on.</item>
/// </list>
/// </summary>
public sealed class ArchiveEncryptionChanges(ArchivedFolders archives, ArchivePasswords passwords, ConfigTexts texts)
{
    public async Task<EncryptionChange> BeforeSaveAsync(string folderPath, FolderConfig? before, FolderConfig sent, string sentText)
    {
        bool was = before?.OwnArchive is { Enabled: true, Encrypted: true };
        bool will = sent.OwnArchive is { Enabled: true, Encrypted: true };
        string? typed = ConfigText.FolderPassword(sentText);

        if (was && !will)
        {
            // Proving it is known is what turning it off asks: whoever only manages the configuration must not get around it
            if (typed is null || before!.PasswordHash is not { } hash)
                return Refused(StatusCodes.Status400BadRequest, texts.EncryptionOffNeedsPassword);
            if (!PasswordHashing.Verify(typed, hash))
                return Refused(StatusCodes.Status400BadRequest, texts.EncryptionOffWrongPassword);
            passwords.Remember(folderPath, typed, hash);
            if (!await archives.UnzipEncryptedAsync(folderPath))
                return Refused(StatusCodes.Status409Conflict, texts.CantUnzipEncryptedNow);
            return new EncryptionChange(KeptHash: hash, ForgetPassword: true);
        }

        if (!was && will)
        {
            string? password = typed ?? passwords.PasswordOf(folderPath);
            if (password is null)
                return Refused(StatusCodes.Status400BadRequest, texts.EncryptionOnNeedsPassword);
            return new EncryptionChange(NewPassword: password, Reencrypt: true);
        }

        if (was && will && typed is not null)
        {
            // The archives can only be opened with the old password: unzipped with it now, zipped with the new one later
            if (archives.NeedsUnknownPassword(folderPath))
                return Refused(StatusCodes.Status409Conflict, texts.UnlockBeforePasswordChange);
            if (!await archives.UnzipEncryptedAsync(folderPath))
                return Refused(StatusCodes.Status409Conflict, texts.CantUnzipEncryptedNow);
            return new EncryptionChange(NewPassword: typed);
        }

        return EncryptionChange.None;
    }

    /// <summary>Once the configuration is saved (with <paramref name="savedHash"/>, the folder's password hash as it is now).</summary>
    public void AfterSave(string folderPath, EncryptionChange change, string? savedHash)
    {
        if (change.NewPassword is { } password && savedHash is not null)
            passwords.Remember(folderPath, password, savedHash);
        if (change.Reencrypt)
            archives.ReencryptInBackground(folderPath);
        if (change.ForgetPassword)
            passwords.Forget(folderPath);
    }

    private static EncryptionChange Refused(int status, string why) => new(Refusal: why, Status: status);
}
