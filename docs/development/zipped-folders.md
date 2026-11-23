# Zipped folders

A zipped folder keeps its contents in an archive and is unzipped while it is used. This page is about how that works on disk and why it never loses anything, and lists the edge cases. So far only the core is built (`Features/ZippedFolders`): zipping, unzipping, checking and recovering a folder's archive. The configuration (`zip` in `.config.json`), the pages and the jobs that use it come next.

## On disk

For a zipped folder `parent/foo` in a location (the root or a [bridge](../administration/bridges.md)):

| Path | What it is |
|---|---|
| `parent/foo/.config.json` | The folder's own settings, readable without unzipping (permissions, `zip`, `passwordHash`). |
| `parent/foo/_.7z` or `_.zip` | The archive: everything in the folder, with its own `.config.json` files and its history (`_/history`). |
| `parent/foo/_.7z.new` | Only while it is zipped again: the new archive, before it replaces the old one. |
| `_/temp/unzipped/parent/foo` | Only while it is unzipped: its contents, which are what counts then. |
| `_/temp/zipping/unzipping/<id>` | Only while it is unzipped: the archive being unzipped. |
| `_/temp/zipping/deleting/<id>` | Only while the unzipped copy is deleted, once it is zipped. |

The `_/temp` folders are those of the folder's location, so on the same volume as the folder: everything below moves things with renames, which only work within a volume and either happen or don't.

## Zipping and unzipping (`FolderArchiver`)

Zipping, from the unzipped copy:

1. The new archive is written to `_.7z.new`, next to the old one. The SHA-256 of each file is worked out while the writer reads it.
2. It is flushed to the disk, then read back from the disk: it must hold exactly those files with those hashes, and the same directories (also the empty ones). Otherwise it is deleted, and the zipping fails with nothing else changed.
3. It is renamed over the old archive, and its directory flushed.
4. Only then is the unzipped copy renamed into `_/temp/zipping/deleting` (so it is gone at once, never half there) and deleted there.

Unzipping is the other way round: into `_/temp/zipping/unzipping/<id>`, every file and directory flushed to the disk, then renamed into `_/temp/unzipped/…`. Until that rename, the archive is what counts.

**The rule recovery relies on:** if the unzipped copy is there (in `_/temp/unzipped`), it holds everything, and the archive may be older. If it isn't, the archive holds everything. A `.new` archive and anything in `_/temp/zipping` are only ever half done, and are deleted (`Recover`, `RecoverWorkDirectory`). The tests stop at every step (`ArchiveStep`): they copy what is on disk at that moment, recover the copy, and check that it still holds everything (`FolderArchiverTests`).

Neither may run twice at once for the same folder, nor while its unzipped copy is being changed: that is up to the caller (a lock for each folder, with the next step).

### Flushing to the disk

A power cut loses what the system had not written to the disk yet, and not necessarily in the order it was written. So each step flushes what the next relies on (`DurableFiles`): the new archive before it is checked, the directory of the archive after the rename (before the copy is deleted), and each unzipped file and directory before the copy is renamed into place. Otherwise a power cut right after unzipping could leave files that are empty, which would then count as the folder's contents and be zipped as they are.

On Linux a directory is flushed with `fsync` of the C library, which .NET has no call for. Windows has no such step: NTFS keeps changes to directories in its journal. A rename on Windows (`File.Move`) is not written through to the disk at once; if a power cut loses it, the old archive and the unzipped copy are still there, and the copy counts.

## Edge cases

| Case | What happens |
|---|---|
| The app stops while the new archive is written, checked, or before it replaces the old one | The old archive and the unzipped copy are untouched; the `.new` is deleted at the next start, and the copy still counts. |
| The app stops after the new archive replaced the old one, before the copy is gone | The copy and the archive hold the same; the copy counts, and zipping it again changes nothing. |
| The app stops while the copy is deleted | It was renamed into `_/temp/zipping/deleting` first, so it is no copy any more; the rest of it is deleted at the next start. |
| The app stops while unzipping | What was unzipped is in `_/temp/zipping/unzipping` and is deleted at the next start; the archive counts. |
| The new archive doesn't hold what was zipped (a disk or library fault) | It is deleted, the zipping fails, and the old archive and the copy stay. |
| Taking the copy away fails, after the archive was replaced (a file of it is open, on Windows) | The zipping fails; the copy is still there and counts, and the archive holds the same. |
| Deleting what was taken away fails | Logged as a warning, and tried again at the next start. Zipping itself worked. |
| A wrong password, or a damaged archive | `ArchiveUnreadableException`; nothing is unzipped. |
| An entry of the archive whose path leads out of the folder (`..`), or that no file can be named on this system | The archive can't be unzipped (`ArchiveUnreadableException`). A leading `/` is taken off. |
| An entry that is there twice, or twice but for case on a file system that doesn't tell them apart | The archive can't be unzipped; neither replaces the other. |
| `\` and `:` in names | Fine on Linux, where they can be in a name; on Windows `\` in an archive counts as a `/`. |
| A link (symbolic link, junction) in the unzipped copy | Left out of the archive, with a warning in the log: it could point anywhere. The app itself never makes links. |
| Hidden and system files, and the folder's `_` (its history) | Go into the archive like anything else. |
| Read-only files (git writes its objects that way) | Zipped like the others; unzipped as normal files. |
| Times of the files | Kept (a 7z keeps them as UTC, a zip as local time, to two seconds). Times of directories are not kept. |
| An empty folder | Zips to an archive with nothing in it, which unzips to an empty folder. |
| Large folders | Unzipping reports how far it got; zipping and unzipping take time and processor, which is why they run outside the app's global lock (next step). Each unzipped file is flushed to the disk, which makes unzipping many small files slower. |
