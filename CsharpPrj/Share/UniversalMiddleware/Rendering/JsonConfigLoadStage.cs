
#nullable enable
using TConfig;
using TConfig.Abstractions;
using UniversalMiddleware.Abstractions;

namespace UniversalMiddleware.Rendering;

public class JsonConfigLoadStage : RenderStageBase
{
    private readonly IConfigLoader _configLoader;

    public override string Name => "JsonConfigLoad";
    public override int Order => 10;

    public JsonConfigLoadStage(IConfigLoader configLoader)
    {
        _configLoader = configLoader ?? throw new ArgumentNullException(nameof(configLoader));
    }

    public override async Task ExecuteAsync(IRenderSession session)
    {
        if (!session.Items.TryGetValue("ConfigPath", out var configPathObj) || configPathObj is not string configPath)
        {
            throw new InvalidOperationException("JsonConfigLoadStage: session.Items[\"ConfigPath\"] is required");
        }

        if (session.Items.TryGetValue("PreloadedConfig", out var preloadedObj) && preloadedObj is TConfig.UIConfiguration preloadedConfig)
        {
            session.Items["UIConfiguration"] = preloadedConfig;
            session.AddDiagnostic("JsonConfigLoad_FromPreloaded", true);
            return;
        }

        var loadResult = _configLoader.LoadConfig(configPath);

        if (!loadResult.Success || loadResult.Config == null)
        {
            throw new InvalidOperationException($"JsonConfigLoadStage: failed to load config from '{configPath}': {loadResult.ErrorMessage}");
        }

        session.Items["UIConfiguration"] = loadResult.Config;
        session.AddDiagnostic("JsonConfigLoad_Path", configPath);
        if (loadResult.Config is TConfig.UIConfiguration uiConfig)
            session.AddDiagnostic("JsonConfigLoad_ViewCount", uiConfig.Views?.Count ?? 0);
    }
}
