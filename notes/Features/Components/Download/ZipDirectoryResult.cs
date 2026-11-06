using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace notes.Features.Components.Download;

/// <summary>
/// Sends a directory as a zip download. The zip is written to the response while it is made, so the download starts
/// at once (no waiting on a temporary file, no proxy timing out on a big directory), which also means there is no Content-Length.
/// </summary>
/// <param name="include">Which entries go into the zip (see <see cref="IZipService.WriteZipAsync"/>).</param>
public sealed class ZipDirectoryResult(IZipService zipService, string physicalDirectory, string fileName, Func<FileSystemInfo, bool> include) : IActionResult
{
    public async Task ExecuteResultAsync(ActionContext context)
    {
        HttpContext http = context.HttpContext;

        http.Response.ContentType = "application/zip";
        ContentDispositionHeaderValue disposition = new("attachment");
        disposition.SetHttpFileName(fileName);
        http.Response.Headers.ContentDisposition = disposition.ToString();

        if (HttpMethods.IsHead(http.Request.Method))
            return;

        await zipService.WriteZipAsync(physicalDirectory, http.Response.Body, include, http.RequestAborted);
    }
}
