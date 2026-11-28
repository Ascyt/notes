namespace Notes.Features.Configuration;

/// <summary>
/// Brings the index of the configuration files in line with the disk (<see cref="FolderConfigStore.Rescan"/>): a file added, changed or
/// deleted on the server by hand counts once this found it. Walks every location, so on a large or slow disk it takes a while; it waits for
/// nothing and nothing waits for it. A run that comes while the last one still walks is left out.
/// </summary>
public sealed class RescanConfigsJob(FolderConfigStore configs, ILogger<RescanConfigsJob> logger)
{
    public const string Id = "rescan-configs";

    private static int _running;

    public void Run()
    {
        if (Interlocked.Exchange(ref _running, 1) == 1)
            return;
        try
        {
            System.Diagnostics.Stopwatch took = System.Diagnostics.Stopwatch.StartNew();
            configs.Rescan();
            logger.LogDebug("Looked through the configurations on disk in {milliseconds} ms", took.ElapsedMilliseconds);
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }
}
