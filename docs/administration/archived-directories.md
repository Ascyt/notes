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
- `encrypted` must be `false` for now; it comes later.
- `onLowestLevel`: see [Only the lowest directories](#only-the-lowest-directories).

Saving it moves what is in the directory into its unzipped copy at once (in one step: if something in it is in use on the server, nothing changes and the editor says so). It is zipped the first time once it isn't used for `Archives:ZipAfterMinutes`.

The directory's own configuration stays next to the archive, so it can be edited while the directory is zipped, and says who may see it without unzipping it. The configurations of the directories inside it are in the archive.

**Copying, moving, renaming, deleting** work as for any directory: what is in it is unzipped to be checked first (which may take a moment for a big one). A moved or renamed archived directory stays archived; a copy is a normal directory (archive it again if it should be).

**Downloading:** a download gives what is in it, as for any directory (an archive of it, made as the dialog says). Admins also get a switch in the download dialog of an archived directory, on at first, to download the directory as it is kept instead: its own archive (zipped first if it is unzipped), named after the directory (`foo.7z`), with the configurations and the history inside it. Only for the directory itself, not for a selection.

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
- A directory that comes to hold files later (a new one, or files moved in) is archived by the job that zips idle directories (every minute; until then it is an ordinary directory). Once archived, a directory stays so while the directory above says `onLowestLevel`, even if a directory above it gains files later (that one is then archived too, with it inside).
- A directory archived this way that is moved somewhere without `onLowestLevel` above it is an ordinary directory again where it is then.
- Turning `onLowestLevel` off again turns off every directory archived by it.

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

## On disk

```
parent/foo/.config.json          the directory's own configuration
parent/foo/_.7z                  everything in it (also the configurations inside, and its history)
_/temp/unzipped/parent/foo/      while it is unzipped: what counts then
_/archived-history/parent/foo/   the histories of directories that were archived, once it was turned off
```

The unzipped copies are in the `_/temp` of the location the directory is in (the root, or a [bridge](bridges.md)), so on the same disk. An archived directory inside another one is unzipped into that one's copy. After a crash or power cut, the app puts everything right when it starts: a copy that was unzipped counts (it may be newer than the archive) and is zipped later; half written archives and half done unzipping are deleted.
