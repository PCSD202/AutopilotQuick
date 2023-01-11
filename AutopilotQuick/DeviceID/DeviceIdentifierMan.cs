#region

using System;
using System.IO;
using LazyCache;
using Newtonsoft.Json;
using NLog;
using shortid;
using shortid.Configuration;

#endregion

namespace AutopilotQuick.DeviceID
{
    public class DeviceIdentifierMan
    {
        private static readonly DeviceIdentifierMan instance = new();
        public static DeviceIdentifierMan getInstance()
        {
            return instance;
        }
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public static string BaseDir => Path.Combine(Path.GetDirectoryName(App.GetExecutablePath()));

        private string FileDataPath => Path.Combine(BaseDir, "DeviceIdentifier.json");

        private string? ReadIDFromDisk()
        {
            if (!File.Exists(FileDataPath))
            {
                return null;
            }

            try
            {
                IDOnDisk data = JsonConvert.DeserializeObject<IDOnDisk>(File.ReadAllText(FileDataPath));
                return data.ID;
            }
            catch (Exception e)
            {
                Logger.Error(
                    $"Got error {e.Message} while trying to deserialize {FileDataPath}. Deleting json file.");
                File.Delete(FileDataPath);
                return null;
            }
        }

        private void SetID(string ID)
        {
            IDOnDisk data = new IDOnDisk()
            {
                ID = ID,
            };
            File.WriteAllText(FileDataPath, JsonConvert.SerializeObject(data, Formatting.Indented));
            cache.Add("DeviceID", ID);
        }

        private string GenerateNewID()
        {
            var options = new GenerationOptions(useSpecialCharacters: false);
            string id = ShortId.Generate(options);
            return id;
        }

        IAppCache cache = new CachingService();
        public string GetDeviceIdentifier()
        {
            if (cache.TryGetValue("DeviceID", out string deviceID))
            {
                return deviceID;
            }

            if (ReadIDFromDisk() is null)
            {
                SetID(GenerateNewID());
            }
            else
            {
                cache.Add("DeviceID", ReadIDFromDisk());
            }

            return ReadIDFromDisk();


        }
    }
}
