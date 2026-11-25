# Limits

How large what users send and make may be. The defaults are far beyond what normal use comes near: they are there so that nothing is unbounded (a single user can't fill the server's memory with one request), not to restrict anyone. Every kind of input, and the limits that can't be changed, are in [User input](../development/user-input.md).

## Settings

In `appsettings.json` (or the environment, e.g. `Config__Limits__MaxUploadBytes`):

| Setting | Default | |
|---|---|---|
| `Config:Limits:MaxUploadBytes` | `68719476736` (64 GiB) | The largest file that may be uploaded. A larger one is refused once it gets there, and what was uploaded of it is discarded. |
| `Config:Limits:MaxUploadsInProgressPerUser` | `100` | How many unfinished uploads one user may have. Each is a temporary file in `_/temp` until it is finished, aborted, or discarded an hour after its last part arrived. |
| `Config:Limits:MaxTextBytes` | `16777216` (16 MiB) | The largest text file that is shown in the editor or rendered, and the largest text that may be saved. A larger file is shown as one that can't be viewed, and can still be downloaded. Must stay below the server's limit for a request (about 28.6 MiB). |
| `Config:Limits:MaxPathBytes` | `2048` | The longest path (in UTF-8 bytes, from the root) a new entry may get, also for everything in a directory that is renamed, copied or moved. At least 255. Entries that are already deeper stay usable. |
| `Config:Limits:MaxConvertedImageBytes` | `268435456` (256 MiB) | The largest image that is converted for the browser (HEIC, raw photos, …). Larger ones can be downloaded. |
| `Config:Limits:MaxConvertedImagePixels` | `200000000` | The most pixels an image that is converted may have. |
| `Config:Limits:MaxUnzippedBytes` | `68719476736` (64 GiB) | How much an uploaded archive that is unzipped may hold, unzipped. What is written counts, not what the archive says about itself: an archive of a few kilobytes can unzip to more than the disk has. Then nothing of it is unzipped: it stays a file, marked as one that couldn't be. |
| `Config:Limits:MaxUnzippedEntries` | `100000` | How many entries an uploaded archive that is unzipped may have (more: as above). |

A value of zero or less stops the app from starting, as does a `MaxPathBytes` below 255.

How much disk space all users together may take is not limited by the app: that is up to the disk, or a quota on the server.
