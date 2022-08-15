﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Nito.AsyncEx;

namespace AutopilotQuick.Steps
{
    internal class RebootStep : StepBaseEx
    {
        public override async Task<StepResult> Run(UserDataContext context, PauseToken pauseToken)
        {
            Title = "Imaging complete - Rebooting";
            if (!IsEnabled || context.DeveloperModeEnabled)
            {
                Title = "Imaging complete - Rebooting - DISABLED";
                await Task.Run(() => CountDown(pauseToken, 5000));
                return new StepResult(true, "Imaging complete - Rebooting machine");
            }

            Process formatProcess = new Process();
            formatProcess.StartInfo.FileName = "wpeutil";
            formatProcess.StartInfo.UseShellExecute = false;
            formatProcess.StartInfo.RedirectStandardOutput = true;
            formatProcess.StartInfo.CreateNoWindow = true;
            formatProcess.StartInfo.Arguments = "reboot";
            formatProcess.Start();
            formatProcess.WaitForExit();
            Environment.Exit(0);
            return new StepResult(true, "Imaging complete - Rebooting machine");
        }
    }
}
