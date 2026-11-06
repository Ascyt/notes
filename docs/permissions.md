# Permissions

Who may see and change what is written down in a file called `.config.json` in each directory of the notes. Only admins (users with the `admin` realm role) can see or change these files. Everything else about what the permissions *mean* is in [security-specification.md](security-specification.md); this document is about how to write them.

## Editing

Admins have a gear button below the breadcrumb of every directory and file (leftmost in a directory, right of Save in a file; shortcut `c`). It opens the permissions of that entry at `?config`, in an editor. Save checks the text completely and says what is wrong, and saves it exactly as written (nothing is reformatted). Reset puts the default into the editor (nothing changes until Save).

- For a **directory** the editor shows its whole `.config.json`.
- For a **file** it shows only that file's entry in the `.config.json` of the directory it is in, without the `name`.

`.config.json` is never listed, can't be opened or created by any other means, and is left out of downloads and copies by everyone but admins. For admins it is part of a zip download of a directory, and of a copy or move of it. When a non-admin copies or moves a directory, the copy has no permissions of its own and has those of where it went.

## The file

JSON, in which comments are allowed (`//` and `/* */`). Everything is checked strictly: an unknown or repeated property, a value of the wrong type or a misspelled principal is an error.

```jsonc
{
    "perms": {
        "inherit": true,                      // required
        "rwAllow":      ["g:team-a"],         // may read and write
        "rwReadOnly":   ["g:team-b", "r:viewer", "u:alice"],   // may read
        "rwDeny":       [],                   // may not read
        "cdAllow":      ["g:team-a"],         // may create and delete
        "cdCreateOnly": ["u:alice"],          // may create
        "cdDeny":       []                    // may neither
    },
    "files": [                                // required: one entry per file, kept up to date by the app
        { "name": "plan.md", "perms": { "inherit": true } },
        { "name": "secret.md", "perms": { "inherit": false, "rwAllow": ["u:alice"] } }
    ]
}
```

- **Principals** are `u:<user name>`, `g:<group>` and `r:<role>`. User names are not case sensitive, group and role names are. A user also matches `u:<id>` with their Keycloak id.
- **`a`** stands for **all users**, logged in or not, so a user who is logged in never gets less than one who is not. Anonymous users can never change anything, so `a` is only allowed in `rwReadOnly`, `rwDeny` and `cdDeny`; in `rwAllow`, `cdAllow` or `cdCreateOnly` it is an error (400 when saving, and a configuration that has it is invalid, which closes it to everyone but admins). Pages of the notes tell anonymous users that they may need to log in to see more or to change anything. Mind that `a` in `rwDeny` or `cdDeny` denies everyone (except admins), those in a group included.
- **`l`** stands for **all users who are logged in**, so that no group with everyone in it has to be made in Keycloak. It is allowed in every list, e.g. `"rwAllow": ["l"]` lets every logged-in user write, while `"rwReadOnly": ["a"]` lets the public read.
- **Only `inherit` is required** in a `perms` block; a list that is missing is empty.
- **A principal can be in only one of the `rw` lists, and in only one of the `cd` lists** (it may be in one of each). Anything else is an error when saving.
- **`files`** has an entry for every file of the directory. The app adds one when a file is created, uploaded, copied or moved in, renames it when the file is renamed, and removes it when the file is deleted or moved away; saving the directory's permissions adds the ones that are missing. Entries are for files only: a directory has its own `.config.json`. The permissions of a file do not travel when it is copied or moved. A file without an entry has the permissions of its directory.

## What they mean

`rw` is about the contents: *read* lets a user open, download, select and copy it; *write* also lets them save a file. `cd` is about its existence: *create* lets them bring entries into a directory (or a file into existence at a path), *delete* also lets them delete, move and rename it. The details, and what else is needed for an operation (for example reading a directory in order to create anything in it), are in the specification.

For a user, each of the two kinds is decided on its own:

1. The layers that apply are looked at from the most specific: the file's own entry (for a file), then the permissions of its directory, then those of the directory above, and so on up to the root. A layer with `"inherit": false` is the last one looked at.
2. The first layer in which any of the user's principals (their name, groups and roles) is in one of the lists decides. **Within that layer, a deny beats an allow, and an allow beats read-only / create-only.**
3. If no layer says anything, the user gets nothing.
4. Admins are not subject to any of this, and neither is a missing `.config.json`: **without any configuration only admins have access.**

So `inherit: true` with empty lists means "as above", and `inherit: false` with empty lists closes the entry to everyone but admins. A more specific layer can both take away and give: it overrides what is above for the principals it mentions.

`inherit` only decides where an entry's own permissions come from: it looks *up*, never down. A folder with `inherit: false` still passes its permissions on to its files and subfolders; each of them decides with its own `inherit` whether to take them. For `/a/b`: with `inherit: true`, `b` has what `a` has (whatever `a`'s `inherit` is), plus or minus `b`'s own lists; with `inherit: false`, `b` has only its own lists. A directory without a `.config.json` counts as `inherit: true` with empty lists.

**An invalid `.config.json`** (edited by hand, say) is never guessed at: until it is fixed, everyone but admins has no access to anything it applies to (the directory and, through inheritance, everything under it). The editor says what is wrong.

A user needs to read the root to get anywhere, and every directory above something to reach it. A directory or file that a logged-in user may not read is still listed by its name (with a lock): to hide something from them entirely, it has to be in a directory they may not read. Anonymous users don't see what they may not read at all.

## Keycloak

The app takes the permissions' principals from the session: the user name (`preferred_username`), the groups (`groups`) and the realm roles (`roles`). The realm file creates the mappers for the last two. Groups are what an admin will use most: create a group in the Keycloak console, add users to it, and write `g:<its name>` into the permissions. A group can also be given realm roles, which then show up as `r:<role>`.

For a realm that was imported before this existed, add the mapper by hand: *Clients* → `notes-web` → *Client scopes* → `notes-web-dedicated` → *Add mapper* → *By configuration* → **Group Membership**, with *Token Claim Name* `groups` and *Full group path* **off**, added to the ID token and access token. Also set the realm's *Access Token Lifespan* to a couple of minutes (the realm file uses 2) and the SSO session idle and maximum to 8 hours (the lifetime of the app's cookie).

### Changes in Keycloak take effect within minutes

Groups and roles are copied from the tokens into the session when a user logs in. To keep them current, the access token is short-lived, and when it has run out the app gets a new one with the refresh token, which carries the groups and roles the user has by then. So a change takes effect within the access token lifespan, with no need to log in again.

If Keycloak says the session is over (the user logged out elsewhere, was disabled or deleted, or the SSO session expired), the user is logged out and the next page tells them so. If Keycloak can't be reached when a token is to be renewed, the session is kept for ten minutes and then ended, with a notice. Refresh token rotation ("Revoke Refresh Token") must stay off in the realm: requests that arrive together would otherwise each use the same refresh token.
