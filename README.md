# notes

## Deployment

The stack in `compose.yaml` is: the app, Keycloak (+ Postgres), Caddy and `cloudflared`. Visitors reach it over HTTPS
through a Cloudflare tunnel, so the machine needs no public IP and no open port.

```
browser --HTTPS--> Cloudflare --tunnel--> cloudflared --> caddy --> notes     (NOTES_HOST)
                                                                \-> keycloak  (AUTH_HOST, only /realms/notes and /resources)
```

### One-time setup

1. Put the domain on Cloudflare and create a tunnel (Zero Trust > Networks > Tunnels > Cloudflared > Docker). Copy its token.
2. Add two public hostnames to the tunnel, both with service `HTTP` and URL `caddy:80`: the notes one (e.g. `notes.ascyt.com`)
   and the Keycloak one (e.g. `auth.ascyt.com`). Cloudflare creates their DNS records.
3. Copy `.env.example` to `.env` and fill it in (`NOTES_HOST`, `AUTH_HOST`, `CLOUDFLARE_TUNNEL_TOKEN`, secrets).
4. `docker compose up -d --build`
5. Keycloak's admin console is not reachable through the proxy. Open <http://localhost:8080/admin> on the host. Because the
   public hostname is fixed, the master realm needs to use the local address once (the console would otherwise load
   parts of itself from the blocked public URL):

   ```bash
   docker compose exec keycloak /opt/keycloak/bin/kcadm.sh config credentials --server http://localhost:8080 --realm master --user <KEYCLOAK_ADMIN_USER>
   docker compose exec keycloak /opt/keycloak/bin/kcadm.sh update realms/master -s attributes.frontendUrl=http://localhost:8080
   ```
6. In the `notes` realm, create your user, give it the `admin` realm role and set a password.

Who may see and change what is configured per directory, by admins, in the app (the gear button): see
[docs/permissions.md](docs/permissions.md). It uses the Keycloak groups and roles of users. The realm file maps the groups to the
`groups` claim and sets short access token lifespans, which keep groups and roles current; for a realm that was imported
before, that document says what to set by hand.

The realm file is only imported when the realm does not exist yet. For an already imported realm, add
`https://<NOTES_HOST>/signin-oidc` as a valid redirect URI and `https://<NOTES_HOST>/signout-callback-oidc` as a valid
post logout redirect URI of the `notes-web` client in the admin console.

### Local development

`dotnet run` uses the running Keycloak: `appsettings.Development.json` reads the realm's discovery document from
`localhost:8080`, but the issuer is the public `AUTH_HOST` URL, so the browser is sent to the public Keycloak to log in
(the tunnel must be up). Adjust `Config:Auth:Authority` there (or with user secrets) if your hostname differs.

### Notes

- Notes live in `NOTES_DIR` (default `./data/notes`). On Linux the folder must be writable by uid 1654.
- Back up `NOTES_DIR` and the `keycloak-db-data` volume (`docker compose exec keycloak-db pg_dump -U keycloak keycloak`).
- Anything but the notes realm on `AUTH_HOST` answers 404 in Caddy. Cloudflare rules (WAF, rate limiting) add to that and
  are not part of this repository.

## Tests

```
tests/notes.Tests.Unit          the logic on its own: paths, the permission parser and its resolution, config text editing, selections, uploads, session renewal
tests/notes.Tests.Integration   the whole app in memory, against docs/security-specification.md: every endpoint, as every kind of user, with every kind of attack
tests/notes.Tests.Ui            the page scripts in a real browser (Playwright, using the installed Edge or Chrome; no download needed)
tests/notes.Tests.Common        what they share: the app with a fake login and a fake clock, users, the tree of notes they start from
```

Users are signed in by a header instead of Keycloak (`TestAuthentication`), and the app's clock (`TimeProvider`) is a fake one that the
tests move, so nothing waits for real time.

- All: `dotnet test`
- Quick (no timing tests): `dotnet test -- --filter-not-trait "Category=Timing"`
- One project: `dotnet test --project tests/notes.Tests.Integration`
- UI tests with another browser: set `NOTES_UI_BROWSER` to `chrome` or `chromium`; to watch them, set `NOTES_UI_HEADED=1`.

Tests tagged `Category=Timing` are about time running out (stale uploads, selection lifetime, session renewal, changes during a running
download or transfer).
