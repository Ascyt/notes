using Microsoft.Extensions.DependencyInjection.Extensions;
using Notes.Features.Components.Assets;
using Notes.Features.EntryActions;

namespace Notes.Features.Passwords;

public static class PasswordServices
{
    /// <summary>
    /// The passwords of entries: which paths they lock for a request, what each browser has unlocked, how wrong passwords are slowed down, and
    /// the actions that unlock and lock (docs/administration/passwords.md). Settings that make no sense don't let the app start.
    /// </summary>
    public static IServiceCollection AddPasswords(this IServiceCollection services, PasswordsConfig settings)
    {
        settings.Check();
        _ = services.AddSingleton(settings);
        // Whether a path is locked depends on the request (its cookie and headers), which the permissions are asked about without
        _ = services.AddHttpContextAccessor();
        _ = services.AddSingleton<PasswordUnlocks>();
        _ = services.AddSingleton<PasswordAttempts>();
        _ = services.AddSingleton<PasswordLocks>();
        _ = services.AddSingleton<PasswordGate>();
        services.TryAddSingleton<PasswordTexts>();
        return services.AddComponentStyles(_PasswordModal.Styles).AddEntryAction<UnlockHandler>().AddEntryAction<LockHandler>();
    }
}
