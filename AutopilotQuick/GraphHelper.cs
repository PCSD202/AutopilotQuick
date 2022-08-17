using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Identity;
using Microsoft.Graph;
using Newtonsoft.Json;
using Nito.AsyncEx;
using NLog;
using File = System.IO.File;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace AutopilotQuick;

public static class GraphHelper
{
    public record struct AppToken(string AppID, string AppSecret, string TenantID, bool Encrypted = true);
    
    
    private static string Password = "hK8J4k9@cgQ8";
    public static AppToken Decrypt(AppToken encryptedAppToken)
    {
        var decryptedTenantId = AESThenHMAC.SimpleDecryptWithPassword(encryptedAppToken.TenantID, Password);
        var decryptedClientId = AESThenHMAC.SimpleDecryptWithPassword(encryptedAppToken.AppID, Password);
        var decryptedClientSecret = AESThenHMAC.SimpleDecryptWithPassword(encryptedAppToken.AppSecret, Password);
        return new AppToken()
        {
            TenantID = decryptedTenantId,
            AppID = decryptedClientId,
            AppSecret = decryptedClientSecret,
            Encrypted = false
        };
    }
    
    public static AppToken Encrypt(AppToken unencryptedAppToken)
    {
        var encryptedTenantId = AESThenHMAC.SimpleEncryptWithPassword(unencryptedAppToken.TenantID, Password);
        var encryptedClientId = AESThenHMAC.SimpleEncryptWithPassword(unencryptedAppToken.AppID, Password);
        var encryptedClientSecret = AESThenHMAC.SimpleEncryptWithPassword(unencryptedAppToken.AppSecret, Password);
        return new AppToken()
        {
            TenantID = encryptedTenantId,
            AppID = encryptedClientId,
            AppSecret = encryptedClientSecret
        };
    }

    public static async Task<AppToken> GetGraphCreds(UserDataContext context)
    {
        var takehomeCredsCacher = new Cacher("http://nettools.psd202.org/AutoPilotFast/TakehomeCreds.json", "TakeHomeCreds.json", context);
        
        if (!(takehomeCredsCacher.FileCached && takehomeCredsCacher.IsUpToDate))
        {
            takehomeCredsCacher.DownloadUpdate();
        }
        AppToken encryptedAppToken = JsonConvert.DeserializeObject<AppToken>(await File.ReadAllTextAsync(takehomeCredsCacher.FilePath));
        AppToken decryptedAppToken = Decrypt(encryptedAppToken);
        return decryptedAppToken;
    }
    
    public static GraphServiceClient ConnectToMSGraph(AppToken AppCreds) {
        if (AppCreds.Encrypted == true)
        {
            AppCreds = Decrypt(AppCreds);
        }
        // using Azure.Identity;
        var options = new TokenCredentialOptions
        {
            AuthorityHost = AzureAuthorityHosts.AzurePublicCloud
        };
        var scopes = new[] { "https://graph.microsoft.com/.default" };
        // https://docs.microsoft.com/dotnet/api/azure.identity.clientsecretcredential
        var clientSecretCredential = new ClientSecretCredential(AppCreds.TenantID, AppCreds.AppID, AppCreds.AppSecret, options);

        var graphClient = new GraphServiceClient(clientSecretCredential, scopes);
        return graphClient;
    }
}