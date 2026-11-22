# Security specification

This document describes who may do what with the notes. It is the reference for the code and for its tests. If they disagree with this document, they are wrong (or this document needs changing first).

- **Developers adding a feature:** read [Permissions](#permissions) and [Rules for new features](#rules-for-new-features). Then add the feature to [Operations](#operations).
- **Tests:** every row of [Operations](#operations) and every point of [What a user may learn](#what-a-user-may-learn) is a case to cover.

## Two layers

1. **No gate.** Anyone, signed in or not, may request any path of the notes; the page of the notes has no authorization of its own. Without `Read` on the root, what they get is the root page with the login link and nothing of the notes. Only what is not part of the notes (the pages under `/_/`, see [Pages outside the notes](#pages-outside-the-notes)) can be gated, and every failed authorization there is answered with the same 404 as a missing path.
2. **Permissions per entry.** For every path inside the notes, `IEntryAccess.GetAccess(user, path)` (`Features/Auth/EntryAccess.cs`) says what the user may do. This is the only place that makes that decision. It gives admins everything. For everyone else it reads the `.config.json` of the folders (see [permissions.md](../administration/permissions.md)): the nearest layer that says anything about the user decides, and with no configuration, or an invalid one, they get nothing.

**Anonymous users** (not logged in) are only the principal `a` of the configuration, which stands for all users, so what is given to `a` is given to logged-in users as well (they never get less than anonymous users), who are also the principal `l`. `a` can be given `Read` (or denied it) but never `Write`, `Create` or `Delete`: the parser refuses such a configuration (`l` may be given anything). They have no user id, so they can't select, upload, transfer anything or have an archive made for them either (a directory downloads as a zip for them at once), and the pages tell them that they may need to log in. Because they can't change anything, they have no way to find out that a name is taken (see "Name collisions" below), so for them an entry they may not read is **hidden**, not just locked: it isn't listed, and its address answers 404 (see [Known, openable, hidden](#known-openable-hidden)).

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
- **Hidden:** not known, meaning the entry is somewhere inside a directory the user may not read (or, for anonymous users, may not be read itself), or not [exposed](#exposed-folders). That holds whatever `GetAccess` says about the entry itself. Walks over the notes (`AccessTree.Walk`) never go into unreadable directories, and for anonymous users leave unreadable entries out.

## What a user may learn

| About | They may learn | They must not learn |
|---|---|---|
| A known entry they may not read | Its name, and whether it is a file or a directory | Anything else: contents, size, dates, what is inside it, whether it is empty |
| A hidden entry | Nothing | That it exists |

How the app keeps to this:

- **Hidden entries answer like missing ones.** A hidden path given in the address or in any request gets the same 404 as a path that doesn't exist. This holds for every query (`?raw`, `?thumbnail`, `?download`, `?rename`, …) and every method (GET, POST, DELETE).
- **Unreadable entries refuse to open.** Opening a known entry the user may not read (its page, `?raw`, `?thumbnail`, `?download`) gets a 403. This is fine, because its name is visible in the listing anyway. (Anonymous users never get there: for them such an entry is hidden.) The one exception is the root: without `Read` on it, the root page shows only the login link.
- **Messages only name known entries.** Messages and tooltips may name known entries, including unreadable ones. They never name hidden ones.
- **Name collisions leak nothing.** They only happen in directories the user may read, where they see every name anyway.
- **Rendered markdown can't run code.** A note is written by whoever may change it and read by everyone else. So `MarkdownRenderer` (`src/Notes/Features/Markdown`) turns no HTML of a note into HTML (it is shown as text), only keeps links and images that lead to `http`, `https` (links also `mailto`) or the notes themselves, and drops every attribute a note can set except ids, which get a prefix so they can't be the ids of the page. The classes that are kept are the ones the extensions make, and none of them is a class of the page (a custom container's name gets a prefix as well). What the browser renders further (math with KaTeX, diagrams with mermaid) comes out of the server as escaped text, which the page reads as text: KaTeX runs without `trust`, mermaid with `securityLevel: 'strict'`. Behind all of that, the site's policy runs no inline script or handler at all (`script-src 'self'`, see the user input document): what got through as markup still couldn't run. Links to other hosts get `rel="noopener noreferrer"`, so the site they open doesn't learn the path of the note.
- **An archive is its maker's.** An archive made to be downloaded (`?archive=`) is kept under the id of the user who asked for it, and downloads for them alone: for everyone else its id answers like one that doesn't exist. Its id is a random GUID, never derived from anything. The password it is encrypted with is used for that only: it is sent in the body (never in an address), and neither logged, kept nor shown.
- **Unreadable directories stay closed.** Operations never look inside a directory the user may not read. Moving, deleting or renaming such a directory, or a directory containing it, fails because of the directory itself, never because of what is in it. So nothing about the contents can be learned from results or timing.

Listings must never show more about an unreadable entry than its name and kind. A feature that adds sizes, dates, previews, counts or the like must leave them out for unreadable entries. The title a markdown file is listed with (its first `#` heading, see `MarkdownTitles`) is part of its contents, so an unreadable one is listed by its file name.

Apart from the notes, nobody but admins learns anything about how the site is run: physical paths, host names, configuration, stack traces, or why the site is down. The source code is public, so how the app works is no secret; these details of the installation are.

### Bridges

A [bridge](../administration/bridges.md) is a directory of the notes kept in a physical folder of its own. For the permissions it is a directory like any other: it is known, openable or hidden as any directory at its virtual path would be, and its configuration starts from that of the directory it is mounted in. On top of that:

- **Nobody but admins learns that a directory is a bridge.** Its listing, its pages and its downloads look like those of any directory. Where something can't be done because of a bridge, everyone else is told what a missing permission would tell them, about the entry they asked for (never about a bridge inside it). Admins are told that it is a bridge, and see the physical path of every entry on the configuration page.
- **A bridge stays where it is.** Deleting, moving or renaming a bridge, or a directory that holds one at any depth, fails (403), also for admins. It is checked last, once everything else about the tree has been checked (so `Read` on everything inside it), so the reason never names what is inside an unreadable directory.
- **A bridge can't be left.** Paths are found from the root one name at a time, and a bridge is entered only by its virtual name, so nothing leads from a bridge into its physical folder's surroundings, into the folder of another bridge, or into the app's data (`_`) of a bridge.
- **What a bridge hides is no part of the notes.** Whatever is on disk where a bridge is mounted (in the folder of the directory above) is answered like a missing path, gets no permissions, isn't listed, walked or kept in the history, and its name is taken (by the bridge).

### Exposed folders

A site may show only some directories of the notes (`Config:Exposed`, see [Using the notes in a site](../library.md)); by default it shows all of them. An entry is exposed if it is one of these directories or inside one, at its path as it is in the notes (`/notes/files/a.md` is `notes/files/a.md`). On top of the permissions:

- **What isn't exposed is no part of the site.** A path that isn't exposed is hidden from everyone, admins too: every request for it, of every kind and in every state of the history, answers like a missing path. Nothing that is exposed leads out of it: listings, walks, downloads, copies and moves stay inside, and the breadcrumb starts at the exposed directory.
- **The directories above still count.** Whether an exposed entry is known is decided as for any entry: the user must be allowed to read every directory above it up to the root, also those that aren't exposed. Exposing a directory never gives anyone more than the permissions do.
- **An exposed directory stays where it is.** Like a bridge, it, or a directory that holds one, can't be deleted, moved or renamed (403, also for admins), checked last; only admins are told why.
- **The exposed directories exist.** A setting that names a directory that isn't there, or whose path isn't valid or is taken by a page of the app, doesn't let the app start.

## Operations

"Known" and "openable" are as defined above. "The tree" means the entry and everything inside it, at any depth. The root directory can never be renamed or deleted, nor can an exposed directory or one that holds one.

| Operation | Request | Required | Unreadable entries inside |
|---|---|---|---|
| Open the root page | `GET /` | Nothing. Without `Read` on the root, it shows only the login link (or, for someone logged in, that they have no access). | — |
| View a directory listing | `GET /dir` | Directory openable (otherwise 403; 404 if not known) | Listed by name, with a lock and a tooltip. They can't be opened or selected. Not listed at all for anonymous users. |
| View a file (preview, editor, media) | `GET /file` | File openable. A `.md` file is shown rendered. Any other text file is shown in the editor, which is read-only without `Write`, and Save is disabled. | — |
| Edit a markdown file | `GET /file.md?edit` | As viewing it. Without `Write`, `?edit` shows its source in the editor, read-only, like any other text file; the ways into the editor (the toolbar's and the one next to each part of the text) are then ways to view the source (`#view-source-button`, `#view-source-here-button`) instead of to edit it. | — |
| Raw bytes of a media file | `GET /file?raw` | File openable, and a media file (image, video, audio, PDF; never SVG, which can carry scripts). An image the browser can't show is converted first; it is only ever read as the format its extension names, never as what its contents claim to be. Any other file answers 403 with the reason (HTML or SVG sent as what it is would run as a page of the site); a directory 404. | — |
| Thumbnail of an image | `GET /file?thumbnail` | File openable, and an image (by its extension), read only as the format its extension names; anything else answers 404, an image that can't be read or is too large 422. | — |
| Download a file | `GET /file?download` | File openable | — |
| Download a directory (zip) | `GET /dir?download` | Directory openable. Each entry is checked as the zip reaches it. The toolbar links to it in an earlier state and for anonymous users; a logged-in user's toolbar asks how to make the archive instead (next row). | Left out, together with what is in them |
| Download a directory as an archive (7z or zip, optionally encrypted) | `POST /dir?archive=directory` (the options in the body), then `GET ?archive=<id>` | Logged in (403 otherwise). Directory openable. Each entry is checked as the archive reaches it, and **all of them again once it is written**: if the user may no longer read one of them, the archive is deleted (409). Only the user who made it can download it (another user's id, or one that is gone, answers 404); it is deleted an hour after it was made. | Left out, together with what is in them. The answer says so. |
| Download the selection | One selected file: `GET /file?download`. Otherwise `POST /entry?archive=selection` (with the version of the selection the page showed), then `GET ?archive=<id>` | As the row above, for every selected entry, which must still be there and openable (a selected entry the user may no longer know of is not named; one they may no longer read is). The selection must be the version the page showed, and no copy, move or delete of it may be running (409). The request may be made of any entry the user may read. | As the row above |
| Save a file | `POST /file` (body) | File known + `ReadAndWrite` on it | — |
| Create a file or subdirectory | `POST /dir?create=file\|directory` | Directory openable + `Create` on the new path | — |
| Upload | `POST /dir?upload=…` | The name is one a [new entry](user-input.md#names-of-new-entries) may have. Directory openable + `Create` on the final path of each file. This is checked with every chunk for the requested name, and again for the name the file actually gets. With `&folder=a/b`, the file goes into those folders inside the directory: each name is one a new entry may have, a folder that is there needs `Read` (and must be a directory, 409 otherwise), and one that isn't needs `Create` (with `Read` on the directory it is made in); they are made under the lock once the last chunk has arrived. | — |
| Rename | `POST /entry?rename=name` | The name is one a [new entry](user-input.md#names-of-new-entries) may have. Entry known, not the root, no [bridge](#bridges) in the tree. `Read` and `CreateAndDelete` on the whole tree, and `Create` on the new path of every entry in the tree. Links are refused. | Fails (403, names the unreadable entry) |
| Delete | `DELETE /entry` or `POST /dir?transfer=delete` | Entry known, not the root, no [bridge](#bridges) in the tree. `Read` and `CreateAndDelete` on the whole tree. All or nothing. | Fails (403, names the unreadable entry) |
| Select / unselect | `POST /?selection=add\|remove\|clear` | Selecting: entry openable. Entries that can't be selected are skipped silently. Unselecting needs nothing. | — |
| Read the selection | `GET /?selection` | Root openable | — |
| Copy the selection here | `POST /dir?transfer=copy` | Target directory openable. Every selected entry openable. `Create` on the destination path of every copied entry. Links are refused. | Left out. The success answer and its message say so. |
| Move the selection here | `POST /dir?transfer=move` | As for copy, plus `CreateAndDelete` on every source tree, and no [bridge](#bridges) in any of them | Fails (403, names the unreadable entry) |
| See the permissions of an entry | `GET /entry?config` | Entry known, and the user may manage permissions (otherwise 404, like a missing path) | — |
| Change the permissions of an entry | `POST /entry?config` (body) | As above. The text is checked completely first; invalid text changes nothing (400). | — |
| Choose an earlier state | the changes button and the history button of the toolbar | Entry openable + `CanViewHistory` on it now, the history is enabled and keeps the entry (`gitignore`). Admins see it disabled, with the reason, where the history doesn't keep the entry; nobody else sees it at all. | — |
| View an earlier state | `GET /entry?before=<time>` or `GET /entry?commit=<id>` (also with `?raw`, `?thumbnail`, `?download`) | As in the [history specification](history-specification.md#addresses-that-cant-be-shown): the entry open at that time. The path need not exist now. | Locked or not listed, as now; a zip leaves out what isn't open at that time |
| View the changes of a commit to an entry | `GET /entry?commit=<id>&changes` (also with `?raw` or `?thumbnail`, and `&previous` for the side before it) | As in the [history specification](history-specification.md#the-changes-of-a-commit): the user may see the history of the entry now, it is open at the commit or before it, and the commit changed it (or something below it) in a way they may see; otherwise as for `?commit=`, or a redirect to the state of the commit. `&changes` without `?commit=`, or with `?download`, answers 400. | Only what the commit changed that the user may see; a side that can't be seen isn't shown (and is not there for anonymous users) |
| The times at which an entry can be seen as it was | `GET /entry?history` | The user may see the history of the entry now (otherwise 404, like a missing path), and the history keeps it | — |
| The list of changes of an entry | `GET /entry?commits` (with `&page=`, and the `?before=` or `?commit=` that is shown) | As `?history`; each commit listed as in the [history specification](history-specification.md#the-list-of-changes) | Only commits that changed what the user may see as it was |
| Change anything in an earlier state | any `POST`, `DELETE`, `?config` or `?selection` with `?before=` | Refused for everyone (400), admins included | — |

Notes on the table:

- **Every request** first resolves the address to a known entry, or answers 404 (`IndexModel`). Each operation then says what else it needs before it is asked at all (`IEntryAction.Needs`: reading the entry, or managing the configuration), which `IndexModel` checks too; its own checks come on top of that.
- **Copy, move, delete and rename are checked completely before anything is changed** (`TransferPlanner`). A copy then copies exactly the entries that were checked: nothing unreadable, and nothing that appeared later.
- **The UI leaves out what the permissions don't allow at all, and disables the rest of what they don't allow, saying why** (see rule 7 below). This covers the selection card (with its Delete) and the Rename, Upload, Create, Save and history controls. The flags on the selection card are recorded when entries are selected. Whatever is then done with the selection is checked again. For deleting, renaming and moving, the UI only looks at the entries themselves, not at what is inside a directory: walking a large tree for every page would make it slow. That is checked when it is done, and a refusal names what is in the way.

### Pages outside the notes

The pages of the app itself are at its own address (`Config:AppPath`, `/_` by default, so `/_/Login`), not in the notes. They are not covered by the permissions of the notes. Their addresses come first: the name they take at the root of the notes (the first part of the app's address, or with an empty one each page's name: `Login`, `About`, …) is no part of the notes, like `_`. A directory of that name is hidden from everyone (answers like a missing path, as does everything inside it), and no new entry at the root gets that name (400, with the reason).

`_` is a reserved name in any case (`PathService.ReservedName`): the `_` directory at the root of the notes holds the app's own data (temporary files of uploads and transfers, the history) and is never part of the notes. So is `_content`, where the scripts and styles of the app are served.

| Page | Request | Required |
|---|---|---|
| Log in, log out | `GET /_/Login`, `POST /_/Logout` | Nothing |
| About | `GET /_/About` | Nothing |
| Error page | shown for an error status | Nothing. It names only the requested path, a request id and the time. |
| Background jobs (Hangfire dashboard) | `/_/Hangfire`, where the site has it | Admin (`AppAccess.AdminPolicyName`, otherwise 404) |
| Shut the site down ([kill switch](../administration/kill-switch.md)) | `POST /_/Shutdown` | Admin (otherwise 404) and the antiforgery token. Afterwards every request, by anyone, gets the same page that only says the site is shut down (503). |

### The history

The [history](../administration/history.md) (`HistoryService`, a git repository in `_/history`) keeps every version of every file it keeps, also what a user may not read, and the names (and emails or Keycloak ids) of the users who changed them. As part of `_` no path of the notes leads to it, so every request for it answers 404, to admins too. The only way into it is the history view (`?before=`, `?commit=` and the list of changes), which decides for every path and state with `IEntryAccess`, as for the notes themselves, and shows nothing of the repository beyond the notes as they were and, for the commits that changed what the user may see, when, why and by whom (the name, never the email or id): see the [history specification](history-specification.md).

What the history does never decides whether an operation is allowed, and never fails it: it runs after the operation's checks, and errors only go to the log.

## Concurrency

- **One lock for changes.** Every operation that adds, removes or renames entries holds the single `StructureLock` while it checks permissions and acts on them: create, upload (only while placing the finished file), rename, delete, copy and move. Nothing can appear inside a directory between a move or delete checking it and moving or deleting it. Saving a file and saving a configuration hold it too, so that what they checked can't be renamed, moved or deleted before they write (a save never makes a file where one was).
- **Nothing is seen half done.** Reading takes no lock, so every change is made so that a reader sees the state before or after it: deletes, copies and moves are single renames into and out of the app's data, a configuration is replaced in one step, and a renamed file has the entry of its new name before it is renamed. How, in detail, is in [User input](user-input.md#concurrency).
- **Names are the same in any case,** also where the file system tells cases apart: a new entry never gets the name of another in another case, which would share (and, when deleted, take away) its permissions.
- **What only reads checks again at its end.** An archive to download holds no lock while it is made, which may take long: every entry in it is checked once it is written, and an archive that holds anything the user may no longer read is deleted instead of handed out (see [principle 13](principles.md#13-long-operations-show-that-they-run-and-check-again-at-the-end)).
- **Changes made outside the app are not covered.** This includes files edited on the server directly, and permissions that change while an operation runs. Such an operation acts on what it checked at its start.
- **Single process only.** The lock and the selections live in the memory of one process. Running several instances would need a distributed lock and store.

## Rules for new features

1. **Ask `IEntryAccess` for every decision about the notes, never the role.** Usually through `AccessTree`:
   - `IsKnown` or `CanOpen` for the entry a request is about;
   - `Walk` for everything inside a directory;
   - `Inspect` for what a page shows about a tree.

   Within the notes, only `EntryAccess` looks at roles and groups (for admin-only features, ask `CanManageConfiguration`). Pages outside the notes that are for admins use the admin policy (`AppAccess.AdminPolicyName`).
2. **Pick the permissions from the definitions above.** Seeing contents needs `Read`, changing contents needs `Write`, bringing something into existence at a path needs `Create`, and taking it away needs `Delete`. Doing any of this inside a directory needs `Read` on that directory. If none of them fits, extend `EntryAccessInfo` and this document. Don't stretch an existing permission.
3. **Answer hidden like missing.** Return the same 404 and the same message. Return 403 only for known entries.
4. **Never look into an unreadable directory.** An operation on a tree either leaves unreadable entries out (only when it merely reads, like copy or download) or fails because of them (anything that would change, move or remove them). Never act on what is inside an unreadable directory, never show anything about it, and never follow links.
5. **Show unreadable entries by name and kind only.** Show no other attribute.
6. **Lock structure changes.** If the feature adds, removes or renames entries, hold the `StructureLock` from the permission check until the change is done.
7. **Don't leave the user guessing in the UI.** What the user may not do at all isn't offered. A control that is there but can't be used here or now is disabled, with the reason as its tooltip, and never left to fail, unless finding that out would mean walking a whole tree (see above): then it fails with the reason. A disabled control's reason only names what the user may know (the configuration, for instance, only for those who manage it). The server checks again regardless.
8. **Check what is sent** as [User input](user-input.md#rules-for-new-features) says: what each value may be, how large, and again under the lock.
9. **Record the feature here.** Add it to [Operations](#operations) (or to [Pages outside the notes](#pages-outside-the-notes)). If it reveals anything new, add that to [What a user may learn](#what-a-user-may-learn).

## Where it is implemented

| What | Where |
|---|---|
| Permission levels, `IEntryAccess`, resolving the configuration for a user | `src/Notes/Features/Auth/EntryAccess.cs` |
| Known and openable entries, walking trees, page hints | `src/Notes/Features/Auth/AccessTree.cs` |
| Admin policy, claim names | `src/Notes/Features/Auth/AppAccess.cs` |
| Principals and `perms` blocks | `src/Notes/Features/Permissions/PermissionModel.cs` |
| The configuration: model, parser (schema), text editing, store, migrations, `?config` handler | `src/Notes/Features/Configuration/*` |
| The history | `src/Notes/Features/History/*` (keeping it), `src/Notes/Features/HistoryViewing/*` (its earlier states, see the [history specification](history-specification.md#where-it-is-implemented)) |
| The configuration page | `src/Notes/Pages/ConfigEditor/ConfigEditorView.cshtml(.cs)` |
| Keeping groups and roles current, notice after a forced logout | `src/Notes/Features/Auth/SessionRefresh.cs`, `SessionNotice.cs` |
| Resolving every request to a known entry, and the access each action needs | `src/Notes/Pages/Index.cshtml.cs`, `src/Notes/Features/EntryActions/IEntryAction.cs` |
| What the site shows, and the addresses and names of the app's own pages | `src/Notes/Features/Addresses` (`ExposedFolders`, `AppAddresses`), applied by `AccessTree` and `EarlierStatePermissions` |
| Where the notes are on disk (the root and the bridges), going from an entry to its directory and to what is in a directory | `src/Notes/Features/Bridges/EntryLocations.cs`, through `src/Notes/Features/Helpers/PathService.cs` for virtual paths |
| Listing (unreadable entries) | `src/Notes/Pages/Browse/DirectoryView.cshtml(.cs)`, `_UnopenableEntry.cshtml` |
| Copy, move, delete, rename checks | `src/Notes/Features/Components/Transfer/TransferPlanner.cs` |
| Structure lock | `src/Notes/Features/Helpers/StructureLock.cs` |
| Create, upload, save, download, the bytes and thumbnails of media | their handlers in `src/Notes/Features/Components/*`, `src/Notes/Features/Editor` and `src/Notes/Features/Media` |
| Downloading as an archive: what goes in, the checks at its end, whose archive it is | `src/Notes/Features/Components/Archive` (`ArchiveHandler`, `ArchiveContents`, `PreparedArchives`) |
| Shutting the site down | `src/Notes/Features/KillSwitch/KillSwitchService.cs`, `src/Notes/Pages/App/Shutdown.cshtml.cs` |
