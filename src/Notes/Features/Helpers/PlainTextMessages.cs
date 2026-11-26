using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Notes.Features.Helpers;

/// <summary>
/// A message answered as an <see cref="ObjectResult"/> (a refusal, why something failed) is always plain text, also to a request that asks
/// for JSON (the scripts that ask how far something got): otherwise it would be a JSON string, and the page would show its quotes.
/// </summary>
public sealed class PlainTextMessages : IAlwaysRunResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        // (Replaced rather than given a content type: one that isn't what the request accepts would be refused with 406)
        if (context.Result is ObjectResult { Value: string message } result)
            context.Result = new ContentResult
            {
                Content = message,
                ContentType = "text/plain; charset=utf-8",
                StatusCode = result.StatusCode ?? StatusCodes.Status200OK
            };
    }

    public void OnResultExecuted(ResultExecutedContext context)
    {
    }
}
