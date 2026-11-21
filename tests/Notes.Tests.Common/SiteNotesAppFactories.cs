using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Notes.Features.Components.Assets;
using Notes.Features.Components.Breadcrumb;
using Notes.Features.Components.Toolbar;
using Notes.Pages.Browse;

namespace Notes.Tests.Common;

/// <summary>
/// The app as part of a site with views of its own (the views in Pages of this project): a <c>_ViewStart</c> and a <c>_Layout</c> for its own
/// pages, as nearly every site has them, and a layout it made for the notes. With the defaults, the notes keep their own layout.
/// </summary>
public class SiteNotesAppFactory : NotesAppFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services => services.AddRazorPages().AddApplicationPart(typeof(SiteNotesAppFactory).Assembly));
    }
}

/// <summary>
/// The app as part of a site whose breadcrumbs start with two links of its own (<c>Config:BreadcrumbParents</c>): its start page, and a page
/// of another site.
/// </summary>
public sealed class BreadcrumbParentsNotesAppFactory : NotesAppFactory
{
    public const string SiteStart = "/";
    public const string Elsewhere = "https://example.org/start";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Config:BreadcrumbParents:0:Name", "The organization");
        builder.UseSetting("Config:BreadcrumbParents:0:Address", SiteStart);
        builder.UseSetting("Config:BreadcrumbParents:1:Name", "Elsewhere");
        builder.UseSetting("Config:BreadcrumbParents:1:Address", Elsewhere);
    }
}

/// <summary>The site of <see cref="SiteNotesAppFactory"/>, which shows the notes in the layout it made for them (<see cref="NotesSetup.Layout"/>).</summary>
public sealed class SiteLayoutNotesAppFactory : SiteNotesAppFactory
{
    public const string Layout = "/Pages/Shared/_SiteLayout.cshtml";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        // What AddNotes was given, as the site's code would have chosen it (the pages ask it as they are shown)
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<NotesSetup>();
            services.AddSingleton(new NotesSetup { Layout = Layout });
        });
    }
}

/// <summary>
/// The app as part of a site that makes some of its components its own (docs/library.md): the styles of the toolbar and the script of the
/// breadcrumb replaced (the latter by nothing), a stylesheet added, and texts of the toolbar and the listing of its own.
/// </summary>
public sealed class SiteComponentsNotesAppFactory : NotesAppFactory
{
    public const string ToolbarStyles = "/site/toolbar.css";
    public const string AddedStyles = "/site/notes.css";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            _ = services.Configure<ComponentAssets>(assets => assets
                .Replace(_Toolbar.Styles, ToolbarStyles)
                .Replace(_Breadcrumb.Script, null)
                .AddStyles(AddedStyles));
            _ = services.AddSingleton<ToolbarTexts, SiteToolbarTexts>();
            _ = services.AddSingleton<ListingTexts, SiteListingTexts>();
        });
    }

    /// <summary>Says one thing differently, and everything else as the app does.</summary>
    public sealed class SiteToolbarTexts : ToolbarTexts
    {
        public const string SiteUpload = "Add files to the folder";

        public override string Upload => SiteUpload;
    }

    public sealed class SiteListingTexts : ListingTexts
    {
        public const string SiteDirectories = "Folders";

        public override string Directories => SiteDirectories;
    }
}
