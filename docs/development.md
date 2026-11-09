# Development

How the app is built and how to work on it.

- [Principles](development/principles.md): the rules every change follows. The place to start.
- [Running locally](development/running-locally.md): starting the app on a development machine.
- [Migrations of the permissions files](development/config-migrations.md): changing the format of `.config.json` without breaking existing files.
- [Tests](development/tests.md): what is tested where, how to run the tests, and the screenshots they take.
- [Security specification](development/security-specification.md): who may do what, the reference for the code and its tests.

## How the app is put together

An ASP.NET Core app with Razor Pages, in `notes/`:

- `Pages/Index.cshtml(.cs)` answers every address of the notes. It finds the file or directory the address points at and hands the request to the feature that answers it; the query string says which one (`?edit`, `?download`, `?config`, …).
- `Features/` holds the features, one folder each: the login and the permissions (`Auth`, `Permissions`), the parts of the pages (`Components/`: toolbar, selection, upload, rename, …), the kill switch.
- `Pages/` holds the pages themselves: the directory listing (`Browse`), the file views and editors (`Editor`), the permissions editor (`ConfigEditor`), and the pages of the app under `/_/`.
- `wwwroot/` holds the scripts and styles, with the libraries in `wwwroot/lib` (LibMan).

Logins go through Keycloak (see [Sessions](administration/sessions.md)); the notes and their permissions are files on disk (see [Permissions](administration/permissions.md)).
