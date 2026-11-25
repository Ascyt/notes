using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Notes.Features.Bridges;
using Notes.Features.EntryActions;
using Notes.Features.Helpers;

namespace Notes.Features.Passwords;

/// <summary>
/// <c>POST /entry?unlock</c> with <c>{ "password": "…" }</c>: unlocks the outermost protection that locks the entry for the browser (see
/// <see cref="PasswordUnlocks"/>), if the password is its. Another one further in may still lock it: the request that needed it is then
/// answered with that one (<see cref="PasswordGate"/>). Wrong passwords make the next ones wait (<see cref="PasswordAttempts"/>).
/// <c>POST /entry?unlock=renew</c> only keeps what is unlocked unlocked (an edit on the page counts as using it), or says what is locked.
/// </summary>
public sealed class UnlockHandler(PasswordLocks locks, PasswordUnlocks unlocks, PasswordAttempts attempts, PasswordGate gate,
    PasswordTexts texts, EntryTexts entryTexts, IEnumerable<IPasswordWitness> witnesses) : IEntryPostAction
{
    public const string UnlockParameter = "unlock";
    public const string RenewValue = "renew";

    /// <summary>Far more than a password and its JSON need.</summary>
    private const int MaxBodyBytes = 4 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record UnlockRequest(string? Password);

    public string Parameter => UnlockParameter;
    public AccessNeeded Needs => AccessNeeded.Known;
    public bool PassesPasswordLocks => true;

    public async Task<IActionResult> PostAsync(EntryRequest request)
    {
        // (Asking keeps every unlock it finds unlocked a while longer)
        Protection? locked = locks.FirstLocked(request.PhysicalPath);
        if (request.Value == RenewValue)
            return locked is null ? new NoContentResult() : gate.Refusal(request.Context, locked, request.PhysicalPath);
        if (request.Value != "")
            return new BadRequestObjectResult(entryTexts.InvalidRequest);

        if (await ReadBodyAsync(request.Context) is not { Password: { } password })
            return new BadRequestObjectResult(entryTexts.InvalidRequest);
        if (PasswordHashing.Check(password) is not PasswordProblem.None and var problem)
            return new BadRequestObjectResult(texts.NotAPassword(problem));
        if (locked is null)
            return new NoContentResult();

        TimeSpan wait = attempts.WaitBeforeNext(request.Context, locked);
        if (wait > TimeSpan.Zero)
            return gate.TooManyWrong(request.Response, wait);

        if (!PasswordHashing.Verify(password, locked.Hash))
        {
            attempts.Wrong(request.Context, locked);
            // How long the next one has to wait is said right away, so the dialog can count down
            TimeSpan next = attempts.WaitBeforeNext(request.Context, locked);
            if (next > TimeSpan.Zero)
                _ = PasswordGate.SetRetryAfter(request.Response, next);
            return new ObjectResult(texts.Wrong) { StatusCode = StatusCodes.Status403Forbidden };
        }

        attempts.Right(request.Context, locked);
        unlocks.Unlock(request.Context, locked);
        foreach (IPasswordWitness witness in witnesses)
            witness.Verified(locked, password);
        return new NoContentResult();
    }

    /// <summary>The body as JSON, read up to <see cref="MaxBodyBytes"/>; null if it is none such.</summary>
    private static async Task<UnlockRequest?> ReadBodyAsync(HttpContext context)
    {
        // ReadFromJsonAsync throws something other than a JsonException for a body that isn't sent as JSON
        if (!context.Request.HasJsonContentType())
            return null;

        byte[] buffer = new byte[MaxBodyBytes + 1];
        int length = 0;
        int read;
        while (length < buffer.Length && (read = await context.Request.Body.ReadAsync(buffer.AsMemory(length), context.RequestAborted)) > 0)
            length += read;
        if (length > MaxBodyBytes)
            return null;

        try
        {
            return JsonSerializer.Deserialize<UnlockRequest>(buffer.AsSpan(0, length), JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// <c>POST /entry?lock</c>: locks everything the browser has unlocked, at once. If that locks the entry, the answer is
/// <c>{ "location": "…" }</c>, the address of the folder the outermost unlocked password along it is in (the deepest one that can still be
/// opened), where the page goes instead of to the password prompt; otherwise 204.
/// </summary>
/// <remarks>
/// The encrypted archived folders those passwords encrypt are zipped right away (in the background), so that what was unlocked is encrypted
/// again, not only locked.
/// </remarks>
public sealed class LockHandler(PasswordUnlocks unlocks, PasswordLocks locks, EntryLocations locations, ZippedFolders.ArchivedFolders archives)
    : IEntryPostAction
{
    public const string LockParameter = "lock";

    public string Parameter => LockParameter;
    public AccessNeeded Needs => AccessNeeded.Known;
    public bool PassesPasswordLocks => true;

    /// <summary>
    /// <c>POST /entry?lock=entry</c> (the lock button of the toolbar): only the entry's own password, and those unlocked inside it. The page
    /// then goes to the folder the entry is in.
    /// </summary>
    public const string EntryValue = "entry";

    public Task<IActionResult> PostAsync(EntryRequest request)
    {
        if (request.Value is not ("" or EntryValue))
            return Task.FromResult<IActionResult>(new BadRequestResult());

        bool isDirectory = Directory.Exists(request.PhysicalPath);
        List<Protection> unlocked = [.. unlocks.UnlockedBy(request.Context)];
        string? outermostUnlocked;
        if (request.Value == EntryValue)
        {
            // Its own (the entry has it, not a folder above), and what is inside it. (An archived folder's password is that of the folder,
            // while the request has its unzipped copy: compared as the folders they stand for)
            string entry = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.PhysicalPath));
            bool IsOwn(Protection protection) => string.Equals(archives.OuterOf(protection.PhysicalPath), archives.OuterOf(entry),
                StringComparison.OrdinalIgnoreCase);
            if (!unlocked.Any(IsOwn))
                return Task.FromResult<IActionResult>(new BadRequestResult());
            unlocked = [.. unlocked.Where(protection => IsOwn(protection) || locations.IsSameOrInside(protection.PhysicalPath, entry))];
            outermostUnlocked = archives.OuterOf(entry);
            foreach (Protection protection in unlocked)
                unlocks.Lock(request.Context, protection);
        }
        else
        {
            // Asked before locking: afterwards nothing along the path is unlocked any more
            outermostUnlocked = locks.UnlockedAlong(request.PhysicalPath, isDirectory).FirstOrDefault();
            unlocks.LockAll(request.Context);
        }
        archives.ZipEncryptedInBackground([.. unlocked.Select(protection => protection.PhysicalPath)]);

        if (outermostUnlocked is null || locations.ParentOf(outermostUnlocked) is not { } folder)
            return Task.FromResult<IActionResult>(new NoContentResult());
        return Task.FromResult<IActionResult>(new OkObjectResult(new { location = VirtualPaths.ToAddress(locations.ToVirtualPath(folder)) }));
    }
}
