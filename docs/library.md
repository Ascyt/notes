# Using the notes in a site

The notes are a library (the project `src/Notes`), which a site of its own uses: it adds them in its `Program.cs` and keeps everything else of its own. The site the repository deploys (`src/Notes.Host`) is one such site, with nothing but the notes; it is the example to start from.

## A site with the notes

```csharp
using Notes;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.AddNotes();

WebApplication app = builder.Build();
app.UseHttpsRedirection();
app.UseNotes();
app.MapStaticAssets();
app.MapNotes();
app.Run();
```

The three calls, in this order:

- **`builder.AddNotes()`** adds the services of the notes, with their settings from the site's configuration (the section `Config`, see [Configuration](administration/configuration.md) and [Limits](administration/limits.md)). Settings that make no sense don't let the site start.
- **`app.UseNotes()`** gets the notes ready before the first request (their folder, the [bridges](administration/bridges.md), the [history](administration/history.md), the migrations of the configurations), schedules the background jobs, and adds what the requests of the notes go through: the security headers, the [kill switch](administration/kill-switch.md), the error page, routing and the login. It comes where the pipeline would have its routing, after what the site does with every request first (forwarded headers, HTTPS, an exception handler).
- **`app.MapNotes()`** maps the pages of the notes and, if the setup has it, Hangfire's dashboard. The scripts and styles of the notes are static assets of the library (at `/_content/Notes/…`), which `app.MapStaticAssets()` of the site serves along with its own.

What is the site's own: forwarded headers behind a reverse proxy, HTTPS and HSTS, an exception handler (the notes have an error page, `app.NotesErrorPage()`, that says nothing about how the site is run), and anything else the site has.

## Choices in code

What whoever runs the site sets is in the settings. What the site's code chooses is given to `AddNotes`:

```csharp
builder.AddNotes(setup =>
{
    setup.HangfireDashboard = false;                          // no dashboard
    setup.ConfigureHangfire = hangfire => hangfire.UseSqlServerStorage(connectionString);
});
```

| Choice | Default | What it is |
|---|---|---|
| `ConfigSection` | `"Config"` | The section of the configuration with the settings of the notes. |
| `Layout` | the notes' own | The layout the pages of the notes are shown in, by its full path (see [A layout of the site's own](#a-layout-of-the-sites-own)). |
| `ConfigureHangfire` | in memory | Where Hangfire keeps the background jobs. They are scheduled again at every start, so memory loses nothing that matters. |
| `HangfireDashboard` | `true` | Whether the site has Hangfire's dashboard, for admins only, among the pages of the app (`/_/Hangfire`). Without it there is no link to it in the menu either. |

Hangfire is part of the notes: the background jobs need it, the dashboard doesn't. When the jobs run is a setting (`Config:Jobs`, see [Background jobs](administration/jobs.md)).

## What the notes take of the site

By default the notes answer every address of the site, and the pages of the app itself are under `/_/` (`/_/Login`, `/_/About`, …). Two settings change that:

| Setting | Default | What it is |
|---|---|---|
| `Config:Exposed` | all of the notes | The directories of the notes the site shows, each with everything in it, at their paths as they are in the notes. |
| `Config:AppPath` | `/_` | Where the pages of the app are: `/_`, another address such as `/app`, or empty for the root (`/Login`). |

A site with pages of its own, and the notes in its `files` directory:

```json
"Config": {
  "Dir": "/data/notes",
  "Exposed": [ "files" ],
  "AppPath": ""
}
```

Then `/files/report.md` is `files/report.md` of the notes, and its breadcrumb starts at `files`. Every other address is the site's: its own start page at `/`, with a link to `/files`, and the pages of the app at `/Login`, `/About` and so on. A directory may also be deeper in the notes (`"notes/files"` is shown at `/notes/files`). Both kinds of name are taken from the notes:

- **What isn't exposed is no part of the site**, for anyone, admins too: it answers like a missing path, also in the history, and nothing exposed leads out of it. Whether someone may see an exposed directory is still up to the permissions, also of the directories above it, which aren't shown (see the [security specification](development/security-specification.md#exposed-folders)). An exposed directory can't be deleted, moved or renamed. A directory in the setting that isn't there doesn't let the site start.
- **The pages of the app come first.** The name they take at the root of the notes (`_` for `/_`, `app` for `/app`, or with an empty address `Login`, `About`, `Error`, `Logout`, `Shutdown` and `Hangfire`) is no part of the notes: a directory of that name is hidden, and nothing new at the root gets it. So is `_content`, where the scripts and styles are.

A breadcrumb starts at the exposed directory. Links of the site can stand before it, as if they were directories above (`Config:BreadcrumbParents`, in their order), so that the notes look like a part of the site:

```json
"BreadcrumbParents": [
  { "Name": "The organization", "Address": "/" },
  { "Name": "Projects", "Address": "/projects" }
]
```

Each has a name and an address: one of the site (`/…`) or of another one (`https://…`). They lead out of the notes, so an earlier state that is shown stays behind. One without a name, or with an address that is none, doesn't let the site start.

The logins go through Keycloak, which the settings name (`Config:Auth`, see [Sessions](administration/sessions.md)).

## What the app adds to a request

The security headers, the kill switch, the error page and the notice of a session that ended are for the app's own requests only: those of its pages, of its assets (`/_content/Notes/…`) and of an address that is an entry (an exposed one, if `Config:Exposed` is set). A page of the site keeps what the site makes of it: no headers of the app, no kill switch (a site whose app was shut down stays up), and its own answer for a missing address (the app's error page is shown only for the app's addresses).

## Changing a component

The parts of the pages are components, each with its HTML, its texts, its styles and its script apart (see [Components](development/components.md)). A site changes one part of one component and leaves the rest.

**Texts.** Derive from the texts class of the component, override what you say differently, and register it in place of the class. The other texts stay the app's:

```csharp
public class SiteToolbarTexts : ToolbarTexts
{
    public override string Upload => "Add files to the folder";
    public override string Download(bool isFile) => isFile ? "Save this file" : "Save this directory";
}

builder.Services.AddSingleton<ToolbarTexts, SiteToolbarTexts>();
```

Texts are typed classes in the site's own folders and not settings, so a site gets them checked by the compiler and may translate them (a class per language, chosen by whatever the site chooses on) or write them from its own source. They are all English by default. The classes (`ToolbarTexts`, `ListingTexts`, `UploadTexts`, … `ConfigTexts`, `NameTexts`) are listed in [Components](development/components.md#layout-and-pages-of-the-app). What the server says in answers (a refusal, a result) is in them too.

**Styles and scripts.** The app's stylesheets are linked in the head, those of the site after them, so the site's rules win. Put a file of the site in place of one, or remove it, by its path in the library (each component has it as a constant):

```csharp
builder.Services.Configure<ComponentAssets>(assets => assets
    .Replace(_Toolbar.Styles, "/css/toolbar.css")     // the site's file instead of the app's
    .Replace(_Breadcrumb.Script, null)                 // the script left out
    .AddStyles("/css/notes.css"));                     // one more, after the app's
```

The addresses are the site's own (`wwwroot` of the site, served by `MapStaticAssets`). A script that is left out leaves its component without what it does, so replace one with a file that does the same.

**HTML.** A view of the site at the path of the component's view takes its place (`/Features/Components/Toolbar/_Toolbar.cshtml` in the site's project). It gets the same model and may use the same texts. The parts of the layout, and a layout of your own, are in the next section.

What is changed of one component doesn't change another, and none of it needs the app's code.

## A layout of the site's own

By default the pages of the notes are shown in the notes' own layout: a menu with the title, About, the theme and the login, and a footer. A site can show them in a layout of its own instead (`setup.Layout = "/Pages/Shared/_NotesInSite.cshtml"`), made of its own header and footer and these parts of the notes, which need nothing of the page:

| Part | Where | What it is |
|---|---|---|
| `/Pages/Shared/AppLayout/_Head.cshtml` | in `<head>`, needed | The styles (Bootstrap and those of the components, `<component-styles/>`) and the scripts the pages use while they load. |
| `/Pages/Shared/AppLayout/_Overlays.cshtml` | at the start of `<body>`, needed | The stack of messages, what covers the page while something long runs, the notice of a session that ended, the dialog of the kill switch for admins. |
| `/Pages/Shared/AppLayout/_Scripts.cshtml` | at the end of `<body>`, needed | Bootstrap's script and the keyboard shortcuts. |
| `/Pages/Shared/AppLayout/_MenuItems.cshtml` | in a `<ul class="navbar-nav">`, optional | About, and for admins Hangfire's dashboard. |
| `/Pages/Shared/AppLayout/_AccountMenu.cshtml` | optional | Logging in and out, and who is logged in. |
| `/Pages/Shared/AppLayout/_ThemeToggle.cshtml` | optional | The button between the light and the dark theme; without it the theme follows the system's. |

```cshtml
<!DOCTYPE html>
<html lang="en">
<head>
    <title>@ViewData["Title"]</title>
    <partial name="/Pages/Shared/AppLayout/_Head.cshtml"/>
</head>
<body>
<header id="site-header" class="sticky-top">
    <a href="/">The organization</a>
    <partial name="/Pages/Shared/AppLayout/_AccountMenu.cshtml"/>
</header>
<partial name="/Pages/Shared/AppLayout/_Overlays.cshtml"/>
<main>@RenderBody()</main>
<partial name="/Pages/Shared/AppLayout/_Scripts.cshtml"/>
</body>
</html>
```

The title of the page is in `ViewData["Title"]`. A header that stays at the top gets the id `site-header`, so that the breadcrumb of the notes stays below it. The site's own `_ViewStart` and `_Layout` stay its own: the notes never use them unless the setup names one.
