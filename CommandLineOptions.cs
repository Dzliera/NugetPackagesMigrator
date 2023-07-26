using CommandLine;

namespace NugetPackagesMigrator;

public class CommandLineOptions
{
    [Option(longName: "from", HelpText = "Name of nuget source to copy packages from", Required = true)]
    public string NugetSourceNameToCopyFrom { get; init; } = null!;

    [Option(longName: "to", HelpText = "Name of target nuget source where packages should be copied", Required = true)]
    public string NugetSourceNameToCopyTo { get; init; } = null!;
}