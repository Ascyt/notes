# Development

## Running locally

`dotnet run` uses the running Keycloak: `appsettings.Development.json` reads the realm's discovery document from `localhost:8080`, but the issuer is the public `AUTH_HOST` URL, so the browser is sent to the public Keycloak to log in (the tunnel must be up). Adjust `Config:Auth:Authority` there (or with user secrets) if your hostname differs.

When running locally, the [kill switch](kill-switch.md) leaves its marker as `notes/killed` in the project. Delete it to get the app back.

## Tests

```
tests/notes.Tests.Unit          the logic on its own: paths, the permission parser and its resolution, config text editing, selections, uploads, session renewal
tests/notes.Tests.Integration   the whole app in memory, against the security specification: every endpoint, as every kind of user, with every kind of attack
tests/notes.Tests.Ui            the page scripts in a real browser (Playwright, using the installed Edge or Chrome; no download needed)
tests/notes.Tests.Common        what they share: the app with a fake login and a fake clock, users, the tree of notes they start from
```

The integration tests check the app against the [security specification](security-specification.md). Users are signed in by a header instead of Keycloak (`TestAuthentication`), and the app's clock (`TimeProvider`) is a fake one that the tests move, so nothing waits for real time.

- All: `dotnet test`
- Quick (no timing tests): `dotnet test -- --filter-not-trait "Category=Timing"`
- One project: `dotnet test --project tests/notes.Tests.Integration`
- UI tests with another browser: set `NOTES_UI_BROWSER` to `chrome` or `chromium`; to watch them, set `NOTES_UI_HEADED=1`.

Tests tagged `Category=Timing` are about time running out (stale uploads, selection lifetime, session renewal, changes during a running download or transfer).
