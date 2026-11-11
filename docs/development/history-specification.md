# History specification

How earlier states of the notes are shown, and who may see which. This is the reference for the code and the tests of the history view, as the [security specification](security-specification.md) is for the rest of the app; where the two meet, the security specification links here. How the history is recorded (the git repository, when it commits) is in [History](../administration/history.md).

## In short

- An address with **`?before=<time>`** shows the notes as they were at that time: the last state the history has from before it. Every page of the notes reached from there keeps the time, so that the earlier state can be browsed like the current one.
- Such a view is **read-only**, for everyone, admins included.
- Seeing it needs **`Read`** and the permission **`history`**, both **now and at that time** (as the configuration was then). What lies between doesn't matter.
- `history` has two levels: **`historyExistingOnly`** for the earlier states of what exists now, **`historyAllow`** also for what was deleted, moved or renamed since. `historyDeny` takes it away.
- **`Read` always comes first:** without it, there is no history either.
- What the [`gitignore`](../administration/history.md#what-it-keeps) of the current configuration leaves out has **no history at all**, even for the times it was kept, for admins too.

## Terms

| Term | Meaning |
|---|---|
| **Now** | The notes as they are on disk, with their configuration on disk. |
| **State** | A commit of the history: the notes and their configuration files as they were committed. |
| **The state before T** | The latest state whose commit time (the author time, from the app's clock) is earlier than T. If there is none (T is before the first commit), there is no state before T. |
| **A historic view** | Any page of the notes with a valid `?before=`. |
| **Exists now** | There is a file or directory at the path on disk now. An entry that was renamed or moved since doesn't exist now at its old path. |

A state holds what was committed, not what was on disk at that moment: changes are only committed [after a while](../administration/history.md#when-it-commits), so the state before T may lack what was changed shortly before T, and what is pending now is in no state yet (only in now). Git keeps files, not directories: a directory is in a state if anything kept below it is (its `.config.json` always is), so an empty directory without a configuration is in no state.

## The address

- `?before=` holds a time in ISO 8601 with a time zone, as the dialog writes it: `2026-11-11T13:30:00Z` (UTC, to the second). An offset other than `Z` is accepted too (`2026-11-11T14:30:00+01:00`); a time without a time zone is not, as it would mean something else in every time zone.
- A value that isn't such a time, or that lies in the future, is not shown: the page is answered with a redirect to the same address without `?before=`, and a message that says why (`danger`).
- `?before=` is kept by every link of the app that leads to a page of the notes: the breadcrumb, the entries of a listing, the way up to the parent directory, the toolbar, links and images inside rendered Markdown that lead to the notes (images load with `?raw&before=`). Links to the app's own pages (`/_/…`) and to other sites don't get it. The way out of the historic view (see [The card](#the-card)) is the one link of the notes without it.

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
5. **Known, now and then:** every directory above P passes 2. to 4. — the historic counterpart of [known](security-specification.md#known-openable-hidden). (Not 1.: a directory that is kept again below one that is left out has its history, though the one above has none.)

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

## A historic view

A historic page is the page of the entry as it was: its listing or its contents at the state before T. What it offers is limited to reading:

- **No changes.** No Save, Edit, Delete, Rename, Upload, Create, selection or configuration: the controls aren't there (the editor is read-only, Markdown shows only its preview). `?edit` shows the same as the plain address.
- **The server refuses every change.** Every `POST` and `DELETE` with `?before=` answers 400 and changes nothing, whatever the user may do now. So do `?config` and `?selection` with `?before=` (also for admins): they are about now.
- **Reading works as it does now:** viewing, `?raw`, `?download` (a zip of a directory as it was, with each entry checked as described here), Save as PDF, and the history button (to choose another time).

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

### Addresses that can't be shown

What an address with `?before=` answers depends first on what P is now:

| P now | P at T | Answer |
|---|---|---|
| Openable (exists, known and readable) | Open at T | The page as it was. |
| Openable | Not open at T, for any reason | A redirect to P without `?before=`, with a message that says why (`danger`): left out of the history, insufficient permissions to see its history, or not there at that time. It reveals nothing that the page of P without `?before=` doesn't. |
| Known, but not readable | — | 403, as for an unreadable entry now. |
| Hidden | — | 404, as for a missing path. |
| Doesn't exist | Open at T | The page as it was. |
| Doesn't exist | Not open at T, for any reason | 404, as for a missing path. |

A hidden entry, and one that doesn't exist now and may not be seen as it was, answer exactly like a missing path, so that nothing about them can be learned by trying.

## The interface

### The history button

The calendar icon (`fa-calendar`, `#history-button`, shortcut Alt+Shift+H) is in the toolbar of a directory and of a file, right of the configuration button and left of everything else that only reads (the other views, Save as PDF, the download). Who sees it:

| User | Shown |
|---|---|
| Anyone with `history` and `Read` on the entry now, if the history keeps it (1.) | Yes |
| An admin, where the history doesn't keep the entry | Yes, disabled, with the reason (the configuration leaves it out). Admins may know about the configuration; nobody else is told. |
| Anyone else | No: there are no earlier states for them. |

Without a history at all (`Config:History:Enabled` is false), it is there for nobody.

It opens a dialog (`#history-modal`):

- A field for the date and time (`datetime-local`, in the browser's time zone), set to the time that is shown (in a historic view) or to now. It can't be set later than now.
- The times at which the entry can be seen (open, as above): from the earliest state in which it is open, up to now, without the times at which it isn't. They are worked out for the entry and the directories above it, for this user.
- **View** (Enter) opens the same address with `?before=` set to that time in UTC, and nothing else of the query (`?edit` and the like are about changing). It is disabled while the field is empty, invalid or in the future, with the reason as its tooltip and under the field (for phones, which have no tooltips). **Cancel** (Escape) closes the dialog.

### The card

While a historic view is shown, a card at the bottom of the page (one of the floating cards, like the selection card) says which time is shown, in the browser's time zone. It stays as long as the page. Its × leads to the same address without `?before=`: the entry as it is now.

## What a user may learn

On top of [what the security specification allows](security-specification.md#what-a-user-may-learn):

| About | They may learn | They must not learn |
|---|---|---|
| An entry open at T | Its contents and listing as they were at T, and the names (and kinds) of what was in it at T | — |
| An entry in a directory open at T that is readable now and at T, but not open at T | Its name and kind at T, also if it no longer exists | Its contents at T |
| An entry openable now that isn't open at T | Why (left out, permissions, not there then) | — |
| Their own permissions | When they could see the history of an entry (the times in the dialog) | The permissions of others; why theirs changed |
| The configuration | That an entry is left out of the history (admins only) | Anything about it, for everyone else |
| The history itself | — | Commit messages, authors and their emails or ids, the commits themselves, `.config.json` of any state (also for admins, as part of the view: the configuration page is about now) |

## Where it is implemented

| What | Where |
|---|---|
| The `history` permission, resolving it for now | `notes/Features/Auth/EntryAccess.cs` (`HistoryAccess`, `CanViewHistory`), `notes/Features/Permissions/PermissionModel.cs`, `notes/Features/Configuration/ConfigParser.cs` |
| What the history keeps now | `notes/Features/History/KeptFiles.cs` (`KeepsEntry`), through `HistoryService.Keeps` |
| Whether a page has the history button, and its dialog | `notes/Pages/Index.cshtml.cs` (`OfferHistory`), `notes/Features/Components/Toolbar/_Toolbar.cshtml.cs` (`ToolbarButton.History`), `notes/Features/History/_HistoryModal.cshtml(.cs)` |

## Tests

Every rule above is a case to cover, in particular:

- The permission: parsing (`historyAllow`, `historyExistingOnly`, `historyDeny`; `a` and `l` allowed; the three lists exclusive), the order within a layer, resolution through the layers, file entries, `inherit`, admins, invalid configurations, that `Read` comes first.
- What is kept: files and directories, a subfolder kept again below one that is left out, and that what the configuration says now decides.
- The button and dialog: there exactly for those who have `history` now, where the entry is kept; disabled with a reason for admins where it isn't, and not there for anyone else; its place in the toolbar; the address the dialog makes (UTC, nothing else of the query), and that it refuses an empty, invalid or future time.
- Each row of the example table above, as states of a test history made with a fake clock, and an entry deleted since with `historyAllow` and with `historyExistingOnly`.
- Each row of [The entries of a listing](#the-entries-of-a-listing) and [Addresses that can't be shown](#addresses-that-cant-be-shown), for logged-in and anonymous users, and that a hidden entry answers exactly like a missing one.
- That `gitignore` of now beats a state in which the entry was kept, and that an invalid or old-format configuration in a state is read as it was then.
- That every change request with `?before=` is refused and changes nothing, also for admins; that `?config` and `?selection` with it are refused.
- That every link of the notes in a historic page keeps `?before=`, and the card's × doesn't.
- Downloads and `?raw` of an earlier state, and that a zip leaves out what isn't open at T.
