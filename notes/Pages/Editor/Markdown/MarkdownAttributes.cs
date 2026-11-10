using System.Text.RegularExpressions;
using Markdig.Extensions.CustomContainers;
using Markdig.Renderers.Html;

namespace notes.Pages.Editor.Markdown;

/// <summary>
/// The attributes of the rendered markdown: what a note sets itself (<c>{#id .class}</c>) is cut down to what can't touch the page,
/// and what the extensions make gets classes that no part of the page itself uses.
/// </summary>
internal static partial class MarkdownAttributes
{
    /// <summary>What the ids of headings (and <c>{#id}</c>) get in front of them, so that none of them can be the id of something of the page itself.</summary>
    public const string IdPrefix = "user-content-";

    /// <summary>The classes the extensions put on what they make. Any other class (from <c>{.class}</c>) is dropped.</summary>
    private static readonly HashSet<string> GeneratedClasses = ["contains-task-list", "task-list-item", "math"];

    /// <summary>Beginnings of classes the extensions make, which nothing of the page itself uses: a fenced code block's language, an alert's kind.</summary>
    private static readonly string[] GeneratedClassPrefixes = ["language-", "markdown-alert"];

    /// <summary>
    /// The class of a custom container (<c>:::name</c>). Its name becomes a second class behind this one (<c>markdown-container-name</c>),
    /// so that it can't be a class of the page.
    /// </summary>
    private const string ContainerClass = "markdown-container";

    public static void Sanitize(HtmlAttributes attributes)
    {
        if (attributes.Id is not null)
            attributes.Id = IdPrefix + attributes.Id;

        _ = attributes.Classes?.RemoveAll(name => !GeneratedClasses.Contains(name)
                                              && !GeneratedClassPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)));
        attributes.Properties = null;
    }

    public static void AddContainerClasses(CustomContainer container)
    {
        container.GetAttributes().AddClass(ContainerClass);
        if (ContainerName().IsMatch(container.Info ?? ""))
            container.GetAttributes().AddClass(ContainerClass + "-" + container.Info!.ToLowerInvariant());
    }

    /// <summary>What a custom container's name may be to become a class: anything else gets none.</summary>
    [GeneratedRegex("^[A-Za-z0-9-]+$")]
    private static partial Regex ContainerName();
}
