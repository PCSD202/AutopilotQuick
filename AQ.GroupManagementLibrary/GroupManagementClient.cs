using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using RestSharp;
using RestSharp.Authenticators;

namespace AQ.GroupManagementLibrary;

public class GroupManagementClient : IDisposable
{
    private readonly ILogger _logger;
    readonly RestClient _client;

    public GroupManagementClient(ILogger<GroupManagementClient> logger, string apiKey, string apiBaseURL = "http://localhost:7071/api")
    {
        var options = new RestClientOptions(apiBaseURL);
        options.MaxTimeout = 100 * 1000; //100 seconds max timeout
        _client = new RestClient(options)
        {
            Authenticator = new GroupManagementAuthenticator(apiKey)
        };
        _logger = logger;
    }

    public async Task<IsSharedPCMemberResult?> IsSharedPCMember(string ServiceTag)
    {
        var request = new RestRequest("IsSharedPCMember", Method.Get);
        request.AddJsonBody(new NormalRequestBody(ServiceTag));
        var result = await _client.ExecuteAsync<IsSharedPCMemberResult>(request);
        if (result.IsSuccessful) return result.Data;
        _logger.LogError("Got status code: {statuscode} when calling IsSharedPCMember. Content: {content}", result.StatusCode, result.Content);
        return null;
    }
    
    public async Task<AddToGroupOutput?> AddToSharedPCGroup(string ServiceTag)
    {
        var request = new RestRequest("AddToSharedPCGroup", Method.Post);
        request.AddJsonBody(new NormalRequestBody(ServiceTag));
        var result = await _client.ExecuteAsync<AddToGroupOutput>(request);
        if (result.IsSuccessful) return result.Data;
        _logger.LogError("Got status code: {statuscode} when calling AddToSharedPCGroup. Content: {content}", result.StatusCode, result.Content);
        return null;
    }
    
    public async Task<RemoveFromGroupOutput?> RemoveFromSharedPCGroup(string ServiceTag)
    {
        var request = new RestRequest("RemoveFromSharedPCGroup", Method.Delete);
        request.AddJsonBody(new NormalRequestBody(ServiceTag));
        var result = await _client.ExecuteAsync<RemoveFromGroupOutput>(request);
        if (result.IsSuccessful) return result.Data;
        _logger.LogError("Got status code: {statuscode} when calling RemoveFromSharedPCGroup. Content: {content}", result.StatusCode, result.Content);
        return null;
    }
    
    public async Task<CheckAutopilotProfileSyncStatusOutput?> CheckAutopilotProfileSyncStatus(string ServiceTag)
    {
        var request = new RestRequest("CheckAutopilotProfileSyncStatus", Method.Get);
        request.AddJsonBody(new NormalRequestBody(ServiceTag));
        var result = await _client.ExecuteAsync<CheckAutopilotProfileSyncStatusOutput>(request);
        if (result.IsSuccessful) return result.Data;
        _logger.LogError("Got status code: {statuscode} when calling CheckAutopilotProfileSyncStatus. Content: {content}", result.StatusCode, result.Content);
        return null;
    }
    
    public async Task<AutopilotProfileExistsOutput?> CheckAutopilotProfileExists(string ServiceTag)
    {
        var request = new RestRequest("AutopilotProfileExists", Method.Get);
        request.AddJsonBody(new NormalRequestBody(ServiceTag));
        var result = await _client.ExecuteAsync<AutopilotProfileExistsOutput>(request);
        if (result.IsSuccessful) return result.Data;
        _logger.LogError("Got status code: {statuscode} when calling CheckAutopilotProfileExists. Content: {content}", result.StatusCode, result.Content);
        return null;
    }
    
    public void Dispose()
    {
        _client?.Dispose();
        GC.SuppressFinalize(this);
    }
}


public record struct IsSharedPCMemberResult(bool TransitiveMemberInGroup, bool DirectMemberInGroup);

public record struct AddToGroupOutput(bool AlreadyInGroup, bool Success);

public record struct RemoveFromGroupOutput(bool AlreadyRemoved, bool Success);

public record struct CheckAutopilotProfileSyncStatusOutput(bool synced);

public record struct AutopilotProfileExistsOutput(bool Exists);


internal class GroupManagementAuthenticator : AuthenticatorBase {
    readonly string _apiKey;

    public GroupManagementAuthenticator( string apiKey) : base("") {
        _apiKey = apiKey;
    }

    protected override async ValueTask<Parameter> GetAuthenticationParameter(string accessToken) {
        return new HeaderParameter("x-functions-key", _apiKey);
    }
}