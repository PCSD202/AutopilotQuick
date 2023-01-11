#region

using AQ.Watchdog.Commands;
using Newtonsoft.Json;
using ZetaIpc.Runtime.Client;

#endregion

namespace AQ.Watchdog;

public class WatchdogClientComs
{
    private int port;
    private readonly IpcClient _client = new IpcClient();
    public WatchdogClientComs(int port)
    {
        this.port = port;
    }

    public bool SendCommand(ICommand command)
    {
        string jsonTypeNameAll = JsonConvert.SerializeObject(command, Formatting.None, new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.All
        });
        Console.WriteLine($"SEND> {jsonTypeNameAll}");
        var response = _client.Send(jsonTypeNameAll);
        Console.WriteLine($"RECV> {response}");
        if (response.ToLower() == "true")
        {
            return true;
        }
        else
        {
            return false;
        }
    }

    public void Enable()
    {
        _client.Initialize(port);
    }
}