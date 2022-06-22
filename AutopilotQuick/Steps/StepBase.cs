using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Nito.AsyncEx;
using NLog;

namespace AutopilotQuick.Steps
{
    internal abstract class StepBase
    {
        public event EventHandler<StepStatus> StepUpdated;

        
        public bool IsEnabled => TaskManager.getInstance().Enabled;

        private StepStatus _status { get; set; } = new StepStatus(0, true, "Please wait...", "");

        public StepStatus Status
        {
            get => _status;
            set
            {
                _status = value;
                StepUpdated?.Invoke(this, _status);
            }
        }

        public bool IsIndeterminate
        {
            get => Status.IsIndeterminate;
            set => Status = Status with { IsIndeterminate = value };
        }
        public double Progress
        {
            get => Status.Progress;
            set => Status = Status with { Progress = value };
        }
        public string Message
        {
            get => Status.Message;
            set => Status = Status with { Message = value };
        }
        public string Title
        {
            get => Status.Title;
            set => Status = Status with { Title = value };
        }

        public readonly record struct StepStatus(double Progress, bool IsIndeterminate, string Message, string Title);

        public readonly record struct StepResult(bool Success, string Message);

        public abstract Task<StepResult> Run(UserDataContext context, PauseToken pauseToken);
    }
}
