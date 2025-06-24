#region

using System;
using System.Threading.Tasks;
using AutopilotQuick.WMI;
using Newtonsoft.Json;
using NLog;
using ORMi;
using WimManDatabase = System.Collections.Generic.Dictionary<string, AutopilotQuick.WimManData>;

#endregion

namespace AutopilotQuick
{
    internal class WimMan
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private static readonly WimMan instance = new();

        private Cacher _configCacher;
        public static WimMan getInstance()
        {
            return instance;
        }

        private UserDataContext _context;
        WMIHelper helper = new WMIHelper("root\\CimV2");
        public void SetContext(UserDataContext context)
        {
            _context = context;
            _configCacher = new Cacher(CachedResourceUris.WimManConfig, _context);
            InternetMan.GetInstance().InternetBecameAvailable += WimMan_InternetBecameAvailable;
        }

        private void WimMan_InternetBecameAvailable(object? sender, EventArgs e)
        {
            if (!_configCacher.IsUpToDate)
            {
                _configCacher.DownloadUpdate();
            }
        }

        private WimManDatabase GetWimManData()
        {
            if (!_configCacher.FileCached)
            {
                _configCacher.DownloadUpdate();
            }
            return JsonConvert.DeserializeObject<WimManDatabase>(_configCacher.ReadAllText());
        }

        public void Preload() {
            Task task = Task.Run(async () => await PreloadAsync());
            task.Wait();
        }

        public async Task PreloadAsync() {
            var WimManData = GetWimManData();
            foreach (var modelName in WimManData.Keys) {
                var cacher = GetCacherForModel(modelName);
                if (!cacher.IsUpToDate) {
                    await cacher.DownloadUpdateAsync();
                }
            }

        }

        

        private string _modelName;
        public string ModelName => _modelName ??= helper.QueryFirstOrDefault<ComputerSystem>().Model;

        public Cacher GetCacherForModel(string? model = null)
        {
            model ??= ModelName;
            var wimManData = GetWimManData();
            if (wimManData.ContainsKey(model))
            {
                return new Cacher(wimManData[model].URL, wimManData[model].Name, _context);
            }
            else
            {
                return new Cacher(wimManData["default"].URL, wimManData["default"].Name, _context);
            }

        }
        
        public int GetImageIndexForModel(string? model = null)
        {
            model ??= ModelName;
            var wimManData = GetWimManData();
            return wimManData.TryGetValue(model, out var value) ? value.Index : wimManData["default"].Index;

        }
    }
}
