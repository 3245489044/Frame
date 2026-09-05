
#nullable enable
using TConfig;
using TConfig.Abstractions;
using UniversalMiddleware.Abstractions;

namespace UniversalMiddleware.Rendering;

public abstract class ConfigRenderPipeline<TContainer> : RenderPipelineBase<TContainer>
    where TContainer : IConfigContainer
{
    private readonly IConfigLoader _configLoader;

    protected ConfigRenderPipeline(IConfigLoader configLoader, string name = "ConfigRenderPipeline")
        : base(name)
    {
        _configLoader = configLoader ?? throw new ArgumentNullException(nameof(configLoader));
    }

    public void BuildDefaultPipeline(bool hasTemplateProcessor = false)
    {
        if (hasTemplateProcessor)
        {
            AddStage(new TemplatePreprocessStage());
        }

        AddStage(new JsonConfigLoadStage(_configLoader));
    }

    public void BuildSimplePipeline()
    {
        AddStage(new JsonConfigLoadStage(_configLoader));
    }

    public static RenderSession CreateSession(
        TContainer container,
        string configPath,
        Microsoft.Extensions.Logging.ILogger? logger = null,
        Func<string, string>? templateProcessor = null,
        Action<TContainer, TConfig.UIConfiguration>? onPostProcess = null)
    {
        var session = new RenderSession(container, configPath);

        session.Items["ConfigPath"] = configPath;

        session.Items["Container"] = container;

        if (logger != null)
            session.Items["Logger"] = logger;

        if (templateProcessor != null)
            session.Items["TemplateProcessor"] = templateProcessor;

        if (onPostProcess != null)
            session.Items["OnPostProcess"] = onPostProcess;

        return session;
    }

    public async Task<TContainer> ExecuteAsync(
        string configPath,
        TContainer container,
        Microsoft.Extensions.Logging.ILogger? logger = null,
        Func<string, string>? templateProcessor = null,
        Action<TContainer, TConfig.UIConfiguration>? onPostProcess = null)
    {
        if (GetStageCount() == 0)
        {
            BuildDefaultPipeline(hasTemplateProcessor: templateProcessor != null);
        }

        var session = CreateSession(container, configPath, logger, templateProcessor, onPostProcess);

        await RenderAsync(container, session);

        if (onPostProcess != null && session.Items.TryGetValue("UIConfiguration", out var configObj)
            && configObj is TConfig.UIConfiguration config)
        {
            onPostProcess(container, config);
        }

        return container;
    }

    protected int GetStageCount()
    {
        return StageCount;
    }


    protected abstract override Task ExecuteCreateStage(TContainer target, IRenderSession session);

    protected abstract override Task ExecuteLayoutStage(TContainer target, IRenderSession session);

    protected abstract override Task ExecuteBindEventsStage(TContainer target, IRenderSession session);
}
