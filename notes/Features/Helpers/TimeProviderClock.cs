using Microsoft.Extensions.Internal;

namespace notes.Features.Helpers;

/// <summary>The app's <see cref="TimeProvider"/> as the clock the memory cache expires its entries by (it predates <see cref="TimeProvider"/>).</summary>
public sealed class TimeProviderClock(TimeProvider time) : ISystemClock
{
    public DateTimeOffset UtcNow => time.GetUtcNow();
}
