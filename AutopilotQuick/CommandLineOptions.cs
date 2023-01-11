#region

using CommandLine;

#endregion

namespace AutopilotQuick;

public class CommandLineOptions
{
    [Option('w', "watchdog", Required = false, HelpText = "Weather or not to start the watchdog server", Default = false)]
    public bool AmWatchdog { get; set; }
    
    [Option('p', "pid", Required = false, HelpText = "Process ID to monitor", Default = -1)]
    public int processIDToMonitor { get; set; }
    
    [Option('r', "run", Required = false, HelpText = "Disables dry run")]
    public bool Enabled { get; set; }
    
    [Option('d', "data", Required = false, HelpText = "Sets the directory of the application data", Default = "")]
    public string DataLocation { get; set; }
    
}