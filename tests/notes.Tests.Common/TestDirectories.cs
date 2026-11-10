namespace notes.Tests.Common;

public static class TestDirectories
{
    /// <summary>
    /// Deletes the directory and everything in it, also files that are read-only: git writes its objects read-only (the history in <c>_/history</c>),
    /// which on Windows <see cref="Directory.Delete(string, bool)"/> refuses to delete.
    /// </summary>
    public static void Delete(string path)
    {
        if (!Directory.Exists(path))
            return;

        foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(path, recursive: true);
    }
}
