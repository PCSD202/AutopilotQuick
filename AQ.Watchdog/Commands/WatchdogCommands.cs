namespace AQ.Watchdog.Commands;


public class EnableWatchdogCommand : ICommand
{
    public string Name => "Enable";
}

public class DisableWatchdogCommand : ICommand
{
    public string Name => "Disable";
}

public class CloseWatchdogCommand : ICommand
{
    public string Name => "Close";
}