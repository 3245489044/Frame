
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
            // 可选端口：缺省用命名空对象（N），禁匿名 ?? new 默认冒充装配。
            _configProvider = configProvider ?? NullConfigProvider.Instance;
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

    }

    /// <summary>配置供给命名空对象（N）：未注入时无操作（版本恒 default / 配置恒 null）——显式声明"此处允许无操作"。</summary>
    internal sealed class NullConfigProvider : IConfigProvider
    {
        public static readonly NullConfigProvider Instance = new();

        public Task<string> GetLatestVersionAsync(CancellationToken cancellationToken)
            => Task.FromResult("default");

        public Task<object?> LoadConfigAsync(string version, CancellationToken cancellationToken)
            => Task.FromResult<object?>(null);
    }
}
