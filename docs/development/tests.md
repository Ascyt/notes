# Tests

What is tested where, and how to run the tests. What tests check (and what they don't) is in the [principles](principles.md#22-tests-check-logic-not-wording-or-looks).

```
tests/Notes.Tests.Unit          methods on their own: paths, the permission parser and its resolution, config text editing, selections, uploads, session renewal
tests/Notes.Tests.Integration   endpoints of the whole app in memory, against the security specification: every endpoint, as every kind of user, with every kind of attack
tests/Notes.Tests.Ui            the scripts of the pages in a real browser (Playwright, using the installed Edge or Chrome; no download needed), and the screenshots
tests/Notes.Tests.Common        what they share: the app with a fake login and a fake clock, users, the tree of notes they start from
```

The integration tests check the app against the [security specification](security-specification.md). Users are signed in by a header instead of Keycloak (`TestAuthentication`), and the app's clock (`TimeProvider`) is a fake one that the tests move, so nothing waits for real time. Most tests start from the same tree of notes and users (`BaseTree`, `Users`), in which every kind of entry the specification talks about exists for every kind of user. The tests of [bridges](../administration/bridges.md) start the app with bridges to physical folders outside the notes (`BridgedNotesAppFactory`, `BridgeTest`).

## Running them

- All: `dotnet test`
- Quick (no timing tests): `dotnet test -- --filter-not-trait "Category=Timing"`
- One project: `dotnet test --project tests/Notes.Tests.Integration`
- UI tests with another browser: `NOTES_UI_BROWSER` set to `chrome` or `chromium`; to watch them, `NOTES_UI_HEADED=1`.

Every UI test also fails if the site's Content Security Policy blocked something on its pages (an inline script or handler, a script from elsewhere), which would otherwise only be said in the browser's console. A test of an attack that the policy stops as well says so with `AllowBlockingByPolicy()`.

Tests tagged `Category=Timing` are about time running out (stale uploads, selection lifetime, session renewal, changes during a running download or transfer).

## Sites that change the app

`SiteNotesAppFactory`, `ExposedNotesAppFactory` and `SiteComponentsNotesAppFactory` (in `Notes.Tests.Common`) start the app as a site that has pages of its own, shows only some directories, or changes components (a texts class, a replaced style, a script left out). `SiteRequestTests` checks that what the app adds to requests stays off the site's pages, and `ComponentAssetTests` that the styles, scripts and texts of components are served and can be replaced one by one.

## Screenshots

`ScreenshotTests` (in the UI tests) takes a picture of every main view, on a desktop in the light theme and on a phone in the dark one, into `docs/screenshots`. They are taken again with every run of the UI tests, and the documents show them, so they always show the app as it is. A picture that didn't change gives the same file, and so no change in git. A new main view gets a screenshot there too.
