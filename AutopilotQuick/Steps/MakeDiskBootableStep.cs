using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Nito.AsyncEx;
using NLog;

namespace AutopilotQuick.Steps
{
    internal class MakeDiskBootableStep : StepBaseEx
    {
        public readonly Logger Logger = LogManager.GetCurrentClassLogger();
        public override async Task<StepResult> Run(UserDataContext context, PauseToken pauseToken)
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
                Logger.Info($"Output: {Regex.Replace(output, @"^\s*$\n|\r", string.Empty, RegexOptions.Multiline).TrimEnd()}");
                Progress = 100;
            }

            return new StepResult(true, "Successfully made disk bootable");
        }
    }
}
