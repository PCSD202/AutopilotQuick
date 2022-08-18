using DiskQueue;
using NLog;
using NLog.Config;
using NLog.Targets;

namespace AutopilotQuick.LogMan;

[Target("DiskQueue")] 
public sealed class LogDiskQueueTarget: TargetWithLayout
{ 
    public LogDiskQueueTarget()
    {
    }
 
    [RequiredParameter] 
    public IPersistentQueue DiskQueue { get; set; }
 
    protected override void Write(LogEventInfo logEvent) 
    { 
        string logMessage = this.Layout.Render(logEvent); 

        SendTheMessageToTheQueue(this.DiskQueue, logMessage); 
    } 
 
    private void SendTheMessageToTheQueue(IPersistentQueue queue, string message)
    {
        using var session = queue.OpenSession();
        byte[] bytes= System.Text.Encoding.UTF8.GetBytes(message);
        session.Enqueue(bytes);
        session.Flush();
    } 
} 