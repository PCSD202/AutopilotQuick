using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Nito.AsyncEx;

namespace AutopilotQuick.Steps
{
    internal class DisableTakeHomeStep : StepBaseEx
    {
        public override string Name() => "Disable take home step";
        public override async Task<StepResult> Run(UserDataContext context, PauseToken pauseToken,
            IOperationHolder<RequestTelemetry> StepOperation)
        {
            Title = "Disabling take home option";
            Progress = 100;
            context.TakeHomeToggleEnabled = false;
            return new StepResult(true, "Disabled takehome option");
        }
    }
}
