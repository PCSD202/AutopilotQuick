using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Humanizer;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Newtonsoft.Json;
using NLayer.NAudioSupport;
using Notification.Wpf;
using RandN;
using RandN.Distributions;

namespace AutopilotQuick.Banshee;

public class BansheePlayer
{
    private static BansheePlayer? Instance { get; set; }

    public static BansheePlayer GetInstance()
    {
        Instance ??= new BansheePlayer();
        return Instance;
    }
    
    private readonly ILogger Logger = App.GetLogger<BansheePlayer>();
    
    private float _volume = 0.5f;
    private const float VolumeChangeAmount = 0.05f;
    
    private MMDevice? PlayingDevice { get; set; }
    private IWavePlayer? _wavePlayer { get; set; }

    public bool IsPlaying()
    {
        return _wavePlayer?.PlaybackState is PlaybackState.Playing or PlaybackState.Paused;
    }
    private void ChangeVolume(float amount)
    {
        if(_wavePlayer is null) return;
        lock (_wavePlayer)
        {
            var newVolume = _wavePlayer.Volume + amount;
            if (newVolume is > 1 or < 0)
            {
                return;
            }
            _volume = newVolume;
            _wavePlayer.Volume = newVolume;
        }
    }
    public void IncVolume()
    {
        ChangeVolume(VolumeChangeAmount);
    }
    public void DecVolume()
    {
        ChangeVolume(-1*VolumeChangeAmount);
    }

    public void Stop()
    {
        if(_wavePlayer is null) return;
        lock (_wavePlayer)
        {
            _wavePlayer.Stop();
            _wavePlayer.Dispose();
            _wavePlayer = null;
        }
    }
    
    private StandardRng rng = StandardRng.Create();
    
    private Stream LoadAudioStream(string name = "Elevator Music.mp3")
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

    private WaveStream LoadTrack(UserDataContext context)
    {
        var cachedMusic = new Cacher("https://nettools.psd202.org/AutoPilotFast/Music/Music.mp3", "Music.mp3", context);
        var cachedMusicStartTimes = new Cacher("https://nettools.psd202.org/AutoPilotFast/Music/SongStartingPoints.json", "SongStartingPoints.json", context);
        if (InternetMan.GetInstance().IsConnected)
        {
            if (!cachedMusic.FileCached || !cachedMusic.IsUpToDate)
            {
                cachedMusic.DownloadUpdate();
            }

            if (!cachedMusicStartTimes.FileCached || !cachedMusicStartTimes.IsUpToDate)
            {
                cachedMusicStartTimes.DownloadUpdate();
            }
        }
        var file = "Elevator Music.mp3";
        if (cachedMusic.FileCached && cachedMusicStartTimes.FileCached)
        {
            file = cachedMusic.FilePath;
        }
        var audioStream = LoadAudioStream(file);
        var player = new ManagedMpegStream(audioStream);
        var loopPlayer = new LoopStream(player);

        if (cachedMusic.FileCached && cachedMusicStartTimes.FileCached)
        {
            var timeRand = Uniform.NewInclusive(0.Seconds(), player.TotalTime);
            var randSeconds  = timeRand.Sample(rng).TotalSeconds;
            try
            {
                //Load json file
                var songData = JsonConvert.DeserializeObject<Dictionary<string, TimeSpan>>(cachedMusicStartTimes.ReadAllText());
                        
                var dx = Uniform.NewInclusive(0, songData.Count-1);
                        
                var index = dx.Sample(rng);
                var kvp = songData.ElementAt(index);
                randSeconds = kvp.Value.TotalSeconds;
                context.NotifcationManager.Show("Now Playing", $"{kvp.Key}", NotificationType.Information, "", 5.Seconds());

            }
            catch (JsonException e)
            {
                //Delete file
                cachedMusicStartTimes.Delete();
            }

            player.CurrentTime = randSeconds.Seconds();
        }

        return loopPlayer;

    }

    public void Play(UserDataContext context, bool headphoneOnly = false)
    {
        PlayingDevice = null;
        if (SoundUtils.HeadphonesPresent())
        {
            PlayingDevice = SoundUtils.GetHeadphone();
        } 
        else if (SoundUtils.SpeakerPresent() && !headphoneOnly)
        {
            PlayingDevice = SoundUtils.GetSpeaker();
        }

        if (PlayingDevice is null)
        {
            Logger.LogInformation("Could not find any device to play audio on");
        }
        
        Logger.LogInformation("Using {deviceName} to play audio...", PlayingDevice?.FriendlyName);
        
        if(IsPlaying()) {Stop();}
        var audioTrack = LoadTrack(context);
        _wavePlayer = new WasapiOut(AudioClientShareMode.Shared, true, 100){Volume = _volume};
        lock (_wavePlayer)
        {
            _wavePlayer.Init(audioTrack);
            _wavePlayer.Play();
            context.Playing = true;
            _wavePlayer.PlaybackStopped += (sender, args) =>
            {
                Logger.LogInformation("Playback finished");
                context.Playing = false;
            };
        }
        Logger.LogInformation("Started playback of audio");
    }
}