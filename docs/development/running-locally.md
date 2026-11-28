# Running locally

Building needs the .NET SDK (the version in `global.json`) and Node with npm, which build the styles and scripts of the components (see [Components](components.md#styles-and-scripts)). The packages for that are fetched by the build itself the first time (`npm ci` in `src/Notes`).

`dotnet run` (in `src/Notes.Host/`) uses the running Keycloak: `appsettings.Development.json` reads the realm's discovery document from `localhost:8080`, but the issuer is the public `AUTH_HOST` URL, so the browser is sent to the public Keycloak to log in (the tunnel must be up). If the hostname differs, `Config:Auth:Authority` is changed there (or with user secrets).

When running locally, the [kill switch](../administration/kill-switch.md) leaves its marker as `src/Notes.Host/killed` in the project. Deleting it brings the app back.
