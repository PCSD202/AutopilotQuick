using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using AQ.SecretArchive;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AutopilotQuick;

public static class SecretNames
{
    public const string GroupMan = "GroupMan.json";
    public const string Graph = "TakehomeCreds.json";
    public const string TakeHomeLogger = "TakehomeLoggerInfo.json";
    public const string TakeHome = "Takehome.json";
    public const string AzureLogs = "AzureLogSettings.json";
    public const string BiosPassword = "Bios/BiosPassword.txt";
    public const string DellBiosSettings = "DellBiosSettings.zip";
    public const string AppInsights = "AppInsights.json";
    public const string WifiPackage = "Wifi.ppkg";
    public const string BiosUpdatePackage = "Invoke-BiosUpdate.ppkg";
}

/// <summary>One authenticated snapshot per process. No plaintext files or old credential endpoints are used.</summary>
public static class AppSecrets
{
    private static ArchiveContents? _contents;
    private static ArchiveContents Contents => _contents ?? throw new InvalidOperationException("Secrets have not been unlocked.");

    public static void Initialize(ArchiveContents contents)
    {
        if (_contents is not null) throw new InvalidOperationException("Secrets are already unlocked.");
        Validate(contents);
        _contents = contents;
    }

    internal static void Validate(ArchiveContents contents)
    {
        var required = new Dictionary<string, string[]>
        {
            [SecretNames.GroupMan] = new[] { "APIKEY", "URL" },
            [SecretNames.TakeHomeLogger] = new[] { "ApiKey", "Url" },
            [SecretNames.TakeHome] = new[] { "ApiKey", "Url" },
            [SecretNames.AzureLogs] = new[] { "ConnectionString" },
            [SecretNames.AppInsights] = new[] { "ConnectionString" }
        };
        // The active pipeline calls the Takehome API; direct Graph steps are retained but inactive.
        if (contents.Names.Contains(SecretNames.Graph, StringComparer.OrdinalIgnoreCase))
            required.Add(SecretNames.Graph, new[] { "AppID", "AppSecret", "TenantID" });
        try
        {
            foreach (var item in required)
            {
                var json = JObject.Parse(contents.ReadText(item.Key));
                foreach (var field in item.Value)
                {
                    var value = json.GetValue(field, StringComparison.OrdinalIgnoreCase);
                    if (value?.Type != JTokenType.String || string.IsNullOrWhiteSpace(value.Value<string>()))
                        throw new InvalidDataException("Missing required configuration field.");
                }
                if (item.Key == SecretNames.Graph)
                {
                    var encrypted = json.GetValue("Encrypted", StringComparison.OrdinalIgnoreCase);
                    if (encrypted?.Type != JTokenType.Boolean || encrypted.Value<bool>())
                        throw new InvalidDataException("Graph credentials must use Encrypted=false inside the archive.");
                }
                var url = json.GetValue("Url", StringComparison.OrdinalIgnoreCase)?.Value<string>();
                if (url is not null && (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo)))
                    throw new InvalidDataException("Service URL must use HTTPS without embedded credentials.");
            }
            if (string.IsNullOrWhiteSpace(contents.ReadText(SecretNames.BiosPassword)))
                throw new InvalidDataException("BIOS password is missing.");
            var bundle = contents.ReadBytes(SecretNames.DellBiosSettings);
            try
            {
                using var zipBytes = new MemoryStream(bundle, false);
                using var zip = SecretArchive.ReadZip(zipBytes);
                foreach (var name in new[] { "cctk.exe", "LaptopBiosSettings.cmd", "DesktopBiosSettings.cmd" })
                    if (!zip.Names.Contains(name, StringComparer.OrdinalIgnoreCase))
                        throw new InvalidDataException("Dell BIOS bundle is incomplete.");
            }
            finally { CryptographicOperations.ZeroMemory(bundle); }
            foreach (var name in new[] { SecretNames.WifiPackage, SecretNames.BiosUpdatePackage })
            {
                var bytes = contents.ReadBytes(name);
                try { if (bytes.Length == 0) throw new InvalidDataException("Provisioning package is empty."); }
                finally { CryptographicOperations.ZeroMemory(bytes); }
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or FormatException or InvalidCastException or ArgumentException)
        {
            // JSON parser exceptions can contain secret values. Do not retain the original exception.
            throw new InvalidDataException("Archive configuration is incomplete or invalid. See the migration guide.");
        }
    }

    public static T ReadJson<T>(string name)
    {
        try { return JsonConvert.DeserializeObject<T>(Contents.ReadText(name)) ?? throw new InvalidDataException(); }
        catch (JsonException) { throw new InvalidDataException("Invalid archive configuration."); }
    }

    public static string ReadText(string name) => Contents.ReadText(name);
    public static byte[] ReadBytes(string name) => Contents.ReadBytes(name);
    public static void Clear() { _contents?.Dispose(); _contents = null; }
}
