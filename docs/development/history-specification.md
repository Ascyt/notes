# History specification

How earlier states of the notes are shown, and who may see which. This is the reference for the code and the tests of the history view, as the [security specification](security-specification.md) is for the rest of the app; where the two meet, the security specification links here. How the history is recorded (the git repository, when it commits) is in [History](../administration/history.md).

## In short

- An address with **`?before=<time>`** shows the notes as they were at that time: the last state the history has from before it. **`?commit=<id>`** shows them as one commit left them. Every page of the notes reached from there keeps the time or the commit, so that the earlier state can be browsed like the current one.
- The **list of changes** of an entry shows the commits that changed it (or what is in it) in a way the user may see as it was, with when, why and by whom (the user name, nothing else about them).
- Such a view is **read-only**, for everyone, admins included.
- Seeing an entry as it was needs **`Read`** and the permission **`history`** on it, both **now and at that time** (as the configuration was then), and `Read` on the directories above it, now and then. What lies between doesn't matter.
- `history` has two levels: **`historyExistingOnly`** for the earlier states of what exists now, **`historyAllow`** also for what was deleted, moved or renamed since. `historyDeny` takes it away.
- **`Read` always comes first:** without it, there is no history either.
- What the [`gitignore`](../administration/history.md#what-it-keeps) of the current configuration leaves out has **no history at all**, even for the times it was kept, for admins too.

## Terms

| Term | Meaning |
|---|---|
| **Now** | The notes as they are on disk, with their configuration on disk. |
| **State** | A commit of the history: the notes and their configuration files as they were committed. |
| **The state before T** | The latest state whose commit time (the author time, from the app's clock) is earlier than T. If there is none (T is before the first commit), there is no state before T. |
| **The state of commit C** | The state C made: the notes as committed in C. It is the state before any time from C's commit time up to the next commit (when the next commit has the same time, `?before=` can't tell them apart, `?commit=` can). |
| **A historic view** | Any page of the notes with a valid `?before=` or `?commit=`. "The state before T" below is then the state of that commit. |
| **Exists now** | There is a file or directory at the path on disk now. An entry that was renamed or moved since doesn't exist now at its old path. |

A state holds what was committed, not what was on disk at that moment: changes are only committed [after a while](../administration/history.md#when-it-commits), so the state before T may lack what was changed shortly before T, and what is pending now is in no state yet (only in now). Git keeps files, not directories: a directory is in a state if anything kept below it is (its `.config.json` always is), so an empty directory without a configuration is in no state.

## The address

- `?before=` holds a time in ISO 8601 with a time zone, as the dialog writes it: `2026-11-11T13:30:00Z` (UTC, to the second). An offset other than `Z` is accepted too (`2026-11-11T14:30:00+01:00`); a time without a time zone is not, as it would mean something else in every time zone.
- A value that isn't such a time, or that lies in the future, is not shown: the page is answered with a redirect to the same address without `?before=` (the rest of the query as it was), and a message that says why (`danger`). So is any `?before=` while there is no history (`Config:History:Enabled` is false, or nothing was committed yet); only admins are told that there is none, everyone else that the permissions don't allow it.
- `?commit=` holds the id of a commit: its full hash, 40 hexadecimal digits (upper case is accepted, as git does). It is answered like `?before=` with the time of that state, except that a value that isn't such an id, and an id the history has no commit of, are sent to the address without it, with a message that says so (`danger`; what doesn't exist now answers 404 instead, as below). Both `?before=` and `?commit=` at once answer 400.
- The message travels with the redirect in a short-lived cookie (`EarlierStateNotice`), and the page it leads to shows it once.
- `?before=` (and in the same way `?commit=`) is kept by every link of the app that leads to a page of the notes: the breadcrumb, the entries of a listing, the way up to the parent directory, the toolbar, links inside rendered Markdown that lead to the notes. Links to the app's own pages (`/_/…`), to other sites and within the page don't get it. The way out of the historic view (see [The card](#the-card)) is the one link of the notes without it. The script of the card adds it to the links of the page, so that every link the page has, now or later, is covered; what the browser loads by itself (images, media, PDFs, also those in rendered Markdown) gets `?raw&before=` from the server.
- **`&changes`** with `?commit=` asks for [the changes of that commit](#the-changes-of-a-commit) instead of its state. With `?before=`, or without a state, it answers 400, and so does `?download` with it. Links keep it together with the commit, as they keep `?commit=`; the way out of the historic view leaves out both. `?raw` with it is the bytes of the file as the commit left them, with `&previous` as they were before it; each answers 404 where that side of the change is not shown.
- `?history` asks for the times at which the entry can be seen as it was, as JSON (`{ "ranges": [ { "after": "…Z", "until": "…Z" or null } ] }`, the latest first), for the dialog of the history button. It answers 404, like a missing path, to whoever may not see the history of the entry now (see 1., 3. and 4. below, for now), or where the history doesn't keep it.
- `?commits` asks for a page of the [list of changes](#the-list-of-changes) of the entry, as the HTML of the sidebar's contents (`_CommitList`: the commits of the page, the newest first, and the way to the other pages). `&page=` says which page (from 1, the newest; anything else is page 1). With the `?before=` or `?commit=` of a historic view, the list starts at the state that is shown and goes back from there; with one the history doesn't have, it is empty. Its first page then has **Show newer…** on top exactly when the list has commits after that state. With `&newer` it is instead the lines of the (up to 25) commits right after that state (`_CommitLines`), the newest first, with **Show newer…** on top while there are newer ones still; without a state (now) there are none. It answers 404 as `?history` does.

## Who may see a state

### The permission

`history` is decided by `IEntryAccess` as the other permissions are (see [Permissions](../administration/permissions.md#what-they-mean)): with the layers of the configuration, the first layer that mentions the user decides, and within it `historyDeny` beats `historyAllow`, which beats `historyExistingOnly`. Nothing that says anything means no. It may be given to `a` (all users, also those who are not logged in) and to `l`. Admins have `historyAllow` everywhere.

| Level (`HistoryAccess`) | From | Allows |
|---|---|---|
| `None` | `historyDeny`, or nothing | No earlier states. |
| `Existing` | `historyExistingOnly` | Earlier states of a path where an entry exists now. |
| `ExistingAndDeleted` | `historyAllow` | Also earlier states of a path where nothing exists now. |

The same is asked of the configuration of a state, as it was committed there: the `.config.json` files of that state, brought up to the current format in memory by the [migrations](../administration/configuration.md#versions-and-migrations). A configuration that was invalid in that state, or that can't be migrated, gives nothing there, as it did then. A configuration from before this permission existed (before version 1.2.0) has no history lists, and so gives the history to nobody but admins: **the history can only be seen for the times at which it was given.**

### The rule

For a user, a path P and a time T, **P is open at T** when all of these hold:

1. **It is kept:** the current configuration doesn't leave P out of the history (`gitignore`, for a directory: the nearest configuration of it or above it). This holds for admins too.
2. **It existed:** P is in the state before T.
3. **Read, now and then:** the user may read P now, and in the state before T.
4. **History, now and then:** in the state before T the user has at least `Existing` on P; now they have at least `Existing` if P exists now, and `ExistingAndDeleted` if it doesn't.
5. **Known, now and then:** the user may read every directory above P now, and in the state before T — the historic counterpart of [known](security-specification.md#known-openable-hidden), so that nothing is seen as it was inside a directory that couldn't be read then. Their `history` doesn't matter: giving `history` on a directory is enough to see its history, without giving it on the directories above. Neither does 1.: a directory that is kept again below one that is left out has its history, though the one above has none.

Only now and T count: what the permissions were between them doesn't matter. So:

- Giving `history` opens the times at which it was given, and only those: giving it today opens nothing from before today, unless it was given then as well.
- Taking `history` away now (or denying it) closes all of the history of the entry, also the times when it was given.
- Taking `Read` away now closes the history too. A time when `Read` was missing stays closed, whatever is given later: `Read` always comes first.
- With `historyExistingOnly` now, an entry that was deleted, moved or renamed since can't be seen as it was, even if it could be read then.

Examples, for alice and `/plan.md`, which exists now (a "–" is "not given"):

| Time | Read | history | Open at that time? |
|---|---|---|---|
| now | yes | yes | — |
| October | yes | yes | yes |
| September | – | yes | no: no Read then |
| August | yes | yes | yes |
| July | yes | – | no: no history then |
| June | yes | yes | yes: only now and June count |

If alice had no `history` (or no `Read`) now, none of these times would be open.

### The list of changes

A commit C has its parent, the state before it (the first commit has none: before it there was nothing). **A change of C can be seen** by a user when it is a change of a file F (added, changed or deleted between the parent and C) and F is open at C, or open at the parent (for what C deleted). A change of a configuration file never can, as configuration files are no entries of the notes; neither can one of what the current `gitignore` leaves out.

**The list of changes of an entry P** has every commit C for which, for that user:

- they may see the history of P now (as for `?history`; otherwise there is no list),
- P is open at C, so that the commit can be opened from the list,
- C changed P, or for a directory something below it, and at least one of those changes can be seen.

A commit that changed only what the user may not see (files they can't read, or whose history they can't see, or only configuration files) is not in any of their lists: they don't learn that it was made, nor by whom. A user **may know of a commit** when it changed anything of the notes they can see; only then does the card of `?commit=` say what it was (see [The card](#the-card)).

Of a commit the user may know of, the app says (the list only when and by whom, the [card](#the-card) of its state all three):

| | |
|---|---|
| When | Its commit time. |
| Why | Its message, as text (`CommitDescriptions`). A message about the configuration (`#migration`, `#before-gitignore`, `#gitignore`) is only told as such to those who manage the configuration; everyone else, and any message this version doesn't know, gets a plain "Changes". |
| By whom | The user name of its author. Never the email or id. What no user made in the app (the author `_`) says "automatic". |

## A historic view

A historic page is the page of the entry as it was: its listing or its contents at the state before T. What it offers is limited to reading:

- **No changes.** No Save, Edit, Delete, Rename, Upload, Create, selection or configuration: the controls aren't there (the editor is read-only). A Markdown file has the way to view its source instead of to edit it, as for whoever may not change it now: `?edit` shows the source in the read-only editor.
- **The server refuses every change.** Every `POST` and `DELETE` with `?before=` or `?commit=` answers 400 and changes nothing, whatever the user may do now. So do `?config` and `?selection` with either (also for admins): they are about now.
- **Reading works as it does now:** viewing, `?raw`, `?download` (a zip of a directory as it was, with each entry checked as described here), Save as PDF, the history button (to choose another time) and the changes button.

### The entries of a listing

Each entry of a historic listing (as it was at T) is shown by what the user may do with it at T:

| The entry at T | Shown as |
|---|---|
| Open at T | A link to it, with `?before=`. |
| Not readable now or at T (3. fails) | As an unreadable entry is now: its name with the lock for logged-in users, not at all for anonymous users. |
| Readable now and at T, but no `history` (4. fails), and it exists now | Its name, in the low-emphasis style, with a crossed-out calendar (`fa-calendar-xmark`) where the lock would be, and a tooltip that says why. It is a link to it, which leads to the explanation (see [below](#addresses-that-cant-be-shown)). |
| The same, but it doesn't exist now | The same, but not a link: there is nothing now to lead to. |
| Left out by the current `gitignore` (1. fails) | Not listed: it is not part of the history. |

`Read` always comes first: an entry that fails 3. shows the lock, never the calendar.

The listing shows nothing about an entry beyond its name and kind, as now (see [What a user may learn](security-specification.md#what-a-user-may-learn)).

### The breadcrumb

The directories above P can all be read, now and at T (5.), but their own history may not be seen: a directory above that isn't open at T itself is shown in the breadcrumb like such an entry of a listing, in the low-emphasis style with a crossed-out calendar (after the house icon, for the root) and a tooltip that says why. It is a link to it, which leads to the explanation, unless it doesn't exist now. The tooltip says that the configuration leaves it out of the history only to those who manage the configuration, as the explanation does (see [below](#addresses-that-cant-be-shown)); everyone else is told that the permissions don't allow it.

### Addresses that can't be shown

What an address with `?before=` answers depends first on what P is now:

| P now | P at T | Answer |
|---|---|---|
| Openable (exists, known and readable) | Open at T | The page as it was. |
| Openable | Not open at T, for any reason | A redirect to P without `?before=`, with a message that says why (`danger`): left out of the history (only to those who manage the configuration; everyone else is told that the permissions don't allow it), insufficient permissions to see it as it was, or not there at that time. It reveals nothing that the page of P without `?before=` doesn't. |
| The root, not readable | — | A redirect to the root without `?before=`, with the message about the permissions: the root page is there for everyone (it has the way to log in). |
| Known, but not readable | — | 403, as for an unreadable entry now. |
| Hidden | — | 404, as for a missing path. |
| Doesn't exist | Open at T | The page as it was. |
| Doesn't exist | Not open at T, for any reason | 404, as for a missing path. |

A hidden entry, and one that doesn't exist now and may not be seen as it was, answer exactly like a missing path, so that nothing about them can be learned by trying.

### The changes of a commit

`?commit=C&changes` shows what the commit C changed of the entry P: the state before it (of its parent B; before the first commit there was nothing) next to the state it made. It is a historic view like the others: nothing can be changed, and every link keeps it.

**Who may see it:** whoever may see a change of P in C. They may see the history of P now (as for `?history`), P is open at C or at B (so that what a commit deleted can be seen too), and C changed P, or for a directory something below it, in a way they may see (as for [the list of changes](#the-list-of-changes)). Otherwise:

| | Answer |
|---|---|
| What is there now can't be opened, the commit id is invalid or unknown | As for `?commit=` (see [Addresses that can't be shown](#addresses-that-cant-be-shown)) |
| P is open at neither C nor B | As for `?commit=` where P isn't open: a redirect to now with why, or 404 where P doesn't exist now |
| P is open, but C changed nothing of it that may be seen | A redirect to the state of the commit (`?commit=C`), with a message that says so (`danger`) |

**The two sides** of a change, before (B) and after (C), are each:

- *shown*, where P is open at that state;
- *not there*, where it wasn't there: the change added or deleted it;
- *not shown*, where it was there but can't be seen as it was. For anonymous users this is *not there*: as in a listing, they don't learn of what they couldn't read.

**A directory** lists what in it C changed in a way the user may see, and nothing else: each entry that was added, changed or deleted, or that has such a change below it. An entry without such a change isn't listed, so a locked one never is. Each entry is marked in the colors of git, with a tooltip that says what happened to it: added (green plus), changed (yellow pen) or deleted (red minus, with the name struck through); a directory that is there on both sides and only has changes below it has no mark. Each leads to its own changes; a directory that is open at neither C nor B is shown like an entry without history in a listing (the crossed-out calendar).

**A file** shows its two sides. Text (Markdown too, as its source) is shown in Monaco's diff editor, read-only, side by side, or inline where there isn't room for both; a side that is not there is empty. Where one side is not shown, only the other one is, in the read-only editor, with a note that says which side can't be seen as it was. Where either side is binary, the two are shown next to each other (under each other on a phone), each as the file's page would show it (media through `?raw`) with its size, or with a note that it is not there or not shown.

**The page** has, in the toolbar, the changes and history buttons, the way between the state of the commit and its changes (`#commit-changes-button`, Shift+H, also on the page of `?commit=`: `fa-code-compare` there, `fa-code-commit` here; disabled with the reason where the other can't be shown: C changed nothing of P that may be seen, or P isn't there in the state of C) and, for text, the zoom buttons; nothing else (no download, no other views). The [card](#the-card) says that the changes of the commit are shown (`fa-code-compare`), with what and by whom as for `?commit=`. The list of changes is shown as in the view of a commit.

## The interface

### The history button

The calendar icon (`fa-calendar`, `#history-button`, shortcut Alt+H) is in the toolbar of a directory and of a file, right of the configuration button and the changes button, and left of everything else that only reads (the other views, Save as PDF, the download). Who sees it:

| User | Shown |
|---|---|
| Anyone with `history` and `Read` on the entry now, if the history keeps it (1.) | Yes |
| An admin, where the history doesn't keep the entry | Yes, disabled, with the reason (the configuration leaves it out). Admins may know about the configuration; nobody else is told. |
| Anyone else | No: there are no earlier states for them. |

Without a history at all (`Config:History:Enabled` is false), it is there for nobody.

It opens a dialog (`#history-modal`):

- A field for the date and time (`datetime-local`, to the second, as the states are, in the browser's time zone), set to the time that is shown (in a historic view) or to now. It can't be set later than now.
- The times at which the entry can be seen (open, as above), the latest first: each from the state in which it is open on, up to the next state in which it isn't (or up to now). They are worked out for the entry and the directories above it, for this user, and asked for (`?history`) when the dialog opens, as working them out goes through the whole history. While they are loading, or if they can't be loaded, only the server decides whether a time can be seen.
- **View** (Enter) opens the same address with `?before=` set to that time in UTC, and nothing else of the query (`?edit` and the like are about changing). It is disabled while the field is empty, invalid, in the future or outside the times listed, with the reason as its tooltip and under the field (for phones, which have no tooltips). **Cancel** (Escape) closes the dialog.

### The changes button

The git icon (`fa-brands fa-git-alt`, `#commits-button`, shortcut H) is left of the history button, and shown, disabled and left out exactly as it is. It shows the [list of changes](#the-list-of-changes) of the entry in a sidebar at the left of the page (`#commit-sidebar`). The page moves to the right to make room for it, so that both can be used; on a narrow screen (a phone) the list is shown instead of the page. While the focus is in it, it has a border in the primary color.

- Each commit is one line: its time (in the browser's time zone) and by whom; the newest at the top. Why it was made is on the card of its state. In a historic view the list starts at the state that is shown (with `?commit=`, that commit, marked) and goes back from there; **Show newer…** at its top, there only where the list has newer commits, adds them above, 25 at a time.
- The server renders what the sidebar shows (`?commits`, a page of 25 at a time). In the view of a commit it is in the page from the start; elsewhere it is asked for when the sidebar is first shown, with a spinner on the button meanwhile. **Newer** and **Older** go through the pages, with a spinner on the one that was used until the page is there; each is disabled, with the reason, where there is no such page.
- While the focus is in the sidebar, the arrow keys are the shortcuts (`key`) of what they lead to, so that holding Right Ctrl shows where each goes: up and down to the commit before and after; above the first one **Show newer…** (or Newer, on a later page), below the last one Older; left and right to Newer and Older. Each goes to the commit next to where the focus was (on another page, its first or last). Enter (or a click) opens the same address with `?commit=` set to the commit, and nothing else of the query.
- Each line also has the compare button (`fa-code-compare`, `.commit-compare`), shown on the line that is pointed at or has the focus and on the commit that is shown (on a touch screen, on every line). It opens [the changes of that commit](#the-changes-of-a-commit) (`?commit=` and `&changes`, and nothing else of the query). Shift+Enter is its shortcut while its line has the focus. Enter always opens the state, also in the changes of a commit.
- **In the view of a commit** (`?commit=`) the sidebar is always shown, where there is room for it (not on a phone, where the page comes first and the sidebar is shown when asked for). Its × leads back to now, as the card's does. The button and its shortcut only move the focus into it and back to the page, and so does Escape.
- **Elsewhere** the button shows the sidebar (and focuses it) or hides it. Its shortcut goes through the three: hidden → shown and focused → focused again (when the focus went to the page) → hidden. Escape, in the sidebar, hides it.

### The card

While a historic view is shown, a card at the bottom of the page (one of the floating cards, like the selection card) says which state is shown, in the browser's time zone: the time asked for (`?before=`) or when the commit was made (`?commit=`). For a commit the user [may know of](#the-list-of-changes), a second line says why it was made and by whom, as the list does. In [the changes of a commit](#the-changes-of-a-commit) it says that those are shown, with the compare icon instead of the commit icon. It stays as long as the page. Its × (Escape, unless something else takes it first: an open dialog, a field being typed in, the list of changes while it has the focus) leads to the same address without `?before=` or `?commit=`: the entry as it is now.

## What a user may learn

On top of [what the security specification allows](security-specification.md#what-a-user-may-learn):

| About | They may learn | They must not learn |
|---|---|---|
| An entry open at T | Its contents and listing as they were at T, and the names (and kinds) of what was in it at T | — |
| An entry in a directory open at T that is readable now and at T, but not open at T | Its name and kind at T, also if it no longer exists | Its contents at T |
| An entry openable now that isn't open at T | Why (left out, permissions, not there then) | — |
| Their own permissions | When they could see the history of an entry (the times in the dialog) | The permissions of others; why theirs changed |
| The configuration | That an entry is left out of the history (admins only) | Anything about it, for everyone else |
| A commit that changed something they may see as it was | That it was made, when, why (a message about the configuration only for those who manage it) and the user name of its author; its id; what it changed of what they may see (each side of the change as far as it may be seen) | What else it changed |
| The history itself | — | Commits that changed nothing they may see as it was (also their authors); the emails or ids of authors; `.config.json` of any state (also for admins, as part of the view: the configuration page is about now) |

## Where it is implemented

| What | Where |
|---|---|
| The `history` permission, resolving it for now | `notes/Features/Auth/EntryAccess.cs` (`HistoryAccess`, `CanViewHistory`), `notes/Features/Permissions/PermissionModel.cs`, `notes/Features/Configuration/ConfigParser.cs` |
| What the history keeps now | `notes/Features/History/KeptFiles.cs` (`IsEntryKept`), through `HistoryService.IsKept` |
| Whether a page has the history and changes buttons, the dialog and the sidebar | `notes/Pages/Index.cshtml.cs` (`DecideHistoryButtonState`), `notes/Features/Components/Toolbar/_Toolbar.cshtml.cs` (`ToolbarButton.History`, `ToolbarButton.Commits`), `notes/Features/HistoryViewing/Views/HistoryButtonState.cs`, `notes/Features/HistoryViewing/Views/_HistoryModal.cshtml(.cs)`, `notes/Features/HistoryViewing/Commits/_CommitSidebar.cshtml(.cs)` |
| Which commits a user may know of, the list of changes and what it says of them | `notes/Features/HistoryViewing/Commits/`: `EarlierStateCommits.cs`, `CommitDescriptions.cs`, the sidebar's contents `_CommitList.cshtml(.cs)` and `_CommitLines.cshtml(.cs)`; what a commit changed: `EarlierState.FilesChangedSince` |
| Deciding by the configuration of a state | `IEntryAccess.GetAccess(user, path, IConfigSnapshot)` in `notes/Features/Auth/EntryAccess.cs`; `notes/Features/HistoryViewing/States/EarlierState.cs`, which is the snapshot of a commit |
| Reading states from the repository | `notes/Features/HistoryViewing/States/EarlierStateReader.cs`: each request opens the repository on its own (reading needs no lock), and the configuration files of the history are read once, by their contents |
| The rule ("open at T"), the listings, the times | `notes/Features/HistoryViewing/States/EarlierStatePermissions.cs` |
| Answering `?before=`, `?commit=`, `?history` and `?commits`, raw bytes and downloads of a state | `notes/Features/HistoryViewing/Requests/`: `EarlierStateRequests.cs`, `EarlierStateParameter.cs`, `EarlierStateNotice.cs`, `EarlierStateTime.cs`; called by `notes/Pages/Index.cshtml.cs`. Zips and raw bytes go through the same code as for the notes as they are (`ZipService`, `DownloadHandler.ZipDownload`, `RawHandler.HandleContents`) |
| The changes of a commit (`&changes`): who may see them, the two sides, the listing and the page of a file | `notes/Features/HistoryViewing/Changes/`: `CommitChangesRequests.cs`, `CommitChanges.cs`, `CommitChangesViews.cs`, `_FileChanges.cshtml(.cs)` (with `wwwroot/js/editor/file-changes.js`), the marks of the listing `_ChangeMark.cshtml(.cs)`; which changes may be seen: `notes/Features/HistoryViewing/Commits/ChangeVisibility.cs` |
| The pages of a state | `notes/Features/HistoryViewing/Views/EarlierStateViews.cs`, `notes/Pages/Browse/DirectoryView.cshtml(.cs)` (`Shown`, `InitFromEarlierState`), `notes/Pages/Browse/_UnopenableEntry.cshtml(.cs)` (the lock and the crossed-out calendar), `notes/Features/Components/Breadcrumb/_Breadcrumb.cshtml(.cs)` (`ParentsWithoutHistory`), `notes/Pages/Editor/FileView.cshtml.cs` (`Earlier`, `RawBytesQuery`), `notes/Features/HistoryViewing/Views/_EarlierStateCard.cshtml(.cs)` (the card, and the script that keeps the state on the links) |

How costly it is: a request for a state goes through the commits from the newest until the one before its time (or the one asked for), and checks the configuration of the entry and the directories above it, then and now. The times of the dialog judge every state once, but states in which nothing the entry's permissions depend on changed (its configuration files, whether it and the directories above it were there) are judged alike, so a long history with few changes of the permissions stays quick. The list of changes goes back through the commits until it has its page: a commit that didn't touch the entry is passed over by comparing one object id, and only the files a commit changed below the entry are checked, each against the rule.

## Tests

Every rule above is a case to cover, in particular:

- The permission: parsing (`historyAllow`, `historyExistingOnly`, `historyDeny`; `a` and `l` allowed; the three lists exclusive), the order within a layer, resolution through the layers, file entries, `inherit`, admins, invalid configurations, that `Read` comes first.
- What is kept: files and directories, a subfolder kept again below one that is left out, and that what the configuration says now decides.
- The button and dialog: there exactly for those who have `history` now, where the entry is kept; disabled with a reason for admins where it isn't, and not there for anyone else; its place in the toolbar; the address the dialog makes (UTC, nothing else of the query), and that it refuses an empty, invalid or future time.
- Each row of the example table above, as states of a test history made with a fake clock, and an entry deleted since with `historyAllow` and with `historyExistingOnly`.
- Each row of [The entries of a listing](#the-entries-of-a-listing), [The breadcrumb](#the-breadcrumb) and [Addresses that can't be shown](#addresses-that-cant-be-shown), for logged-in and anonymous users, and that a hidden entry answers exactly like a missing one.
- That `gitignore` of now beats a state in which the entry was kept, and that an invalid or old-format configuration in a state is read as it was then.
- That every change request with `?before=` or `?commit=` is refused and changes nothing, also for admins; that `?config` and `?selection` with it are refused, and both at once.
- That every link of the notes in a historic page keeps `?before=` (or `?commit=`), and the card's × doesn't.
- `?commit=`: that it shows the state of that commit, accepts upper case, sends an invalid or unknown id to now, and that the card says what the commit was only to whoever may know of it.
- The list of changes (`?commits`): of a file and of a directory, without what can't be seen (unreadable, hidden, only configuration files) and with deletions; its authors; its pages; that it starts at the state that is shown, and the newer ones after it (and **Show newer…** only where there are any); and that it answers 404 as `?history` does. Its sidebar: the button and the shortcut, in the view of a commit and elsewhere, and on a narrow screen; the arrow keys and the shortcuts they are given; the pages; the newer commits.
- The changes of a commit (`&changes`): of a file (changed, added, deleted) and of a directory (only what changed visibly, with its marks), each side shown, not there or not shown (and *not there* for anonymous users), the answers of the table above, 400 with `?before=` and with `?download`, `?raw` of both sides, the way between the state and the changes, the card, and the compare button of the list of changes with Shift+Enter.
- Downloads and `?raw` of an earlier state, and that a zip leaves out what isn't open at T; images in rendered Markdown that load from the state.
- The times (`?history`): the ranges for a history with a gap, and that it answers 404 to whoever may not see the history now, for what is hidden and for what the history leaves out.
- The message of a redirect, and that it is shown once.
