using System;
using System.Collections.Generic;
using System.Linq;
using NAudio.CoreAudioApi;

namespace AutopilotQuick.Banshee;

public static class SoundUtils
{
    public static readonly MMDeviceEnumerator DeviceEnumerator = new MMDeviceEnumerator();
    public static List<MMDevice> GetDevices(DataFlow flow = DataFlow.All, DeviceState state = DeviceState.Active)
    {
        return DeviceEnumerator.EnumerateAudioEndPoints(flow, state).ToList();
    }
    
    public static bool HeadphonesPresent()
    {
        var headphone = GetHeadphone();
        if (headphone is null) return false;
        return headphone.State == DeviceState.Active;
    }

    public static bool SpeakerPresent()
    {
        var speaker = GetSpeaker();
        if (speaker is null) return false;
        return speaker.State == DeviceState.Active;
    }
    
    public static MMDevice? GetHeadphone()
    {
        try
        {
            var devices = GetDevices(DataFlow.Render, DeviceState.All);
            return devices.FirstOrDefault(x => x.FriendlyName.ToLower().Contains("headphone"));
        }
        catch (Exception)
        {
            return null;
        }
    }
    
    public static MMDevice? GetSpeaker()
    {
        try
        {
            var devices = GetDevices(DataFlow.Render, DeviceState.All);
            Console.WriteLine(string.Join(", ", devices.Select(x=>x.FriendlyName)));
            return devices.FirstOrDefault(x => x.FriendlyName.ToLower().Contains("speaker"));
        }
        catch (Exception)
        {
            return null;
        }
        
    }
}