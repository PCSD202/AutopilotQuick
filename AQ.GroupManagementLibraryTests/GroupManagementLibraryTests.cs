using AQ.GroupManagementLibrary;
using Microsoft.Extensions.Logging.Abstractions;

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
    public async Task GetMemberOfGroupInvalidST()
    {
        var result = await Client.IsSharedPCMember("AAAAAAAAAAAAA");
        Assert.That(result.HasValue, Is.False, $"Response should return null when invalid ST.");
    }
}