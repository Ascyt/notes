# Background jobs

Five jobs run by themselves, on a schedule, and one only when it is started by hand. Admins see them in Hangfire's dashboard (`/_/Hangfire`, in the menu), where a site has it, and can start any of them there ("Trigger now" under Recurring jobs).

| Job | What it does |
|---|---|
| Clearing stale uploads | Discards the unfinished uploads whose last part arrived more than an hour ago (see [Limits](limits.md)). |
| Clearing stale archives | Deletes the archives made to be downloaded (with the options of the download dialog) once they are an hour old, downloaded or not. |
| Committing idle changes | Commits what is pending in the [history](history.md) once nothing was changed for `History:IdleMinutes`. |
| Zipping idle archived directories | Zips the [archived directories](archived-directories.md) that are unzipped and weren't used for `Archives:ZipAfterMinutes`. First it archives what came to hold files below an `onLowestLevel` directory without the app (files put there on the server), and turns off what was turned off while it was in use. |
| Looking for configurations changed by hand (`rescan-configs`) | The app knows which directories have a configuration file without looking at the disk each time, and knows of its own changes at once. This looks through every location for the files added, changed or deleted on the server by hand, which count from then on (and until then, not: a permission changed by hand takes up to one run to count). On a large or slow disk it takes a while, but nothing waits for it. |
| Soft restart (`soft-restart`) | Never on its own. Does again what the app does when it starts that is safe while it runs, for what was changed on the server: commits what changed to the history, brings the configurations up to date, and turns archiving on and off as the configurations say now. Operations that move or create entries wait while it runs; pages go on working. Not done again: putting right half-done zipping after a crash, and the checks of the bridges (those only at a real start). |

## When they run

In `appsettings.json` (or the environment, e.g. `Config__Jobs__CommitIdleChanges`), as cron expressions: minute, hour, day of the month, month, day of the week.

| Setting | Default | |
|---|---|---|
| `Config:Jobs:ClearStaleUploads` | `0 * * * *` | every hour |
| `Config:Jobs:ClearStaleArchives` | `30 * * * *` | every hour, at half past |
| `Config:Jobs:CommitIdleChanges` | `* * * * *` | every minute |
| `Config:Jobs:ZipIdleArchives` | `* * * * *` | every minute |
| `Config:Jobs:RescanConfigs` | `*/10 * * * *` | every 10 minutes |
| `Config:Jobs:SoftRestart` | `0 0 31 2 *` | never (the 31st of February): started by hand |

```json
"Config": {
  "Jobs": {
    "CommitIdleChanges": "*/5 * * * *"
  }
}
```

A schedule that isn't a cron expression doesn't let the app start, and the log says which one it is. Running the history's job less often makes a commit come later than `History:IdleMinutes`, by up to the time between two runs.
