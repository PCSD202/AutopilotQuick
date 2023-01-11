#region

using AQ.Watchdog.Commands;
using Newtonsoft.Json;
using ZetaIpc.Runtime.Server;

#endregion

namespace AQ.Watchdog;

public class WatchdogServerComs
{
    private int port;
    private readonly IpcServer _server = new IpcServer();
    
    public WatchdogServerComs(int port)
    {
        this.port = port;
        _server.ReceivedRequest += ServerOnReceivedRequest;
    }

    private void ServerOnReceivedRequest(object? sender, ReceivedRequestEventArgs e)
    {
        Console.WriteLine($"RECV> {e.Request}");
        var recv = JsonConvert.DeserializeObject<ICommand>(e.Request, new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.Auto
        });

        if (recv is not null)
        {
            switch (recv.Name)
            {
                case "Enable":
                    Console.WriteLine("Enable recv");
                    Watchdog.Instance.Enabled = true;
                    break;
                case "Disable":
                    Console.WriteLine("Disable recv");
                    Watchdog.Instance.Enabled = false;
                    break;
                case "Close":
                    Console.WriteLine("Close recv");
                    Task.Run(async () =>
                    {
                        Watchdog.Instance.Enabled = false;
                        await Task.Delay(200);
                        Watchdog.Instance.ShouldClose = true;
                    });
                    break;
                default:
                    Console.WriteLine("Not understood");
                    break;
            }
        }
        
        e.Response = "TRUE";
        Console.WriteLine($"SEND> {e.Response}");
        e.Handled = true;
    }

    public void Enable()
    {
        _server.Start(port);
    }

    public void Disable()
    {
        _server.Stop();
    }
}