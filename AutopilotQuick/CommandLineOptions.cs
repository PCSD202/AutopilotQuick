using CommandLine;

namespace AutopilotQuick;

public class CommandLineOptions
{
    [Option('r', "run", Required = false, HelpText = "Disables dry run")]
    public bool Enabled { get; set; }
    
    [Option('d', "data", Required = false, HelpText = "Sets the directory of the application data", Default = "")]
    public string DataLocation { get; set; }
    
}