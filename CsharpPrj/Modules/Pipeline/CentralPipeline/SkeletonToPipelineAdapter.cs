
using SkeletonFlow;
using BP = Modules.Pipeline;

namespace Modules.CentralPipeline;

public class SkeletonToPipelineAdapter<TContext> : BP.PipelineBase<TContext, TContext>
{
    private readonly ISkeleton<TContext, TContext> _skeleton;
    private IReadOnlyDictionary<Type, object>? _globalServices;

    public ISkeleton<TContext, TContext> InnerSkeleton => _skeleton;

    public SkeletonToPipelineAdapter(ISkeleton<TContext, TContext> skeleton)
        : base(skeleton.Name, skeleton.Description)
    {
        _skeleton = skeleton ?? throw new ArgumentNullException(nameof(skeleton));
    }

    public void SetGlobalServices(IReadOnlyDictionary<Type, object> services)
    {
        _globalServices = services;
    }

    protected override TContext ExecuteCore(TContext input, BP.PipelineContext context)
    {
        using var skelCtx = CreateSkeletonContext(context);
        return _skeleton.Execute(input, skelCtx);
    }

    protected override async Task<TContext> ExecuteCoreAsync(TContext input, BP.PipelineContext context)
    {
        using var skelCtx = CreateSkeletonContext(context);
        return await _skeleton.ExecuteAsync(input, skelCtx).ConfigureAwait(false);
    }

    private SkeletonContext CreateSkeletonContext(BP.PipelineContext context)
    {
        var skelCtx = new SkeletonContext();
        skelCtx.CancellationToken = context.CancellationToken;
        skelCtx.ServiceProvider = context.ServiceProvider;
        if (_globalServices != null)
        {
            skelCtx.RegisterServices(_globalServices);
        }
        foreach (var kvp in context.Items)
        {
            if (kvp.Value != null)
            {
                skelCtx.SetItem(kvp.Key, kvp.Value);
            }
        }
        return skelCtx;
    }
}
