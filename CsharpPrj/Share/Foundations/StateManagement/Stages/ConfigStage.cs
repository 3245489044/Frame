
namespace Share.StateManagement.Stages
{
    public class ConfigStage : StageBase
    {
        private readonly IConfigProvider _configProvider;

        public ConfigStage(
            IConfigProvider? configProvider = null,
            string name = "Config",
            string? description = null)
            : base(name, description ?? "通用配置加载 - 支持版本管理")
        {
            _configProvider = configProvider ?? new DefaultConfigProvider();
        }

        protected override async Task ExecuteCoreAsync(
            IStageContext context,
            CancellationToken cancellationToken)
        {
            var latestVersion = await _configProvider.GetLatestVersionAsync(cancellationToken);
            var currentVersion = GetData<string>(context, "ConfigVersion");

            if (currentVersion == latestVersion)
            {
                LogDebug(context, $"配置版本未变化: {latestVersion}");
                return;
            }

            var config = await _configProvider.LoadConfigAsync(latestVersion, cancellationToken);
            if (config != null)
            {
                SetData(context, "ConfigVersion", latestVersion);
                SetData(context, "ConfigContent", config);
                LogInfo(context, $"配置已加载: Version={latestVersion}");
            }
            else
            {
                LogWarn(context, $"配置加载失败: Version={latestVersion}");
            }
        }

        private class DefaultConfigProvider : IConfigProvider
        {
            public Task<string> GetLatestVersionAsync(CancellationToken cancellationToken)
                => Task.FromResult("default");

            public Task<object?> LoadConfigAsync(string version, CancellationToken cancellationToken)
                => Task.FromResult<object?>(null);
        }
    }
}
