namespace notes.Tests.Common;

/// <summary>
/// The notes most tests start with. Every kind of entry the security specification talks about is in it, for each of the <see cref="Users"/>:
/// <code>
/// /                 team: read/write, create/delete; guests, anonymous, carol: read      (inherit: false)
///   readme.md
///   docs/           (no configuration: as the root)
///     a.md
///     nested/b.md
///   team/           team only                                                             (inherit: false)
///     plan.md
///     sub/c.md
///   vault/          carol only (alice: known, can't be opened; what is in it is hidden)   (inherit: false)
///     secret.md
///     inner/deep.md
///   mixed/          as the root, but:
///     open.md
///     locked.md     nobody (file entry, inherit: false)
///     keep.md       team may not delete it (cdDeny)
///     view.md       team may only read it (rwReadOnly)
///     closed/       nobody (inherit: false): alice sees its name only
///       hidden.md
///   drop/           team may create, but not delete (cdCreateOnly)
///     existing.md
///   shelf/          team may read and write, but neither create nor delete (cdDeny)
///     item.md
///   broken/         invalid configuration: only admins
///     x.md
/// </code>
/// Hidden from alice: vault/*, mixed/closed/*, broken/*. Known but not openable for alice: vault, mixed/closed, mixed/locked.md, broken.
/// </summary>
public static class BaseTree
{
    public static readonly string[] HiddenFromAlice = ["vault/secret.md", "vault/inner", "vault/inner/deep.md", "mixed/closed/hidden.md", "broken/x.md"];

    public static readonly string[] UnreadableForAlice = ["vault", "mixed/closed", "mixed/locked.md", "broken"];

    /// <summary>Names that must never show up in anything alice gets (they are only inside directories she may not read).</summary>
    public static readonly string[] SecretNamesForAlice = ["secret.md", "deep.md", "hidden.md"];

    public static void Create(NotesTree tree)
    {
        tree.Config("", Cfg.Folder(Cfg.Perms(inherit: false, rwAllow: ["g:team"], rwReadOnly: ["g:guests", "a", "u:carol"], cdAllow: ["g:team"])));
        tree.File("readme.md", "# Readme");
        tree.File("docs/a.md", "a");
        tree.File("docs/nested/b.md", "b");

        tree.Config("team", Cfg.Folder(Cfg.Perms(inherit: false, rwAllow: ["g:team"], cdAllow: ["g:team"])));
        tree.File("team/plan.md", "plan");
        tree.File("team/sub/c.md", "c");

        tree.Config("vault", Cfg.Folder(Cfg.Perms(inherit: false, rwAllow: ["u:carol"], cdAllow: ["u:carol"])));
        tree.File("vault/secret.md", "the secret");
        tree.File("vault/inner/deep.md", "deep secret");

        tree.Config("mixed", Cfg.Folder(Cfg.Perms(),
            ("locked.md", Cfg.Perms(inherit: false)),
            ("keep.md", Cfg.Perms(cdDeny: ["g:team"])),
            ("view.md", Cfg.Perms(rwReadOnly: ["g:team"]))));
        tree.File("mixed/open.md", "open");
        tree.File("mixed/locked.md", "locked contents");
        tree.File("mixed/keep.md", "keep");
        tree.File("mixed/view.md", "view");
        tree.Config("mixed/closed", Cfg.Folder(Cfg.Perms(inherit: false)));
        tree.File("mixed/closed/hidden.md", "hidden contents");

        tree.Config("drop", Cfg.Folder(Cfg.Perms(cdCreateOnly: ["g:team"])));
        tree.File("drop/existing.md", "existing");

        tree.Config("shelf", Cfg.Folder(Cfg.Perms(cdDeny: ["g:team"])));
        tree.File("shelf/item.md", "item");

        tree.Config("broken", "{ this is not json");
        tree.File("broken/x.md", "x");
    }
}
