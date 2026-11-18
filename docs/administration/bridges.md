# Bridges

A bridge keeps a directory of the notes somewhere else on disk: another folder, another drive. In the app it is a directory like any other, at its place among the notes; only where its files are stored is different. For example, notes that take a lot of room can live on a large hard drive, while the rest of the notes stays on a fast one.

## Setting one up

Bridges are set in `Config:Bridges` of `appsettings.json` (next to `Config:Dir`, the folder of the notes), one entry each:

```json
"Config": {
  "Dir": "E:\\notes",
  "Bridges": [
    { "Virtual": "/hdd", "Physical": "H:\\notes" },
    { "Virtual": "/hdd/archive", "Physical": "J:\\archive", "History": { "Enabled": false } }
  ]
}
```

With this, `/hdd` is the folder `H:\notes`, so `/hdd/test` is `H:\notes\test` (and not `E:\notes\hdd\test`). `/hdd/archive` is `J:\archive`. The names of the settings may be written in any case (`"virtual"`, `"physical"`).

- **`Virtual`** is the path of the directory in the notes. Every name in it must be one the notes allow, and it can't be the root.
- **`Physical`** is the folder on disk, as a full path. It must exist when the app starts, and it must not be a link (give the folder the link leads to).
- **`History:Enabled`** says whether the bridge has a history of its own (see [below](#its-history)). It is `true` if it isn't given.

In Docker, the folder of a bridge is mounted into the container like the notes, and the settings are given as environment variables (`Config__Bridges__0__Virtual: /hdd`, `Config__Bridges__0__Physical: /bridges/hdd`, where `Physical` is the path inside the container). Both go into `compose.override.yaml`, which Docker Compose reads together with `compose.yaml` and which is not part of the repository, as the bridges are those of one site: `compose.override.example.yaml` is copied to it and adapted.

The app doesn't start, and its log says why, when a bridge:

- has a virtual path that can't be one (the root, a name like `_` or `..`), or the same as another bridge;
- has a physical folder that doesn't exist, isn't a full path, or is a link;
- has a physical folder that overlaps another: inside the folder of the notes, inside the folder of another bridge, or around one. The same files with two paths in the notes would have two sets of permissions;
- has no directory to be in: the directory above its virtual path (`/` for `/hdd`, `/hdd` for `/hdd/archive`) must exist, spelled as on disk.

## How it behaves

- **A bridge is a directory of the notes.** It is listed by its virtual name in the directory it is in, can be opened, downloaded and copied, and everything in it can be changed as anywhere else. Copies and moves between a bridge and the rest of the notes (also to another drive) are all or nothing, as any other.
- **A bridge stays where it is.** It can't be deleted, moved or renamed, and neither can a directory that has a bridge in it (at any depth): the configuration puts it there. Its name is taken in its directory, so nothing else can be created, uploaded, copied or renamed to it.
- **A bridge hides what is on disk where it is.** If the folder of the notes has a `hdd` of its own, the bridge `/hdd` hides it: it isn't part of the notes (nor of their history) while the bridge is there. The app's log names it when it starts.
- **Permissions go on through it.** The [configuration](configuration.md) of the bridge directory is the `.config.json` in its physical folder, and like any directory's it starts from that of the directory it is in (unless it says `inherit: false`). So is `gitignore`.
- **The app's own data of a bridge** (its temporary files, its history) is in a `_` folder in its physical folder, as it is in the folder of the notes. A copy into the bridge is first made there, on the same drive, and then put in place at once.
- **Only admins can tell that a directory is a bridge.** For everyone else it is a directory like any other: when a directory with a bridge in it can't be deleted or moved, they are told that the permissions don't allow it. The [configuration page](configuration.md#editing) shows admins where each entry is on disk.

## Its history

Each bridge whose `History:Enabled` is `true` has a [history](history.md) of its own: a git repository in `_/history` of its physical folder, whose work tree is that folder. It commits at the same times as the history of the notes (`Config:History`), and what goes from the notes into a bridge or out of it (a move, say) is in both histories. `Config:History:Enabled` is about the history of the folder of the notes only: each bridge has its own `History:Enabled`.

The app can't show the history of a bridge yet: below a bridge, there is no history button and no list of changes, and the earlier states are not shown (admins see the buttons disabled, with the reason). The repository can be read with git, from the physical folder of the bridge, as the history of the notes can.

## Backups

A bridge is not inside the folder of the notes, so a backup of the notes must take each bridge's physical folder as well. Each holds everything of its part of the notes: the files, their configuration, and their history.
