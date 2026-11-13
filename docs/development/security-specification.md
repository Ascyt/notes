# Security specification

This document describes who may do what with the notes. It is the reference for the code and for its tests. If they disagree with this document, they are wrong (or this document needs changing first).

- **Developers adding a feature:** read [Permissions](#permissions) and [Rules for new features](#rules-for-new-features). Then add the feature to [Operations](#operations).
- **Tests:** every row of [Operations](#operations) and every point of [What a user may learn](#what-a-user-may-learn) is a case to cover.

## Two layers

1. **No gate.** Anyone, signed in or not, may request any path of the notes; the page of the notes has no authorization of its own. Without `Read` on the root, what they get is the root page with the login link and nothing of the notes. Only what is not part of the notes (the pages under `/_/`, see [Pages outside the notes](#pages-outside-the-notes)) can be gated, and every failed authorization there is answered with the same 404 as a missing path.
2. **Permissions per entry.** For every path inside the notes, `IEntryAccess.GetAccess(user, path)` (`Features/Auth/EntryAccess.cs`) says what the user may do. This is the only place that makes that decision. It gives admins everything. For everyone else it reads the `.config.json` of the folders (see [permissions.md](../administration/permissions.md)): the nearest layer that says anything about the user decides, and with no configuration, or an invalid one, they get nothing.

**Anonymous users** (not logged in) are only the principal `a` of the configuration, which stands for all users, so what is given to `a` is given to logged-in users as well (they never get less than anonymous users), who are also the principal `l`. `a` can be given `Read` (or denied it) but never `Write`, `Create` or `Delete`: the parser refuses such a configuration (`l` may be given anything). They have no user id, so they can't select, upload or transfer anything either, and the pages tell them that they may need to log in. Because they can't change anything, they have no way to find out that a name is taken (see "Name collisions" below), so for them an entry they may not read is **hidden**, not just locked: it isn't listed, and its address answers 404 (see [Known, openable, hidden](#known-openable-hidden)).

The rest of this document is about the second layer.

## The configuration files

- `.config.json` (`PathService.ConfigFileName`) exists in the eyes of admins only. For everyone else `GetAccess` answers `None` for it, it is not in listings, `AccessTree.Walk` doesn't return it (so copies, moves, deletes and zips of a directory never see it), and no path leads to it (it is not a valid name, so it can't be created, uploaded, renamed to or opened). For admins it is part of walks, so it travels with a directory they copy, move or download.
- Seeing and changing the configuration (the permissions, and what the history keeps) is a permission of its own: `IEntryAccess.CanManageConfiguration` (admins). `GET ?config` answers 404 to everyone else, whatever the entry, and so does `POST ?config`.
- The file entries (`files`) of a folder's configuration are kept in step with its files by whoever adds, renames or removes a file, under the `StructureLock` (`FolderConfigStore.FileAdded/FileRenamed/FileRemoved`). A failure there never fails the operation.
- A path's permissions come from its folder's file entry (found by name, whether or not the file exists), then the folder's permissions, then those of the folders above, stopping after the first of these with `inherit: false`. A directory is its own folder. Which of these applies to a path therefore depends on whether it is an existing directory, which anyone who may read the directory it is in sees anyway.

## Permissions

`GetAccess` returns an `EntryAccessInfo` with three independent levels. Each level includes the ones before it.

### Read: the contents of the entry

| Level | Meaning |
|---|---|
| `None` | For logged-in users, the entry is still **listed by its name** (and as file or directory) in its directory, but it can't be opened; for anonymous users it is hidden. Its contents stay hidden: the text of a file, everything inside a directory. It can't be selected, copied, downloaded, moved, renamed or deleted. Copies and downloads of the directories it is in leave it out. |
| `Read` | The entry may be opened, downloaded, selected and copied. For a directory, this means its listing can be seen. |
| `ReadAndWrite` | For files, the contents may also be changed (saved). For directories, `Write` currently grants nothing beyond `Read`. |

The name of an entry is not covered by its own Read permission. Every logged-in user who may read a directory sees the names of everything in it (anonymous users only see what they may read). **To hide something completely, put it in a directory without `Read`.** Taking `Read` away from an entry hides what is in it, not the fact that it exists.

### Create: whether the entry may exist at its path

| Level | Meaning |
|---|---|
| `None` | Nothing may be created at the path, and what is there may not be removed. |
| `Create` | An entry may come into existence at the path. This covers a new file or directory, an upload, and the destination of a copy, move or rename. |
| `CreateAndDelete` | Also, the entry at the path may be removed. This covers deleting it, and using it as the source of a move or rename. |

### History: the earlier states of the entry

| Level | Meaning |
|---|---|
| `None` | Only the entry as it is now can be seen. |
| `Existing` | Earlier states of the entry may be seen, read-only (`?before=`), as long as an entry exists at the path now. |
| `ExistingAndDeleted` | Also earlier states of a path where nothing exists now (deleted, moved or renamed since). |

Seeing an entry as it was needs `Read` and `History` on it, now and at that time, and `Read` on the directories above it, now and then; `EntryAccessInfo.CanViewHistory` is true only with `Read`. What exactly may be seen of which state is in the [history specification](history-specification.md#who-may-see-a-state); `GetAccess` answers for now, and the history view asks the same of the configuration of each state.

### How they combine

- **Read and Create are separate.** `Create` without `ReadAndWrite` is meaningful: the user can add new files (for example by uploading) but can't change existing ones.
- **Every change of structure needs Read.**
  - Creating anything inside a directory (new entry, upload, copy or move target) also needs `Read` on that directory. Otherwise, name collisions would tell the user which names are taken inside it.
  - Moving, renaming or deleting an entry needs `Read` on it and on everything inside it, because nothing may be moved or removed unseen.
  - So `Create` without `Read` currently allows nothing.
- **Write doesn't allow renaming, moving or deleting.** Those change *where* an entry exists, which is the Create level's job.
- **History needs Read, and allows no change.** Without `Read` there is no history of an entry, whatever `History` says, and an earlier state can't be changed by anyone, admins included.

### Permissions are decided per path

- **Paths that don't exist yet are asked about too.** That is how the app finds out whether something may be created there. Implementations must:
  - answer for paths that don't exist;
  - answer the same for a path whether or not something exists there, as far as that is something the user may know (see [The configuration files](#the-configuration-files) for the one way it differs);
  - never depend on file contents (the configuration files are the permissions, not contents).
- **New paths are expected to inherit from their directory.** An entry without rules of its own gets the access of its parent. The UI relies on this, but only for hints: to grey out "Create", "Upload", "Copy here" or "Move here", it asks for the directory's own Create level. The server always checks the exact path of what is created. If an implementation breaks inheritance, the UI may offer things that then fail with a 403 and a message. It never allows something it shouldn't.

### Known, openable, hidden

These terms are defined by `AccessTree` (`Features/Auth/AccessTree.cs`), not by `IEntryAccess` alone:

- **Known:** the user may read every directory above the entry, up to the root. They may see its name. For anonymous users the entry must also be readable itself (`AccessTree.SeesUnreadableNames`), so for them known and openable are the same. The root is always known.
- **Openable:** known, and the user may read the entry itself.
- **Hidden:** not known, meaning the entry is somewhere inside a directory the user may not read (or, for anonymous users, may not be read itself). That holds whatever `GetAccess` says about the entry itself. Walks over the notes (`AccessTree.Walk`) never go into unreadable directories, and for anonymous users leave unreadable entries out.

## What a user may learn

| About | They may learn | They must not learn |
|---|---|---|
| A known entry they may not read | Its name, and whether it is a file or a directory | Anything else: contents, size, dates, what is inside it, whether it is empty |
| A hidden entry | Nothing | That it exists |

How the app keeps to this:

- **Hidden entries answer like missing ones.** A hidden path given in the address or in any request gets the same 404 as a path that doesn't exist. This holds for every query (`?raw`, `?download`, `?rename`, …) and every method (GET, POST, DELETE).
- **Unreadable entries refuse to open.** Opening a known entry the user may not read (its page, `?raw`, `?download`) gets a 403. This is fine, because its name is visible in the listing anyway. (Anonymous users never get there: for them such an entry is hidden.) The one exception is the root: without `Read` on it, the root page shows only the login link.
- **Messages only name known entries.** Messages and tooltips may name known entries, including unreadable ones. They never name hidden ones.
- **Name collisions leak nothing.** They only happen in directories the user may read, where they see every name anyway.
- **Rendered markdown can't run code.** A note is written by whoever may change it and read by everyone else. So `MarkdownRenderer` (`notes/Pages/Editor/Markdown`) turns no HTML of a note into HTML (it is shown as text), only keeps links and images that lead to `http`, `https` (links also `mailto`) or the notes themselves, and drops every attribute a note can set except ids, which get a prefix so they can't be the ids of the page. The classes that are kept are the ones the extensions make, and none of them is a class of the page (a custom container's name gets a prefix as well). What the browser renders further (math with KaTeX, diagrams with mermaid) comes out of the server as escaped text, which the page reads as text: KaTeX runs without `trust`, mermaid with `securityLevel: 'strict'`. Links to other hosts get `rel="noopener noreferrer"`, so the site they open doesn't learn the path of the note.
- **Unreadable directories stay closed.** Operations never look inside a directory the user may not read. Moving, deleting or renaming such a directory, or a directory containing it, fails because of the directory itself, never because of what is in it. So nothing about the contents can be learned from results or timing.

Listings must never show more about an unreadable entry than its name and kind. A feature that adds sizes, dates, previews, counts or the like must leave them out for unreadable entries.

Apart from the notes, nobody but admins learns anything about how the site is run: physical paths, host names, configuration, stack traces, or why the site is down. The source code is public, so how the app works is no secret; these details of the installation are.

## Operations

"Known" and "openable" are as defined above. "The tree" means the entry and everything inside it, at any depth. The root directory can never be renamed or deleted.

| Operation | Request | Required | Unreadable entries inside |
|---|---|---|---|
| Open the root page | `GET /` | Nothing. Without `Read` on the root, it shows only the login link (or, for someone logged in, that they have no access). | — |
| View a directory listing | `GET /dir` | Directory openable (otherwise 403; 404 if not known) | Listed by name, with a lock and a tooltip. They can't be opened or selected. Not listed at all for anonymous users. |
| View a file (preview, editor, media) | `GET /file` | File openable. A `.md` file is shown rendered. Any other text file is shown in the editor, which is read-only without `Write`, and Save is disabled. | — |
| Edit a markdown file | `GET /file.md?edit` | As viewing it. Without `Write`, `?edit` shows its source in the editor, read-only, like any other text file; the ways into the editor (the toolbar's and the one next to each part of the text) are then ways to view the source (`#view-source-button`, `#view-source-here-button`) instead of to edit it. | — |
| Raw bytes of a media file | `GET /file?raw` | File openable | — |
| Download a file | `GET /file?download` | File openable | — |
| Download a directory (zip) | `GET /dir?download` | Directory openable. Each entry is checked as the zip reaches it. | Left out, together with what is in them |
| Save a file | `POST /file` (body) | File known + `ReadAndWrite` on it | — |
| Create a file or subdirectory | `POST /dir?create=file\|directory` | Directory openable + `Create` on the new path | — |
| Upload | `POST /dir?upload=…` | Directory openable + `Create` on the final path of each file. This is checked with every chunk for the requested name, and again for the name the file actually gets. | — |
| Rename | `POST /entry?rename=name` | Entry known, not the root. `Read` and `CreateAndDelete` on the whole tree, and `Create` on the new path of every entry in the tree. Links are refused. | Fails (403, names the unreadable entry) |
| Delete | `DELETE /entry` or `POST /dir?transfer=delete` | Entry known, not the root. `Read` and `CreateAndDelete` on the whole tree. All or nothing. | Fails (403, names the unreadable entry) |
| Select / unselect | `POST /?selection=add\|remove\|clear` | Selecting: entry openable. Entries that can't be selected are skipped silently. Unselecting needs nothing. | — |
| Read the selection | `GET /?selection` | Root openable | — |
| Copy the selection here | `POST /dir?transfer=copy` | Target directory openable. Every selected entry openable. `Create` on the destination path of every copied entry. Links are refused. | Left out. The success answer and its message say so. |
| Move the selection here | `POST /dir?transfer=move` | As for copy, plus `CreateAndDelete` on every source tree | Fails (403, names the unreadable entry) |
| See the permissions of an entry | `GET /entry?config` | Entry known, and the user may manage permissions (otherwise 404, like a missing path) | — |
| Change the permissions of an entry | `POST /entry?config` (body) | As above. The text is checked completely first; invalid text changes nothing (400). | — |
| Choose an earlier state | the changes button and the history button of the toolbar | Entry openable + `CanViewHistory` on it now, the history is enabled and keeps the entry (`gitignore`). Admins see it disabled, with the reason, where the history doesn't keep the entry; nobody else sees it at all. | — |
| View an earlier state | `GET /entry?before=<time>` or `GET /entry?commit=<id>` (also with `?raw`, `?download`) | As in the [history specification](history-specification.md#addresses-that-cant-be-shown): the entry open at that time. The path need not exist now. | Locked or not listed, as now; a zip leaves out what isn't open at that time |
| View the changes of a commit to an entry | `GET /entry?commit=<id>&changes` (also with `?raw`, and `&previous` for the side before it) | As in the [history specification](history-specification.md#the-changes-of-a-commit): the user may see the history of the entry now, it is open at the commit or before it, and the commit changed it (or something below it) in a way they may see; otherwise as for `?commit=`, or a redirect to the state of the commit. `&changes` without `?commit=`, or with `?download`, answers 400. | Only what the commit changed that the user may see; a side that can't be seen isn't shown (and is not there for anonymous users) |
| The times at which an entry can be seen as it was | `GET /entry?history` | The user may see the history of the entry now (otherwise 404, like a missing path), and the history keeps it | — |
| The list of changes of an entry | `GET /entry?commits` (with `&page=`, and the `?before=` or `?commit=` that is shown) | As `?history`; each commit listed as in the [history specification](history-specification.md#the-list-of-changes) | Only commits that changed what the user may see as it was |
| Change anything in an earlier state | any `POST`, `DELETE`, `?config` or `?selection` with `?before=` | Refused for everyone (400), admins included | — |

Notes on the table:

- **Every request** first resolves the address to a known entry, or answers 404 (`IndexModel`). Each operation's own checks come on top of that.
- **Copy, move, delete and rename are checked completely before anything is changed** (`TransferPlanner`). A copy then copies exactly the entries that were checked: nothing unreadable, and nothing that appeared later.
- **The UI leaves out what the permissions don't allow at all, and disables the rest of what they don't allow, saying why** (see rule 7 below). This covers the selection card and the Delete, Rename, Upload, Create, Save and history controls. The flags on the selection card are recorded when entries are selected. Whatever is then done with the selection is checked again.

### Pages outside the notes

Every path that starts with `/_/` belongs to the app, not to the notes. `_` is a reserved name (`PathService.ReservedName`): the `_` directory at the root of the notes holds the app's own data (temporary files of uploads and transfers, the history) and is never part of the notes. These pages are not covered by the permissions of the notes.

| Page | Request | Required |
|---|---|---|
| Log in, log out | `GET /_/Login`, `POST /_/Logout` | Nothing |
| About | `GET /_/About` | Nothing |
| Error page | shown for an error status | Nothing. It names only the requested path, a request id and the time. |
| Background jobs (Hangfire dashboard) | `/_/Hangfire` | Admin (`NotesAccess.AdminPolicyName`, otherwise 404) |
| Shut the site down ([kill switch](../administration/kill-switch.md)) | `POST /_/Shutdown` | Admin (otherwise 404) and the antiforgery token. Afterwards every request, by anyone, gets the same page that only says the site is shut down (503). |

### The history

The [history](../administration/history.md) (`HistoryService`, a git repository in `_/history`) keeps every version of every file it keeps, also what a user may not read, and the names (and emails or Keycloak ids) of the users who changed them. As part of `_` no path of the notes leads to it, so every request for it answers 404, to admins too. The only way into it is the history view (`?before=`, `?commit=` and the list of changes), which decides for every path and state with `IEntryAccess`, as for the notes themselves, and shows nothing of the repository beyond the notes as they were and, for the commits that changed what the user may see, when, why and by whom (the name, never the email or id): see the [history specification](history-specification.md).

What the history does never decides whether an operation is allowed, and never fails it: it runs after the operation's checks, and errors only go to the log.

## Concurrency

- **One lock for structure changes.** Every operation that adds, removes or renames entries holds the single `StructureLock` while it checks permissions and acts on them: create, upload (only while placing the finished file), rename, delete, copy and move. Nothing can appear inside a directory between a move or delete checking it and moving or deleting it. Saving changes no entries and doesn't take the lock.
- **Changes made outside the app are not covered.** This includes files edited on the server directly, and permissions that change while an operation runs. Such an operation acts on what it checked at its start.
- **Single process only.** The lock and the selections live in the memory of one process. Running several instances would need a distributed lock and store.

## Rules for new features

1. **Ask `IEntryAccess` for every decision about the notes, never the role.** Usually through `AccessTree`:
   - `IsKnown` or `CanOpen` for the entry a request is about;
   - `Walk` for everything inside a directory;
   - `Inspect` for what a page shows about a tree.

   Within the notes, only `EntryAccess` looks at roles and groups (for admin-only features, ask `CanManageConfiguration`). Pages outside the notes that are for admins use the admin policy (`NotesAccess.AdminPolicyName`).
2. **Pick the permissions from the definitions above.** Seeing contents needs `Read`, changing contents needs `Write`, bringing something into existence at a path needs `Create`, and taking it away needs `Delete`. Doing any of this inside a directory needs `Read` on that directory. If none of them fits, extend `EntryAccessInfo` and this document. Don't stretch an existing permission.
3. **Answer hidden like missing.** Return the same 404 and the same message. Return 403 only for known entries.
4. **Never look into an unreadable directory.** An operation on a tree either leaves unreadable entries out (only when it merely reads, like copy or download) or fails because of them (anything that would change, move or remove them). Never act on what is inside an unreadable directory, never show anything about it, and never follow links.
5. **Show unreadable entries by name and kind only.** Show no other attribute.
6. **Lock structure changes.** If the feature adds, removes or renames entries, hold the `StructureLock` from the permission check until the change is done.
7. **Don't leave the user guessing in the UI.** What the user may not do at all isn't offered. A control that is there but can't be used here or now is disabled, with the reason as its tooltip, and never left to fail. A disabled control's reason only names what the user may know (the configuration, for instance, only for those who manage it). The server checks again regardless.
8. **Record the feature here.** Add it to [Operations](#operations) (or to [Pages outside the notes](#pages-outside-the-notes)). If it reveals anything new, add that to [What a user may learn](#what-a-user-may-learn).

## Where it is implemented

| What | Where |
|---|---|
| Permission levels, `IEntryAccess`, resolving the configuration for a user | `notes/Features/Auth/EntryAccess.cs` |
| Known and openable entries, walking trees, page hints | `notes/Features/Auth/AccessTree.cs` |
| Admin policy, claim names | `notes/Features/Auth/NotesAccess.cs` |
| Principals and `perms` blocks | `notes/Features/Permissions/PermissionModel.cs` |
| The configuration: model, parser (schema), text editing, store, migrations, `?config` handler | `notes/Features/Configuration/*` |
| The history | `notes/Features/History/*` (keeping it), `notes/Features/HistoryViewing/*` (its earlier states, see the [history specification](history-specification.md#where-it-is-implemented)) |
| The configuration page | `notes/Pages/ConfigEditor/ConfigEditorView.cshtml(.cs)` |
| Keeping groups and roles current, notice after a forced logout | `notes/Features/Auth/SessionRefresh.cs`, `SessionNotice.cs` |
| Resolving every request to a known entry | `notes/Pages/Index.cshtml.cs` |
| Listing (unreadable entries) | `notes/Pages/Browse/DirectoryView.cshtml(.cs)`, `_UnopenableEntry.cshtml` |
| Copy, move, delete, rename checks | `notes/Features/Components/Transfer/TransferPlanner.cs` |
| Structure lock | `notes/Features/Helpers/StructureLock.cs` |
| Create, upload, save, download | their handlers in `notes/Features/Components/*` and `notes/Pages/Editor` |
| Shutting the site down | `notes/Features/KillSwitch/KillSwitchService.cs`, `notes/Pages/App/Shutdown.cshtml.cs` |
