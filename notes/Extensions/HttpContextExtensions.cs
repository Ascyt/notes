using Microsoft.AspNetCore.Diagnostics;

namespace notes.Extensions;

public static class HttpContextExtensions
{
    extension(HttpContext context)
    {
        /// <summary>
        /// Path and query of the request the user actually made. While an error page is shown the request is
        /// re-executed against it, so <c>Request.Path</c> alone would name the error page instead.
        /// </summary>
        public string OriginalPathAndQuery
        {
            get
            {
                if (context.Features.Get<IStatusCodeReExecuteFeature>() is { } reExecute)
                    return reExecute.OriginalPathBase + reExecute.OriginalPath + reExecute.OriginalQueryString;

                if (context.Features.Get<IExceptionHandlerPathFeature>() is { } exception)
                    return exception.Path + context.Request.QueryString;

                return context.Request.PathBase + context.Request.Path + context.Request.QueryString;
            }
        }

        /// <summary>
        /// Leaves a message for the next page in a short-lived cookie that scripts can't read (see <see cref="TakeNoticeCookie"/>): for what a
        /// redirect, or a script's request whose answer nobody reads as a page, has to tell the user.
        /// </summary>
        public void SetNoticeCookie(string name, string value, TimeSpan maxAge)
            => context.Response.Cookies.Append(name, value, new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                Secure = context.Request.IsHttps,
                MaxAge = maxAge
            });

        /// <summary>The message a previous request left (<see cref="SetNoticeCookie"/>), if any. It is removed, so it is only shown once.</summary>
        public string? TakeNoticeCookie(string name)
        {
            if (!context.Request.Cookies.TryGetValue(name, out string? value))
                return null;

            context.Response.Cookies.Delete(name);
            return value;
        }
    }
}
