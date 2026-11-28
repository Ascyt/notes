using System.Collections.Concurrent;
using System.Security.Claims;
using Notes.Features.Auth;
using Notes.Features.Helpers;

namespace Notes.Features.Operations;

/// <summary>What a <see cref="StorageOperation"/> is, for its title.</summary>
public enum OperationKind
{
    /// <summary>An archived folder being zipped.</summary>
    Zipping,

    /// <summary>An archived folder being unzipped.</summary>
    Unzipping,

    /// <summary>An archive being made to be downloaded.</summary>
    PreparingDownload,

    /// <summary>An uploaded archive being unzipped into a folder.</summary>
    UnzippingUpload,

    Copying,
    Moving,
    Deleting
}

/// <summary>
/// Work on the storage that is running now (for the list of <see cref="OperationsHandler"/>): what it is, where (a physical path, whose readers
/// may see it), who started it (null: the app itself), how far it got and what it is doing, and how to stop it.
/// </summary>
public sealed class StorageOperation
{
    private readonly Func<double> _progress;
    private readonly Func<WorkStatus?> _status;
    private readonly Action _cancel;
    private int _cancelled;

    internal StorageOperation(OperationKind kind, string physicalPath, string virtualPath, ClaimsPrincipal? user, DateTimeOffset started,
        Func<double> progress, Func<WorkStatus?> status, Action cancel)
    {
        Kind = kind;
        PhysicalPath = physicalPath;
        VirtualPath = virtualPath;
        IsOfFile = File.Exists(physicalPath);
        UserId = user?.FindFirst(UserClaims.UserId)?.Value;
        UserName = user?.FindFirst(UserClaims.UserName)?.Value ?? UserId;
        Started = started;
        _progress = progress;
        _status = status;
        _cancel = cancel;
    }

    public Guid Id { get; } = Guid.NewGuid();
    public OperationKind Kind { get; }
    public string PhysicalPath { get; }

    /// <summary>
    /// Where it is, as the user sees it, from when it began: what it works on may be somewhere else on disk meanwhile (an archived folder's
    /// unzipped copy is no longer mounted while it is zipped), its address isn't.
    /// </summary>
    public string VirtualPath { get; }

    /// <summary>Whether it is of a file (an uploaded archive), not a directory.</summary>
    public bool IsOfFile { get; }

    /// <summary>Who started it; null if the app did (zipping idle folders, going on after a restart).</summary>
    public string? UserId { get; }

    public string? UserName { get; }
    public DateTimeOffset Started { get; }
    public double Progress => _progress();
    public WorkStatus? Status => _status();

    /// <summary>Whether it was told to stop (it may take a moment, or be past the point where it can).</summary>
    public bool IsCancelled => Volatile.Read(ref _cancelled) != 0;

    public void Cancel()
    {
        if (Interlocked.Exchange(ref _cancelled, 1) == 0)
            _cancel();
    }
}

/// <summary>
/// The <see cref="StorageOperation"/>s running now, in memory. Each long piece of work registers itself (<see cref="Begin"/>) for as long as it
/// runs. Who started it is the user of the request it starts in, if any.
/// </summary>
public sealed class StorageOperations(IHttpContextAccessor accessor, TimeProvider time, IPathService path, OperationTexts texts)
{
    private readonly ConcurrentDictionary<Guid, StorageOperation> _running = new();

    /// <summary>
    /// Registers work that runs from now until the result is disposed. <paramref name="user"/> is who started it; by default the user of the
    /// request this is called in (none outside of one: the app).
    /// </summary>
    /// <param name="cancel">Stops it (as soon as it safely can; past the point where it can't, it finishes).</param>
    public IDisposable Begin(OperationKind kind, string physicalPath, Func<double> progress, Func<WorkStatus?> status, Action cancel,
        ClaimsPrincipal? user = null)
    {
        user ??= accessor.HttpContext?.User is { Identity.IsAuthenticated: true } requester ? requester : null;
        if (user is { Identity.IsAuthenticated: false })
            user = null;
        string virtualPath = path.ToVirtualPath(physicalPath) is var shown and not "." ? shown : "";
        StorageOperation operation = new(kind, physicalPath, virtualPath, user, time.GetUtcNow(), progress, status, cancel);
        _running[operation.Id] = operation;
        return new Ending(() => _running.TryRemove(operation.Id, out _));
    }

    /// <summary>All that run now, the most recent first.</summary>
    public IReadOnlyList<StorageOperation> Running => [.. _running.Values.OrderByDescending(operation => operation.Started)];

    /// <summary>
    /// Those the user may see, the most recent first: admins see all of them, everyone else those in what they may read (a password locks
    /// counts). Nobody who isn't logged in sees any.
    /// </summary>
    public IReadOnlyList<StorageOperation> VisibleTo(ClaimsPrincipal user, AccessTree tree)
        => user.Identity?.IsAuthenticated != true ? []
            : user.IsInRole(AppAccess.AdminRole) ? Running
            : [.. Running.Where(operation => tree.GetAccess(user, path.ToPhysicalPath(operation.VirtualPath) ?? operation.PhysicalPath).CanRead)];

    public StorageOperation? Find(Guid id) => _running.GetValueOrDefault(id);

    /// <summary>What the list calls it: what it does, and where ("Zipping /docs/vault").</summary>
    public string TitleOf(StorageOperation operation)
        => texts.TitleOf(operation.Kind, VirtualPaths.ToAddress(operation.VirtualPath));

    /// <summary>
    /// The title of the earliest one that runs now and is of the kind (and not <paramref name="except"/>), for what waits for it: null if none
    /// does.
    /// </summary>
    public string? WaitingFor(OperationKind kind, Func<StorageOperation, bool>? except = null)
        => _running.Values.Where(operation => operation.Kind == kind && except?.Invoke(operation) != true).MinBy(operation => operation.Started)
            is { } running ? TitleOf(running) : null;

    /// <summary>Whether the user may stop it: who started it, and admins (only admins one the app started).</summary>
    public static bool MayCancel(ClaimsPrincipal user, StorageOperation operation)
        => user.IsInRole(AppAccess.AdminRole)
           || (operation.UserId is not null && operation.UserId == user.FindFirst(UserClaims.UserId)?.Value);

    private sealed class Ending(Action end) : IDisposable
    {
        private int _ended;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _ended, 1) == 0)
                end();
        }
    }
}
