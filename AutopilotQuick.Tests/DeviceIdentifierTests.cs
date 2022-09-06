using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using AQ.DeviceIdentifier;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AutopilotQuick.Tests;

public class DeviceIdentifierTests
{
    [SetUp]
    public void Setup()
    {
        
    }

    [Test]
    public void GetDeviceIDNonExistentFile()
    {
        var options = Options.Create<DeviceIdentifierServiceOptions>(new DeviceIdentifierServiceOptions()
        {
            StoragePath = @"C:\DeviceIdentifier.json"
        });
        var fileSystem = new MockFileSystem();
        var logger = new NullLogger<DeviceIdentifierService>();
        var service = new DeviceIdentifierService(fileSystem, options);
        
        var ID = service.Get();
        //We should have an ID now
        Assert.That(ID, Is.Not.Empty);
        
        //We should have created that file in the options
        Assert.That(fileSystem.FileExists(options.Value.StoragePath), Is.True, "File needs to be created when it does not exist");
    }
    
    [Test]
    public void DeviceIDSameOverMultipleCalls()
    {
        var options = Options.Create<DeviceIdentifierServiceOptions>(new DeviceIdentifierServiceOptions()
        {
            StoragePath = @"C:\DeviceIdentifier.json"
        });
        var fileSystem = new MockFileSystem();
        var logger = new NullLogger<DeviceIdentifierService>();
        var service = new DeviceIdentifierService(fileSystem, options);
        
        var ID = service.Get();
        var SecondID = service.Get();
        //We should have an ID now
        Assert.That(ID, Is.Not.Empty, "Device ID is empty");
        Assert.That(ID, Is.EqualTo(SecondID), "ID needs to stay the same across multiple calls");
    }
    
    [Test]
    public void DeviceIDChangesAfterSet()
    {
        var options = Options.Create<DeviceIdentifierServiceOptions>(new DeviceIdentifierServiceOptions()
        {
            StoragePath = @"C:\DeviceIdentifier.json"
        });
        var fileSystem = new MockFileSystem();
        var logger = new NullLogger<DeviceIdentifierService>();
        var service = new DeviceIdentifierService(fileSystem, options);
        
        var ID = service.Get();
        //We should have an ID now
        Assert.That(ID, Is.Not.Empty, "Device ID is empty");
        
        service.Set("TEST");
        var newID = service.Get();
        Assert.That(newID, Is.Not.Empty, "Device ID is empty after set");
        
        Assert.That(newID, Is.EqualTo("TEST"), "ID needs to change when calling Set()");
    }
    
    [Test]
    public void ReplaceFileWhenInvalidData()
    {
        var options = Options.Create<DeviceIdentifierServiceOptions>(new DeviceIdentifierServiceOptions()
        {
            StoragePath = @"C:\DeviceIdentifier.json"
        });
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            {@"C:\DeviceIdentifier.json", new MockFileData("INVALID DATA")}
        });
        var logger = new NullLogger<DeviceIdentifierService>();
        var service = new DeviceIdentifierService(fileSystem, options);
        
        var ID = service.Get();
        Assert.Multiple(() =>
        {

            //We should have an ID now
            Assert.That(ID, Is.Not.Empty, "Device ID is empty");

            Assert.That(fileSystem.File.ReadAllText(options.Value.StoragePath), Is.Not.EqualTo("INVALID DATA"), "Replace file when invalid data is found");
        });
    }
}