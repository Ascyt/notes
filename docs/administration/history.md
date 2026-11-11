# History

The app keeps a history of the notes in a git repository of its own, so that what was changed, and by whom, can be found again. Who may see earlier states of the notes in the app, and how, is in the [history specification](../development/history-specification.md); the repository itself is for admins with access to the server.

## Where it is

The repository is `_/history` in the notes folder, next to the app's other data in `_`. It is a bare repository whose work tree is the notes folder, so nothing of it (no `.git`) appears among the notes, and `_` itself is never part of the history. It is made when the app first starts, beginning with an empty commit. It is part of the notes folder, so it is part of every backup of it (see [Deployment](deployment.md#running-it)), and it moves along when the folder does.

Git itself can read it, from the notes folder:

```sh
git --git-dir=_/history log --stat
git --git-dir=_/history show HEAD~3:plans/plan.md
```

Only read it this way: the app expects to be the only one that writes to it.

Git refuses a repository whose folders belong to another user than the one it runs as. The app doesn't check this for its own repository, as the notes folder is often mounted with an owner of its own (root, in Docker Desktop). Git itself still does: if it says the folder has "dubious ownership", add `-c safe.directory='*'` after `git` (for that command only).

## When it commits

Changes are collected and committed when:

- **Nothing was changed in the app for a while:** `Config:History:IdleMinutes` (10 by default). A background job looks every minute.
- **Many lines changed** since the last commit: when the added lines plus the removed lines times `Config:History:RemovedLineWeight` (2 by default: what is removed is gone from the notes) reach `Config:History:LineThreshold` (200 by default).
- **Another user** (or something outside the app) changed what a user is about to change: what is there is committed first, so that each change stays theirs.
- **A binary file that is in the history is deleted.** Binary files have no lines, so they would otherwise wait for the next commit.
- **A configuration changes what the history keeps** (`gitignore`, see [What it keeps](#what-it-keeps)).
- **The app starts**, and something changed while it didn't run. When the app has brought the [configuration files](configuration.md#versions-and-migrations) up to date, that is a commit of its own.

When a file is **saved**, this is decided before it is written: if the save would reach the threshold, or another user changed the file, what was there before is committed, and the save comes into the next commit. Everything else (creating, uploading, renaming, copying, moving, deleting) is committed with the operation when it reaches the threshold, as what it brings can't be told before.

The message of a commit is a keyword that says why it was made, not text: the list of changes in the app turns it into text (see the [history specification](../development/history-specification.md#the-list-of-changes)). Keywords start with `#`, so that text can follow them one day and still be told apart. They are stored in the history, so a keyword never changes once it is released (`CommitKeywords` in the code).

| Message | Why |
|---|---|
| `#init` | The empty commit the history begins with. |
| `#idle` | Nothing was changed for a while. |
| `#threshold` | Many lines changed. |
| `#other-user` | Another user (or something outside the app) changed what a user is about to change. |
| `#binary-deleted` | A binary file that is in the history was deleted. |
| `#startup` | Something changed while the app didn't run. |
| `#migration` | The app brought the configuration files up to date. |
| `#before-gitignore` | A configuration was about to change what the history keeps: what was pending until then. |
| `#gitignore` | A configuration changed what the history keeps: the configuration, and the files that leave or come back with it. |

## Who commits

Each commit is by the user who made its changes: their user name, and their email, or their Keycloak id if they have none (git needs something there). When changes of several users are pending, each user gets a commit of their own. What nobody changed through the app (files edited on the server, changes from before the app started, the commits the app makes on its own) is by `_`, with `_` as its email too. The list of changes in the app shows the user name to those who may see the change, never the email or id.

## What it keeps

Every file of the notes, unless the [configuration](configuration.md#the-file) of its directory says `"gitignore": true`: then the files of that directory and below are left out (a subdirectory with its own configuration decides for itself). The `.config.json` files are always kept.

When a configuration saved in the app changes what is kept, that is committed right away, in two steps:

1. What was pending until then is committed first (`#before-gitignore`), by whoever changed it, so that no change to the files that are about to be left out is lost.
2. Then the configuration is saved and committed (`#gitignore`), by whoever saved it, together with the files that leave the history (or come back, for `"gitignore": false`).

Files that leave the history stay on disk, and their earlier versions stay in the commits that have them: `gitignore` never removes anything from what is already in the history. A configuration changed outside the app takes effect with the next commit, without these two steps.

What is left out is worth thinking about for large binary files (videos, archives): the history keeps every version of every file it keeps, so it grows with each of them.

## Settings

In `appsettings.json` (or the environment, e.g. `Config__History__IdleMinutes`):

| Setting | Default | |
|---|---|---|
| `Config:History:Enabled` | `true` | Whether there is a history at all. |
| `Config:History:IdleMinutes` | `10` | Minutes without a change after which what is pending is committed. |
| `Config:History:LineThreshold` | `200` | Changed lines after which what is pending is committed at once. |
| `Config:History:RemovedLineWeight` | `2` | How much a removed line counts, compared to an added one. |

If something goes wrong with the history, what the user did still happens: the error is in the log.
