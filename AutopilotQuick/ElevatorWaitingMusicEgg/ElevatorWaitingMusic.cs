using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using Humanizer;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Newtonsoft.Json;
using Nito.AsyncEx;
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
    
    public Stream LoadAudioStream(string name = "Elevator Music.mp3")
    {
        string resourceName = Assembly.GetExecutingAssembly().GetManifestResourceNames().FirstOrDefault(str => str.EndsWith(name), "");
        Stream? resource = null;
        try
        {
            resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        }
        catch (Exception)
        {
            if (File.Exists(name))
            {
                Logger.LogInformation("Loading file");
                resource = File.OpenRead(name);
            }
        }

        Logger.LogInformation("Stream loaded. Length: {length}", resource.Length);
        
        
        return resource;
    }
    
    public void Stop()
    {
        output?.Stop();
    }

    private float ChangeAmount = 0.05f;
    
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

    public bool Portal = false;
    public void SwitchSongs()
    {
        Portal = !Portal;
        Stop();
    }
    public static Random rnd = new Random();
    
    public static double Map (double value, double fromSource, double toSource, double fromTarget, double toTarget)
    {
        return (value - fromSource) / (toSource - fromSource) * (toTarget - fromTarget) + fromTarget;
    }
    
    public async Task Play(UserDataContext context)
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

        try
        {
            for (int n = 0; n < WaveIn.DeviceCount; n++)
            {
                var caps = WaveIn.GetCapabilities(n);
                Console.WriteLine($"{n}: {caps.ProductName}");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine("Got error while looking for DSO devices");
        }
        

        try
        {
            Console.WriteLine("Direct sound out:");
            foreach (var dev in DirectSoundOut.Devices)
            {
                Console.WriteLine($"\t{dev.Guid} {dev.ModuleName} {dev.Description}");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine("Got error while looking for DSO devices");
        }
        

        try
        {
            var enumerator = new MMDeviceEnumerator();
            Console.WriteLine("WASAPI:");
            foreach (var wasapi in enumerator.EnumerateAudioEndPoints(DataFlow.All, DeviceState.All))
            {
                Console.WriteLine(
                    $"\t{wasapi.DataFlow} {wasapi.FriendlyName} {wasapi.DeviceFriendlyName} {wasapi.State}");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine("Got error while looking for WASAPI devices");
        }
        
        var times = new Dictionary<string, TimeSpan>();
        var cachedMusic = new Cacher("https://nettools.psd202.org/AutoPilotFast/Music/Music.mp3", "Music.mp3", context);
        var cachedMusicStartTimes = new Cacher("https://nettools.psd202.org/AutoPilotFast/Music/SongStartingPoints.json", "SongStartingPoints.json", context);
        if (InternetMan.GetInstance().IsConnected)
        {
            if (!cachedMusic.FileCached || !cachedMusic.IsUpToDate)
            {
                await cachedMusic.DownloadUpdateAsync();
            }

            if (!cachedMusicStartTimes.FileCached || !cachedMusicStartTimes.IsUpToDate)
            {
                await cachedMusicStartTimes.DownloadUpdateAsync();
            }
        }


        
        if(selectedDevice == -2) return;

        var file = "Elevator Music.mp3";
        if (Portal)
        {
            file = "Portal.mp3";
        }

        if (cachedMusic.FileCached && !Portal)
        {
            file = cachedMusic.FilePath;
        }
        try
        {
            await using var audioStream = LoadAudioStream(file);
            output = new WaveOutEvent() { DeviceNumber = selectedDevice, Volume = volume };
            await using var player = new ManagedMpegStream(audioStream);
            await using var loopPlayer = new LoopStream(player);
            if (!Portal && cachedMusic.FileCached)
            {

                var randPos = rnd.NextDouble();
                var randSeconds = Map(randPos, 0, 1, 0, player.TotalTime.TotalSeconds);
                if (cachedMusicStartTimes.FileCached)
                {
                    try
                    {
                        //Load json file
                        var songData =
                            JsonConvert.DeserializeObject<Dictionary<string, TimeSpan>>(
                                await cachedMusicStartTimes.ReadAllTextAsync());
                        var index = rnd.Next(songData.Values.ToList().Count);
                        randSeconds = songData.Values.ToList()[index].TotalSeconds;
                    }
                    catch (JsonException e)
                    {
                        //Delete file
                        cachedMusicStartTimes.Delete();
                    }
                     
                }
                
                player.CurrentTime = randSeconds.Seconds();
            }
            output.Init(loopPlayer);
            output.Play();
            var mre = new AsyncManualResetEvent();
            output.PlaybackStopped += (sender, args) => mre.Set();
            await mre.WaitAsync();
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Got error while playing music {e}", e);
        }
        Logger.LogInformation("Playback finished");
    }
}