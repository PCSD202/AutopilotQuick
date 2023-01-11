using System.Diagnostics;
using System.Text;
using System.Text.Unicode;
using AQ.Watchdog.Commands;
using CommandLine;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ZetaIpc.Runtime.Client;
using ZetaIpc.Runtime.Server;

namespace AQ.Watchdog;


public class Watchdog
{
    private const string serverMutexName = "WatchdogServer";
    private const int IPCPort = 42069;

    private WatchdogServerComs watchdogServer = new WatchdogServerComs(IPCPort);
    private WatchdogClientComs watchdogClient = new WatchdogClientComs(IPCPort);

    public bool Enabled = true;
    private static readonly Lazy<Watchdog> instance = new(()=>new Watchdog());
    public static Watchdog Instance => instance.Value;

    public bool IsServer { get; private set; } = false;
    
    public Process WatchdogProcess { get; set; } = null;
    private Process _MonitoredProcess { get; set; } = null;
    public bool ShouldClose = false; //Only works for server
    
    private ILogger<Watchdog> Logger = NullLogger<Watchdog>.Instance;
    
    public void ConfigureLogger(ILogger<Watchdog> logger)
    {
        Logger = logger;
    }

    private bool LaunchWatchdogServer(IEnumerable<string> args)
    {
        var argList = args.ToList();
        using (var mutex = new Mutex(true, serverMutexName, out var createdNew))
        {
            if (!createdNew)
            {
                return false; //Server was already started
            }
        }
        
        //Start the watchdog server with the same args as before but add the --watchdog arg and the --pid arg
        var executable = Process.GetCurrentProcess().MainModule.FileName;
        if (executable is null)
        {
            throw new NullReferenceException("Executable path cannot be null");
        }
        
        argList.Add("--watchdog");
        argList.Add($"--pid {Environment.ProcessId}");
        
        var p = new Process(){EnableRaisingEvents = true, StartInfo = new ProcessStartInfo()
        {
            FileName = executable,
            Arguments = string.Join(" ", argList)
        }};
        
        Logger.LogInformation("Launching watchdog server...");
        p.Start();

        WatchdogProcess = p;
        WatchdogProcess.Exited += (sender, eventArgs) =>
        {
            if (WatchdogProcess.ExitCode != 0)
            {
                Logger.LogWarning("The watchdog process closed unexpectedly. Restarting it...");
                p.Start();
            }
        };

        return true;
    }

    public void HandleArgs(IEnumerable<string> args)
    {
        
        //If we do not have the --watchdog arg, start ourselves with the same args and the watchdog arg
        Parser.Default.ParseArguments<WatchdogCommandlineArgs>(args)
            .WithParsed(o =>
            {
                if (o.AmWatchdog)
                {
                    IsServer = true;
                }

                if (o.processIDToMonitor != -1)
                {
                    try
                    {
                        MonitorProcess(o.processIDToMonitor);
                    }
                    catch (ArgumentException)
                    {
                        Logger.LogCritical("Could not find process with provided ID: {id}, watchdog exiting.", o.processIDToMonitor);
                        throw new Exception($"Could not find process with provided ID: {o.processIDToMonitor}");
                    }
                }
            });

        if (!IsServer)
        {
            LaunchWatchdogServer(args);
            watchdogClient.Enable();
            
        }

        if (IsServer)
        {
            try
            {
                WatchdogProcess = Process.GetCurrentProcess(); //Its me!
                HandleServer();
            }
            catch (Exception e)
            {
                Logger.LogError("Watchdog server threw: {e}", e);
            }
            
        }

    }

    public bool SendCommand(ICommand command)
    {
        try
        {
            var r = watchdogClient.SendCommand(command);
            return r;
        }
        catch (Exception e)
        {
            Logger.LogError("Got error {e} while sending message", e);
            return false;
        }
        
    }

    private void HandleServer()
    {
        Logger.LogInformation("I am the watchdog.");
        watchdogServer.Enable();
        var mutex = new Mutex(true, serverMutexName, out var createdNew);
        while (true)
        {
            Thread.Sleep(1000);
            if (ShouldClose)
            {
                Environment.Exit(0);
            }
        }
    }

    private void MonitorProcess(int pid)
    {
        MonitorProcess(Process.GetProcessById(pid));
    }
    private void MonitorProcess(Process processToMonitor)
    {
        _MonitoredProcess = processToMonitor;
        _MonitoredProcess.Exited -= MonitoredProcessOnExited;
        _MonitoredProcess.EnableRaisingEvents = true;
        _MonitoredProcess.Exited += MonitoredProcessOnExited;
        Logger.LogInformation("Watching process: {id}", _MonitoredProcess);
    }
    
    private void MonitoredProcessOnExited(object? sender, EventArgs e)
    {
        Logger.LogWarning("Monitored process exited...");
        if (Enabled)
        {
            var processLaunchArgs = Environment.GetCommandLineArgs().ToList();
            processLaunchArgs.RemoveRange(processLaunchArgs.Count - 3, 3);
            Logger.LogInformation("LaunchArgs: {args}", string.Join(", ", processLaunchArgs));
            var p = new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName, string.Join(" ", processLaunchArgs.Skip(1)));
            MonitorProcess(Process.Start(p)); //Start the process, and monitor it for exits
        }
    }
}

