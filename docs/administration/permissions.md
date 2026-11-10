# Permissions

Who may see and change what is written down in the `perms` blocks of the [configuration](configuration.md) of each directory (`.config.json`): one for the directory, and one in the entry of each of its files. Only admins (users with the `admin` realm role) can see or change them, with the gear button (see [Editing](configuration.md#editing)). Everything else about what the permissions *mean* is in the [security specification](../development/security-specification.md); this document is about how they are written.

## Writing them

```jsonc
"perms": {
    "inherit": true,                      // required
    "rwAllow":      ["g:team-a"],         // may read and write
    "rwReadOnly":   ["g:team-b", "r:viewer", "u:alice"],   // may read
    "rwDeny":       [],                   // may not read
    "cdAllow":      ["g:team-a"],         // may create and delete
    "cdCreateOnly": ["u:alice"],          // may create
    "cdDeny":       []                    // may neither
}
```

- **Principals** are `u:<user name>`, `g:<group>` and `r:<role>`. User names are not case sensitive, group and role names are. A user also matches `u:<id>` with their Keycloak id.
- **`a`** stands for **all users**, logged in or not, so a user who is logged in never gets less than one who is not. Anonymous users can never change anything, so `a` is only allowed in `rwReadOnly`, `rwDeny` and `cdDeny`; in `rwAllow`, `cdAllow` or `cdCreateOnly` it is an error (400 when saving, and a configuration that has it is invalid, which closes it to everyone but admins). Pages of the notes tell anonymous users that they may need to log in to see more or to change anything. `a` in `rwDeny` or `cdDeny` denies everyone (except admins), those in a group included.
- **`l`** stands for **all users who are logged in**, so that no group with everyone in it has to be made in Keycloak. It is allowed in every list, e.g. `"rwAllow": ["l"]` lets every logged-in user write, while `"rwReadOnly": ["a"]` lets the public read.
- **Only `inherit` is required** in a `perms` block; a list that is missing is empty.
- **A principal can be in only one of the `rw` lists, and in only one of the `cd` lists** (it may be in one of each). Anything else is an error when saving.

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

The app takes the permissions' principals from the session: the user name (`preferred_username`), the groups (`groups`) and the realm roles (`roles`). The realm file creates the mappers for the last two. Groups are what an admin uses most: a group made in the Keycloak console, with users added to it, is `g:<its name>` in the permissions. A group can also be given realm roles, which then show up as `r:<role>`.

For a realm that was imported before this existed, the mapper is added by hand: *Clients* → `notes-web` → *Client scopes* → `notes-web-dedicated` → *Add mapper* → *By configuration* → **Group Membership**, with *Token Claim Name* `groups` and *Full group path* **off**, added to the ID token and access token. The realm's *Access Token Lifespan* is set to a couple of minutes (the realm file uses 2). The session lifetimes are in [Sessions](sessions.md#keycloak).

### Changes in Keycloak take effect within minutes

Groups and roles are copied from the tokens into the session when a user logs in. To keep them current, the access token is short-lived, and when it has run out the app gets a new one with the refresh token, which carries the groups and roles the user has by then. So a change takes effect within the access token lifespan, with no need to log in again.

What happens when Keycloak refuses, or can't be reached, is in [Sessions](sessions.md#when-a-session-ends).
