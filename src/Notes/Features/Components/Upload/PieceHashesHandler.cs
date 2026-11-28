using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Notes.Features.Auth;
using Notes.Features.EntryActions;
using Notes.Features.Helpers;

namespace Notes.Features.Components.Upload;

/// <summary>
/// <c>GET /folder?hashes=name&amp;size=n&amp;piece=p</c>: the SHA-256 of each piece (of <c>p</c> bytes) of the file of that name in the folder, if
/// it is <c>n</c> bytes large, so that uploading can leave out a file that is there already, the same (the browser hashes its pieces as well:
/// it can't hash a large file in one go). <c>{ "hashes": [ "hex", … ] }</c>, or <c>{ "hashes": null }</c> if there is no such file, it is of
/// another size, or the user may not read it (a password locks it, say): then it is uploaded, and gets a number if its name is taken.
/// </summary>
public sealed class PieceHashesHandler(IPathService path, AccessTree tree) : IEntryGetAction
{
    public const string HashesParameter = "hashes";

    /// <summary>The pieces the browser may ask for: around the size of the chunks of an upload, so that each is hashed in memory.</summary>
    private const int MinPieceBytes = 1024 * 1024;
    private const int MaxPieceBytes = 64 * 1024 * 1024;

    public string Parameter => HashesParameter;
    public AccessNeeded Needs => AccessNeeded.Readable;

    public async Task<IActionResult> GetAsync(EntryRequest request)
    {
        string name = request.Value;
        if (name.Length == 0 || name.Contains('/') || !long.TryParse(request.Request.Query["size"], out long size) || size < 0
            || !int.TryParse(request.Request.Query["piece"], out int piece) || piece is < MinPieceBytes or > MaxPieceBytes)
            return new BadRequestResult();

        if (path.ToPhysicalPath(request.VirtualPath == "" ? name : request.VirtualPath + "/" + name) is not { } file
            || !File.Exists(file) || new FileInfo(file).Length != size || !tree.GetAccess(request.User, file).CanRead)
            return new JsonResult(new { hashes = (string[]?)null });

        List<string> hashes = [];
        byte[] buffer = new byte[piece];
        try
        {
            await using FileStream stream = new(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, useAsync: true);
            // (An empty file has one empty piece, as the browser sends it)
            do
            {
                int length = await stream.ReadAtLeastAsync(buffer, piece, throwOnEndOfStream: false, request.Context.RequestAborted);
                hashes.Add(Convert.ToHexStringLower(SHA256.HashData(buffer.AsSpan(0, length))));
                if (length < piece)
                    break;
            } while (stream.Position < stream.Length);
        }
        catch (IOException)
        {
            // Changed or gone meanwhile: as if it weren't there
            return new JsonResult(new { hashes = (string[]?)null });
        }
        return new JsonResult(new { hashes });
    }
}
