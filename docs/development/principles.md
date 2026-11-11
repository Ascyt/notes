# Principles

The rules every change to this repository follows. Not everything follows them perfectly yet; anything that doesn't is something to fix, not an example to copy.

## The project

### 1. It is never finished

The app keeps changing, and so does everything around it. Code, comments and documents are written so that they stay true when something is added:

- Rules and reasons are written down, not lists of what currently exists. "Admin-only pages use the admin policy" stays true; "the Hangfire policy is the one other place that mentions the admin role" doesn't, as soon as a second admin page comes along.
- Nothing is left over from an older version: no outdated comments, no code that nothing uses, no text that describes how something used to work.
- A feature is complete when it comes in, not a draft to be finished later: it works with the keyboard, on phones and in both themes, says why something can't be done, is tested, documented and in the [security specification](security-specification.md) where it touches who may do what.

### 2. Modular and customizable

Each feature is self-contained (see [principle 17](#17-code-that-reads-easily)), so that it can be changed or removed without touching the others. What one installation may want differently from another (the title, texts, which features are there) is configuration, not code. Much is still missing here: customizability is a direction to move in, and new code doesn't make it harder.

## Security

### 3. The security specification comes first

[The security specification](security-specification.md) says who may do what, and it is the source of truth for the code and the tests. A new feature goes into it before or together with the code. Anything a user may not access answers the same 404 as something that doesn't exist, so nobody can find out what is there by trying.

### 4. Nothing about how the site is run is shown to users

The code is open source, so how the app works is no secret. How a particular site is run is: physical paths, server and host names, stack traces, configuration, and why something was refused beyond what the user may know. Anonymous users and users who aren't admins never see any of it. Admins may. Everything else goes to the log.

### 5. When in doubt, deny

If something can't be checked, the answer is no. An invalid permissions file gives access to admins only, a session that can't be checked with Keycloak is ended, and an error during a check refuses.

## The interface

These are guidelines rather than laws: where breaking one makes the interface clearly better, it is broken on purpose.

### 6. Everything works with the keyboard

Every action has a shortcut: a `key` attribute on the button, link or field (the top of `wwwroot/shortcuts/shortcuts.js` says how to write one). Holding Right Ctrl shows all shortcuts of the page.

- **The rarer an action, the longer its shortcut.** A single key is for what is done all the time on that page (`e` to edit, `Delete`, the numbers for the entries). Something done now and then gets a modifier (Alt+PageDown to download). Something rare or dangerous gets several (Ctrl+Alt+Shift+Delete for the kill switch).
- **A group of rarely used links gets one shortcut for all of them.** The shortcut is on the element around them (`tabindex="-1"`), which it focuses, and Tab and Enter go from there. The links in the footer work like this.
- **Ctrl+Alt with a letter is avoided.** On many keyboard layouts (German, for instance) it is AltGr, which types characters such as € or @.
- Inside a dialog, Enter confirms and Escape cancels.

### 7. Works on phones, in light and dark

Every page works at phone width and in both themes. That is why there is a simple editor next to Monaco, which doesn't work well on phones.

### 8. Icons first

A descriptive icon matters more than a label: the toolbar has icons only. Every icon that does something has its description as tooltip and `aria-label`, so it can still be found and read out.

### 9. Gray unless it should stand out

Everything is gray (Bootstrap's secondary styles, low emphasis), unless it is something that should catch the eye. For example:

- the links to the files and directories, which is where the page leads,
- the Save button while there is something to save,
- a button that deletes something, but only while it is hovered or focused (`btn-danger-on-hover`, or the toolbar's danger tone),
- a warning that something went wrong.

### 10. Nobody is left wondering why

What a user may not do at all isn't offered to them: someone who may change nothing in a directory doesn't see Upload or Delete there, and someone without the history doesn't see its button. A control that is there but can't be used right now, or not here, is disabled and always says why (as its tooltip, from `data-disabled-reason`): nothing selected yet, nothing to save, one entry in the directory that may not be deleted. Where hiding would make no sense, things are shown and say why they can't be used: an entry that can't be opened is still listed with a lock, as its name is visible anyway (see the security specification). What someone may not even know exists is never shown or named (see [principle 4](#4-nothing-about-how-the-site-is-run-is-shown-to-users)). An error says what went wrong and, where it helps, what can be done about it.

### 11. Neutral wording

Texts in the app and in the documents are neutral, descriptive and informative. They don't talk to the reader: "The file has unsaved changes" rather than "You have unsaved changes", "Logging in again is needed" rather than "Please log in again". The passive is fine and often the better choice.

## Structure

### 12. Addresses are the notes

Every address is a path of the notes (a virtual directory or file), except those that start with `/_/`, which belong to the app itself (`/_/Login`, `/_/About`, …). These are written in PascalCase.

### 13. Another view is not another address

Different ways to look at the same thing share its address. They are told apart by a query parameter (`?edit`, `?raw`, `?config`) or by a choice kept in the browser (the simple editor, the theme), never by another path.

### 14. Data lives with the notes, not in a database

Notes are files and folders on disk, and what the app keeps about them lives with them: the configuration of a folder (such as its permissions) in its `.config.json`, anything that belongs to all of the notes (or isn't a note, like the temporary files of uploads, or their history) in the `_` directory at their root. Zipping the folder of the notes takes everything along. There is no database to set up, back up or migrate; only Keycloak has one, as it handles the logins. What the app keeps besides that lives in memory (selections, background jobs) and may be lost on a restart.

Later, some virtual directories may be stored in physical directories of their own. Data that belongs to a directory is therefore kept with that directory, and nothing assumes that all of the notes are under one physical root.

### 15. The app serves its own files

Scripts, styles and fonts come from the app itself (put in `wwwroot/lib` with LibMan), never from a CDN at runtime, so the browser only talks to the app and to Keycloak. Content that users put into their notes, such as an image in a Markdown file, may come from anywhere.

### 16. Existing installations keep working

A change to a format that is stored raises its version and comes with a migration, as for `.config.json` (see [Configuration](../administration/configuration.md#versions-and-migrations)). Keycloak imports the realm file only once, so a change to `keycloak/notes-realm.json` comes with the steps to make it by hand on a realm imported before (see [Deployment](../administration/deployment.md#a-realm-that-was-imported-before)).

## Code

### 17. Code that reads easily

- Names say what something is, without cryptic abbreviations.
- Few comments, short and simple, that say why, not what.
- Every view and partial gets its data through a typed viewmodel, not through loose variables or `ViewData`.
- Code is grouped by feature (`Features/<feature>/`), not by kind (all controllers in one place, all services in another).
- Files stay short. One that grows past around 250 lines is split up along what its parts do (deciding, storing, looking up, …), not into a first and a second half; tests are split by topic, with what they share in a base class. This is a guide, not a limit: a file that clearly reads better in one piece stays one.
- Namespaces always follow the folders: what is in `notes/Pages/Editor/Markdown` is in `notes.Pages.Editor.Markdown`. That is why the pages of the app itself are in `Pages/App`, with their `/_/` addresses given in their `@page`: a folder named `_` would make a namespace `_`, which hides the discard.
- A return value that isn't used is discarded on purpose (`_ = Directory.CreateDirectory(path);`), so that it is clear it wasn't forgotten. Calls that only return what they were called on, to be chained (builders, `StringBuilder.Append`), and assertions are left as they are.

### 18. Few dependencies

Before writing something, look for a built-in ASP.NET feature or an existing library that does it. But every new package, service or container is discussed first: each one has to be kept up to date and trusted.

## Documentation

### 19. Written for someone new

A feature comes with its documentation in `docs/`, in simple words, so that someone new to the repository can learn what the app is and how it works. The documents form a tree: each topic has an overview (`docs/<topic>.md`) and its details in a folder of the same name (`docs/<topic>/`). The app will show these documents itself, so links between them are relative and stay inside `docs/`, and files elsewhere in the repository are named, not linked.

Examples help more than descriptions. Pictures of the app come from `docs/screenshots`, which the UI tests take again with every run (see [Tests](tests.md#screenshots)), so they never go out of date; no other screenshots are put into the repository.

## Tests

### 20. Tested, without waiting

Every feature gets tests at the level that fits (see [Tests](tests.md)). The integration tests matter most: they test every endpoint as every kind of user, with every kind of attack that applies, much like a penetration test. Time comes from the injected `TimeProvider` wherever a test would otherwise have to wait (a selection that runs out, a background job that runs every hour), so that the test moves a fake clock instead.

### 21. Tests check logic, not wording or looks

Unit tests check methods, integration tests check endpoints, and UI tests check the scripts of the pages. None of them checks the exact wording of a text or anything about CSS, so that a sentence can be rephrased or a style changed without a test failing. Instead they check:

- the state that results: files on disk, the selection the server has, whether a box is ticked,
- what kind of answer it is: the status code, the kind of message (`data-kind` of a toast), whether a disabled control gives a reason at all (`data-disabled-reason`),
- that two answers are the same where they must be (a hidden entry answers exactly like a missing one).

Elements are found by their id or a `data-` attribute, not by their text or their classes. Text is only checked where it is data: a file name the message has to mention, or what a user wrote into a note.
