using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace notes.Tests.Common;

/// <summary>
/// Signs in the user of the <see cref="TestUser.HeaderName"/> header, in front of the app's own pipeline. Everything else about
/// authentication stays as it is in production: the request has no session cookie, so the app's authentication keeps this user, and
/// challenges and forbids still go to the app's own schemes.
/// </summary>
public sealed class TestAuthentication : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        => app =>
        {
            app.Use(async (HttpContext context, Func<Task> nextMiddleware) =>
            {
                if (context.Request.Headers.TryGetValue(TestUser.HeaderName, out var header) && TestUser.FromHeader(header.ToString()) is { } user)
                    context.User = user.ToPrincipal();
                await nextMiddleware();
            });
            next(app);
        };
}
