# Running locally

`dotnet run` (in `notes/`) uses the running Keycloak: `appsettings.Development.json` reads the realm's discovery document from `localhost:8080`, but the issuer is the public `AUTH_HOST` URL, so the browser is sent to the public Keycloak to log in (the tunnel must be up). If the hostname differs, `Config:Auth:Authority` is changed there (or with user secrets).

When running locally, the [kill switch](../administration/kill-switch.md) leaves its marker as `notes/killed` in the project. Deleting it brings the app back.
