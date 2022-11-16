using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteDB;

namespace AutopilotQuick.Banshee;

public class WeightedItem<T> 
{
    public T Item { get; set; }
    public double Weight { get; set; } = 1;
}

class WeightedRandom<T>
{
    private static Random random = new Random();
    
    private List<WeightedItem<T>> list { get; set; } = new List<WeightedItem<T>>();
    public WeightedItem<T> this[T item] => list.FirstOrDefault(x => x.Item.Equals(item)) ?? ((Func<WeightedItem<T>>)(() => { list.Add(new WeightedItem<T>{ Item = item }); return this[item]; }))();
    public double TotalWeight => list.Sum(x => x.Weight);
    public WeightedRandom(params T[] items) => list.AddRange(items.Select(x => new WeightedItem<T> { Item = x }));
    public WeightedRandom(IEnumerable<T> items):this(items.ToArray()) {}
    
    public T Next()
    {
        var limit = list.Sum(x => x.Weight);
        var value = random.NextDouble() * limit;
        var e = list.GetEnumerator();
        while(e.MoveNext())
            if((e.Current.Weight) > value)
                return e.Current.Item;
            else
                value -= e.Current.Weight;
        
        return e.Current.Item;
    }
}

public class Song
{
    public int Id { get; set; }
    public string Name { get; set; }
    public DateTime LastPlayed { get; set; }
}

public record PickedSong(string Name, TimeSpan startPoint);
public class SongPicker
{
    private string DBPath = Path.Join(Cacher.BaseDir, "SongPicker.db");


    private List<Song> GetSongPlayDataFromDB()
    {
        using (var db = new LiteDatabase(DBPath))
        {
            // Get a collection (or create, if doesn't exist)
            var col = db.GetCollection<Song>("SongData");
            return col.FindAll().ToList();
        }
    }
    
    private void PopulateDB(List<string> Songs)
    {
        var songsInDB = GetSongPlayDataFromDB().Select(x => x.Name).ToHashSet();
        var songsNotInDB = Songs.Where(x => !songsInDB.Contains(x));
        using var db = new LiteDatabase(DBPath);
        // Get a collection (or create, if doesn't exist)
        var col = db.GetCollection<Song>("SongData");
        foreach (var songNotInDB in songsNotInDB)
        {
            var newSongToAdd = new Song
            {
                Name = songNotInDB,
                LastPlayed = DateTime.MinValue
            };
            col.Insert(newSongToAdd);
        }
    }
    
    private void UpdateSong(Song song)
    {
        using var db = new LiteDatabase(DBPath);
        // Get a collection (or create, if doesn't exist)
        var col = db.GetCollection<Song>("SongData");
        col.Update(song.Id, song);
    }
    
    public PickedSong PickSong(Dictionary<string, TimeSpan> SongData)
    {
        PopulateDB(SongData.Keys.ToList());
        var dbData = GetSongPlayDataFromDB();
        var rand = new WeightedRandom<string>(SongData.Keys.ToList());

        var now = DateTime.UtcNow;
        foreach (var song in dbData)
        {
            //Calculate how long ago that song was played
            var longAgo = now - song.LastPlayed;
            rand[song.Name].Weight = Math.Abs(longAgo.TotalMinutes);
        }
        
        var picked = rand.Next();
        var pickedSong = dbData.First(x => x.Name == picked);
        pickedSong.LastPlayed = DateTime.UtcNow;
        UpdateSong(pickedSong);
        return new PickedSong(picked, SongData[picked]);
    }
}