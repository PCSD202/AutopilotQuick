#region

using System;

#endregion

namespace AutopilotQuick
{
    public class CachedResourceData
    {
        private static readonly Uri BaseUrl = new Uri("https://nettools.psd202.org/AutoPilotFast/");
        public CachedResourceData(string location, string FileName)
        {
            Uri =  new Uri(BaseUrl, location);
            this.FileName = FileName;
        }
        
        public CachedResourceData(string FileName)
        {
            Uri = new Uri(BaseUrl, FileName);
            this.FileName = FileName;
        }

        public readonly Uri Uri;
        public readonly string FileName;
    }
    public static class CachedResourceUris
    {
        public static readonly CachedResourceData GroupManConfig = new CachedResourceData("GroupMan.json");
        public static readonly CachedResourceData TakeHomeConfig = new CachedResourceData("TakehomeCreds.json");
        public static readonly CachedResourceData TakeHomeLoggerConfig = new CachedResourceData( "TakehomeLoggerInfo.json");
        public static readonly CachedResourceData TakeHomeCredConfig = new CachedResourceData("Takehome.json");
        public static readonly CachedResourceData WimManConfig = new CachedResourceData("WimManConfig.json");
        public static readonly CachedResourceData DellBiosSettingsZip = new CachedResourceData("DellBiosSettings.zip");
        public static readonly CachedResourceData OsdImage = new CachedResourceData("OSDCloud_NoPrompt.iso", "OSDImage.iso");
        public static readonly CachedResourceData AzureLogSettings = new CachedResourceData( "AzureLogSettings.json");
        public static readonly CachedResourceData GithubCreds = new CachedResourceData( "GithubCreds.json");
        public static readonly CachedResourceData HardwareTester = new CachedResourceData( "HardwareTester/HardwareTester.exe", "HardwareTester.exe");
        public static readonly CachedResourceData DellBiosCatalogConfig = new CachedResourceData( "Bios/DellBiosCatalogSettings.json", "DellBiosCatalogSettings.json");
    }
}

