# Archived directories

A directory can be kept in an archive (7z or zip) instead of as files: it is unzipped while it is used, and zipped again once it wasn't used for a while. For the users it is a directory like any other, with a zipper after its name: solid and blue while it is zipped (going into it takes a moment, which the page shows with a bar), an outline in gray while it is unzipped, and the outline with a bar while it is being zipped (opening it then stops that, as what is in it is still unzipped). How it works on disk, and why nothing is lost when the app stops at any point, is in [Zipped folders](../development/zipped-folders.md).

## Turning it on

In the [configuration](configuration.md) of the directory:

```jsonc
"archive": { "enabled": true, "filetype": "7z", "compressionLevel": 2, "encrypted": false, "onLowestLevel": false }
```

- `enabled` is all that is needed; the rest have the defaults shown.
- `filetype`: `"7z"` or `"zip"`. Changing it on an archived directory writes the other format the next time it is zipped, in place of the old archive.
- `compressionLevel`: 0 (none; a 7z, which always compresses, the least) to 3 (most, and slowest). A new one is used from the next zipping on.
- `encrypted`: see [Encrypted](#encrypted).
- `onLowestLevel`: see [Only the lowest directories](#only-the-lowest-directories).

Saving it moves what is in the directory into its unzipped copy at once (in one step: if something in it is in use on the server, nothing changes and the editor says so). It is zipped the first time once it isn't used for `Archives:ZipAfterMinutes`.

The directory's own configuration stays next to the archive, so it can be edited while the directory is zipped, and says who may see it without unzipping it. The configurations of the directories inside it are in the archive.

**Copying, moving, renaming, deleting** work as for any directory: what is in it is unzipped to be checked first (which may take a moment for a big one). A moved or renamed archived directory stays archived; a copy is a normal directory (archive it again if it should be).

**Downloading:** the download dialog (of a directory, or of the selection) lists the archived directories in what is downloaded.

- **Admins** get a box for each, "Also unzip …", not ticked at first, and one above them all that ticks or unticks every one (a dash while only some are). Not ticked, a directory goes in as it is kept: its `.config.json` and its archive (`foo/.config.json`, `foo/_.7z`, encrypted if it is), zipped first if it is unzipped, with the configurations and the history inside it, as on disk, so that putting it back makes it the archived directory it was. A directory inside one that goes in as it is kept is part of that archive, so its box is greyed out. Ticked, it is unzipped, and what is in it goes in like any other directory. Directories inside one that is zipped now are only listed once it is unzipped. If the directory of the page is itself one and isn't ticked, the download is its own archive, named after it (`foo.7z`), and format, compression and password are greyed out.
- **Everyone else** is told that they are unzipped for the download (which may take a while) and zipped again once they aren't used.

The dialog scrolls when the list is longer than the screen. An encrypted directory that goes in as it is kept and whose password the app doesn't know asks for it first.

**The toolbar** of an archived directory that is unzipped has a zip button (the shield, yellow, for an encrypted one), which zips just that directory (and those unzipped inside it) at once and goes to the directory above it.

The root, a bridge, and a directory that holds a bridge can't be archived (with `onLowestLevel` they can: they aren't archived themselves).

**The names `_.7z` and `_.zip`** (and `_.7z.new`, `_.zip.new`) are reserved in every directory, like `_` and `.config.json`: nothing can be created, uploaded or renamed to them, so an archive is never taken for a user's file or the other way round.

## Turning it off

Set `enabled` to `false` (or remove `archive`, or set it to `null`). Saving it puts what is in the directory back as ordinary files, where they were: an unzipped directory at once, a zipped one once its archive is unzipped (which takes a moment for a big one). Its archive is deleted. If something in it is in use on the server, nothing changes (the configuration neither) and the editor says so.

Its own history from while it was archived can't become part of the history around it: it is set aside, never deleted, in `_/archived-history/<path of the directory>/<date and time>` of its location, a git repository like the others ([History](history.md) says how to read one). Archived directories inside it stay archived.

A configuration that isn't valid never counts as turned off: the directory stays as it is until it is fixed. Turning it off by editing the file while the app doesn't run works too: it is done when the app starts.

## Only the lowest directories

With `"onLowestLevel": true`, the directory itself isn't archived. Instead, each directory below that holds files is, with the settings of this one, and nothing between them is. A directory that holds files and directories is archived with those directories inside it. Directories that hold only directories stay as they are.

- Saving it sets `"archive": null` in the configuration of every directory below that has one: `null` (or no `archive`) leaves it to the directory above. Directories without a configuration are left to it all the same.
- It can't be turned on over entries below that have a password: archiving them this way would let the configuration of the directory above get around them. The editor lists them. Nor over directories that are archived with settings of their own (or `onLowestLevel` themselves): set those to `null` first.
- `{ "enabled": false }` on a directory below takes it and what is below it out: what was archived there is turned off. Below it, a directory can be archived on its own again.
- A directory that comes to hold files later in the app (a file created or uploaded, files copied or moved in) is archived right away, and shows as unzipped. One that comes to hold files on the server is archived by the job that zips idle directories (every minute), or the soft restart ([Background jobs](jobs.md)). Once archived, a directory stays so while the directory above says `onLowestLevel`, even if a directory above it gains files later (that one is then archived too, with it inside).
- A directory archived this way that is moved somewhere without `onLowestLevel` above it is an ordinary directory again where it is then.
- Turning `onLowestLevel` off again turns off every directory archived by it.

**Inside an archived directory**, archiving a directory (on its own, or with `onLowestLevel`) is saved, with a warning: what is in it is in the archive around it already, so it is only zipped and unzipped twice. It makes sense with encryption.

## Encrypted

With `"encrypted": true`, the archive is encrypted with the directory's [password](passwords.md) (AES-256; for a 7z the names of the files inside too, for a zip only their contents). It needs a password: a configuration without one is refused. With `onLowestLevel`, every directory it archives is encrypted with the password of the directory that says `onLowestLevel` (they can't have passwords of their own below it).

The app only knows the password while it needs it, **in memory, never on disk**: from when someone unlocks the directory (in the dialog, or with the `X-Notes-Password` header) until it is zipped again, encrypted, and no other unzipped directory needs it. While it is zipped, nobody can get into it without the password, admins included, whatever else they can do on the server.

- **Opening it** asks for the password, as for any protected directory, then unzips it (with a bar). If the app forgot the password meanwhile (it restarted, or the directory was zipped and someone still had it unlocked), it asks again.
- **The lock button** (Alt+L) locks what the browser unlocked and zips the encrypted directories behind those passwords right away.
- **Zipping again** after `Archives:ZipAfterMinutes` encrypts it with the password it knows, once the configuration still has the hash that password was found right for. A changed password is never used blindly.
- **What was unzipped is overwritten** with random data (`Archives:OverwritePasses` times, 3 by default) before it is deleted, as a deleted file stays readable on the disk until something else is written there. So is what a crash left in the work of zipping, and the old archive when encryption is turned on. On an SSD, a copy-on-write file system (btrfs, ZFS, APFS) or with snapshots, the disk may keep the old blocks anyway: encrypting the whole disk is what helps there.
- **If the app stops while it is unzipped**, the unzipped copy stays (it counts, as always) until someone unlocks the directory again; then it is zipped, encrypted. The log says so.
- **Moving, renaming, deleting or downloading it as it is** zips it first. If the app doesn't know the password then (someone still has it unlocked from before), the dialog asks for it, and it goes on once it is typed.

**Turning it on** needs the password known: unlocked (as the editor of a protected directory is anyway), or typed into `"password"`. A directory that is zipped is encrypted right away (in the background): unzipped, zipped encrypted, its old archive overwritten. An unzipped one is encrypted the next time it is zipped.

**Turning it off** (or turning archiving off) needs the password typed into `"password"` in place of `"passwordHash"`: the archive is opened with it (also those of the directories it archives, with `onLowestLevel`). It is saved as the hash it had, not as a new password. A wrong one is refused, and encryption stays on.

**A new password** (typed while it is encrypted) unzips the archives encrypted with the old one first (the old one is known: changing a password needs it unlocked); they are encrypted with the new one when they are zipped again.

Forget the password, and what is in the archive is gone: there is no other way in.

In the listing and the breadcrumb, an encrypted directory has a shield in place of the zipper: a whole yellow one while it is zipped (encrypted), a gray half one while it is unzipped. Its lock is a mark of its own, which only says whether its password is unlocked in this browser (yellow while locked, gray once unlocked): a directory below an `onLowestLevel` one is encrypted with the password of that one and has no lock of its own, and one can be unzipped for someone else while it is locked for you.

## While it is used

- **Unzipping** happens when someone goes into it (or lists it) who may open it, and has its password if it has one. Everyone else finds it as it is: they can't see into it either way. A small one is unzipped before the page shows; a big one shows a page with a bar until it is done.
- **The archive button** (a gray zipper in the navbar, Alt+L) zips at once every archived directory that is unzipped and that the user may open, instead of waiting; a page inside one goes to the directory above it. It is shown only while something is unzipped and nothing is unlocked: the lock button (same place, same shortcut) goes first.
- **Zipping again** happens once nothing has used it for `Archives:ZipAfterMinutes` (10 by default; the job looks every minute, see [Background jobs](jobs.md)). It waits for whatever is going on in it. A file that is open in the editor while that happens is saved anyway: the page unzips it again first, and nothing typed is lost.
- **History:** an archived directory has a history of its own, which is in its archive (in its `_/history`), like a bridge has its own, unless `gitignore` leaves it out. What is pending in it is committed before it is zipped. The history of the directory it is in keeps its configuration, but neither the archive nor what is in it.

## Settings

In `appsettings.json`, under `Config:Archives`:

| Setting | Default | |
|---|---|---|
| `ZipAfterMinutes` | `10` | How long an unzipped directory stays unzipped after it was last used. |
| `WaitSeconds` | `1.5` | How long a request waits for unzipping before the page shows how far it got instead. |
| `OverwritePasses` | `3` | How often what was unzipped from an encrypted directory is overwritten with random data before it is deleted (0 to 35; 0 only deletes it). |

## On disk

```
parent/foo/.config.json          the directory's own configuration
parent/foo/_.7z                  everything in it (also the configurations inside, and its history)
_/temp/unzipped/parent/foo/      while it is unzipped: what counts then
_/archived-history/parent/foo/   the histories of directories that were archived, once it was turned off
```

The unzipped copies are in the `_/temp` of the location the directory is in (the root, or a [bridge](bridges.md)), so on the same disk. An archived directory inside another one is unzipped into that one's copy. After a crash or power cut, the app puts everything right when it starts: a copy that was unzipped counts (it may be newer than the archive) and is zipped later; half written archives and half done unzipping are deleted.
