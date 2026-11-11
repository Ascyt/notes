# Configuration

What applies to a directory of the notes and the files in it is written down in a file called `.config.json` in that directory: who may see and change what (see [Permissions](permissions.md)), and whether the [history](history.md) keeps them. Only admins (users with the `admin` realm role) can see or change these files.

## Editing

Admins have a gear button below the breadcrumb of every directory and file (leftmost in a directory, right of Save in a file; shortcut `c`). It opens the configuration of that entry at `?config`, in an editor. Save checks the text completely and says what is wrong, and saves it exactly as written (nothing is reformatted). Reset puts the default into the editor (nothing changes until Save). The magnifying glass icons zoom the editor, as in the [editor of a file](../usage/viewing-and-editing.md#the-editors), with which it shares the zoom.

- For a **directory** the editor shows its whole `.config.json`.
- For a **file** it shows only that file's entry in the `.config.json` of the directory it is in, without the `name`.

`.config.json` is never listed, can't be opened or created by any other means, and is left out of downloads and copies by everyone but admins. For admins it is part of a zip download of a directory, and of a copy or move of it. When a non-admin copies or moves a directory, the copy has no configuration of its own and has that of where it went.

## The file

JSON, in which comments are allowed (`//` and `/* */`). Everything is checked strictly: an unknown or repeated property, a value of the wrong type or a misspelled principal is an error.

```jsonc
{
    "version": "1.2.0",                       // the version of this format, see "Versions and migrations"
    "perms": {                                // required: who may do what, see Permissions
        "inherit": true,                      // required
        "rwAllow":      ["g:team-a"],         // may read and write
        "rwReadOnly":   ["g:team-b", "r:viewer", "u:alice"],   // may read
        "rwDeny":       [],                   // may not read
        "cdAllow":      ["g:team-a"],         // may create and delete
        "cdCreateOnly": ["u:alice"],          // may create
        "cdDeny":       [],                   // may neither
        "historyAllow": ["g:team-a"],         // may see earlier states, also of what was deleted since
        "historyExistingOnly": ["u:alice"],   // may see earlier states of what exists now
        "historyDeny":  []                    // may not
    },
    "gitignore": false,                       // required: whether the history leaves this directory out
    "files": [                                // required: one entry per file, kept up to date by the app
        { "name": "plan.md", "perms": { "inherit": true } },
        { "name": "secret.md", "perms": { "inherit": false, "rwAllow": ["u:alice"] } }
    ]
}
```

- **`version`** is the version of the format of the file, `major.minor.patch` (see [Versions and migrations](#versions-and-migrations)). The app keeps it up to date; it is not changed by hand. Saving a directory's configuration needs it, and refuses one that is newer than the app's.
- **`perms`** are the permissions of the directory, which its files and subdirectories start from. How they are written and what they mean is in [Permissions](permissions.md).
- **`gitignore`** says whether the [history](history.md) leaves out the files of the directory and of everything below it. A subdirectory with a `.config.json` of its own decides for itself (so `false` there brings it back); one without follows the directory above. The `.config.json` files themselves are always kept, also in a directory that is left out.
- **`files`** has an entry for every file of the directory. The app adds one when a file is created, uploaded, copied or moved in, renames it when the file is renamed, and removes it when the file is deleted or moved away; saving the directory's configuration adds the ones that are missing. Entries are for files only: a directory has its own `.config.json`. The permissions of a file do not travel when it is copied or moved. A file without an entry has the permissions of its directory.

A directory without a `.config.json` says nothing: for each setting, the directory above decides.

## Versions and migrations

The format of `.config.json` changes over time (a new required property, a property that moves into an object, ...). So that files that were written for an older format keep working, every file says which version of the app wrote it (`version`), and the app brings older files up to date.

- The version of the app is **`Config:Version`** in `appsettings.json` (`major.minor.patch`, e.g. `1.2.0`). The app doesn't start if it isn't a version.
- A file **without** a `version` is from before there were versions: it counts as `0.0.0`.
- Not every version needs a migration. When a file is read, and its version is lower than `Config:Version`, every migration after its version up to the current one is applied, oldest first (there may be none), and the file gets the current version. A file with the current version is not touched. A file with a **higher** version (written by a newer app, then rolled back) is never guessed at: it counts as invalid, so only admins have access where it applies, and the editor says why.
- The migrated text is what the app uses and what the editor shows. It is **written to disk** when the app starts (every `.config.json` under the notes is looked at once), and by anything that writes the file anyway (saving it, or adding, renaming and removing a file's entry). A file that can't be migrated (a migration fails) is left as it is, logged, and counts as invalid.
- **Comments:** a file that goes through a real migration is written again in the standard layout, without its comments (a migration changes the structure, which text can't follow). A file for which only the version has to be set (no migration lies between, which is the case for most versions) keeps everything, comments included.

The migrations there are:

| Version | What changes |
|---|---|
| 1.1.0 | `gitignore` comes in, and every file gets `"gitignore": false`: everything stays in the history. |
| 1.2.0 | `historyAllow`, `historyExistingOnly` and `historyDeny` come in. They are optional, so files only get the new version: nobody but admins may see the history until it is given. |

How a migration is added is in [Migrations of the configuration files](../development/config-migrations.md).
