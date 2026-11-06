# Security specification

This document describes who may do what with the notes. It is the reference for the code and for its tests. If they disagree with this document, they are wrong (or this document needs changing first).

- **Developers adding a feature:** read [Permissions](#permissions) and [Rules for new features](#rules-for-new-features). Then add the feature to [Operations](#operations).
- **Tests:** every row of [Operations](#operations) and every point of [What a user may learn](#what-a-user-may-learn) is a case to cover.

## Two layers

1. **App-wide gate.** `NotesAccessHandler` (`Features/Auth/NotesAccess.cs`) lets only users with the `admin` role in. Everyone else, signed in or not, may only open the root page. That page shows them the login link and nothing of the notes. Every failed authorization is answered with the same 404 as a missing path. *This gate is temporary.* It goes away once users are supported, and then the second layer is all there is.
2. **Permissions per entry.** For every path inside the notes, `IEntryAccess.GetAccess(user, path)` (`Features/Auth/EntryAccess.cs`) says what the user may do. This is the only place that makes that decision. Right now it gives admins everything and everyone else nothing. Restricting files or directories later only means changing this one implementation.

The rest of this document is about the second layer.

## Permissions

`GetAccess` returns an `EntryAccessInfo` with two independent levels. Each level includes the ones before it.

### Read: the contents of the entry

| Level | Meaning |
|---|---|
| `None` | The entry is still **listed by its name** (and as file or directory) in its directory, but it can't be opened. Its contents stay hidden: the text of a file, everything inside a directory. It can't be selected, copied, downloaded, moved, renamed or deleted. Copies and downloads of the directories it is in leave it out. |
| `Read` | The entry may be opened, downloaded, selected and copied. For a directory, this means its listing can be seen. |
| `ReadAndWrite` | For files, the contents may also be changed (saved). For directories, `Write` currently grants nothing beyond `Read`. |

The name of an entry is not covered by its own Read permission. Whoever may read a directory sees the names of everything in it. **To hide something completely, put it in a directory without `Read`.** Taking `Read` away from an entry hides what is in it, not the fact that it exists.

### Create: whether the entry may exist at its path

| Level | Meaning |
|---|---|
| `None` | Nothing may be created at the path, and what is there may not be removed. |
| `Create` | An entry may come into existence at the path. This covers a new file or directory, an upload, and the destination of a copy, move or rename. |
| `CreateAndDelete` | Also, the entry at the path may be removed. This covers deleting it, and using it as the source of a move or rename. |

### How the two combine

- **Read and Create are separate.** `Create` without `ReadAndWrite` is meaningful: the user can add new files (for example by uploading) but can't change existing ones.
- **Every change of structure needs Read.**
  - Creating anything inside a directory (new entry, upload, copy or move target) also needs `Read` on that directory. Otherwise, name collisions would tell the user which names are taken inside it.
  - Moving, renaming or deleting an entry needs `Read` on it and on everything inside it, because nothing may be moved or removed unseen.
  - So `Create` without `Read` currently allows nothing.
- **Write doesn't allow renaming, moving or deleting.** Those change *where* an entry exists, which is the Create level's job.

### Permissions are decided per path

- **Paths that don't exist yet are asked about too.** That is how the app finds out whether something may be created there. Implementations must:
  - answer for paths that don't exist;
  - answer the same for a path whether or not something exists there;
  - never depend on file contents.
- **New paths are expected to inherit from their directory.** An entry without rules of its own gets the access of its parent. The UI relies on this, but only for hints: to grey out "Create", "Upload", "Copy here" or "Move here", it asks for the directory's own Create level. The server always checks the exact path of what is created. If an implementation breaks inheritance, the UI may offer things that then fail with a 403 and a message. It never allows something it shouldn't.

### Known, openable, hidden

These terms are defined by `AccessTree` (`Features/Auth/AccessTree.cs`), not by `IEntryAccess` alone:

- **Known:** the user may read every directory above the entry, up to the root. They may see its name. The root is always known.
- **Openable:** known, and the user may read the entry itself.
- **Hidden:** not known, meaning the entry is somewhere inside a directory the user may not read. That holds whatever `GetAccess` says about the entry itself. Walks over the notes (`AccessTree.Walk`) never go into unreadable directories.

## What a user may learn

| About | They may learn | They must not learn |
|---|---|---|
| A known entry they may not read | Its name, and whether it is a file or a directory | Anything else: contents, size, dates, what is inside it, whether it is empty |
| A hidden entry | Nothing | That it exists |

How the app keeps to this:

- **Hidden entries answer like missing ones.** A hidden path given in the address or in any request gets the same 404 as a path that doesn't exist. This holds for every query (`?raw`, `?download`, `?rename`, …) and every method (GET, POST, DELETE).
- **Unreadable entries refuse to open.** Opening a known entry the user may not read (its page, `?raw`, `?download`) gets a 403. This is fine, because its name is visible in the listing anyway. The one exception is the root: without `Read` on it, the root page shows only the login link.
- **Messages only name known entries.** Messages and tooltips may name known entries, including unreadable ones. They never name hidden ones.
- **Name collisions leak nothing.** They only happen in directories the user may read, where they see every name anyway.
- **Unreadable directories stay closed.** Operations never look inside a directory the user may not read. Moving, deleting or renaming such a directory, or a directory containing it, fails because of the directory itself, never because of what is in it. So nothing about the contents can be learned from results or timing.

Listings must never show more about an unreadable entry than its name and kind. A feature that adds sizes, dates, previews, counts or the like must leave them out for unreadable entries.

## Operations

"Known" and "openable" are as defined above. "The tree" means the entry and everything inside it, at any depth. The root directory can never be renamed or deleted.

| Operation | Request | Required | Unreadable entries inside |
|---|---|---|---|
| Open the root page | `GET /` | Nothing (app gate allows everyone). Without `Read` on the root, it shows only the login link. | — |
| View a directory listing | `GET /dir` | Directory openable (otherwise 403; 404 if not known) | Listed by name, with a lock and a tooltip. They can't be opened or selected. |
| View a file (editor, media) | `GET /file` | File openable. Without `Write`, the editor is read-only and Save is disabled. | — |
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

Notes on the table:

- **Every request** first resolves the address to a known entry, or answers 404 (`IndexModel`). Each operation's own checks come on top of that.
- **Copy, move, delete and rename are checked completely before anything is changed** (`TransferPlanner`). A copy then copies exactly the entries that were checked: nothing unreadable, and nothing that appeared later.
- **The UI disables what the permissions don't allow and says why.** This covers the selection card and the Delete, Rename, Upload, Create and Save controls. The flags on the selection card are recorded when entries are selected. Whatever is then done with the selection is checked again.

## Concurrency

- **One lock for structure changes.** Every operation that adds, removes or renames entries holds the single `StructureLock` while it checks permissions and acts on them: create, upload (only while placing the finished file), rename, delete, copy and move. Nothing can appear inside a directory between a move or delete checking it and moving or deleting it. Saving changes no entries and doesn't take the lock.
- **Changes made outside the app are not covered.** This includes files edited on the server directly, and permissions that change while an operation runs. Such an operation acts on what it checked at its start.
- **Single process only.** The lock and the selections live in the memory of one process. Running several instances would need a distributed lock and store.

## Rules for new features

1. **Ask `IEntryAccess` for every decision, never the role.** Usually you go through `AccessTree`:
   - `IsKnown` or `CanOpen` for the entry a request is about;
   - `Walk` for everything inside a directory;
   - `Inspect` for what a page shows about a tree.

   Only `NotesAccessHandler` (the temporary app gate) and `EntryAccess` itself may look at roles.
2. **Pick the permissions from the definitions above.** Seeing contents needs `Read`, changing contents needs `Write`, bringing something into existence at a path needs `Create`, and taking it away needs `Delete`. Doing any of this inside a directory needs `Read` on that directory. If none of them fits, extend `EntryAccessInfo` and this document. Don't stretch an existing permission.
3. **Answer hidden like missing.** Return the same 404 and the same message. Return 403 only for known entries.
4. **Never look into an unreadable directory.** An operation on a tree either leaves unreadable entries out (only when it merely reads, like copy or download) or fails because of them (anything that would change, move or remove them). Never act on what is inside an unreadable directory, never show anything about it, and never follow links.
5. **Show unreadable entries by name and kind only.** Show no other attribute.
6. **Lock structure changes.** If the feature adds, removes or renames entries, hold the `StructureLock` from the permission check until the change is done.
7. **Don't leave the user guessing in the UI.** A control the user can't use is disabled, with the reason as its tooltip. Don't hide it, and don't leave it to fail. The server checks again regardless.
8. **Record the feature here.** Add it to [Operations](#operations). If it reveals anything new, add that to [What a user may learn](#what-a-user-may-learn).

## Where it is implemented

| What | Where |
|---|---|
| Permission levels, `IEntryAccess`, current implementation | `notes/Features/Auth/EntryAccess.cs` |
| Known and openable entries, walking trees, page hints | `notes/Features/Auth/AccessTree.cs` |
| App-wide gate | `notes/Features/Auth/NotesAccess.cs` |
| Resolving every request to a known entry | `notes/Pages/Index.cshtml.cs` |
| Listing (unreadable entries) | `notes/Pages/Browse/DirectoryView.cshtml(.cs)`, `_LockedEntry.cshtml` |
| Copy, move, delete, rename checks | `notes/Features/Components/Transfer/TransferPlanner.cs` |
| Structure lock | `notes/Features/Helpers/StructureLock.cs` |
| Create, upload, save, download | their handlers in `notes/Features/Components/*` and `notes/Pages/Editor` |
