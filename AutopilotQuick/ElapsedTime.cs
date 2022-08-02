using System.Diagnostics;
using System.Text;
using NLog;
using NLog.Config;
using NLog.LayoutRenderers;

namespace AutopilotQuick;

[LayoutRenderer("elapsedtime")]
[ThreadAgnostic]
public class ElapsedTimeLayoutRenderer : LayoutRenderer
{
    static Stopwatch sw;

    public ElapsedTimeLayoutRenderer()
    {
        if (sw is null)
        {
            sw = Stopwatch.StartNew();
        }
    }

    protected override void Append(StringBuilder builder, LogEventInfo logEvent)
    {
        builder.Append(sw.ElapsedMilliseconds.ToString());
        //sw.Restart();
    }
}