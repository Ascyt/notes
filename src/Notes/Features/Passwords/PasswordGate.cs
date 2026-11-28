using Microsoft.AspNetCore.Mvc;
using Notes.Features.Addresses;
using Notes.Features.Helpers;

namespace Notes.Features.Passwords;

/// <summary>
/// What a request for a locked entry is answered with: 403, with the address of the entry whose password is needed in
/// <see cref="RequiredHeader"/>, which the scripts of the pages see (<c>_PasswordModal.ts</c>) and ask for it, then send the request again.
/// A request that brought a password along that couldn't be checked yet (too many wrong ones) gets 429, with how long to wait in
/// <c>Retry-After</c>.
/// </summary>
/// <remarks>
/// The entry with the password may be a directory above those the site shows (<see cref="ExposedFolders"/>), which is no part of the site:
/// then the directory the site shows is named in its place. Unlocking there unlocks the outermost password above it, which is that one.
/// </remarks>
public sealed class PasswordGate(PasswordLocks locks, IPathService paths, ExposedFolders exposed, PasswordTexts texts)
{
    public const string RequiredHeader = "X-Notes-Password-Required";

    /// <summary>The virtual path of the entry that is named for the protection, for a request of the entry at the physical path.</summary>
    public string Display(Protection protection, string requestedPhysicalPath)
    {
        string virtualPath = VirtualOf(protection.PhysicalPath);
        return exposed.IsExposed(virtualPath) ? virtualPath : exposed.RootOf(VirtualOf(requestedPhysicalPath));
    }

    /// <summary>The virtual path, "" for the root (which <see cref="IPathService.ToVirtualPath"/> gives as ".").</summary>
    private string VirtualOf(string physicalPath)
        => paths.ToVirtualPath(physicalPath) is var virtualPath && virtualPath == "." ? "" : virtualPath;

    /// <summary>The address of what <see cref="Display"/> names (escaped, so it can be a header), where it is unlocked.</summary>
    public string AddressOf(Protection protection, string requestedPhysicalPath)
        => VirtualPaths.ToAddress(Display(protection, requestedPhysicalPath));

    /// <summary>
    /// Sets what tells the scripts that the password is needed (or that it has to wait), and returns the status it is answered with. For the
    /// page of the entry, which answers with the page that asks for it.
    /// </summary>
    public int MarkRequired(HttpContext context, Protection protection, string requestedPhysicalPath)
    {
        if (locks.WaitForHeaderPassword(context) is { } wait)
        {
            _ = SetRetryAfter(context.Response, wait);
            return StatusCodes.Status429TooManyRequests;
        }

        context.Response.Headers[RequiredHeader] = AddressOf(protection, requestedPhysicalPath);
        return StatusCodes.Status403Forbidden;
    }

    /// <summary>The answer to a request (anything but the page of the entry) that the protection locks.</summary>
    public IActionResult Refusal(HttpContext context, Protection protection, string requestedPhysicalPath)
    {
        int status = MarkRequired(context, protection, requestedPhysicalPath);
        return status == StatusCodes.Status429TooManyRequests
            ? TooManyWrong(context.Response, locks.WaitForHeaderPassword(context)!.Value)
            : new ObjectResult(texts.Locked(Display(protection, requestedPhysicalPath))) { StatusCode = status };
    }

    /// <summary>The answer while the requester has to wait before the next password is checked.</summary>
    public IActionResult TooManyWrong(HttpResponse response, TimeSpan wait)
    {
        int seconds = SetRetryAfter(response, wait);
        return new ObjectResult(texts.WaitSeconds(seconds)) { StatusCode = StatusCodes.Status429TooManyRequests };
    }

    /// <returns>The whole seconds it says.</returns>
    public static int SetRetryAfter(HttpResponse response, TimeSpan wait)
    {
        int seconds = Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds));
        response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return seconds;
    }
}
