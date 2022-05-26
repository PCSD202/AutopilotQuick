using AutopilotQuick.WMI;
using Newtonsoft.Json;
using NLog;
using ORMi;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using WimManDatabase = System.Collections.Generic.Dictionary<string, AutopilotQuick.WimManData>;

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
            _configCacher = new Cacher("http://sccm2.psd202.org/WIM/WimManConfig.json", "WimManConfig.json", _context);
            InternetMan.getInstance().InternetBecameAvailable += WimMan_InternetBecameAvailable;
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
            return JsonConvert.DeserializeObject<WimManDatabase>(File.ReadAllText(_configCacher.FilePath));
        }

        

        private string _modelName;
        public string ModelName => _modelName ??= helper.QueryFirstOrDefault<ComputerSystem>().Model;

        public Cacher GetCacherForModel(string? model = null)
        {
            _logger.Info(ModelName);
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
    }
}
