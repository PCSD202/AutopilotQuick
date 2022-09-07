using System.IO.Abstractions;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace AQ.DeviceIdentifierMan;

public class DeviceIdentifierMan
{
    readonly IFileSystem fileSystem;
    private readonly ILogger<DeviceIdentifierMan> Logger;

    private readonly string _baseDir;

    private string FileDataPath => Path.Combine(_baseDir, "DeviceIdentifier.json");
    
    public DeviceIdentifierMan(IFileSystem fileSystem, ILogger<DeviceIdentifierMan> logger, string BaseDir)
    {
        this.fileSystem = fileSystem;
        this.Logger = logger;
        this._baseDir = BaseDir;
    }

    public DeviceIdentifierMan(string baseDir, ILogger<DeviceIdentifierMan> logger) : this(BaseDir:baseDir, fileSystem: new FileSystem(), logger: logger)
    {
    }
    
    private string? ReadIDFromDisk()
    {
        if (!File.Exists(FileDataPath))
        {
            return null;
        }

        try
        {
            IDOnDisk? data = JsonConvert.DeserializeObject<IDOnDisk>(File.ReadAllText(FileDataPath));
            return data?.ID;
        }
        catch (Exception e)
        {
            Logger.LogError(e, $"Got error {e.Message} while trying to deserialize {FileDataPath}. Deleting json file.");
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
}