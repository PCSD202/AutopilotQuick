using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Nito.AsyncEx;

namespace AutopilotQuick.Steps
{
    internal class DisableTakeHomeStep : StepBaseEx
    {
        public override async Task<StepResult> Run(UserDataContext context, PauseToken pauseToken)
        {
            context.TakeHomeToggleEnabled = false;
            return new StepResult(true, "Disabled takehome option");
        }
    }
}
