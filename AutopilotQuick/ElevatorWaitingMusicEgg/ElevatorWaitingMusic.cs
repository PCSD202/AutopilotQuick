using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NLayer.NAudioSupport;


namespace AutopilotQuick.ElevatorWaitingMusicEgg;

public class ElevatorWaitingMusic
{
    private readonly ILogger Logger = App.GetLogger<ElevatorWaitingMusic>();

    private WaveOutEvent? output = null;
    private float volume = 0.5f;
    private static ElevatorWaitingMusic? Instance = null;
    public static ElevatorWaitingMusic? GetInstance()
    {
        Instance ??= new ElevatorWaitingMusic();
        return Instance;
    }

    public string LoadAudioFile()
    {
        var TempDir = Path.Combine(App.GetExecutableFolder(), "Cache");
        var outputFile = Path.Combine(TempDir, "ElevatorMusic.mp3");
        
        if (!Directory.Exists(Path.GetDirectoryName(TempDir)))
        {
            Directory.CreateDirectory(TempDir);
        }
        var resource = LoadAudioStream();
        using var file = new FileStream(outputFile, FileMode.Create, FileAccess.Write);
        Logger.LogInformation("Stream copying");
        resource.CopyTo(file);
        Logger.LogInformation("Stream done copying");

        return outputFile;
    }

    public Stream? audioStream = null;
    public Stream LoadAudioStream()
    {
        if (audioStream is not null && audioStream.CanRead)
        {
            return audioStream;
        }
        
        string resourceName = Assembly.GetExecutingAssembly().GetManifestResourceNames().Single(str => str.EndsWith("Elevator Music.mp3"));
        
        var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        if (resource is null)
        {
            Logger.LogError("Elevator Music.mp3 not found in application");
            throw new FileNotFoundException();
        }
        else
        {
            Logger.LogInformation("Stream loaded. Length: {length}", resource.Length);
        }
        audioStream = resource;
        return resource;
    }
    
    public void Stop()
    {
        output?.Stop();
    }

    private float ChangeAmount = 0.1f;
    
    public void IncVolume()
    {
        if(output is null) {return;}

        var newVolume = output.Volume + ChangeAmount;
        if (newVolume > 1)
        {
            return;
        }

        volume = newVolume;
        output.Volume = newVolume;
    }

    public void DecVolume()
    {
        if(output is null) {return;}

        var newVolume = output.Volume - ChangeAmount;
        if (newVolume < 0)
        {
            return;
        }

        volume = newVolume;
        output.Volume = newVolume;

    }

    public bool IsPlaying()
    {
        if (output is null)
        {
            return false;
        }

        return output.PlaybackState == PlaybackState.Playing;
    }
    
    public async Task Play()
    {
        var selectedDevice = -2;
        for (int n = 0; n < WaveOut.DeviceCount; n++)
        {
            var caps = WaveOut.GetCapabilities(n);
            Console.WriteLine($"WAVE: {n}: {caps.ProductName}");
            if (!caps.ProductName.ToLower().Contains("speaker")) continue;
            selectedDevice = n;
            break;
        }
        
        
        if(selectedDevice == -2) return;
        var audioStream = LoadAudioStream();
        try
        {
            output = new WaveOutEvent() { DeviceNumber = selectedDevice, Volume = volume };
            await using var player = new ManagedMpegStream(audioStream);
            await using var loopPlayer = new LoopStream(player);
            output.Init(loopPlayer);
            output.Play();
            while (output.PlaybackState == PlaybackState.Playing)
            {
                await Task.Delay(500);
            }
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Got error while playing music {e}", e);
        }
        Logger.LogInformation("Playback finished");

    }
}