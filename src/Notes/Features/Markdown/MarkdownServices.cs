using Microsoft.Extensions.DependencyInjection.Extensions;
using Notes.Features.Markdown.Preview;

namespace Notes.Features.Markdown;

public static class MarkdownServices
{
    /// <summary>
    /// Rendered Markdown on the page of a file: its texts (the site's to replace, see <see cref="MarkdownTexts"/>). Its styles are only linked
    /// by the pages that have it (<see cref="MarkdownAssets.Styles"/>).
    /// </summary>
    public static IServiceCollection AddMarkdown(this IServiceCollection services)
    {
        services.TryAddSingleton<MarkdownTexts>();
        return services;
    }
}
