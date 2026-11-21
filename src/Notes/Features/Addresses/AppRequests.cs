namespace Notes.Features.Addresses;

/// <summary>
/// Which requests are the app's, and so get what it adds to its answers (the security headers, the kill switch, its error page, the notice
/// of a session that ended): those that its pages answer, its scripts and styles, and, where nothing answers, those at its addresses (the
/// pages of the app and the exposed directories). Everything else is the site's own and stays as the site makes it.
/// </summary>
public sealed class AppRequests(AppAddresses addresses, ExposedFolders exposed)
{
    // Where the site serves the static assets of the library (the site maps them, along with its own)
    private static readonly string StaticAssetsPath = $"{AppAddresses.StaticAssetsName}/{typeof(AppRequests).Assembly.GetName().Name}/";

    /// <summary>Whether the request is the app's. Only known once routing has picked what answers it.</summary>
    public bool IsForTheApp(HttpContext context)
    {
        string path = context.Request.Path.Value?.Trim('/') ?? "";
        if (path.StartsWith(StaticAssetsPath, StringComparison.OrdinalIgnoreCase))
            return true;
        return context.GetEndpoint() is { } endpoint
            ? endpoint.Metadata.GetMetadata<AppEndpoint>() is not null
            : IsAtAnAddressOfTheApp(path);
    }

    // A missing page of the app (/_/Nothing), or a missing entry of an exposed directory
    private bool IsAtAnAddressOfTheApp(string path)
        => (path != "" && addresses.TakesPath(path)) || exposed.IsExposed(path);
}

/// <summary>
/// Marks the endpoints of the app (its pages, Hangfire's dashboard), to tell them from those of the site (<see cref="AppRequests"/>).
/// </summary>
public sealed class AppEndpoint
{
    public static AppEndpoint Instance { get; } = new();

    private AppEndpoint()
    {
    }
}

/// <summary>
/// Lets the page of the entries (<c>{**path:exposed}</c>) answer only the paths the site shows (<see cref="ExposedFolders.IsExposed"/>), so
/// that every other address is left to the site's own pages, or to nothing.
/// </summary>
public sealed class ExposedPathConstraint(ExposedFolders exposed) : IRouteConstraint
{
    public const string Name = "exposed";

    public bool Match(HttpContext? httpContext, IRouter? route, string routeKey, RouteValueDictionary values, RouteDirection routeDirection)
        // Links are made from the paths themselves (VirtualPaths.ToAddress), not by routing
        => routeDirection != RouteDirection.IncomingRequest
           || exposed.IsExposed((values.TryGetValue(routeKey, out object? path) ? path as string : null)?.Trim('/') ?? "");
}
