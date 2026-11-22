using Notes.Features.Components.Archive;

namespace Notes.Tests.Unit;

/// <summary>What the archive dialog may send (docs/development/user-input.md, "How to make an archive").</summary>
public sealed class ArchiveOptionsTests
{
    [Theory]
    [InlineData("7z", "min", ArchiveFormat.SevenZip, ArchiveCompression.Min, ".7z")]
    [InlineData("7z", "medium", ArchiveFormat.SevenZip, ArchiveCompression.Medium, ".7z")]
    [InlineData("7z", "max", ArchiveFormat.SevenZip, ArchiveCompression.Max, ".7z")]
    [InlineData("zip", "store", ArchiveFormat.Zip, ArchiveCompression.Store, ".zip")]
    [InlineData("zip", "high", ArchiveFormat.Zip, ArchiveCompression.High, ".zip")]
    public void The_values_of_the_dialog_are_read(string format, string compression, ArchiveFormat expectedFormat,
        ArchiveCompression expectedCompression, string extension)
    {
        ArchiveOptions? options = ArchiveOptions.Parse(format, compression, null);

        Assert.NotNull(options);
        Assert.Equal(expectedFormat, options.Format);
        Assert.Equal(expectedCompression, options.Compression);
        Assert.Equal(extension, options.Extension);
        Assert.Null(options.Password);
    }

    [Theory]
    [InlineData("7z", "store")]
    [InlineData("zip", "min")]
    [InlineData("tar", "high")]
    [InlineData("Zip", "high")]
    [InlineData("zip", "High")]
    [InlineData("zip", "9")]
    [InlineData(null, null)]
    public void Anything_else_is_refused(string? format, string? compression)
        => Assert.Null(ArchiveOptions.Parse(format, compression, null));

    [Fact]
    public void An_empty_password_is_none_and_any_other_is_kept_as_it_is()
    {
        Assert.Null(ArchiveOptions.Parse("7z", "high", "")!.Password);
        Assert.Equal(" ünï cødé 🔑 ", ArchiveOptions.Parse("7z", "high", " ünï cødé 🔑 ")!.Password);
    }

    [Theory]
    [InlineData("a\nb")]
    [InlineData("a\u0007")]
    [InlineData("\u007f")]
    public void A_password_with_control_characters_is_refused(string password)
        => Assert.Null(ArchiveOptions.Parse("zip", "high", password));

    [Fact]
    public void A_password_longer_than_the_limit_is_refused()
    {
        Assert.NotNull(ArchiveOptions.Parse("zip", "high", new string('x', ArchiveOptions.MaxPasswordLength)));
        Assert.Null(ArchiveOptions.Parse("zip", "high", new string('x', ArchiveOptions.MaxPasswordLength + 1)));
    }
}
