#nullable enable
using System.Collections.Concurrent;

namespace FrameworkAbstractions.Configuration
{
    public class ConfigurationChangeNotifier : IConfigurationChangeNotifier
    {
        private readonly ConcurrentDictionary<string, Action<string>> _changeHandlers = new(StringComparer.OrdinalIgnoreCase);

        public static ConfigurationChangeNotifier Default { get; } = new();

        public void RegisterChangeHandler(string configPath, Action<string> handler)
        {
            if (string.IsNullOrEmpty(configPath))
                throw new ArgumentException("Config path cannot be null or empty", nameof(configPath));
            handler = PortGuard.Required(handler, nameof(handler));

            _changeHandlers[configPath] = handler;
        }

        public void UnregisterChangeHandler(string configPath)
        {
            if (string.IsNullOrEmpty(configPath))
                throw new ArgumentException("Config path cannot be null or empty", nameof(configPath));

            _changeHandlers.TryRemove(configPath, out _);
        }

        public void NotifyChanged(string configPath)
        {
            if (string.IsNullOrEmpty(configPath))
                return;

            if (_changeHandlers.TryGetValue(configPath, out var handler))
            {
                handler(configPath);
            }
        }

        public void Clear()
        {
            _changeHandlers.Clear();
        }

        public bool HasHandler(string configPath)
        {
            return _changeHandlers.ContainsKey(configPath);
        }
    }
}
