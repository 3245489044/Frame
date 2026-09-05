
using System.Text.Json;
using System.Text.Json.Serialization;
using Modules.Framework.Abstractions;

namespace Modules.Framework
{
    public class ConfigLoader<TConfig> : IConfigLoader<TConfig>
        where TConfig : class
    {
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly ConfigMerger<TConfig> _merger;

        public string ExtendsPropertyName { get; set; } = "Extends";

        public bool EnableExtendsResolution { get; set; } = true;

        public ConfigLoader(JsonSerializerOptions? options = null)
        {
            _jsonOptions = options ?? new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };
            _merger = new ConfigMerger<TConfig>();
        }

        public TConfig? Load(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
                throw new ArgumentException("配置源不能为空", nameof(source));

            if (File.Exists(source))
            {
                return LoadFromFile(source);
            }

            return LoadFromString(source);
        }

        public TConfig? LoadFromFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("文件路径不能为空", nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"配置文件未找到: {filePath}", filePath);

            string json = File.ReadAllText(filePath);
            return LoadFromString(json);
        }

        public TConfig? LoadFromString(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                throw new ArgumentException("内容字符串不能为空", nameof(content));

            return JsonSerializer.Deserialize<TConfig>(content, _jsonOptions);
        }

        public TConfig? LoadMerged(IEnumerable<string> sources)
        {
            if (sources == null)
                throw new ArgumentNullException(nameof(sources));

            TConfig? merged = null;

            foreach (var source in sources)
            {
                var config = Load(source);
                if (config != null)
                {
                    if (merged == null)
                    {
                        merged = config;
                    }
                    else
                    {
                        _merger.Merge(merged, config);
                    }
                }
            }

            return merged;
        }

        public TConfig? LoadWithExtends(string filePath, Func<string, string, string>? resolveBasePath = null)
        {
            if (!EnableExtendsResolution)
                return LoadFromFile(filePath);

            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("文件路径不能为空", nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"配置文件未找到: {filePath}", filePath);

            var config = LoadFromFile(filePath);
            if (config == null)
                return null;

            return ResolveExtendsChain(config, filePath, resolveBasePath, new HashSet<string>());
        }

        private TConfig? ResolveExtendsChain(
            TConfig config,
            string currentFilePath,
            Func<string, string, string>? resolveBasePath,
            HashSet<string> visitedPaths)
        {
            var extendsValue = GetExtendsValue(config);
            if (string.IsNullOrWhiteSpace(extendsValue))
                return config;

            string basePath;
            if (resolveBasePath != null)
            {
                basePath = resolveBasePath(extendsValue, currentFilePath);
            }
            else
            {
                string? dir = Path.GetDirectoryName(currentFilePath);
                basePath = Path.Combine(dir ?? "", extendsValue);
            }

            basePath = Path.GetFullPath(basePath);

            if (!visitedPaths.Add(basePath))
            {
                throw new InvalidOperationException(
                    $"检测到循环继承: {extendsValue} 已被加载。继承链: {string.Join(" → ", visitedPaths)}");
            }

            if (!File.Exists(basePath))
            {
                throw new FileNotFoundException($"父配置文件未找到: {basePath} (从 {currentFilePath} 的 Extends 引用)", basePath);
            }

            var baseConfig = LoadFromFile(basePath);
            if (baseConfig == null)
                return config;

            baseConfig = ResolveExtendsChain(baseConfig, basePath, resolveBasePath, visitedPaths);

            _merger.Merge(baseConfig, config);
            return baseConfig;
        }

        private string? GetExtendsValue(TConfig config)
        {
            if (config == null) return null;

            var type = typeof(TConfig);
            var property = type.GetProperty(ExtendsPropertyName,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.IgnoreCase);

            if (property == null || !property.CanRead)
                return null;

            var value = property.GetValue(config);
            return value?.ToString();
        }
    }
}
