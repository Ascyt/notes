# Kill switch

For when something has gone badly wrong (an account that got out, data that is being taken) and the site has to be down now. Admins have a power button left of their user name (shortcut Ctrl+Alt+Shift+Delete). After confirming, the app:

1. answers every request with a page that only says the site has been shut down (status 503), for everyone, the admin included,
2. writes the marker file `killed` to the `notes-keys` volume, so that this stays so after a restart,
3. disables the `notes` realm in Keycloak, so that nobody can log in or renew a session.

The page doesn't say why the site is down. Who pulled the kill switch, and whatever couldn't be done (the marker or the realm), is only in the app's log (`docker compose logs notes`). Keycloak keeps running, so that the realm can be enabled again from its admin console.

## Bringing the site back

On the server:

```bash
docker compose exec notes rm /keys/killed
docker compose restart notes
```

Then enable the realm again in the admin console: *Realm settings* → *Enabled*.

## Keycloak

To disable the realm, the app logs in as the service account of its own client, which has the realm's `manage-realm` role. The realm file sets this up. For a realm that was imported before, set by hand: *Clients* → `notes-web` → *Capability config*: *Service accounts roles* on. Then, on its *Service accounts roles* tab, *Assign role* → filter by clients → `realm-management` `manage-realm`.

Without it the kill switch still shuts the app down, but says that the realm could not be disabled.

`manage-realm` lets the app change the settings of the `notes` realm (not of the master realm). So someone who takes over the app could change them too.
