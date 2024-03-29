﻿#region

using System;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Nito.AsyncEx;

#endregion

namespace AutopilotQuick.Steps
{
    public abstract class StepBase
    {
        public event EventHandler<StepStatus> StepUpdated;

        public abstract string Name();

        public abstract double ProgressWeight();

        public bool IsEnabled => TaskManager.GetInstance().Enabled;

        private StepStatus _status { get; set; } = new StepStatus(0, true, "Please wait...", "");

        public StepStatus Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    StepUpdated?.Invoke(this, _status);
                }
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
            set
            {
                if(Math.Abs(value - Progress) < 0.1) return;
                Status = Status with { Progress = value };
            }
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

        public virtual bool IsCritical() //This defines if task manager should continue or not if this task fails. 
        {
            return true; 
        } 

        public readonly record struct StepStatus(double Progress, bool IsIndeterminate, string Message, string Title);

        public readonly record struct StepResult(bool Success, string Message);

        public abstract Task<StepResult> Run(UserDataContext context, PauseToken pauseToken,
            IOperationHolder<RequestTelemetry> StepOperation);
    }
}
