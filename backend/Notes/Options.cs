using CommandLine;

namespace Notes;

public class Options
{
    [Option('d', "directory", Required = false, HelpText = "Entry point directory.")]
    public string Directory { get; set; } = "";
}