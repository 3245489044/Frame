
#nullable enable
using System;
using System.Threading.Tasks;
using TConfig.Abstractions;
using UniversalMiddleware.Rendering;

namespace UniversalMiddleware.Container;

public class ContainerOrchestrator
{
    private readonly IRenderPipeline _pipeline;
    private readonly string _name;

    public ContainerOrchestrator(IRenderPipeline pipeline, string? name = null)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _name = name ?? "ContainerOrchestrator";
    }

    public string Name => _name;

    public IRenderPipeline Pipeline => _pipeline;


    public virtual async Task<(string? applyLayoutError, bool eventsBoundSuccess, string? eventsBoundError)> RenderFullAsync(
        IContainer container, object config, object? sessionContext = null)
    {
        if (container == null)
            throw new ArgumentNullException(nameof(container));
        if (config == null)
            throw new ArgumentNullException(nameof(config));

        DisposeSession(container);

        await _pipeline.ExecuteCreateStageOnlyAsync(container, config);

        if (sessionContext != null)
        {
            RegisterSubContainers(container, sessionContext);
        }


        bool eventsBoundSuccess = true;
        string? eventsBoundError = null;
        try
        {
            await _pipeline.BindEventsAsync(container).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            eventsBoundSuccess = false;
            eventsBoundError = ex.Message;
        }

        return (applyLayoutError: null, eventsBoundSuccess, eventsBoundError);
    }


    public virtual void DisposeSession(object target)
    {
    }


    public virtual void RegisterSubContainers(IContainer container, object? sessionContext)
    {
    }
}
