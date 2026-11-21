using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Options;

namespace Notes.Features.Components.Assets;

/// <summary>
/// <c>&lt;component-styles/&gt;</c>: the stylesheets of the components, and those the site adds (<see cref="ComponentAssets.StylesToLink"/>).
/// In the head of every layout (<c>_Head</c>).
/// </summary>
[HtmlTargetElement("component-styles", TagStructure = TagStructure.WithoutEndTag)]
public sealed class ComponentStylesTagHelper(IOptions<ComponentAssets> assets) : TagHelper
{
    [ViewContext, HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = null!;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = null;
        HttpContext http = ViewContext.HttpContext;
        http.Items[PageStylesTagHelper.LinkedKey] = true;
        foreach (string address in assets.Value.StylesToLink(PageStylesTagHelper.AskedFor(http)))
        {
            _ = output.Content.AppendHtml("<link rel=\"stylesheet\" href=\"");
            _ = output.Content.Append(UniqueAddresses.Of(ViewContext.HttpContext, address));
            _ = output.Content.AppendHtml("\"/>\n");
        }
    }
}

/// <summary>
/// <c>&lt;component-style path="…"/&gt;</c>: a stylesheet that only the page that has it needs (such as that of rendered Markdown). A page is
/// made before its layout, so the stylesheet is linked in the head with those of the components, before those the site adds. Where the head
/// is already made (in the layout itself), it is linked where it is.
/// </summary>
[HtmlTargetElement("component-style", Attributes = "path", TagStructure = TagStructure.WithoutEndTag)]
public sealed class PageStylesTagHelper(IOptions<ComponentAssets> assets) : TagHelper
{
    internal const string LinkedKey = "app.component-styles-linked";
    private const string AskedForKey = "app.page-styles";

    /// <summary>The stylesheet, by its path in the library.</summary>
    [HtmlAttributeName("path")]
    public string Path { get; set; } = "";

    [ViewContext, HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = null!;

    /// <summary>The stylesheets the page asked for, by their paths in the library.</summary>
    internal static IReadOnlyList<string> AskedFor(HttpContext http)
        => http.Items[AskedForKey] as List<string> ?? [];

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        HttpContext http = ViewContext.HttpContext;
        if (http.Items[LinkedKey] is null)
        {
            if (http.Items[AskedForKey] is not List<string> asked)
                http.Items[AskedForKey] = asked = [];
            asked.Add(Path);
            output.SuppressOutput();
            return;
        }

        output.TagName = "link";
        output.TagMode = TagMode.SelfClosing;
        output.Attributes.SetAttribute("rel", "stylesheet");
        if (assets.Value.AddressOf(Path) is { } address)
            output.Attributes.SetAttribute("href", UniqueAddresses.Of(http, address));
        else
            output.SuppressOutput();
    }
}

/// <summary>
/// A style or script of a component, by its path in the library: <c>&lt;script component-asset="Features/…/_Breadcrumb.js"&gt;</c>, or
/// <c>&lt;link rel="stylesheet" component-asset="…"/&gt;</c> for one that the head links itself. It is loaded from where
/// <see cref="ComponentAssets"/> says, and not at all where the site left it out.
/// </summary>
[HtmlTargetElement("script", Attributes = PathAttribute)]
[HtmlTargetElement("link", Attributes = PathAttribute)]
public sealed class ComponentAssetTagHelper(IOptions<ComponentAssets> assets) : TagHelper
{
    private const string PathAttribute = "component-asset";

    [HtmlAttributeName(PathAttribute)]
    public string Path { get; set; } = "";

    [ViewContext, HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = null!;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (assets.Value.AddressOf(Path) is not { } address)
        {
            output.SuppressOutput();
            return;
        }

        output.Attributes.SetAttribute(output.TagName == "script" ? "src" : "href", UniqueAddresses.Of(ViewContext.HttpContext, address));
    }
}

/// <summary>
/// The address of a static asset with its fingerprint in it, as <c>asp-append-version</c> makes it, so that the browser may keep it for good
/// and loads it again once it has changed. Addresses of other sites, and assets the site doesn't know, stay as they are.
/// </summary>
internal static class UniqueAddresses
{
    public static string Of(HttpContext context, string address)
    {
        if (!address.StartsWith('/') || address.StartsWith("//", StringComparison.Ordinal))
            return address;

        // The assets of the page's endpoint (MapRazorPages().WithStaticAssets()), by their path without the "/" at the start
        ResourceAssetCollection? known = context.GetEndpoint()?.Metadata.GetMetadata<ResourceAssetCollection>();
        string unique = known is null ? address : "/" + known[address[1..]];
        return context.Request.PathBase + unique;
    }
}
