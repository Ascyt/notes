namespace Notes.Core.Disk.Naming;

public sealed class Service : IService
{
    public string GetFormattedName(string dir, string? name)
    {
        string? formattedName = null;
        if (!string.IsNullOrWhiteSpace(name))
        {
            // Replace invalid characters with underscores
            char[] invalidChars = System.IO.Path.GetInvalidFileNameChars();
            formattedName = new([.. name.Select(c => invalidChars.Contains(c) ? '_' : c)]);
            formattedName = formattedName
                .Trim()
                .Replace(' ', '_');
        }
        formattedName ??= "untitled";
        
        int? attempts = 0;
        string tempFormattedName = formattedName;
        while (System.IO.File.Exists(System.IO.Path.Combine(dir, tempFormattedName + ".md")) || 
            System.IO.Directory.Exists(System.IO.Path.Combine(dir, tempFormattedName)))
        {
            attempts++;

            const int MAX_ATTEMPTS = 256;
            if (attempts >= MAX_ATTEMPTS) // Prevent infinite loops
                throw new InvalidOperationException($"Unable to generate a unique name after {MAX_ATTEMPTS} attempts.");

            tempFormattedName = $"{formattedName}__{attempts}";
        }

        return tempFormattedName;
    }
}
