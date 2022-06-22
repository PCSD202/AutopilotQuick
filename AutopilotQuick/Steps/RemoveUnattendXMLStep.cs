using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Nito.AsyncEx;
using NLog;

namespace AutopilotQuick.Steps
{
    internal class RemoveUnattendXMLStep : StepBaseEx
    {
        public readonly Logger Logger = LogManager.GetCurrentClassLogger();
        public override async Task<StepResult> Run(UserDataContext context, PauseToken pauseToken)
        {
            Title = "Removing Unattend XML file";
            Progress = 0;
            if (!IsEnabled)
            {
                Title = "Removing Unattend XML file - DISABLED";
                Message = "Will continue after 5 seconds";
                await Task.Run(() => CountDown(pauseToken, 5000));
                return new StepResult(true, "Removed Unattend.xml successfully");
            }
            try
            {
                File.Delete(@"W:\Windows\Panther\unattend\unattend.xml");
            }
            catch (Exception ex)
            {
                //Removal failed but it doesn't matter
            }
            Progress = 100;
            return new StepResult(true, "Removed Unattend.xml successfully");
        }
    }
}
