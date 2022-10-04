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

    private List<WasapiOut> outputs = new List<WasapiOut>();
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
        lock (outputs)
        {
            foreach (var output in outputs)
            {
                output?.Stop();
                output?.Dispose();
            }

            outputs.Clear();
        }
    }
    

    private float ChangeAmount = 0.05f;
    
    public void IncVolume()
    {
        if(outputs.Count == 0) {return;}

        var newVolume = outputs[0].Volume + ChangeAmount;
        if (newVolume > 1)
        {
            return;
        }

        volume = newVolume;
        foreach (var output in outputs)
        {
            output.Volume = newVolume;
        }
        
    }

    public void DecVolume()
    {
        if(outputs.Count == 0) {return;}

        var newVolume = outputs[0].Volume - ChangeAmount;
        if (newVolume < 0)
        {
            return;
        }

        volume = newVolume;
        foreach (var output in outputs)
        {
            output.Volume = newVolume;
        }

    }

    public bool IsPlaying()
    {
        lock (outputs)
        {
            if (outputs.Count == 0)
            {
                return false;
            }

            return outputs.Any(x => x.PlaybackState == PlaybackState.Playing);
        }
        
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

    public async Task Play(UserDataContext context, bool HeadphoneOnly = false)
    {
        var speakers = new List<MMDevice>();
        var headphones = new List<MMDevice>();
        var SelectedDevices = new List<MMDevice>();
        try
        {
            var enumerator = new MMDeviceEnumerator();
            var activeDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();
            foreach (var wasapi in activeDevices)
            {
                try
                {
                    Console.WriteLine(
                        $"\t{wasapi.DataFlow} {wasapi.FriendlyName} {wasapi.DeviceFriendlyName} {wasapi.State}");
                    if (wasapi.FriendlyName.ToLower().Contains("headphone"))
                    {
                        headphones.Add(wasapi);
                    }
                    else if (wasapi.FriendlyName.ToLower().Contains("speaker"))
                    {
                        speakers.Add(wasapi);
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

        SelectedDevices.AddRange(headphones.Count == 0 && !HeadphoneOnly ? speakers : headphones);

        var cachedMusic = new Cacher("https://nettools.psd202.org/AutoPilotFast/Music/Music.mp3", "Music.mp3", context);
        var cachedMusicStartTimes =
            new Cacher("https://nettools.psd202.org/AutoPilotFast/Music/SongStartingPoints.json",
                "SongStartingPoints.json", context);
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



        if (SelectedDevices.Count == 0) return;

        var file = "Elevator Music.mp3";
        if (Portal)
        {
            file = "Portal.mp3";
        }

        if (cachedMusic.FileCached && !Portal)
        {
            file = cachedMusic.FilePath;
        }

        var WavePlayers = new List<WaveStream>();
        var LoopPlayers = new List<WaveStream>();
        try
        {

            await using var audioStream = LoadAudioStream(file);
            foreach (var device in SelectedDevices)
            {
                outputs.Add(new WasapiOut(device, AudioClientShareMode.Shared, true, 200) { Volume = volume });
            }

            foreach (var output in outputs)
            {
                var player = new ManagedMpegStream(audioStream);
                var loopPlayer = new LoopStream(player);
                LoopPlayers.Add(loopPlayer);
                WavePlayers.Add(player);
                output.Init(loopPlayer);
            }

            if (!Portal && cachedMusic.FileCached)
            {

                var randPos = rnd.NextDouble();
                var randSeconds = Map(randPos, 0, 1, 0, WavePlayers[0].TotalTime.TotalSeconds);
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

                foreach (var wavePlayer in WavePlayers)
                {
                    wavePlayer.CurrentTime = randSeconds.Seconds();
                }
            }

            foreach (var output in outputs)
            {
                output.Play();
            }
            
            context.Playing = true;
            while (IsPlaying())
            {
                await Task.Delay(50);
            }
            context.Playing = false;
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Got error while playing music {e}", e);
        }
        finally
        {
            foreach (var loopPlayer in LoopPlayers)
            {
                await loopPlayer.DisposeAsync();
            }

            foreach (var wavePlayer in WavePlayers)
            {
                await wavePlayer.DisposeAsync();
            }

            context.Playing = false;
        }

        Logger.LogInformation("Playback finished");
    }
}