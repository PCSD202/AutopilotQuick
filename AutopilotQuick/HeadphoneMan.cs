using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Documents;
using AutopilotQuick.Banshee;
using Humanizer;
using NAudio.CoreAudioApi;
using Octokit;

namespace AutopilotQuick;

public enum HeadphoneState
{
    Loading,
    NotFound,
    Disconnected,
    Connected
}
public class HeadphoneMan
{
    private static HeadphoneMan Instance = new HeadphoneMan();

    public static HeadphoneMan GetInstance()
    {
        return Instance;
    }

    private Timer _timer;
    private UserDataContext _context;
    
    public void StartTimer(UserDataContext context)
    {
        _context = context;
        _timer = new Timer(Run, null,0.Seconds(), 100.Milliseconds());
    }
    
    
    public void Run(object? o)
    {
        var headphones = SoundUtils.GetHeadphone();

        HeadphoneState newState = HeadphoneState.NotFound;
        if (headphones is not null)
        {
            newState = headphones.State == DeviceState.Active ? HeadphoneState.Connected : HeadphoneState.Disconnected;
        }

        if (_context.HeadphonesActive != newState)
        {
            _context.HeadphonesActive = newState;
            var ewm = BansheePlayer.GetInstance();
            if (newState == HeadphoneState.Connected)
            {
                
                if (ewm.IsPlaying())
                {
                    ewm.Stop();
                }

                ewm.Play(_context, true);
            }
            if (newState == HeadphoneState.Disconnected)
            {
                if (ewm.IsPlaying())
                {
                    ewm.Stop();
                }
            }
        }
        
    }
}