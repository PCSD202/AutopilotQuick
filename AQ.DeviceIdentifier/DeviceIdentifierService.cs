using System.IO.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using shortid;
using shortid.Configuration;

namespace AQ.DeviceIdentifier;

public record struct IDOnDisk(string ID);
public class DeviceIdentifierService : IDeviceIdentifierService
{
    private readonly IFileSystem fileSystem;
    private readonly string _storagePath;

    public DeviceIdentifierService(
        IFileSystem fileSystem,
        IOptions<DeviceIdentifierServiceOptions> options)
    {
        this.fileSystem = fileSystem;
        _storagePath = options.Value.StoragePath;
    }

    private string? ReadFromDisk()
    {
        if (!fileSystem.File.Exists(_storagePath))
        {
            return null;
        }

        try
        {
            var data = JsonConvert.DeserializeObject<IDOnDisk>(fileSystem.File.ReadAllText(_storagePath));
            return data.ID;
        }
        catch (Exception e)
        {
            fileSystem.File.Delete(_storagePath);
            return null;
        }
    }
    
    private string GenerateNewID()
    {
        var options = new GenerationOptions(useSpecialCharacters: false);
        string id = ShortId.Generate(options);
        return id;
    }
    

    public void Set(string newId)
    {
        var d = new IDOnDisk(newId);
        fileSystem.File.WriteAllText(_storagePath, JsonConvert.SerializeObject(d, Formatting.Indented));
    }

    public string Get()
    {
        var readFromDisk = ReadFromDisk();
        if (readFromDisk is not null) return readFromDisk;
        
        var newId = GenerateNewID();
        Set(newId);
        return newId;

    }
}