using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NLog;
using shortid;
using shortid.Configuration;

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
               Logger.Error($"Got error {e.Message} while trying to deserialize {FileDataPath}. Deleting json file.");
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
        }

        private string GenerateNewID()
        {
            var options = new GenerationOptions(useSpecialCharacters: false);
            string id = ShortId.Generate(options);
            return id;
        }

        public string GetDeviceIdentifier()
        {
            if (ReadIDFromDisk() != null) return ReadIDFromDisk();
            SetID(GenerateNewID());
            return GetDeviceIdentifier();


        }
    }
}
