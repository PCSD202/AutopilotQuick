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
        _timer = new Timer(Run, null,0.Seconds(), 1.Seconds()); //Give some time for the app to startup before we start checking for internet
    }
    
    public void Run(object? o)
    {
        var headphones = new List<MMDevice>();
        try
        {
            var enumerator = new MMDeviceEnumerator();
            var activeDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();
            foreach (var wasapi in activeDevices)
            {
                try
                {
                    if (wasapi.FriendlyName.ToLower().Contains("headphone"))
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

        var newState = headphones.Count != 0;
        
        if (_context.HeadphonesActive != newState)
        {
            if (newState)
            {
                var ewm = ElevatorWaitingMusicEgg.ElevatorWaitingMusic.GetInstance();
                if (ewm.IsPlaying())
                {
                    ewm.Stop();
                }

                Task.Run(async ()=> await ewm.Play(_context, true));
            }
        }
        _context.HeadphonesActive = newState;
        

    }
}