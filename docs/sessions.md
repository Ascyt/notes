# Sessions

Users log in through Keycloak. The app keeps the session in a cookie, and Keycloak decides how long it lasts: the app asks it for new tokens every couple of minutes (see [Permissions](permissions.md#changes-in-keycloak-take-effect-within-minutes)), and the session ends as soon as Keycloak refuses.

## How long a session lasts

| | Ends after this long without use | Ends after this long at most | When the browser closes |
|---|---|---|---|
| Without "Remember me" | 30 minutes | 8 hours | the session ends |
| With "Remember me" | 7 days | 30 days | the session is kept |

Keycloak doesn't tell the app whether "Remember me" was ticked. The app tells the two kinds of session apart by how long Keycloak lets the session last: one that may last longer than 8 hours is a remembered one, and only then does the cookie outlive the browser. So the maximum of a session without "Remember me" must stay at 8 hours or less, or the app takes every session for a remembered one (`AuthExtensions.IsRemembered`).

## When a session ends

If Keycloak says the session is over (the user logged out elsewhere, was disabled or deleted, or the session ran out), the user is logged out and the next page tells them so. If Keycloak can't be reached when a token is to be renewed, the session is kept for ten minutes and then ended, with a notice.

Logging out in the app also ends the session in Keycloak. To end someone else's sessions (a lost device, a password that got out), sign them out in the admin console: *Users* → the user → *Sessions*. A changed password doesn't end the sessions there are.

## Keycloak

For a realm that was imported before these settings were in the realm file, set by hand:

- *Realm settings* → *Login*: *Remember me* on.
- *Realm settings* → *Sessions*: *SSO Session Idle* 30 minutes, *SSO Session Max* 8 hours, *SSO Session Idle Remember Me* 7 days and *SSO Session Max Remember Me* 30 days.

Refresh token rotation (*Revoke Refresh Token*) must stay off in the realm: requests that arrive together would otherwise each use the same refresh token.
