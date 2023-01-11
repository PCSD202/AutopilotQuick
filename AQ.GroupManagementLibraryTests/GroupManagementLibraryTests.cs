#region

using AQ.GroupManagementLibrary;

#endregion

namespace AQ.GroupManagementLibraryTests;

[NonParallelizable]
public class Tests
{
    private const string STToTestWith = "5LM5GW2";
    private GroupManagementClient Client;
    
    [SetUp]
    public void Setup()
    {
        Client = new GroupManagementClient(TestLogger.Create<GroupManagementClient>(), "");
    }

    [Test]
    public async Task GetMemberOfGroupValid()
    {
        var result = await Client.IsSharedPCMember(STToTestWith);
        Assert.That(result.HasValue, Is.True, $"Response should return with valid ST. Please make sure {STToTestWith} is valid.");
    }
    
    [Test]
    public async Task AutopilotProfileExistsValid()
    {
        var result = await Client.CheckAutopilotProfileExists(STToTestWith);
        Assert.That(result.HasValue);
        Assert.That(result.Value.Exists, Is.True, $"Response should return with valid ST. Please make sure {STToTestWith} is valid.");
    }
    
    [Test]
    public async Task AutopilotProfileExistsInvalid()
    {
        var result = await Client.CheckAutopilotProfileExists("AAAAAAAAAAAAA");
        Assert.That(result.HasValue);
        Assert.That(result.Value.Exists, Is.False, $"Response should be false when invalid ST.");
    }
    
    [Test]
    public async Task GetMemberOfGroupInvalidST()
    {
        var result = await Client.IsSharedPCMember("AAAAAAAAAAAAA");
        Assert.That(result.HasValue, Is.False, $"Response should return null when invalid ST.");
    }
}