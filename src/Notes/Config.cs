using Notes.Features.Auth;
using Notes.Features.Bridges;
using Notes.Features.Helpers;
using Notes.Features.History;

namespace Notes;

public sealed class Config
{
    public required string Dir { get; init; }
    public string Title { get; init; } = "Notes";
    /// <summary>
    /// The directories the site shows (virtual paths, such as <c>files</c> or <c>notes/files</c>), each with everything in
    /// it; empty for all of the entries. See <see cref="Features.Addresses.ExposedFolders"/>.
    /// </summary>
    public string[] Exposed { get; init; } = [];
    /// <summary>
    /// Where the pages of the app itself are: <c>/_</c> (so <c>/_/Login</c>), another address such as <c>/app</c>, or empty for the root
    /// (<c>/Login</c>). See <see cref="Features.Addresses.AppAddresses"/>.
    /// </summary>
    public string AppPath { get; init; } = "/_";
    /// <summary>
    /// Links of the site shown before the start of every breadcrumb, as if they were directories above it (e.g. the site's start page, where
    /// the entries are a part of it). See <see cref="Features.Components.Breadcrumb.BreadcrumbParent"/>.
    /// </summary>
    public Features.Components.Breadcrumb.BreadcrumbParent[] BreadcrumbParents { get; init; } = [];
    /// <summary>Where the keys protecting the login session and antiforgery tokens are kept, so they survive restarts.</summary>
    public string? KeysDir { get; init; }
    /// <summary>
    /// The file that keeps the site shut down once the kill switch was pulled. By default "killed" in <see cref="KeysDir"/> (or the app's
    /// directory).
    /// </summary>
    public string? KillSwitchMarker { get; init; }
    public required AuthConfig Auth { get; init; }
    public HistoryConfig History { get; init; } = new();
    /// <summary>When the background jobs run, see <see cref="JobsConfig"/>.</summary>
    public JobsConfig Jobs { get; init; } = new();
    /// <summary>Virtual directories kept in physical directories of their own, see <see cref="EntryLocations"/>.</summary>
    public BridgeConfig[] Bridges { get; init; } = [];
    /// <summary>How large what users send and make may be, see <see cref="LimitsConfig"/>.</summary>
    public LimitsConfig Limits { get; init; } = new();
}
