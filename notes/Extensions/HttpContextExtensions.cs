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
    }
}
