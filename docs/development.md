# Development

How the app is built and how to work on it.

- [Principles](development/principles.md): the rules every change follows. The place to start.
- [Running locally](development/running-locally.md): starting the app on a development machine.
- [Migrations of the configuration files](development/config-migrations.md): changing the format of `.config.json` without breaking existing files.
- [Tests](development/tests.md): what is tested where, how to run the tests, and the screenshots they take.
- [Security specification](development/security-specification.md): who may do what, the reference for the code and its tests.
- [User input](development/user-input.md): every kind of input the app takes, how it is checked, its limits, and what happens when requests run at the same time.
- [Components](development/components.md): the parts of the pages (HTML, texts, styles, script), and how a site swaps one.
- [History specification](development/history-specification.md): who may see which earlier state of the notes, and how they are shown.
- [Zipped folders](development/zipped-folders.md): how a folder kept as an archive is zipped and unzipped without ever losing anything, and its edge cases.

## How the app is put together

An ASP.NET Core app with Razor Pages, in two projects (`Notes.slnx` holds them, with the tests):

- `src/Notes` is the app itself, as a library (a Razor class library): its pages, features, scripts and styles, which a site uses. Its namespaces start with `Notes`, and its static files (the scripts and styles next to the components, and `wwwroot/lib`) are served at `/_content/Notes/…`. Everything below is in it.
- `src/Notes.Host` is the site that runs it, as it is deployed (see [Deployment](administration/deployment.md)): `Program.cs`, the settings (`appsettings.json`), the Dockerfile, and the notes used in development (`notes-test`). How it, or any other site, uses the library is in [Using the notes in a site](library.md).

In `src/Notes`:

- `Pages/Index.cshtml(.cs)` answers every address of the notes. It finds the file or directory the address points at and hands the request to the feature that answers it; the query string says which one (`?edit`, `?download`, `?config`, …). Requests about the history (`?before=`, `?commit=`, …) go to the history (`HistoryRequests`). Everything else that can be done with an entry is an action (`Features/EntryActions`): a class that says which parameter asks for it and what access it needs, registered by its feature (see [Adding an action](#adding-an-action)). Without one, a GET shows the entry.
- `Features/` holds the features, one folder each: where the notes are on disk, with their [bridges](administration/bridges.md) (`Bridges`), the login and the permissions (`Auth`, `Permissions`), the `.config.json` of each directory and its migrations (`Configuration`), the history of the notes, kept (`History`) and seen as it was (`HistoryViewing`), the parts of the pages (`Components/`: toolbar, selection, upload, rename, …), saving files and telling text from binary (`Editor`), the bytes and thumbnails of media (`Media`), rendering Markdown (`Markdown`), the kill switch.
- `Pages/` holds the pages themselves: the directory listing (`Browse`), the page of a file with its editors (`Editor`), the configuration editor (`ConfigEditor`), the pages of the app under `/_/` (`App`), and the layout with the parts a site's own layout takes too (`Shared/AppLayout`). The pages set their layout themselves (`NotesSetup.Layout`), and the library has no `_ViewStart` or `_Layout` at the usual paths: a site's own files there would take their place.
- Each component keeps its view, texts class, styles (SCSS) and script (TypeScript) in one folder, and the styles and scripts stay there, built into CSS and JavaScript beside them, which `Notes.csproj` serves; `wwwroot/` only holds the libraries in `wwwroot/lib` (LibMan). See [Components](development/components.md), which also says how to add one.
- The security headers, the kill switch and the error page apply to the app's own requests only (`AppRequests`, `UseWhen` in `NotesHosting.cs`); a site's pages are left alone.
- Each feature registers its own services in an extension method of its folder (`AddComponents`, `AddHistory`, …), which `AddNotes` (`NotesHosting.cs`) calls, along with what the whole app shares. What a site chooses in code is in `NotesSetup.cs`, what whoever runs it sets in `Config.cs`.

## Adding an action

An action is something done with one entry, asked for by a parameter of the query (`POST /docs?create=file`).

1. A class in its feature implements `IEntryGetAction`, `IEntryPostAction` or `IEntryDeleteAction` (several, if it answers several methods). Its `Parameter` is the name in the query; its value is in `EntryRequest.Value`. A `Parameter` of null makes it what the method does when no parameter asks for anything else (saving a file for POST, deleting for DELETE); each method has at most one such action.
2. `Needs` says what the user must be allowed before it is asked at all: `Known` (every request needs this), `Readable`, or `ManagesConfiguration`. The page checks it and answers like the [security specification](development/security-specification.md) says. Anything else the action checks itself.
3. The feature registers it with `services.AddEntryAction<TheAction>()`. Two actions of a method with the same parameter don't let the app start.

A request asks for at most one action: one that names two is refused with 400 rather than one of them chosen. Parameters that go with an action (`&name=`, `&offset=` of an upload) or with a view (`?edit`) are no actions. The value of the parameter, and everything else the request sends, is checked like all [user input](development/user-input.md).

Logins go through Keycloak (see [Sessions](administration/sessions.md)); the notes and their configuration are files on disk (see [Configuration](administration/configuration.md)), some of them in folders of their own (see [Bridges](administration/bridges.md)), and so is their history (see [History](administration/history.md)).
