#nullable enable

namespace FrameworkAbstractions.Configuration
{
    public interface IConfigurationManager<TConfig> : IConfigurationCache<TConfig>, IConfigurationChangeNotifier
        where TConfig : class
    {
        TConfig Load(string configPath, Func<string, TConfig> loader);

        Task SaveAsync(string configPath, TConfig config);

        TConfig Reload(string configPath, Func<string, TConfig> loader);
    }
}
