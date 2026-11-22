using Notes.Features.Markdown;

namespace Notes.Tests.Unit;

/// <summary>Which name a markdown file is shown with: the text of its first top-level heading, or none.</summary>
public sealed class MarkdownTitlesTests
{
    [Theory]
    [InlineData("# Title", "Title")]
    [InlineData("Some text first.\n\n## Below\n\n# Title\n\n# Second", "Title")]
    [InlineData("# Title with *emphasis*, `code` and [a link](x.md) #", "Title with emphasis, code and a link")]
    public void The_first_top_level_heading_is_the_title(string markdown, string title)
        => Assert.Equal(title, MarkdownTitles.Of(markdown));

    [Theory]
    [InlineData("No heading at all")]
    [InlineData("## Only a lower heading")]
    [InlineData("#Not a heading without a space")]
    [InlineData("```\n# In a code block\n```")]
    [InlineData("> # In a quote")]
    [InlineData("#   ")]
    public void Without_a_top_level_heading_there_is_no_title(string markdown)
        => Assert.Null(MarkdownTitles.Of(markdown));

    [Fact]
    public void A_file_is_read_for_its_title()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "# From the file\n\nText.");
            Assert.Equal("From the file", MarkdownTitles.OfFile(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_missing_file_has_no_title()
        => Assert.Null(MarkdownTitles.OfFile(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".md")));
}
