# Configuration

What applies to a directory of the notes and the files in it is written down in a file called `.config.json` in that directory: who may see and change what (see [Permissions](permissions.md)), and whether the [history](history.md) keeps them. Only admins (users with the `admin` realm role) can see or change these files.

## Editing

Admins have a gear button below the breadcrumb of every directory and file (leftmost in a directory, right of Save in a file; shortcut `c`). It opens the configuration of that entry at `?config`, in an editor. Save checks the text completely and says what is wrong, and saves it exactly as written (nothing is reformatted). Reset puts the default into the editor (nothing changes until Save). The magnifying glass icons zoom the editor, as in the [editor of a file](../usage/viewing-and-editing.md#the-editors), with which it shares the zoom.

- For a **directory** the editor shows its whole `.config.json`.
- For a **file** it shows only that file's entry in the `.config.json` of the directory it is in, without the `name`.

Below the editor is where the entry is on disk (its physical path), which tells which folder its `.config.json` is in, also for a [bridge](bridges.md).

`.config.json` is never listed, can't be opened or created by any other means, and is left out of downloads and copies by everyone but admins. For admins it is part of a zip download of a directory, and of a copy or move of it. When a non-admin copies or moves a directory, the copy has no configuration of its own and has that of where it went.

## The file

JSON, in which comments are allowed (`//` and `/* */`). Everything is checked strictly: an unknown or repeated property, a value of the wrong type or a misspelled principal is an error.

```jsonc
{
    "version": "1.6.0",                       // the version of this format, see "Versions and migrations"
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
    "passwordHash": "$2a$12$…",               // optional: the hash of a password that protects the directory, see Passwords
    "archive": { "enabled": false, "filetype": "7z", "compressionLevel": 2, "encrypted": false, "onLowestLevel": false },
    "files": [                                // required: one entry per file, kept up to date by the app
        { "name": "plan.md", "perms": { "inherit": true } },
        { "name": "secret.md", "perms": { "inherit": false, "rwAllow": ["u:alice"] } },
        { "name": "diary.md", "perms": { "inherit": true }, "passwordHash": "$2a$12$…" },
        { "name": "photos.zip", "perms": { "inherit": true }, "unzipPending": true }
    ]
}
```

- **`version`** is the version of the format of the file, `major.minor.patch` (see [Versions and migrations](#versions-and-migrations)). The app keeps it up to date; it is not changed by hand. Saving a directory's configuration needs it, and refuses one that is newer than the app's.
- **`perms`** are the permissions of the directory, which its files and subdirectories start from. How they are written and what they mean is in [Permissions](permissions.md).
- **`gitignore`** says whether the [history](history.md) leaves out the files of the directory and of everything below it. A subdirectory with a `.config.json` of its own decides for itself (so `false` there brings it back); one without follows the directory above. The `.config.json` files themselves are always kept, also in a directory that is left out.
- **`files`** has an entry for every file of the directory. The app adds one when a file is created, uploaded, copied or moved in, renames it when the file is renamed, and removes it when the file is deleted or moved away; saving the directory's configuration adds the ones that are missing. Entries are for files only: a directory has its own `.config.json`. The permissions of a file do not travel when it is copied or moved. A file without an entry has the permissions of its directory.

- **`password`** (in the editor) or **`passwordHash`** (as it is saved), of the directory or of a file's entry, protects it with a password that everyone needs to open it, admins included. Saving replaces a password with its hash. `"password": null` is no password; a new configuration has it, so that it says how one is set. How it works is in [Passwords](passwords.md).

- **`unzipPending`** and **`unzipError`** (a file's entry only) are the app's: an uploaded archive that is being unzipped into a directory next to it (see [Organizing](../usage/organizing.md#uploading)), and why that failed. Removing `unzipPending` stops the unzipping (what was unzipped so far is thrown away); removing `unzipError` takes the red mark away. Neither is kept when the file is renamed. Setting `unzipPending` by hand on a `.zip` or `.7z` unzips it once its directory is listed.

- **`archive`** (directories only) keeps the directory in an archive, `_.7z` or `_.zip` inside it, which is unzipped while it is used and zipped again after a while (see [Archived directories](archived-directories.md)). `enabled` is required in it; the rest have defaults: `filetype` `"7z"` or `"zip"` (default 7z), `compressionLevel` from 0 (none; a 7z, which always compresses, the least) to 3 (most), default 2, `onLowestLevel` (default false) archives only the directories below that hold files, instead of this one. `encrypted` (default false) encrypts the archive with the directory's password, which it then needs ([Encrypted](archived-directories.md#encrypted)). Without `archive`, or with `"archive": null`, a directory is not archived, unless a directory above says `onLowestLevel`; a new configuration has it, with every setting. Setting `enabled` to `false` on an archived directory turns it back into an ordinary one.

A directory without a `.config.json` says nothing: for each setting, the directory above decides.

## Versions and migrations

The format of `.config.json` changes over time (a new required property, a property that moves into an object, ...). So that files that were written for an older format keep working, every file says which version of the app wrote it (`version`), and the app brings older files up to date.

- The version of the format is the app's own (`ConfigVersions.Current` in the code, `major.minor.patch`, e.g. `1.2.0`), not a setting: it changes with the app, which knows the migrations up to it.
- A file **without** a `version` is from before there were versions: it counts as `0.0.0`.
- Not every version needs a migration. When a file is read, and its version is lower than the app's, every migration after its version up to the current one is applied, oldest first (there may be none), and the file gets the current version. A file with the current version is not touched. A file with a **higher** version (written by a newer app, then rolled back) is never guessed at: it counts as invalid, so only admins have access where it applies, and the editor says why.
- The migrated text is what the app uses and what the editor shows. It is **written to disk** when the app starts (every `.config.json` under the notes is looked at once), and by anything that writes the file anyway (saving it, or adding, renaming and removing a file's entry). A file that can't be migrated (a migration fails) is left as it is, logged, and counts as invalid.
- **Comments:** a file that goes through a migration that changes its structure is written again in the standard layout, without its comments (text can't follow such a change). A migration that only adds something works on the text and keeps everything, comments included, as does a file for which only the version has to be set (no migration lies between).

The migrations there are:

| Version | What changes |
|---|---|
| 1.1.0 | `gitignore` comes in, and every file gets `"gitignore": false`: everything stays in the history. |
| 1.2.0 | `historyAllow`, `historyExistingOnly` and `historyDeny` come in. They are optional, so files only get the new version: nobody but admins may see the history until it is given. |
| 1.3.0 | `password` and `passwordHash` come in (see [Passwords](passwords.md)). They are optional, so files only get the new version. |
| 1.4.0 | A directory without a password gets `"password": null` before `files`, with the comment a new configuration has there, so that every file says how a password is set. This migration works on the text: comments and layout stay. (A file's entry doesn't get one; the comment says that it can.) |
| 1.5.0 | `archive` comes in. Every directory gets it, not enabled, with every setting at its default and a comment, before `files` (on the text, so comments stay). |
| 1.6.0 | `unzipPending` and `unzipError` come in, in the entries of files. They are optional, so files only get the new version. |

How a migration is added is in [Migrations of the configuration files](../development/config-migrations.md).
