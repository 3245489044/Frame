
using Share.LogicComposition.Abstractions;

namespace Share.LogicComposition.DefaultImplementations
{
    public class TemplateConfigProvider<TConfig> : ITemplateConfigProvider<TConfig>
        where TConfig : class
    {
        private readonly Dictionary<string, TConfig> _templates = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new();
        private string _defaultName = "Default";

        public TemplateConfigProvider(string defaultName = "Default")
        {
            _defaultName = defaultName;
        }

        public static TemplateConfigProvider<TConfig> FromDictionary(
            IReadOnlyDictionary<string, TConfig> templates,
            string defaultName = "Default")
        {
            var provider = new TemplateConfigProvider<TConfig>(defaultName);
            provider.RegisterRange(templates);
            return provider;
        }

        public void Register(string name, TConfig template)
        {
            ArgumentNullException.ThrowIfNull(name);
            ArgumentNullException.ThrowIfNull(template);

            lock (_lock)
            {
                _templates[name] = template;
            }
        }

        public void RegisterRange(IEnumerable<KeyValuePair<string, TConfig>> templates)
        {
            ArgumentNullException.ThrowIfNull(templates);

            lock (_lock)
            {
                foreach (var kvp in templates)
                {
                    _templates[kvp.Key] = kvp.Value;
                }
            }
        }

        public TConfig Get(string name)
        {
            lock (_lock)
            {
                if (!string.IsNullOrEmpty(name) && _templates.TryGetValue(name, out var template))
                    return template;

                if (_templates.TryGetValue(_defaultName, out var defaultTemplate))
                    return defaultTemplate;

                throw new KeyNotFoundException($"未找到模板 '{name ?? "(null)"}'，且默认模板 '{_defaultName}' 也不存在。");
            }
        }

        public bool TryGet(string name, out TConfig? template)
        {
            lock (_lock)
            {
                if (!string.IsNullOrEmpty(name) && _templates.TryGetValue(name, out template))
                    return true;

                if (_templates.TryGetValue(_defaultName, out var defaultTemplate))
                {
                    template = defaultTemplate;
                    return true;
                }

                template = null;
                return false;
            }
        }

        public IEnumerable<string> GetAllNames()
        {
            lock (_lock)
            {
                return _templates.Keys.ToList();
            }
        }

        public void SetDefault(string name)
        {
            ArgumentNullException.ThrowIfNull(name);

            lock (_lock)
            {
                if (!_templates.ContainsKey(name))
                    throw new KeyNotFoundException($"无法将 '{name}' 设为默认模板：该模板不存在。");

                _defaultName = name;
            }
        }

        public bool Contains(string name)
        {
            lock (_lock)
            {
                return !string.IsNullOrEmpty(name) && _templates.ContainsKey(name);
            }
        }

        public int Count
        {
            get
            {
                lock (_lock) return _templates.Count;
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _templates.Clear();
            }
        }
    }
}
