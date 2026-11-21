using Microsoft.AspNetCore.Hosting;

namespace Notes.Tests.Common;

/// <summary>
/// The app as a site that shows only some directories of the <see cref="BaseTree"/> (<c>Config:Exposed</c>): <see cref="Docs"/>, and
/// <see cref="Nested"/> without the directory above it.
/// </summary>
public sealed class ExposedNotesAppFactory : NotesAppFactory
{
    public const string Docs = "docs";
    public const string Nested = "team/sub";

    public ExposedNotesAppFactory()
    {
        // They have to be there when the app starts (the tree of each test makes them again)
        _ = Directory.CreateDirectory(Path.Combine(NotesDir, Docs));
        _ = Directory.CreateDirectory(Path.Combine(NotesDir, "team", "sub"));
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Config:Exposed:0", Docs);
        builder.UseSetting("Config:Exposed:1", Nested);
    }
}

/// <summary>The app with the pages of the app at the root (<c>Config:AppPath</c> empty): <c>/Login</c>, <c>/About</c>, ….</summary>
public sealed class RootAppPathNotesAppFactory : NotesAppFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Config:AppPath", "");
    }
}

/// <summary>The app with the pages of the app at an address of their own other than <c>/_</c> (<c>Config:AppPath</c> <c>/app</c>).</summary>
public sealed class OtherAppPathNotesAppFactory : NotesAppFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Config:AppPath", "/app");
    }
}
