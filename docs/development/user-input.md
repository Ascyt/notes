# User input

Everything that comes from outside the app is input, and almost every security problem starts with input handled carelessly. This document lists every kind of input the app takes, how it is checked, and how large it may be. It sits next to the [security specification](security-specification.md), which says who may do what; this one says what may be sent at all.

Every value is checked where it comes in, against a rule that says what it may be (a list of allowed values, a pattern, a parser), not against a list of what it must not be. What doesn't fit is refused with a 400 and a reason. Nothing is cleaned up and then used: a name with a `/` in it is refused, not cut at the `/`. The one exception is turning a name into Unicode's composed form (see [Names of new entries](#names-of-new-entries)), which changes how it is stored, not what it says.

## Where input comes from

| Input | Who sends it | Checked by |
|---|---|---|
| The path of the address | Anyone | `PathService` (one name at a time, see [Paths](#paths)) |
| The query (`?edit`, `?rename=…`, `?before=…`, …) | Anyone | `IndexModel`, then the feature it is for (see [The query](#the-query)) |
| Bodies of requests (text of a file, chunks of an upload, JSON, a configuration) | Logged-in users (the antiforgery token is checked for every `POST` and `DELETE`) | The feature's handler (see [Bodies](#bodies)) |
| Contents of the notes (Markdown, images) | Whoever may change a note, read by everyone who may read it | `MarkdownRenderer`, `ImageForBrowser` (see [Contents of the notes](#contents-of-the-notes)) |
| `.config.json` files | Admins (through the editor) and whoever can write to the folder on the server | `ConfigParser` |
| Cookies and headers | Anyone | ASP.NET (see [Cookies and headers](#cookies-and-headers)) |
| What Keycloak says about a user (id, name, groups, roles) | Keycloak, signed | The OIDC handler; the id is only used as a folder name if it is safe (see [Users](#users)) |
| The settings of the app (`appsettings.json`, the environment) | The admin of the server | Checked when the app starts: a setting that makes no sense stops it |

## Paths

The path of an address names an entry of the notes, one name per segment. `PathService.Resolve` walks from the root one name at a time and only follows a name that is a bridge or an entry the file system actually lists in the current directory. So a path can never lead anywhere the file system didn't give it: `..`, drive letters, UNC paths, encoded slashes and the like name nothing and answer 404.

- Each name has to be one that could be looked up at all (`PathService.IsValid`): not empty, no space at the start or end, no dot at the end (Windows drops it, so `x.` would be another spelling of `x`), not `.`, `..`, `_` (the app's data) or `.config.json` in any case, and none of the characters the system can't have in a name, nor `\`.
- Links (symbolic links, junctions) are never followed: they could point out of the notes.
- Names are compared **ignoring case** everywhere: in paths (the exact spelling wins where a file system has several), in the entries of a configuration, and in name collisions (see [Concurrency](#concurrency)).
- The addresses of the app (`/_/…` by default, see `Config:AppPath`) belong to the app, not to the notes, and so does the name they take at the root (`AppAddresses`). A path the site doesn't show (`Config:Exposed`, `ExposedFolders`) answers 404 before anything else looks at it.

How long a path may be in the address is limited by the server itself (see [Limits](#limits)). Names that are already there and break the stricter rules for new names (made outside the app) can still be opened, renamed and deleted.

### Names of new entries

A name that a user gives (an upload, a rename) has to keep more rules (`NewNames.Check`), so that it means the same on every system the notes may be moved to and can be shown without fooling anyone:

- at most 255 bytes in UTF-8 (what Linux allows; most characters take one byte, others up to four);
- none of `< > : " | ? *` (which Windows doesn't allow), on every system;
- no control characters (line breaks, tabs, …) and no invisible formatting characters, such as the marks that turn the direction of the text around (with U+202E in it, `photo<U+202E>gpj.exe` is shown as `photoexe.jpg`), zero-width spaces, line and paragraph separators. The zero-width joiner is allowed, as emoji are made of it;
- not a name Windows keeps for devices (`CON`, `PRN`, `AUX`, `NUL`, `COM0`–`COM9`, `LPT0`–`LPT9`), also not with an extension (`nul.txt`);
- at the root of the notes, not a name an address of the app takes (`AppAddresses.NewEntryRefusal`: `_content`, and the first name of `Config:AppPath`, or with an empty one the names of its pages, such as `Login`); this is checked with the path the entry gets, also for what is copied or moved there;
- the rules of every name above.

The name is stored in Unicode's composed form (NFC), so that `é` typed on a Mac (where it may arrive as `e` and an accent) is the same name as on any other system.

The path the new entry gets, from the root, may be at most `Config:Limits:MaxPathBytes` long. For a directory that is renamed, copied or moved, this holds for everything inside it.

New files and directories that the app names itself (`2026-11-09_14-30.md`) keep these rules by how they are made.

### Paths as addresses

A name may contain what means something in an address (`#`, `%`, `?` where the file system allows it). Every link to an entry is made with `VirtualPaths.ToAddress`, which escapes each name, so that `c# notes.md` is linked as `/c%23%20notes.md` and doesn't stop at the `#`. Razor escapes HTML, not addresses: `href="/@path"` is never enough.

## The query

`IndexModel` decides what a request is by which parameters it has. Every parameter may be given **only once**: `?rename=a&rename=b` would otherwise be read as `a,b`, which is a valid name. A repeated parameter is answered with 400. A request also asks for **at most one action** (`?selection=clear&create=file` would otherwise do one of them and not say which), or for one thing to read of an earlier state (`?raw&download`): more than one is answered with 400.

| Parameter | Allowed values | Otherwise |
|---|---|---|
| `edit`, `raw`, `thumbnail`, `download`, `config`, `history`, `changes`, `previous`, `abort`, `last`, `newer` | Only whether they are there counts | — |
| `create` | `file`, `directory` | 400 |
| `rename` | A [name for a new entry](#names-of-new-entries) | 400 with the reason |
| `upload` | `start`, or an id the server made (a GUID) | 400 |
| `name` (of an upload) | A [name for a new entry](#names-of-new-entries) | 400 with the reason |
| `folder` (of an upload) | Names for new entries, separated by `/` (no `.` or `..`) | 400 with the reason |
| `offset` (of an upload) | A whole number from 0, no gap after what was uploaded so far | 400, 409 |
| `unzip` (of an upload) | Only whether it is there counts; only for a `.zip` or `.7z` (for any other file it means nothing) | 403 if the folder it would be unzipped into may not be made |
| `unzipping` (GET of a directory) | The name of a file in it (no `/` or `\`) | 400 |
| `operations` | Nothing (GET: the list); `cancel` (POST, with `{ "id": "<guid>" }` as JSON) | 404 (GET with a value, or not logged in); 400 |
| `hashes` (GET of a directory) | The name of a file in it, with `size` (a whole number from 0) and `piece` (bytes, from 1 MiB to 64 MiB) | 400; `{ "hashes": null }` for no such file, another size, or one the user may not read |
| `selection` | `add`, `remove`, `clear` (POST); nothing (GET) | 400 |
| `transfer` | `copy`, `move`, `delete` | 400 |
| `archive` | `directory`, `selection` (POST); the id of an archive the server made, a GUID (GET) | 400; 404 for an id that is not one of the user's archives |
| `before` | A time in ISO 8601 with a time zone (`2026-11-11T13:30:00Z`), not in the future | A redirect to now with the reason; 400 for `?commits` |
| `commit` | The full id of a commit: 40 hexadecimal digits (upper case is accepted). It is compared with the ids of the history, never handed to git as a revision (`HEAD~1` and the like mean nothing) | A redirect to now with the reason; 400 for `?commits` |
| `page` (of `?commits`) | A whole number from 1 | Taken as 1 |
| `returnUrl` (of `/_/Login`) | A local address (`Url.IsLocalUrl`) | The root: the login never leads to another site |

Parameters the page doesn't know are ignored.

## Bodies

| Body | Of | Rules |
|---|---|---|
| Text of a file | `POST /file` (save) | UTF-8, at most `Config:Limits:MaxTextBytes` (413 otherwise). Read completely before anything is checked or written. Bytes that aren't valid UTF-8 are refused with 400, not replaced. |
| Chunk of an upload | `POST /dir?upload=<id>&…` | Raw bytes, each at most the server's request limit; the whole file at most `Config:Limits:MaxUploadBytes` (413, and the upload is discarded). Chunks of one upload come one after the other (409 for one that arrives while another is written). |
| An archive to unzip | `POST /dir?upload=<id>&…&unzip` | Uploaded as any file; `&unzip` means something only for a `.zip` or `.7z` (by its name), which then gets `"unzipPending": true` in its entry and is unzipped in the background into a folder of the app's data first (`_/temp/unzip`), then renamed into place under the structure lock. Every entry is checked as a new name (one that can't be one, or that leads out of it: `..`, a path from the root, fails it); configurations, archives of archived folders and the app's data (`_`) are left out and counted. At most `Config:Limits:MaxUnzippedBytes` written and `Config:Limits:MaxUnzippedEntries` entries. Encrypted ones are not unzipped. What fails it leaves the archive as it is, with `unzipError` in its entry; nothing of what was unzipped is kept. |
| Paths to select or unselect | `POST /?selection=add\|remove` | JSON (`Content-Type: application/json`) `{ "paths": [...] }`, at most 10,000 paths. Paths that can't be selected are left out. |
| Version of the selection | `POST /dir?transfer=…` | JSON `{ "version": <number> }`. A transfer only runs if it is the version the user's selection has. |
| How to make an archive | `POST /entry?archive=directory\|selection` | JSON `{ "format": "7z"\|"zip", "compression": "store"\|"min"\|"medium"\|"high"\|"max", "password": "…", "version": <number> }`, at most 4 KiB (the version only for the selection, see above). `store` only with `zip`, `min` only with `7z` (the 7z writer always compresses). The password is optional (missing or empty: none), at most 256 characters, without control characters; see [Passwords of archives](#passwords-of-archives). Anything else is refused with 400. |
| A configuration | `POST /entry?config` | At most 1 MiB of text, in UTF-8 (400 otherwise, not replaced), parsed completely by `ConfigParser` (strict JSON with comments: unknown or repeated properties, wrong types and misspelled principals are errors; a principal is at most 256 characters) before anything is written. |

### Passwords of archives

The password an archive is encrypted with is the one secret a user types into the app, so it is kept as short a time and in as few places as can be:

- It is sent in the body of a `POST`, never in an address (which would end up in the history of the browser, the logs of proxies and the `Referer`), and the download of the finished archive (`GET ?archive=<id>`) carries only the id.
- It is used to encrypt the archive and nothing else: it is not logged, not stored, not shown, and not kept in the browser (the dialog forgets it once it is closed; the format and compression it keeps). The field is a password field that password managers are told not to fill in (`autocomplete="new-password"`), and it can be shown while typing.
- A 7z is encrypted with AES-256, its names included (nothing about what is in it can be read without the password); a zip with AES-256 too (WinZip's AES, which 7-Zip and most programs open, not zip's old encryption, which is easily broken), with its names readable, as zip has no way to hide them.
- The finished archive is a file in the app's data (`_/temp/archive`) until it is deleted, an hour after it was made at the latest. It is as safe as its password: the app doesn't check how strong that is.

### Passwords of entries

The [password of an entry](../administration/passwords.md) is typed in two places, and handled like that of an archive:

- **In the configuration editor**, as `password`: checked with the rest of the text (not empty, no control characters, at most 72 bytes of UTF-8, which is what bcrypt reads; longer is refused, never cut off), and replaced by its hash before anything is written. The text the editor gets back has only the hash.
- **To unlock**, in the body of `POST ?unlock` (JSON, at most 4 KB), or in the header `X-Notes-Password`, base64 of its UTF-8 (one per header value or comma-separated; at most 16 are decoded per request, each at most 288 characters, and what isn't valid base64 of UTF-8 is left out). Checked the same way before it is compared with the hash; it is not kept, logged or shown. The dialog forgets it once it is closed, and the field is one password managers may fill in (`autocomplete="current-password"`).

## Contents of the notes

A note is written by whoever may change it and read by everyone who may read it, so its contents are input for everyone who opens it.

- **Markdown** never becomes HTML that can run code: see "Rendered markdown can't run code" in the [security specification](security-specification.md#what-a-user-may-learn).
- **Text** is shown in the editor or rendered only up to `Config:Limits:MaxTextBytes`; a larger file is shown as one that can't be viewed, and can still be downloaded. A file whose start has a NUL byte or isn't UTF-8 is binary.
- **Media** is sent with the type its extension names (never what its contents claim, never SVG, which can carry scripts) and `X-Content-Type-Options: nosniff`.
- **Images the browser can't show** (HEIC, raw photos, …) are converted by ImageMagick, which only ever reads them as the format their extension names (so none of its other readers, such as scripts or URLs, can be reached). Files larger than `Config:Limits:MaxConvertedImageBytes`, or with more than `Config:Limits:MaxConvertedImagePixels` pixels or 30,000 on a side, aren't converted (they can be downloaded). The same goes for the thumbnails of images (`?thumbnail`), of every image format, which are read only as the format their extension names too; the one exception is an image the browser shows itself (JPEG, PNG, GIF, WebP, AVIF, BMP, ICO), whose first bytes may say it is another of these (a WebP named `.jpg`, as images saved from the web often are), never anything else. Only so many images are converted at once (half the processors), thumbnails included; the rest wait.
- **Downloads** are always `application/octet-stream`, as attachments.

## Cookies and headers

- The login cookie (`.notes.auth`) and the antiforgery cookie are protected by ASP.NET's data protection: one that was changed is no cookie.
- The notice cookies (`.notes.notice`, `.notes.history-notice`) only carry a key; what is shown is the text the app has for that key, and any other value shows nothing.
- `X-Forwarded-For` and `X-Forwarded-Proto` are trusted, as the app is only reachable through its reverse proxy (see [Deployment](../administration/deployment.md)). Running it reachable in another way would let anyone claim another address or HTTPS.
- Every answer tells the browser (`SecurityHeaders`): no other site may show the app in a frame (`frame-ancestors 'self'`, `X-Frame-Options: SAMEORIGIN`), so that nobody can make a user click its buttons unawares; never to guess a type (`nosniff`); no `<base>` that would send links elsewhere; to run only the app's own script files (`script-src 'self'`: no inline scripts or handlers, nothing from other sites), so that markup in a note that ever got past the Markdown renderer still couldn't run; and not to tell other sites which address of the notes a request came from (`Referrer-Policy: same-origin`).

## Users

The id of a user (Keycloak's `sub`) names the folder of their unfinished uploads. It is used as it is only if it is made of letters, digits and `-` and is at most 64 characters (as Keycloak's are); otherwise the folder is named after a hash of it. Names, groups and roles are only compared with the configuration, never used as paths or markup without escaping.

## Limits

Nothing a user sends is unbounded. The limits that can be changed are in `Config:Limits` (see [Limits](../administration/limits.md)); their defaults are far beyond normal use. The others are fixed:

| What | Limit | Where it comes from |
|---|---|---|
| A name of a new entry | 255 bytes (UTF-8) | `NewNames.MaxNameBytes`: what file systems allow |
| The path of a new entry, from the root | `Config:Limits:MaxPathBytes` (2048 bytes) | So that its address stays usable |
| The request line (method, path and query) | 8 KiB | Kestrel |
| All headers of a request | 32 KiB | Kestrel |
| A request body | 30,000,000 bytes (about 28.6 MiB) | Kestrel. Uploads are sent in chunks of 8 MiB, so a file may be larger. |
| An uploaded file | `Config:Limits:MaxUploadBytes` (64 GiB) | |
| What an uploaded archive holds, unzipped | `Config:Limits:MaxUnzippedBytes` (64 GiB), `Config:Limits:MaxUnzippedEntries` (100,000) | What is written counts |
| Unfinished uploads of one user | `Config:Limits:MaxUploadsInProgressPerUser` (100); they are discarded an hour after their last chunk | |
| Text that is saved, shown in the editor or rendered | `Config:Limits:MaxTextBytes` (16 MiB) | |
| Paths in one selection request | 10,000 | `SelectionHandler` |
| The options of an archive | 4 KiB of JSON; a password of 256 characters | `ArchiveHandler`, `ArchiveOptions` |
| Archives being made | One at a time for each user (409 for another); for everyone together half the processors, the others wait. The compression of a 7z takes up to about 400 MB of memory (Max) while it runs | `PreparedArchives`, `ArchiveWriter` |
| Archives kept to be downloaded | 5 for each user (a new one deletes the oldest); each is deleted an hour after it was made | `PreparedArchives`, `ClearStaleArchivesJob` |
| A configuration | 1 MiB of text; a principal 256 characters | `ConfigParser`, `Principal` |
| An image that is converted | `Config:Limits:MaxConvertedImageBytes` (256 MiB), `MaxConvertedImagePixels` (200 million), 30,000 pixels on a side | `ImageForBrowser` |

How much disk space all users together may take is not limited by the app: that is the job of the disk (or a quota of the server).

## Concurrency

Requests run at the same time, and a check is only worth something if what it checked can't change before it is acted on.

- **Changes of structure hold one lock.** Create, upload (while placing the finished file), rename, copy, move, delete, saving a file and saving a configuration hold the `StructureLock` from their checks until they are done, and check again under it whatever they looked at before (a file being saved is still there and still writable; the directory an upload goes into is still there and still readable). Saving reads the text first, as the client may send it slowly, but only ever replaces the file that is there: it never makes one where a file was renamed or deleted in the meantime.
- **Reading holds no lock, so nothing may ever be seen half done.** Every change is made so that someone reading at any moment sees the state before or the state after:
  - deleting moves the entries into the app's data (`_`) in one rename each and only then removes them there, so a directory never stands without its `.config.json` while its files are still there;
  - copying and moving copy into the app's data first and then rename the finished copy into place, with its `.config.json` inside it;
  - a configuration and the text of a saved file are written to a temporary file and put in place of the old one (`File.Replace`), so that whoever reads them meanwhile gets all of the old text or all of the new one. A reader that already has the file open goes on reading the old text. On Windows, a file held open by something that doesn't allow it to be replaced (another program, or a download that is still running) isn't saved: 409 for a file, 500 for a configuration;
  - a renamed file gets the entry of its new name in the configuration (a copy of its own) *before* it is renamed, and the old entry goes after, so it is never under a name without its own permissions;
  - the cache of the configurations doesn't keep what it read while a configuration was being written.
- **Names are the same in any case.** On a file system that tells cases apart (Linux, so the Docker image), `Secret.md` next to `secret.md` would share the entry of `secret.md` in the configuration (entries are found ignoring case), and deleting it would take that entry away, opening `secret.md` to everyone the folder lets in. So a new entry never gets a name that is there in another case.
- **What isn't covered:** files changed on the server directly (outside of the app), and permissions that change while an operation runs (it acts on what it checked). A request that has checked an entry and is reading it may still get what was there a moment ago when someone with the right to do so replaced it at that instant: it never gets what the user may not see at the time they asked.
- **One instance only.** The lock, the selections and the uploads in progress live in the memory and disk of one process.

## Rules for new features

1. **Say what a value may be, and refuse the rest.** A list of values, a pattern, a parser. A value that doesn't fit is a 400 with a reason, never something guessed at or cleaned up.
2. **Names users give go through `NewNames.Check`,** paths through `PathService`; nothing joins a user's text into a physical path in any other way.
3. **Everything has a limit.** A body is read with a limit (never `ReadToEndAsync` on what a client sends), a list has a maximum length, a file that is read into memory has a size it isn't read beyond. Limits that an admin may want to change go into `Config:Limits`; every limit goes into the table above.
4. **Links are made with `VirtualPaths.ToAddress`.**
5. **Check again under the lock** whatever a change depends on, and make every change so that a reader sees the state before or after it, never between.
6. **Add it here,** with its rules and limits, and test what is refused (see [Tests](tests.md)).

## Where it is implemented

| What | Where |
|---|---|
| Paths, names that can be looked up | `src/Notes/Features/Helpers/PathService.cs` |
| Names of new entries | `src/Notes/Features/Helpers/NewNames.cs` |
| Links to entries | `src/Notes/Features/Helpers/VirtualPaths.cs` |
| The limits | `src/Notes/Features/Helpers/LimitsConfig.cs` |
| The query, repeated parameters, one action per request | `src/Notes/Pages/Index.cshtml.cs`, `src/Notes/Features/EntryActions/EntryActions.cs` |
| Headers of every answer | `src/Notes/Extensions/SecurityHeaders.cs` |
| The options and password of an archive | `src/Notes/Features/Components/Archive/ArchiveOptions.cs`, `ArchiveHandler.cs` |
| Names in any case | `EntryLocations.IsFree` (`src/Notes/Features/Bridges/EntryLocations.cs`) |
| The tests | `tests/Notes.Tests.Integration/NewNameTests.cs`, `UserInputTests.cs`, `LimitsTests.cs`, `PathTraversalTests.cs`, `RequestProtectionTests.cs` |
