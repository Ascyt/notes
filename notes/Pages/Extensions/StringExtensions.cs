namespace notes.Pages.Extensions;

public static class StringExtensions
{
    extension(string? s)
    {
        public string? Normalized()
            => string.IsNullOrWhiteSpace(s) ? null : s;

        public string NormalizedWith(string v)
            => s.Normalized() ?? v;
    }
}