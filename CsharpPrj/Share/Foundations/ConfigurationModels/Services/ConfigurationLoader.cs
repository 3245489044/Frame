#nullable enable
using System.Collections.Concurrent;
using System.Text.Json;
using TConfig;

namespace Modules.ConfigurationModels.Services

{
    public class ConfigurationLoader : IConfigurationLoader
    {
        protected static readonly JsonSerializerOptions DefaultJsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        protected readonly ConcurrentDictionary<string, UIConfiguration> _configCache = new();
        protected readonly ConcurrentDictionary<string, Action<string>> _changeHandlers = new();
        protected readonly ConcurrentDictionary<string, HashSet<string>> _reverseDependencies = new(StringComparer.OrdinalIgnoreCase);
        protected readonly JsonSerializerOptions _options;

        public ConfigurationLoader(JsonSerializerOptions? options = null)
        {
            _options = options ?? DefaultJsonOptions;
        }

        public virtual async Task<UIConfiguration> LoadConfigAsync(string configPath)
        {
            return await LoadConfigInternalAsync(configPath, null);
        }

        private async Task<UIConfiguration> LoadConfigInternalAsync(string configPath, HashSet<string>? visiting)
        {
            if (string.IsNullOrEmpty(configPath))
                throw new ArgumentNullException(nameof(configPath));

            if (_configCache.TryGetValue(configPath, out var cached))
                return cached;

            visiting ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!visiting.Add(configPath))
                throw new InvalidOperationException($"Circular Extends reference detected: {configPath}");

            if (!File.Exists(configPath))
                throw new FileNotFoundException("Configuration file not found", configPath);

            var json = await File.ReadAllTextAsync(configPath);
            var config = Deserialize(json)
                ?? throw new InvalidOperationException("Failed to deserialize configuration");

            if (!string.IsNullOrEmpty(config.Extends))
            {
                string basePath = Path.Combine(Path.GetDirectoryName(configPath) ?? "", config.Extends);
                var baseConfig = await LoadConfigInternalAsync(basePath, visiting);
                config = ConfigurationMerger.Merge(baseConfig, config);
                config.Extends = null;

                _reverseDependencies.AddOrUpdate(
                    basePath,
                    _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { configPath },
                    (_, deps) => { deps.Add(configPath); return deps; });
            }

            _configCache[configPath] = config;
            return config;
        }

        public virtual UIConfiguration GetConfig(string configPath)
        {
            if (_configCache.TryGetValue(configPath, out var config))
                return config;

            throw new KeyNotFoundException($"Configuration not loaded: {configPath}");
        }

        public virtual async Task SaveConfigAsync(string configPath, UIConfiguration config)
        {
            if (string.IsNullOrEmpty(configPath))
                throw new ArgumentNullException(nameof(configPath));
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            var json = JsonSerializer.Serialize(config, _options);
            await File.WriteAllTextAsync(configPath, json);

            _configCache[configPath] = config;

            if (_changeHandlers.TryGetValue(configPath, out var handler))
                handler(configPath);
        }

        public virtual JsonValidationResult? ValidateConfigContent(string jsonContent)
        {
            try
            {
                var config = Deserialize(jsonContent);
                if (config == null)
                    return JsonValidationResult.Failure("Failed to deserialize configuration");

                var errors = new List<string>();

                if (config.Form != null)
                {
                    if (config.Form.Width <= 0)
                        errors.Add("Form width must be greater than 0");
                    if (config.Form.Height <= 0)
                        errors.Add("Form height must be greater than 0");
                }

                foreach (var view in config.Views)
                {
                    if (string.IsNullOrWhiteSpace(view.Name))
                        errors.Add("View name cannot be empty");
                }

                return errors.Count > 0
                    ? JsonValidationResult.Failure(string.Join("; ", errors))
                    : JsonValidationResult.Success();
            }
            catch (JsonException ex)
            {
                return JsonValidationResult.Failure($"JSON parsing error: {ex.Message}");
            }
        }

        public virtual async Task ReloadConfigAsync(string configPath)
        {
            if (_configCache.ContainsKey(configPath))
            {
                _configCache.TryRemove(configPath, out _);
                await LoadConfigAsync(configPath);

                if (_changeHandlers.TryGetValue(configPath, out var handler))
                    handler(configPath);
            }

            if (_reverseDependencies.TryGetValue(configPath, out var dependents))
            {
                foreach (var dependentPath in dependents.ToArray())
                {
                    await ReloadConfigAsync(dependentPath);
                }
            }
        }

        public virtual void RegisterConfigChangeHandler(string configPath, Action<string> handler)
        {
            _changeHandlers[configPath] = handler;
        }

        public virtual void UnregisterConfigChangeHandler(string configPath)
        {
            _changeHandlers.TryRemove(configPath, out _);
        }

        public virtual void ClearCache()
        {
            _configCache.Clear();
        }

        protected virtual UIConfiguration? Deserialize(string json)
        {
            return JsonSerializer.Deserialize<UIConfiguration>(json, _options);
        }
    }
}
