# Background jobs

Four jobs run by themselves, on a schedule. Admins see them in Hangfire's dashboard (`/_/Hangfire`, in the menu), where a site has it.

| Job | What it does |
|---|---|
| Clearing stale uploads | Discards the unfinished uploads whose last part arrived more than an hour ago (see [Limits](limits.md)). |
| Clearing stale archives | Deletes the archives made to be downloaded (with the options of the download dialog) once they are an hour old, downloaded or not. |
| Committing idle changes | Commits what is pending in the [history](history.md) once nothing was changed for `History:IdleMinutes`. |
| Zipping idle archived directories | Zips the [archived directories](archived-directories.md) that are unzipped and weren't used for `Archives:ZipAfterMinutes`. |

## When they run

In `appsettings.json` (or the environment, e.g. `Config__Jobs__CommitIdleChanges`), as cron expressions: minute, hour, day of the month, month, day of the week.

| Setting | Default | |
|---|---|---|
| `Config:Jobs:ClearStaleUploads` | `0 * * * *` | every hour |
| `Config:Jobs:ClearStaleArchives` | `30 * * * *` | every hour, at half past |
| `Config:Jobs:CommitIdleChanges` | `* * * * *` | every minute |
| `Config:Jobs:ZipIdleArchives` | `* * * * *` | every minute |

```json
"Config": {
  "Jobs": {
    "CommitIdleChanges": "*/5 * * * *"
  }
}
```

A schedule that isn't a cron expression doesn't let the app start, and the log says which one it is. Running the history's job less often makes a commit come later than `History:IdleMinutes`, by up to the time between two runs.
