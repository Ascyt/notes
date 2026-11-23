# Components

How the parts of the pages are built, so that each part can be swapped by a site (how a site does that is in [Using the app in a site](../library.md#changing-a-component)).

A component is a part of a page that has its own folder, with what it is made of kept apart:

| Part | File | What it is |
|---|---|---|
| HTML | `_Name.cshtml` and its model `_Name.cshtml.cs` | The skeleton. It holds no text a user reads and no style. |
| Texts | `NameTexts.cs` | Every text of the component that isn't made by a user: labels, tooltips, messages, what the server answers. A class with a virtual member for each text. |
| CSS | `_Name.css` | The styles, in a file of their own, never in a `<style>` of the view. |
| JS | `_Name.js` | The script, next to the view, never inline (see [Principles](principles.md)). |

Features that are no part of a page (an action, a service) keep their texts class in their folder too (`UploadTexts` for the upload, with its `UploadHandler`). The pages themselves are components of the same kind: `Pages/Browse` has `DirectoryView.cshtml`, `DirectoryView.css` and `ListingTexts.cs`, and its parts one folder deeper (`EntryTable`, `MediaGrid`).

## Texts

A texts class is not sealed and has no state. A text without parts is a virtual property, one with parts a virtual method, whose parameters are what is put in (a name, a count, whether it is about a file or a directory):

```csharp
public class RenameTexts
{
    public virtual string Rename => "Rename";
    public virtual string Renamed => "Renamed to {name}.";
}

public class ToolbarTexts
{
    public virtual string Download(bool isFile) => isFile ? "Download this file" : "Download this directory as a zip";
}
```

- The default is English, written in the neutral wording of the [principles](principles.md). A site overrides what it says differently.
- A text that says what a user did wrong, or what happened, does not name the things of this app that are no part of what the user sees.
- Each class is registered by its feature with `TryAddSingleton`, so a class of a site that is registered before or after takes its place. Add a class to the feature's registration (`AddComponents`, `AddEditor`, `NotesHosting.cs` for the shared ones).
- Views and handlers get it by injection (`@inject ToolbarTexts Texts`, a constructor parameter), never by `new`. Where nothing can be injected (a value parsed in a static method) there is `ConfigTexts.Default`; use it as the last resort.
- Text that the server says in a response (a refusal, a message of a save) is in a texts class too, so a site's wording reaches it. Only what no user of the app ever sees stays in code: log lines, exceptions for a mistake in the code or in the settings, the text of a kill switch marker.
- Tests don't check a text's wording ([principle 24](principles.md#24-tests-check-logic-not-wording-or-looks)). A test that needs one takes it from the class.

### Texts in scripts

A script gets its texts from the same class. The view writes them into a block, and the script reads them:

```cshtml
<script-texts id="upload-texts" texts="@Texts"/>
<script component-asset="Features/Components/Upload/_Upload.js"></script>
```

```js
const texts = textsOf('upload-texts');
notify(fillIn(texts.uploaded, { count: 3 }));    // "Uploaded {count} files."
```

Every public string property of the class is in the block, by its name in camelCase, so a property is all it takes to make a text reach the script. A text with parts is a property with the parts in braces (`{count}`), which `fillIn` puts in (both are in `site.js`); methods are not in the block. The block is on the page for everyone who sees it, so a text only the server shows, and only to some users (to admins, say), is marked `[NotForScripts]` and left out. Give `texts` an array to pass several classes. The block is JSON in a `<script type="application/json">`, which the browser doesn't run, so the site's policy needs no exception for it.

## Styles and scripts

The `.css` and `.js` files of the components stay next to their views. `Notes.csproj` serves them as static assets of the library (at `/_content/Notes/` and the path of the file, with a fingerprint in the address and compressed), so `wwwroot` only holds the libraries (`wwwroot/lib`, LibMan). The `ComponentAsset` items there name which files: everything under `Features/` and `Pages/` that ends in `.css` or `.js`. Their names don't end in `.cshtml.css`, which would make them [scoped CSS](https://learn.microsoft.com/aspnet/core/razor-pages/ui-class) of the view: that is bundled and can't be replaced.

A component names its files by their path in the library, as a constant next to the component, which a site uses to replace one:

```csharp
public class _Toolbar : PageModel
{
    public const string Styles = "Features/Components/Toolbar/_Toolbar.css";
}
```

Linking them:

- `<component-styles/>`, in the head of every layout (`_Head.cshtml`), links the styles of all components, and then those the site added (so the site's win where they set the same). A feature adds its stylesheets with `services.AddComponentStyles(_Toolbar.Styles, …)`.
- `<component-style path="…"/>` in a view links a stylesheet that only that page needs (the Markdown preview's). It lands in the head with the others: a page is made before its layout, so the tag helper hands the path to `<component-styles/>`.
- `<script component-asset="…">` and `<link rel="stylesheet" component-asset="…"/>` link a script or style by its path. The address comes from `ComponentAssets`, with the fingerprint, and the tag is left out where the site replaced the file with nothing.

All of that is in `Features/Components/Assets` (`ComponentAssets`, `ComponentAssetTagHelpers`, `ScriptTextsTagHelper`).

## Adding a component

1. A folder `Features/Components/Name/` (a part of a page of one feature can live in that feature's folder, as `Pages/Browse/EntryTable`).
2. `_Name.cshtml` and `_Name.cshtml.cs`, a typed model for the view with a `public const string Styles`/`Script` for its files. The view is used with `<partial name="_Name" model="…"/>` and gets its texts with `@inject NameTexts Texts`.
3. `NameTexts.cs` with a virtual member for each text, and the registration `TryAddSingleton<NameTexts>()` in the feature's `Add…` method.
4. `_Name.css` and `_Name.js` if it needs them, and their constants. Styles used by every page are registered with `AddComponentStyles`; a script is linked by the view with `component-asset`. Nothing inline.
5. Replacing it by a site needs nothing more: the view is replaced by a view at the same path, the texts by a class, the styles and scripts by `Replace`.
6. A test in `ComponentAssetTests` if it has something the others don't have.

## Layout and pages of the app

The layout is made of components too (`Pages/Shared/AppLayout`: `_Head`, `_Overlays`, `_Scripts`, `_MenuItems`, `_AccountMenu`, `_ThemeToggle`) with `LayoutTexts`, and the pages of the app under `/_/` have `AppPageTexts`, `AuthTexts` and `KillSwitchTexts`. Which classes hold which texts:

| Class | What it holds |
|---|---|
| `ToolbarTexts`, `BreadcrumbTexts`, `ToastTexts` | The toolbar's tooltips, the breadcrumb, the stack of messages |
| `ListingTexts` | The directory listing (headings, empty states, the media grid) |
| `CreateTexts`, `RenameTexts`, `SelectionTexts`, `UploadTexts`, `TransferTexts`, `ArchiveTexts` | The components of those names (the archive's is the dialog that asks how to make one), and what their handlers answer |
| `BusyTexts` | What covers the page while something long runs (`_BusyOverlay`, see `whileBusy` in `site.js`) |
| `EditorTexts`, `InsertMediaTexts`, `MarkdownTexts`, `MediaTexts` | The editors and the dialog that puts a file into a note, the preview, what is shown of media |
| `HistoryTexts` | The history views, and the description of a commit |
| `ConfigEditorTexts`, `ConfigTexts` | The configuration editor, and what is said about a configuration (errors, the meaning of each setting) |
| `EntryTexts`, `NameTexts` | What any action about an entry answers (not found, refused); why a name can't be used |
| `LayoutTexts`, `AppPageTexts`, `AuthTexts`, `KillSwitchTexts` | The layout, the pages of the app, the login, the page of a shut down app |

Texts are for the interface only. The names of things in the code stay English and generic (`App`, `Entry`).

## Later

The scripts are plain JavaScript for now. A move to TypeScript is planned: the files would stay next to their components and be built into the same static assets, so nothing in how a site replaces them changes.
