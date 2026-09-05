
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Modules.Framework.Abstractions;

namespace Modules.Framework
{
    public class ConfigBuilder<TConfig> : IConfigBuilder<TConfig>
        where TConfig : class, new()
    {
        private readonly IConfigLoader<TConfig> _loader;
        private readonly IConfigMerger<TConfig> _merger;
        private TConfig _config;
        private TConfig? _defaults;
        private readonly List<string> _errors = new();

        public ConfigBuilder(IConfigLoader<TConfig>? loader = null, IConfigMerger<TConfig>? merger = null)
        {
            _loader = loader ?? new ConfigLoader<TConfig>();
            _merger = merger ?? new ConfigMerger<TConfig>();
            _config = new TConfig();
        }

        public IConfigBuilder<TConfig> LoadFromFile(string filePath)
        {
            var loaded = _loader.LoadFromFile(filePath);
            if (loaded != null)
            {
                _merger.Merge(_config, loaded);
            }
            return this;
        }

        public IConfigBuilder<TConfig> LoadFromString(string content)
        {
            var loaded = _loader.LoadFromString(content);
            if (loaded != null)
            {
                _merger.Merge(_config, loaded);
            }
            return this;
        }

        public IConfigBuilder<TConfig> Merge(TConfig config)
        {
            if (config != null)
            {
                _merger.Merge(_config, config);
            }
            return this;
        }

        public IConfigBuilder<TConfig> WithDefaults(TConfig defaults)
        {
            _defaults = defaults;
            return this;
        }

        public IConfigBuilder<TConfig> Validate()
        {
            _errors.Clear();

            var properties = typeof(TConfig).GetProperties(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

            foreach (var prop in properties)
            {
                var value = prop.GetValue(_config);

                if (prop.GetCustomAttribute(typeof(RequiredAttribute)) != null)
                {
                    if (value == null || (value is string str && string.IsNullOrEmpty(str)))
                    {
                        _errors.Add($"必需属性 '{prop.Name}' 未设置");
                    }
                }

                var stringLengthAttr = prop.GetCustomAttribute(typeof(StringLengthAttribute))
                    as StringLengthAttribute;
                if (stringLengthAttr != null && value is string strValue)
                {
                    if (strValue.Length > stringLengthAttr.MaximumLength)
                    {
                        _errors.Add($"属性 '{prop.Name}' 长度超过最大值 {stringLengthAttr.MaximumLength}");
                    }
                }

                var rangeAttr = prop.GetCustomAttribute(typeof(RangeAttribute))
                    as RangeAttribute;
                if (rangeAttr != null && value is IComparable comparable)
                {
                    if (comparable.CompareTo(rangeAttr.Minimum) < 0 ||
                        comparable.CompareTo(rangeAttr.Maximum) > 0)
                    {
                        _errors.Add($"属性 '{prop.Name}' 超出范围 [{rangeAttr.Minimum}, {rangeAttr.Maximum}]");
                    }
                }
            }

            return this;
        }

        public TConfig Build()
        {
            if (_defaults != null)
            {
                _merger.Merge(_config, _defaults, overwrite: false);
            }

            Validate();

            if (_errors.Count > 0)
            {
                throw new InvalidOperationException(
                    $"配置验证失败:\n{string.Join("\n", _errors.Select(e => $"  - {e}"))}");
            }

            return _config;
        }

        public IReadOnlyList<string> Errors => _errors.AsReadOnly();

        public bool HasErrors => _errors.Count > 0;
    }
}
