# Deployment

The stack in `compose.yaml` is: the app, Keycloak (+ Postgres), Caddy and `cloudflared`. Visitors reach it over HTTPS through a Cloudflare tunnel, so the machine needs no public IP and no open port.

```
browser --HTTPS--> Cloudflare --tunnel--> cloudflared --> caddy --> notes     (NOTES_HOST)
                                                                \-> keycloak  (AUTH_HOST, only /realms/notes and /resources)
```

## One-time setup

1. **Cloudflare tunnel:** the domain is on Cloudflare, with a tunnel (Zero Trust > Networks > Tunnels > Cloudflared > Docker). Its token is needed in step 3.
2. **Hostnames:** the tunnel gets two public hostnames, both with service `HTTP` and URL `caddy:80`: the one of the notes (e.g. `notes.example.com`) and the one of Keycloak (e.g. `auth.example.com`). Cloudflare creates their DNS records.
3. **Settings:** `.env.example` is copied to `.env` and filled in (`NOTES_HOST`, `AUTH_HOST`, `CLOUDFLARE_TUNNEL_TOKEN`, secrets).
4. **Start:** `docker compose up -d --build`
5. **Keycloak's admin console** is not reachable through the proxy, only at <http://localhost:8080/admin> on the host. Because the public hostname is fixed, the master realm needs to use the local address once (the console would otherwise load parts of itself from the blocked public URL):

   ```bash
   docker compose exec keycloak /opt/keycloak/bin/kcadm.sh config credentials --server http://localhost:8080 --realm master --user <KEYCLOAK_ADMIN_USER>
   docker compose exec keycloak /opt/keycloak/bin/kcadm.sh update realms/master -s attributes.frontendUrl=http://localhost:8080
   ```
6. **The first admin:** in the `notes` realm, a user is created, given the `admin` realm role and a password.

Who may see and change what is configured per directory, by admins, in the app (the gear button): see [Permissions](permissions.md).

## A realm that was imported before

The realm file (`keycloak/notes-realm.json`) is only imported when the realm does not exist yet. What it has gained since has to be set by hand in the admin console:

- The redirect URIs of the `notes-web` client: `https://<NOTES_HOST>/signin-oidc` as a valid redirect URI and `https://<NOTES_HOST>/signout-callback-oidc` as a valid post logout redirect URI.
- The groups mapper and the access token lifespan: see [Permissions](permissions.md#keycloak).
- The session lifetimes and "Remember me": see [Sessions](sessions.md#keycloak).
- The service account the kill switch uses: see [Kill switch](kill-switch.md#keycloak).

## Running it

- Notes live in `NOTES_DIR` (default `./data/notes`). On Linux the folder must be writable by uid 1654.
- A backup consists of `NOTES_DIR` and the `keycloak-db-data` volume (`docker compose exec keycloak-db pg_dump -U keycloak keycloak`). The notes folder holds everything of the app besides the logins: the notes, their permissions, and the app's own data in `_`.
- Anything but the notes realm on `AUTH_HOST` answers 404 in Caddy. Cloudflare rules (WAF, rate limiting) add to that and are not part of this repository.
