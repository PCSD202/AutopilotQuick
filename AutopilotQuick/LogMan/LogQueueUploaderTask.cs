using System;
using System.Threading;
using System.Threading.Tasks;
using DiskQueue;
using NLog;

namespace AutopilotQuick.LogMan;

public class LogQueueUploaderTask
{
    private static readonly LogQueueUploaderTask instance = new();

    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
    public static LogQueueUploaderTask getInstance()
    {
        return instance;
    }
    public async Task Run(UserDataContext context, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            using (var session = App.LogQueue.OpenSession())
            {
                var data = session.Dequeue();
                if (data == null) {await Task.Delay(100, ct); continue;}
                string result = System.Text.Encoding.UTF8.GetString(data);
                Console.WriteLine("Got data from queue: "+result);
                session.Flush();
            }
        }
    }
}