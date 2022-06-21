using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AutopilotQuick.ImageSteps
{
    public enum StepStatus
    {
        Successful,
        Failed
    }

    public readonly record struct StepResult(StepStatus Status, string Message);

    public abstract class ImageStep
    {
        public event EventHandler<StepNameChangedEventArgs> StepNameChanged;
        public event EventHandler<StepDescriptionChangedEventArgs> StepDescriptionChanged;
        public event EventHandler<StepProgressChangedEventArgs> StepProgressChanged;

        public abstract StepResult Run();

        private string _name { get; set; }
        private string _description { get; set; }
        private double _progress { get; set; }
        private bool _isIndeterminate { get; set; }

        public string Name {
            get
            {
                return _name;
            }
            set
            {
                _name = value;
                StepNameChanged?.Invoke(this, new StepNameChangedEventArgs() { Name = value });
            }
        }
        public string Description {
            get
            {
                return _description;
            }
            set
            {
                _description = value;
                StepDescriptionChanged?.Invoke(this, new StepDescriptionChangedEventArgs() { Description = value });
            }
        }
        public double Progress { 
            get
            {
                return _progress;
            }
            set
            {
                _progress = value;
                StepProgressChanged?.Invoke(this, new StepProgressChangedEventArgs() { Progress = value, IsIndeterminate = _isIndeterminate});
            }
        }
        public bool isIndeterminate
        {
            get
            { return _isIndeterminate; }
            set
            {
                _isIndeterminate = value;
                StepProgressChanged?.Invoke(this, new StepProgressChangedEventArgs { IsIndeterminate = value, Progress = _progress });
            }
        }
    }

    public class StepNameChangedEventArgs : EventArgs
    {
        public string Name;
    }

    public class StepDescriptionChangedEventArgs : EventArgs
    {
        public string Description;
    }

    public class StepProgressChangedEventArgs: EventArgs
    {
        public bool IsIndeterminate;
        public double Progress;
    }

    public class StepChangedEventArgs : EventArgs
    {
        public string Name;
        public string Description;

        public double Progress;
        public bool isIndeterminate;
    }
}
