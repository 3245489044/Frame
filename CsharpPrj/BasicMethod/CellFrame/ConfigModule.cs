using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.IO;
using System;
using System.Collections.Generic;
using CellGateway;

namespace CellConfigModule
{
    public class ConfigModule : IConfigModule
    {
        private ConfigModuleConfig _config;
        private Dictionary<string, object> _configData = new Dictionary<string, object>();

        public void Initialize(Dictionary<string, object> config)
        {
            _config = new ConfigModuleConfig
            {
                ConfigPath = config.TryGetValue("ConfigPath", out var cp) ? cp.ToString() : "./config/",
                ConfigFileName = config.TryGetValue("ConfigFileName", out var cfn) ? cfn.ToString() : "config.json",
                EnableAutoReload = config.TryGetValue("EnableAutoReload", out var ear) ? Convert.ToBoolean(ear) : false,
                AutoReloadInterval = config.TryGetValue("AutoReloadInterval", out var ari) ? Convert.ToInt32(ari) : 60,
                EnableValidation = config.TryGetValue("EnableValidation", out var ev) ? Convert.ToBoolean(ev) : true,
                SchemaFile = config.TryGetValue("SchemaFile", out var sf) ? sf.ToString() : "schema.json"
            };
        }

        public void Start()
        {
            if (!string.IsNullOrEmpty(_config.ConfigPath))
            {
                LoadConfig(Path.Combine(_config.ConfigPath, _config.ConfigFileName));
            }
        }

        public void Stop()
        {
            _configData.Clear();
        }

        public object GetConfig(string key)
        {
            _configData.TryGetValue(key, out var value);
            return value;
        }

        public T GetConfig<T>(string key)
        {
            if (_configData.TryGetValue(key, out var value))
            {
                return JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(value));
            }
            return default;
        }

        public bool SetConfig(string key, object value)
        {
            _configData[key] = value;
            return true;
        }

        public bool LoadConfig(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    var content = File.ReadAllText(filePath);
                    _configData = JsonConvert.DeserializeObject<Dictionary<string, object>>(content) ?? new Dictionary<string, object>();
                    return true;
                }
            }
            catch
            {
            }
            return false;
        }

        public bool SaveConfig(string filePath)
        {
            try
            {
                var directory = Path.GetDirectoryName(filePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                var content = JsonConvert.SerializeObject(_configData, Formatting.Indented);
                File.WriteAllText(filePath, content);
                return true;
            }
            catch
            {
            }
            return false;
        }

        public bool ReloadConfig()
        {
            return LoadConfig(Path.Combine(_config.ConfigPath, _config.ConfigFileName));
        }

        public ValidationResult ValidateConfig(object schema)
        {
            return new ValidationResult { IsValid = true };
        }

        public List<string> GetAllKeys()
        {
            return new List<string>(_configData.Keys);
        }

        public bool ClearConfig(string key)
        {
            return _configData.Remove(key);
        }

        public object Execute(string action, Dictionary<string, object> parameters)
        {
            return action switch
            {
                "Get" => GetConfig(parameters["key"].ToString()),
                "Set" => SetConfig(parameters["key"].ToString(), parameters["value"]),
                "Load" => LoadConfig(parameters["filePath"].ToString()),
                "Save" => SaveConfig(parameters["filePath"].ToString()),
                "Reload" => ReloadConfig(),
                "Keys" => GetAllKeys(),
                _ => throw new NotSupportedException($"Action {action} is not supported")
            };
        }

        public object Query(string query)
        {
            return GetAllKeys();
        }

        public bool Update(Dictionary<string, object> data)
        {
            foreach (var kvp in data)
            {
                SetConfig(kvp.Key, kvp.Value);
            }
            return true;
        }

        public Dictionary<string, object> GetState()
        {
            return new Dictionary<string, object>
            {
                { "ConfigCount", _configData.Count },
                { "AutoReloadEnabled", _config.EnableAutoReload },
                { "ValidationEnabled", _config.EnableValidation }
            };
        }

        public void SetState(Dictionary<string, object> state)
        {
            _configData = new Dictionary<string, object>(state);
        }
    }
}


namespace CellConfigModule
{
    public interface IConfigModule : ICellModule
    {
        object GetConfig(string key);
        T GetConfig<T>(string key);
        bool SetConfig(string key, object value);
        bool LoadConfig(string filePath);
        bool SaveConfig(string filePath);
        bool ReloadConfig();
        ValidationResult ValidateConfig(object schema);
        List<string> GetAllKeys();
        bool ClearConfig(string key);
    }

    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public string Message { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
    }

    public class ConfigModuleConfig
    {
        public string ConfigPath { get; set; } = "./config/";
        public string ConfigFileName { get; set; } = "config.json";
        public bool EnableAutoReload { get; set; } = false;
        public int AutoReloadInterval { get; set; } = 60;
        public bool EnableValidation { get; set; } = true;
        public string SchemaFile { get; set; } = "schema.json";
    }
}