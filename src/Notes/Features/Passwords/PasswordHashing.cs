using System.Text;
using System.Text.RegularExpressions;

namespace Notes.Features.Passwords;

/// <summary>Why a password can't be one (see <see cref="PasswordHashing.Check"/>).</summary>
public enum PasswordProblem
{
    None,
    Empty,

    /// <summary>Longer than bcrypt reads (<see cref="PasswordHashing.MaxBytes"/>): the rest would be ignored without anyone noticing.</summary>
    TooLong,

    /// <summary>Control characters can't be typed into a field, and are read differently by different programs.</summary>
    ControlCharacters
}

/// <summary>
/// Turns the passwords of entries into hashes and checks passwords against them, with bcrypt: what is kept in the configuration can't be
/// turned back into the password, and each guess takes long (<see cref="PasswordsConfig.WorkFactor"/>), also on a graphics card.
/// </summary>
public static partial class PasswordHashing
{
    /// <summary>
    /// How many bytes (UTF-8) bcrypt reads of a password; it ignores the rest. A longer password is refused rather than cut off, so that
    /// nobody believes the rest counts. A character outside ASCII takes two to four bytes.
    /// </summary>
    public const int MaxBytes = 72;

    /// <summary>What may be sent as a password at all, before it is looked at more closely (also <see cref="MaxBytes"/> of four-byte characters).</summary>
    public const int MaxLength = MaxBytes;

    /// <summary>Why the text can't be a password, or <see cref="PasswordProblem.None"/>.</summary>
    public static PasswordProblem Check(string password)
        => password.Length == 0 ? PasswordProblem.Empty
            : password.Any(char.IsControl) ? PasswordProblem.ControlCharacters
            : Encoding.UTF8.GetByteCount(password) > MaxBytes ? PasswordProblem.TooLong
            : PasswordProblem.None;

    /// <summary>The hash of a password that <see cref="Check"/> found nothing wrong with, with a salt of its own.</summary>
    public static string Hash(string password, int workFactor)
    {
        if (Check(password) != PasswordProblem.None)
            throw new ArgumentException("Not a password that can be hashed.", nameof(password));
        return BCrypt.Net.BCrypt.HashPassword(password, workFactor);
    }

    /// <summary>
    /// Whether the password is the one the hash was made of. A password that can't be one (too long, say) is never right, also not by what
    /// bcrypt would read of it.
    /// </summary>
    public static bool Verify(string password, string hash)
    {
        if (Check(password) != PasswordProblem.None || !IsHash(hash))
            return false;
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            return false;
        }
    }

    /// <summary>Whether the text is a bcrypt hash as <see cref="Hash"/> makes them (<c>$2a$12$</c> and 53 characters).</summary>
    public static bool IsHash(string text)
        => HashPattern().IsMatch(text);

    [GeneratedRegex(@"^\$2[aby]\$(0[4-9]|[12][0-9]|3[01])\$[./A-Za-z0-9]{53}$")]
    private static partial Regex HashPattern();
}
