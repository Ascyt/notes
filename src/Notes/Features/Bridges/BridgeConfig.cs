namespace Notes.Features.Bridges;

/// <summary>
/// A virtual directory that is kept in a physical directory of its own (<c>Config:Bridges</c> in appsettings), see
/// docs/administration/bridges.md.
/// </summary>
public sealed class BridgeConfig
{
    /// <summary>The virtual path of the directory, such as <c>/hdd</c> or <c>/hdd/archive</c>. Never the root.</summary>
    public required string Virtual { get; init; }

    /// <summary>
    /// The physical directory that holds it, such as <c>H:\notes\test</c>. It must exist, and not overlap the entries or another bridge.
    /// </summary>
    public required string Physical { get; init; }

    public BridgeHistoryConfig History { get; init; } = new();
}

/// <summary>
/// Whether the bridge has a history of its own (in <c>_/history</c> of its physical directory). When it commits is as for the root
/// (<c>Config:History</c>).
/// </summary>
public sealed class BridgeHistoryConfig
{
    public bool Enabled { get; init; } = true;
}
