#region

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;

#endregion

namespace AutopilotQuick.Steps
{
    internal class RemoveUnattendXMLStep : StepBaseEx
    {
        public override string Name() => "Remove UnattendXML step";
        public readonly ILogger Logger = App.GetLogger<RemoveUnattendXMLStep>();
        public override async Task<StepResult> Run(UserDataContext context, PauseToken pauseToken,
            IOperationHolder<RequestTelemetry> StepOperation)
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
