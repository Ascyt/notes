# Passwords

A directory or a file can have a password. What it protects can only be opened by someone who types it, **admins included**: the permissions say who may see something at all, the password is a second key on top of them. A password on a directory protects everything in it.

## Setting one

A password is set in the [configuration](configuration.md) of the entry, as `password`:

```jsonc
{
    "version": "1.5.0",
    "perms": { "inherit": true },
    "gitignore": false,
    "password": "correct horse battery staple",   // protects this directory and everything in it
    "files": [
        { "name": "diary.md", "perms": { "inherit": true }, "password": "another one" }   // protects this file
    ]
}
```

In the configuration of a single file (the gear button on its page), it is `password` next to its `perms`. A new configuration already has `"password": null` (no password), with a comment that says how to set one, and an older one of a directory gets it when it is migrated (1.4.0); `null` is the same as leaving it out.

- **Saving keeps only the hash.** The editor replaces the password with `"passwordHash": "$2a$12$…"`, a bcrypt hash, and shows the configuration as it was saved. The password itself is never written anywhere. A hash can't be turned back into the password, and checking a guess takes about a quarter of a second (`Config:Passwords:WorkFactor`), also on a graphics card.
- **At most 72 bytes.** That is what bcrypt reads of a password (UTF-8: a letter of ASCII is one byte, an umlaut two, most emoji four). A longer one is refused when it is saved, not cut off. It may not be empty or have control characters in it.
- **Only the editor sets one.** A `password` written into a `.config.json` in another way (on the server, by hand) makes the configuration invalid: only admins have access there until it is saved in the editor, which hashes it. `password` and `passwordHash` can't both be there (but for `"password": null`, which says nothing).
- **Changing or removing a password needs it unlocked**, by admins too. It is removed by replacing the `passwordHash` with `"password": null` (or taking it out): otherwise editing the configuration would be a way around it. Setting a password where there was none needs nothing. Whoever sets a password has it unlocked afterwards.

## Opening what a password protects

The page of a protected entry (and of anything inside a protected directory, whether it exists or not) asks for the password. Listings show such an entry like any other, but with a yellow lock after its name (and a Markdown file by its file name, not its title): it can be opened, with the password. It can't be selected while it is locked. Once it is unlocked, a gray unlocked padlock (with its keyhole) takes the yellow one's place, in the listing and in the breadcrumb, so it stays clear that the password will be asked for again. In the breadcrumb it is after the entry that has the password, not after what is inside it. The dialog that asks also comes up whenever something done on a page needs a password: saving a file after the password ran out, say. The change that wasn't saved yet stays in the editor, and is saved once the password is typed.

- **An unlock lasts 10 minutes** after it was last used (`Config:Passwords:UnlockMinutes`): every request for the entry counts, and so does typing on its page, saved or not. It is kept in the memory of the app for that browser (a cookie of its own leads to it) and for that user; a restart of the app locks everything again.
- **The lock button** (yellow, next to the account, Alt+L) locks everything the browser has unlocked, at once, and zips the [encrypted archived directories](archived-directories.md#encrypted) behind those passwords. If the page was inside something unlocked, it then goes to the folder that holds the outermost of those entries, which still opens without a password, instead of asking for it. Logging out and in moved to Alt+Shift+L for it. While nothing is unlocked, the same place and shortcut zip the archived directories that are unzipped (see [Archived directories](archived-directories.md)).
- **Wrong passwords make the next ones wait.** After 5 wrong ones in a row (`Config:Passwords:FreeAttempts`) for one entry, each further try waits: 1 second, then 2, 4, … up to a minute (`Config:Passwords:MaxWaitSeconds`). The dialog counts down. It is counted for each user (or, for someone not logged in, each address) and entry, and a right password ends it.
- **Programs** can send the password along with any request in the header `X-Notes-Password`, encoded in base64 (of its UTF-8, as in Basic authentication: `X-Notes-Password: b3BlbiBzZXNhbWU=` for "open sesame"), instead of going through the dialog. It unlocks for that request only, and wrong passwords count towards the wait like typed ones. An answer that needs a password is a 403 with the header `X-Notes-Password-Required` (the address of the entry whose password it is), or a 429 with `Retry-After` while it has to wait. A request that needs several passwords sends one header per password, or one with them separated by commas. A value that isn't base64 of UTF-8 counts as no password.

## What a password changes

While it is locked, the entry is as if the user may not read it, whatever the permissions say: it can't be opened, downloaded, selected, copied, moved, renamed or deleted, and neither can anything inside it. Downloads and copies of the directories above leave it out; moving or deleting a directory with it inside fails. Its earlier states can't be seen either, and its configuration can't be opened.

- **The password travels.** A copy or a move takes the password along, unlike the permissions: a copy would otherwise be a way around it. A directory copied by someone who doesn't manage the configuration gets a configuration that says nothing but its password. Renaming keeps it too, and so does the unlock.
- **What the user may not read stays hidden.** A password inside a directory someone may not read is not asked for: for them, it isn't there.
- **Earlier states** are protected by the password the entry had then. One that a password locked then, other than the one it has now (or that was deleted with its password), can't be seen by anyone, as there is nowhere to type that password.
- **On disk, nothing is encrypted**, unless the directory is an [encrypted archived directory](archived-directories.md#encrypted). Otherwise the password is a lock of the app: whoever can read the folder of the notes on the server (or a backup, or the history in `_/history`) reads what it protects.
