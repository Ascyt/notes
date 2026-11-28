using System.Diagnostics;

namespace Notes;

/// <summary>
/// Runs what the app does before its first request, logging when each step starts and when it ends. A start that takes long (a big
/// folder to look through, leftovers to delete) then shows in the log which step it is in, instead of going quiet.
/// </summary>
internal static class StartupSteps
{
    public static void Run(ILogger logger, string description, Action step)
    {
        logger.LogInformation("Startup: {step}", description);
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            step();
        }
        catch (Exception e)
        {
            double failedAfter = stopwatch.Elapsed.TotalSeconds;
            logger.LogError(e, "Startup failed after {seconds:0.0} s: {step}", failedAfter, description);
            throw;
        }
        double seconds = stopwatch.Elapsed.TotalSeconds;
        logger.LogInformation("Startup: done after {seconds:0.0} s: {step}", seconds, description);
    }
}
