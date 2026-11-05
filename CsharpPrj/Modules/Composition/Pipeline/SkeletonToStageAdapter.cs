
#nullable enable
using System.Threading;
using System.Threading.Tasks;
using SkeletonFlow;

namespace Modules.Pipeline
{
    public class SkeletonToStageAdapter<TContext> : PipelineStageBase<TContext>
        where TContext : IPipelineContext
    {
        private readonly ISkeleton<TContext, TContext> _skeleton;

        public SkeletonToStageAdapter(
            ISkeleton<TContext, TContext> skeleton,
            string? name = null,
            string? description = null)
            : base(name ?? skeleton.Name, description ?? $"{skeleton.Name} (adapted from Skeleton)")
        {
            _skeleton = skeleton;
        }

        protected override async Task ExecuteCoreAsync(TContext context, CancellationToken cancellationToken)
        {
            var skeletonCtx = context as SkeletonContext;
            if (skeletonCtx == null)
            {
                skeletonCtx = new SkeletonContext();
                if (cancellationToken != default)
                    skeletonCtx.CancellationToken = cancellationToken;
            }

            await _skeleton.ExecuteAsync(context, skeletonCtx).ConfigureAwait(false);
        }
    }
}
