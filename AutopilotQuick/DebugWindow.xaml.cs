#region

using System.ComponentModel;
using System.Runtime.CompilerServices;
using AQ.DeviceInfo;
using AutopilotQuick.Annotations;
using AutopilotQuick.DeviceID;

#endregion

namespace AutopilotQuick;

public partial class DebugWindow : INotifyPropertyChanged
{
    private string _deviceId;
    private string _sessionId;
    private string _version;
    private string _serviceTag;
    private string _model;
    private string _biosVersion;

    public string DeviceID
    {
        get => _deviceId;
        set
        {
            if (value == _deviceId) return;
            _deviceId = value;
            OnPropertyChanged();
        }
    }

    public string SessionID
    {
        get => _sessionId;
        set
        {
            if (value == _sessionId) return;
            _sessionId = value;
            OnPropertyChanged();
        }
    }

    public string Version
    {
        get => _version;
        set
        {
            if (value == _version) return;
            _version = value;
            OnPropertyChanged();
        }
    }

    public string ServiceTag
    {
        get => _serviceTag;
        set
        {
            if (value == _serviceTag) return;
            _serviceTag = value;
            OnPropertyChanged();
        }
    }

    public string Model
    {
        get => _model;
        set
        {
            if (value == _model) return;
            _model = value;
            OnPropertyChanged();
        }
    }

    public string BiosVersion
    {
        get => _biosVersion;
        set
        {
            if (value == _biosVersion) return;
            _biosVersion = value;
            OnPropertyChanged();
        }
    }

    public DebugWindow()
    {
        InitializeComponent();
        DataContext = this;
        this.DeviceID = DeviceIdentifierMan.getInstance().GetDeviceIdentifier();
        this.SessionID = App.SessionID;
        this.Version = $"{App.GetVersion().FileMajorPart}.{App.GetVersion().FileMinorPart}.{App.GetVersion().FileBuildPart}";
        this.ServiceTag = DeviceInfo.ServiceTag;
        this.Model = DeviceInfo.DeviceModel;
        this.BiosVersion = DeviceInfo.BiosVersion;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    [NotifyPropertyChangedInvocator]
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}