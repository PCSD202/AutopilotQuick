﻿using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using AutopilotQuick.Annotations;

namespace AutopilotQuick;

public partial class DebugWindow : INotifyPropertyChanged
{
    private string _deviceId;
    private string _sessionId;
    private string _version;

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

    public DebugWindow(string DeviceID, string SessionID, string Version)
    {
        InitializeComponent();
        DataContext = this;
        this.DeviceID = DeviceID;
        this.SessionID = SessionID;
        this.Version = Version;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    [NotifyPropertyChangedInvocator]
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}