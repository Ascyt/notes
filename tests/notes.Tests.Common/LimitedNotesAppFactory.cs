using Microsoft.AspNetCore.Hosting;

namespace notes.Tests.Common;

/// <summary>The app with limits (<c>Config:Limits</c>) small enough that a test reaches them with a few bytes.</summary>
public sealed class LimitedNotesAppFactory : NotesAppFactory
{
    public const int MaxUploadBytes = 10;
    public const int MaxUploadsInProgressPerUser = 2;
    public const int MaxTextBytes = 20;
    /// <summary>The smallest that is allowed: the longest name.</summary>
    public const int MaxPathBytes = 255;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Config:Limits:MaxUploadBytes", MaxUploadBytes.ToString());
        builder.UseSetting("Config:Limits:MaxUploadsInProgressPerUser", MaxUploadsInProgressPerUser.ToString());
        builder.UseSetting("Config:Limits:MaxTextBytes", MaxTextBytes.ToString());
        builder.UseSetting("Config:Limits:MaxPathBytes", MaxPathBytes.ToString());
    }
}
