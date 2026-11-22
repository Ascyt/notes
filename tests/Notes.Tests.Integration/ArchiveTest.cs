using System.Net;
using System.Net.Http.Json;
using ICSharpCode.SharpZipLib.Zip;
using SharpCompress.Archives;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Readers;

namespace Notes.Tests.Integration;

/// <summary>What the tests of downloading as an archive share: asking for one as the dialog does, downloading it, and reading what is in it.</summary>
public abstract class ArchiveTest(NotesAppFactory app) : IntegrationTest(app)
{
    /// <summary>The answer of the server once the archive is made.</summary>
    protected sealed record PreparedArchiveDto(Guid Id, bool LeftOutUnreadable);

    /// <summary>Asks for an archive of the directory (<c>kind</c> "directory") or of the selection ("selection", with its version).</summary>
    protected Task<HttpResponseMessage> PrepareAsync(TestUser user, string address, string kind, string format = "7z", string compression = "high",
        string? password = null, long? version = null)
        => Client.PostJsonAsync(user, address + "?archive=" + kind, new { format, compression, password, version });

    /// <summary>Makes the archive (which must work) and downloads it.</summary>
    protected async Task<(PreparedArchiveDto Prepared, byte[] Bytes)> PrepareAndDownloadAsync(TestUser user, string address, string kind,
        string format = "7z", string compression = "high", string? password = null, long? version = null)
    {
        HttpResponseMessage prepared = await PrepareAsync(user, address, kind, format, compression, password, version);
        await AssertStatus(HttpStatusCode.OK, prepared);
        PreparedArchiveDto dto = (await prepared.Content.ReadFromJsonAsync<PreparedArchiveDto>(Cancellation))!;

        HttpResponseMessage download = await Client.GetAsync(user, address + "?archive=" + dto.Id);
        await AssertStatus(HttpStatusCode.OK, download);
        Assert.Equal("application/octet-stream", download.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition?.DispositionType);
        return (dto, await download.Content.ReadAsByteArrayAsync(Cancellation));
    }

    /// <summary>The archive files the server keeps (in the app's data at the root).</summary>
    protected List<string> ArchiveFiles()
    {
        string directory = Tree.Physical("_/temp/archive");
        return Directory.Exists(directory) ? [.. Directory.EnumerateFiles(directory)] : [];
    }

    /// <summary>The paths of the files in a 7z (folders end in "/"), and what each file holds.</summary>
    protected static Dictionary<string, string> SevenZipContents(byte[] bytes, string? password = null)
    {
        using IArchive archive = SevenZipArchive.OpenArchive(new MemoryStream(bytes), new ReaderOptions { Password = password });
        Dictionary<string, string> contents = new(StringComparer.Ordinal);
        foreach (IArchiveEntry entry in archive.Entries)
        {
            if (entry.IsDirectory)
            {
                contents[entry.Key!.Replace('\\', '/') + "/"] = "";
                continue;
            }

            using StreamReader reader = new(entry.OpenEntryStream());
            contents[entry.Key!.Replace('\\', '/')] = reader.ReadToEnd();
        }

        return contents;
    }

    /// <summary>The paths of the entries of a zip (folders end in "/"), and what each file holds.</summary>
    protected static Dictionary<string, string> ZipContents(byte[] bytes, string? password = null)
    {
        using ZipFile zip = new(new MemoryStream(bytes)) { Password = password };
        Dictionary<string, string> contents = new(StringComparer.Ordinal);
        foreach (ZipEntry entry in zip)
        {
            if (entry.IsDirectory)
            {
                contents[entry.Name] = "";
                continue;
            }

            using StreamReader reader = new(zip.GetInputStream(entry));
            contents[entry.Name] = reader.ReadToEnd();
        }

        return contents;
    }
}
