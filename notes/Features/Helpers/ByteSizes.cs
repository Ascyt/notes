namespace notes.Features.Helpers;

public static class ByteSizes
{
    /// <summary>A number of bytes as it is shown: in the largest unit (KiB, MiB, …) that keeps it at 1 or more, with one decimal at most.</summary>
    public static string Format(long bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB"];
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes} B" : $"{size:0.#} {units[unit]}";
    }
}
