using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;

namespace AutopilotQuick.Steps
{
    internal class MakeDiskBootableStep : StepBaseEx
    {
        public override string Name() => "Make disk bootable step";
        public readonly ILogger Logger = App.GetLogger<FormatStep>();
        public override async Task<StepResult> Run(UserDataContext context, PauseToken pauseToken,
            IOperationHolder<RequestTelemetry> StepOperation)
        {
            if (IsEnabled)
            {
                Title = "Making disk bootable";
                
                IsIndeterminate  = true;
                var script = @"
W:\Windows\System32\bcdboot W:\Windows /s S:
md R:\Recovery\WindowsRE
xcopy /h W:\Windows\System32\Recovery\Winre.wim R:\Recovery\WindowsRE\
W:\Windows\System32\Reagentc /Setreimage /Path R:\Recovery\WindowsRE /Target W:\Windows
W:\Windows\System32\Reagentc /Info /Target W:\Windows
";
                Progress = 50;
                var output = await InvokePowershellScriptAndGetResultAsync(script, CancellationToken.None);
                Logger.LogInformation("Output: {output}", Regex.Replace(output, @"^\s*$\n|\r", string.Empty, RegexOptions.Multiline).TrimEnd());
                Progress = 100;
            }

            return new StepResult(true, "Successfully made disk bootable");
        }
    }
}
