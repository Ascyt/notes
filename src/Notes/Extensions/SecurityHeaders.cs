namespace Notes.Extensions;

/// <summary>Headers every answer of the app gets, telling the browser what it must not do with it.</summary>
public static class SecurityHeaders
{
    /// <summary>
    /// <list type="bullet">
    /// <item>No other site may show the app in a frame, which could make a user click a button of it unawares (deleting, the kill switch).
    /// The app itself may: a PDF is shown in an element of its own page.</item>
    /// <item>The browser takes the type it is told, and never guesses another (an uploaded file that looks like HTML is not a page of the
    /// app).</item>
    /// <item>No base address that would make the relative links of a page lead elsewhere.</item>
    /// <item>Other sites aren't told which address of the app a request came from (an image from elsewhere in a Markdown file), as the path of
    /// an entry may be private.</item>
    /// <item>Only the app's own script files run: no inline scripts or handlers, nothing from other sites, no eval. Should something of a file
    /// ever get past the Markdown renderer as markup, it still can't run. Monaco starts its workers from blob: addresses.</item>
    /// </list>
    /// Set when the answer starts, so that they survive the error page, which clears the headers of what failed.
    /// </summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
        => app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                IHeaderDictionary headers = context.Response.Headers;
                headers.XContentTypeOptions = "nosniff";
                headers.XFrameOptions = "SAMEORIGIN";
                headers.ContentSecurityPolicy = "frame-ancestors 'self'; base-uri 'self'; object-src 'self'; script-src 'self'; worker-src 'self' blob:";
                headers["Referrer-Policy"] = "same-origin";
                return Task.CompletedTask;
            });

            await next(context);
        });
}
