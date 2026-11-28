# Principles

The rules every change to this repository follows. Not everything follows them perfectly yet; anything that doesn't is something to fix, not an example to copy.

## The project

### 1. It is never finished

The app keeps changing, and so does everything around it. Code, comments and documents are written so that they stay true when something is added:

- Rules and reasons are written down, not lists of what currently exists. "Admin-only pages use the admin policy" stays true; "the Hangfire policy is the one other place that mentions the admin role" doesn't, as soon as a second admin page comes along.
- Nothing is left over from an older version: no outdated comments, no code that nothing uses, no text that describes how something used to work.
- A feature is complete when it comes in, not a draft to be finished later: it works with the keyboard, on phones and in both themes, says why something can't be done, is tested, documented and in the [security specification](security-specification.md) where it touches who may do what.

### 2. Modular and customizable

Each feature is self-contained (see [principle 20](#20-code-that-reads-easily)), so that it can be changed or removed without touching the others. What one installation may want differently from another (the title, which features are there) is configuration, not code. What a site's own code changes is a part of a [component](components.md): its texts (a class with virtual members, never settings), its styles, its script or its HTML, each in a file of its own that the site can replace alone. A text a user reads is never written into a view, a script or a handler. Much is still missing here: customizability is a direction to move in, and new code doesn't make it harder.

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

Every action has a shortcut: a `key` attribute on the button, link or field (the top of `Features/Shortcuts/shortcuts.js` says how to write one). Holding Right Ctrl shows all shortcuts of the page.

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
- a button that deletes something, but only while it is hovered or focused (`btn-danger-on-hover`),
- a warning that something went wrong.

### 10. Nobody is left wondering why

What a user may not do at all isn't offered to them: someone who may change nothing in a directory doesn't see Upload or Delete there, and someone without the history doesn't see its button. A control that is there but can't be used right now, or not here, is disabled and always says why (as its tooltip, from `data-disabled-reason`): nothing selected yet, nothing to save, one entry in the directory that may not be deleted. Where hiding would make no sense, things are shown and say why they can't be used: an entry that can't be opened is still listed with a lock, as its name is visible anyway (see the security specification). What someone may not even know exists is never shown or named (see [principle 4](#4-nothing-about-how-the-site-is-run-is-shown-to-users)). An error says what went wrong and, where it helps, what can be done about it.

### 11. Neutral wording

Texts in the app and in the documents are neutral, descriptive and informative. They don't talk to the reader: "The file has unsaved changes" rather than "You have unsaved changes", "Logging in again is needed" rather than "Please log in again". The passive is fine and often the better choice.

They are also short, without the words that only bind the others together: "Incorrect password." rather than "The password is wrong.", "Enter password" rather than "Enter the password", "Password-protected" rather than "Protected by a password, which opening it asks for". What a text has to say stays; only the padding goes.

### 12. Plain, without fluff

No animations: things appear, move and go away at once, without sliding, fading or growing. The only exception is what shows that something is still going on, such as a spinner. The same goes for other decoration that only makes the interface look busier (shadows that pulse, effects on hovering beyond a change of color, transitions between states): it hides what is going on rather than making it clearer. Something like it may come later, done properly and on purpose, but not by the way.

### 13. Long operations show that they run, and check again at the end

Whatever may take a while shows that it is running: a progress bar where it can be told how far it is (an upload, a copy or move, preparing an archive), a spinner only where that can't be told without great effort (renaming a directory with much in it). When building something long, plan for the bar: it is often only counting what was done against what was measured before. In the scripts, `whileBusy` (`site.js`) does this for one request:

- While it runs, leaving the page asks first (the browser's own warning), as its answer would be lost with the page.
- Where using the page meanwhile could get in its way (renaming again, changing what is being copied or archived), the page is covered until it is done (`blockPage`, with `_BusyOverlay`), and its shortcuts do nothing. The messages and the card of the selection stay above it, so its progress can still be seen.
- Work that knows how far it got is given `setProgress` (0 to 1), which shows a bar in the cover in place of the spinner.

Something that may take minutes is not done within the request that asks for it: a proxy in front of the app cuts off a request that gets no answer for a minute or so (nginx after 60 s by default). It runs on its own, the request is answered at once, and the page asks how far it got until it is done (as the archive does with `Prefer: respond-async` and `?progress`). Nobody asking for a while means the page was left, which cancels it.

On the server, such an operation checks the permissions when it starts and again when it ends, as they may change while it runs: an archive is checked once it is written against every entry in it, and it is not handed out if the user may no longer read one of them. Changes of structure hold the `StructureLock` instead, from their checks until they are done, so that what they checked can't change in between (see [User input](user-input.md#concurrency)).

## Structure

### 14. Addresses are the notes

Every address of the notes is their path (a virtual directory or file): `/docs/a.md` is `docs/a.md`, also where a site shows only some of them (`Config:Exposed`); addresses are never translated. The pages of the app itself are at its own address (`Config:AppPath`, `/_/Login`, `/_/About`, … by default), which comes first, and are written in PascalCase. Code never writes these addresses out: links are made with `VirtualPaths.ToAddress` and `asp-page`, the app's own with `AppAddresses`.

### 15. Another view is not another address

Different ways to look at the same thing share its address. They are told apart by a query parameter (`?edit`, `?raw`, `?config`) or by a choice kept in the browser (the simple editor, the theme), never by another path.

### 16. Data lives with the notes, not in a database

Notes are files and folders on disk, and what the app keeps about them lives with them: the configuration of a folder (such as its permissions) in its `.config.json`, anything that belongs to all of the notes (or isn't a note, like the temporary files of uploads, or their history) in the `_` directory at their root. Zipping the folder of the notes takes everything along. There is no database to set up, back up or migrate; only Keycloak has one, as it handles the logins. What the app keeps besides that lives in memory (selections, background jobs) and may be lost on a restart.

Some virtual directories are stored in physical directories of their own ([bridges](../administration/bridges.md)), each with its own `_`. Data that belongs to a directory is therefore kept with that directory, and nothing assumes that all of the notes are under one physical root: which directory an entry is in, and what is in a directory, is asked of `EntryLocations` (`src/Notes/Features/Bridges`), not read off the physical paths.

### 17. The app serves its own files

Scripts, styles and fonts come from the app itself (put in `wwwroot/lib` with LibMan), never from a CDN at runtime, so the browser only talks to the app and to Keycloak. Content that users put into their notes, such as an image in a Markdown file, may come from anywhere.

Scripts and styles are files next to the component they belong to (`_Name.js`, `_Name.css`, see [Components](components.md)), served as static assets of the app. Scripts are never inline: no `<script>` with code in a view and no `onclick=` and the like, which the site's policy doesn't run (`script-src 'self'`), and no `<style>` in a view. What a script needs from the server goes into `data-` attributes or a `<script type="application/json">` block (its texts with `<script-texts>`), and a toolbar button gets its listener with `onClickOf` (`site.js`).

### 18. Existing installations keep working

A change to a format that is stored raises its version and comes with a migration, as for `.config.json` (see [Configuration](../administration/configuration.md#versions-and-migrations)). Keycloak imports the realm file only once, so a change to `keycloak/notes-realm.json` comes with the steps to make it by hand on a realm imported before (see [Deployment](../administration/deployment.md#a-realm-that-was-imported-before)).

## Code

### 19. A configuration file says what can be done with it

What can be set in a `.config.json` is clear from reading the file alone, without the documentation: a new file has every setting there is, with its default value even where that is "nothing" (`"password": null`), and a comment above it that says what it does and which values it takes. A setting that is only described somewhere else is one nobody finds.

### 20. Code that reads easily

- Names say what something is, or what it does: descriptive, and preferably not abstract, without cryptic abbreviations or words that need looking up, and no longer than that takes. `TimesAtWhichEntryCanBeSeen` rather than `OpenTimes`, `CheckWhatDependsOnTheState` rather than `JudgeAt`.
- Comments are short and simple, and say why rather than what. Code that is hard to follow (a loop that walks a tree, a method that weighs several cases, an algorithm) also gets a short comment for each step, saying what it does, so that it can be read without working it out first.
- Every view and partial gets its data through a typed viewmodel, not through loose variables or `ViewData`.
- Code is grouped by feature (`Features/<feature>/`), not by kind (all controllers in one place, all services in another).
- Files stay short. One that grows past around 250 lines is split up along what its parts do (deciding, storing, looking up, …), not into a first and a second half; tests are split by topic, with what they share in a base class. This is a guide, not a limit: a file that clearly reads better in one piece stays one.
- Folders stay small too: ideally around five files each (a view and its model, `_Card.cshtml` and `_Card.cshtml.cs`, count as one). A part of a feature that has grown files of its own gets a folder of its own inside the feature's, as the list of changes of the history has (`Features/HistoryViewing/Commits`). Again a guide, not a limit.
- Namespaces always follow the folders: what is in `src/Notes/Features/HistoryViewing/Commits` is in `Notes.Features.HistoryViewing.Commits`. That is why the pages of the app itself are in `Pages/App`, with their `/_/` addresses given in their `@page`: a folder named `_` would make a namespace `_`, which hides the discard.
- A return value that isn't used is discarded on purpose (`_ = Directory.CreateDirectory(path);`), so that it is clear it wasn't forgotten. Calls that only return what they were called on, to be chained (builders, `StringBuilder.Append`), and assertions are left as they are.

### 21. Few dependencies

Before writing something, look for a built-in ASP.NET feature or an existing library that does it. But every new package, service or container is discussed first: each one has to be kept up to date and trusted.

### 22. As few file operations as possible

Every look at the disk costs, and on a slow disk (a hard drive, a network share) it costs a lot. A request reads what it needs once, on the way from the root to the entry: for a file five directories down, the configurations of the root and of those five directories (a file's settings are in the configuration of its folder), each once. A directory's page adds the configuration of each directory it lists. Whatever else the request needs to know (the permissions, the passwords, the archived folders) comes from what was read, not from reading again. Only an operation that works on what is below (a copy, a move, a download) looks below, as part of its work. What is only there to be safe in rare cases (a configuration edited by hand on the server) is caught up with in the background, not by every request.

## Documentation

### 23. Written for someone new

A feature comes with its documentation in `docs/`, in simple words, so that someone new to the repository can learn what the app is and how it works. The documents form a tree: each topic has an overview (`docs/<topic>.md`) and its details in a folder of the same name (`docs/<topic>/`). The app will show these documents itself, so links between them are relative and stay inside `docs/`, and files elsewhere in the repository are named, not linked.

Examples help more than descriptions. Pictures of the app come from `docs/screenshots`, which the UI tests take again with every run (see [Tests](tests.md#screenshots)), so they never go out of date; no other screenshots are put into the repository.

## Tests

### 24. Tested, without waiting

Every feature gets tests at the level that fits (see [Tests](tests.md)). The integration tests matter most: they test every endpoint as every kind of user, with every kind of attack that applies, much like a penetration test. Time comes from the injected `TimeProvider` wherever a test would otherwise have to wait (a selection that runs out, a background job that runs every hour), so that the test moves a fake clock instead.

### 25. Tests check logic, not wording or looks

Unit tests check methods, integration tests check endpoints, and UI tests check the scripts of the pages. None of them checks the exact wording of a text or anything about CSS, so that a sentence can be rephrased or a style changed without a test failing. Instead they check:

- the state that results: files on disk, the selection the server has, whether a box is ticked,
- what kind of answer it is: the status code, the kind of message (`data-kind` of a toast), whether a disabled control gives a reason at all (`data-disabled-reason`),
- that two answers are the same where they must be (a hidden entry answers exactly like a missing one).

Elements are found by their id or a `data-` attribute, not by their text or their classes. Text is only checked where it is data: a file name the message has to mention, or what a user wrote into a note.
