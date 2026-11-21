using Microsoft.AspNetCore.WebUtilities;

namespace Notes.Pages.App;

/// <summary>
/// The texts of the pages of the app itself: About, and the error page. A site with texts of its own derives from this class and registers
/// it in place of this one, as for <see cref="Features.Components.Toolbar.ToolbarTexts"/>.
/// </summary>
public class AppPageTexts
{
    public virtual string About => "About";

    /// <summary>What the app is, a paragraph each.</summary>
    public virtual IReadOnlyList<string> AboutTheApp =>
    [
        "A self-hosted app for notes and other files: Markdown notes and any other files in directories, viewed and edited in the browser, "
            + "with permissions per directory and file.",
        "The app is a work in progress. Its source code is available under the AGPL-3.0 license."
    ];

    /// <summary>The name of an error, by its status code: HTTP's own.</summary>
    public virtual string ErrorName(int statusCode)
        => ReasonPhrases.GetReasonPhrase(statusCode) is { Length: > 0 } reason ? reason : "Error";

    /// <summary>What an error means, by its status code.</summary>
    public virtual string ErrorDescription(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "The request could not be understood by the server.",
        StatusCodes.Status401Unauthorized => "Login required.",
        StatusCodes.Status403Forbidden => "Insufficient permissions.",
        StatusCodes.Status404NotFound => "There is nothing at this address.",
        StatusCodes.Status405MethodNotAllowed => "This address does not support that kind of request.",
        StatusCodes.Status408RequestTimeout => "The request took too long to arrive.",
        StatusCodes.Status413PayloadTooLarge => "The request was too large.",
        StatusCodes.Status429TooManyRequests => "Too many requests were made in a short time. Retrying in a moment may help.",
        StatusCodes.Status502BadGateway or StatusCodes.Status503ServiceUnavailable or StatusCodes.Status504GatewayTimeout
            => "The server is temporarily unavailable. Retrying in a moment may help.",
        >= 500 => "Something went wrong on the server while handling the request.",
        _ => "The request could not be completed."
    };

    public virtual string MayNeedLogin => "Not logged in. Some files can only be seen after logging in.";
    public virtual string LogIn => "Log in";
    public virtual string BackToStart => "Back to start";
    public virtual string TryAgain => "Try again";
    public virtual string Requested => "Requested";
    public virtual string RequestId => "Request ID";
    public virtual string Time => "Time";
}
