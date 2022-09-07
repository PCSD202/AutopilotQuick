using Humanizer;

namespace AQ.Connectivity;

public class ConnectivityServiceOptions
{
    public TimeSpan RefreshTime { get; set; } = 5.Seconds();
    public TimeSpan Timeout { get; set; } = 2.Seconds();
    
    public TimeSpan StartDelay { get; set; } = 2.Seconds();
    
    public string URL { get; set; } = "https://www.google.com";
}