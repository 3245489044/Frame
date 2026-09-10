#nullable enable

namespace FrameworkAbstractions.Configuration
{
    public interface IConfigurationCache<TConfig> where TConfig : class
    {
        bool TryGet(string key, out TConfig? config);

        TConfig GetOrAdd(string key, TConfig config);

        void Remove(string key);

        void Clear();

        bool Contains(string key);
    }

    public interface IConfigurationChangeNotifier
    {
        void RegisterChangeHandler(string configPath, Action<string> handler);

        void UnregisterChangeHandler(string configPath);

        void NotifyChanged(string configPath);
    }
}
