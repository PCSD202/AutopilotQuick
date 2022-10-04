using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Documents;
using Humanizer;
using NAudio.CoreAudioApi;
using Octokit;

namespace AutopilotQuick;

public enum HeadphoneState
{
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
        _timer = new Timer(Run, null,0.Seconds(), 1.Seconds());
    }
    
    
    public void Run(object? o)
    {
        var headphones = new List<MMDevice>();
        try
        {
            var enumerator = new MMDeviceEnumerator();
            var activeDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.All).ToList();
            foreach (var wasapi in activeDevices)
            {
                try
                {
                    if (wasapi.FriendlyName.ToLower().Contains("headphone") && wasapi.State is DeviceState.Active or DeviceState.Unplugged)
                    {
                        headphones.Add(wasapi);
                    }
                }
                catch (Exception)
                {
                    // ignored
                }
            }
        }
        catch (Exception e)
        {
            Console.WriteLine("Got error while looking for WASAPI devices");
        }

        HeadphoneState newState = HeadphoneState.NotFound;
        if (headphones.Count > 0)
        {
            newState = headphones.Any(x => x.State == DeviceState.Active) ? HeadphoneState.Connected : HeadphoneState.Disconnected;
        }

        if (_context.HeadphonesActive != newState)
        {
            if (newState == HeadphoneState.Connected)
            {
                var ewm = ElevatorWaitingMusicEgg.ElevatorWaitingMusic.GetInstance();
                if (ewm.IsPlaying())
                {
                    ewm.Stop();
                }

                Task.Run(async ()=> await ewm.Play(_context, true));
            }

            if (newState == HeadphoneState.Disconnected)
            {
                var ewm = ElevatorWaitingMusicEgg.ElevatorWaitingMusic.GetInstance();
                if (ewm.IsPlaying())
                {
                    ewm.Stop();
                }
            }
        }
        _context.HeadphonesActive = newState;
    }
}