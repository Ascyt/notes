using Microsoft.AspNetCore.Mvc;
using Notes.Features.Passwords;

namespace Notes.Features.ZippedFolders;

/// <summary>
/// Asks for the password an encrypted folder needs to be zipped (<see cref="ArchivePasswordNeededException"/>), with the answer of any locked
/// entry (403 and <see cref="PasswordGate.RequiredHeader"/>): the page's scripts open the dialog and send the request again, which then finds
/// the password known (<see cref="ArchivePasswords"/>). The browser's unlock of it is dropped first: unlocking what is unlocked checks
/// nothing, so the app would never learn the password.
/// </summary>
public sealed class ArchivePasswordPrompt(PasswordUnlocks unlocks, PasswordGate gate)
{
    public IActionResult Ask(HttpContext context, Protection protection, string requestedPhysicalPath)
    {
        unlocks.Lock(context, protection);
        return gate.Refusal(context, protection, requestedPhysicalPath);
    }
}
