namespace Notes.Features.Passwords;

/// <summary>
/// How the passwords of entries behave (<c>Config:Passwords</c> in appsettings). See docs/administration/passwords.md.
/// </summary>
public sealed class PasswordsConfig
{
    /// <summary>For how many minutes an entry stays unlocked after it was last used (any request for it, or an edit on its page).</summary>
    public double UnlockMinutes { get; init; } = 10;

    /// <summary>
    /// The cost of bcrypt (each step doubles the time a check takes). 12 takes about a quarter of a second, which a user hardly notices
    /// and which makes guessing slow.
    /// </summary>
    public int WorkFactor { get; init; } = 12;

    /// <summary>How many wrong passwords in a row are answered at once, before each further one has to wait.</summary>
    public int FreeAttempts { get; init; } = 5;

    /// <summary>The longest wait after a wrong password, in seconds. The wait doubles with each wrong one after the free ones, up to this.</summary>
    public double MaxWaitSeconds { get; init; } = 60;

    /// <summary>Throws if a setting makes no sense, so that the app doesn't start with it.</summary>
    public void Check()
    {
        if (UnlockMinutes <= 0)
            throw new InvalidOperationException("Config:Passwords:UnlockMinutes must be more than 0.");
        // (What bcrypt itself allows)
        if (WorkFactor is < 4 or > 31)
            throw new InvalidOperationException("Config:Passwords:WorkFactor must be between 4 and 31.");
        if (FreeAttempts < 0)
            throw new InvalidOperationException("Config:Passwords:FreeAttempts can't be negative.");
        if (MaxWaitSeconds < 1)
            throw new InvalidOperationException("Config:Passwords:MaxWaitSeconds must be at least 1.");
    }
}
