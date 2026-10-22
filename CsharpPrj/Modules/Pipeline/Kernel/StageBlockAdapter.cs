#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using BasicMethod.Block;

namespace Modules.Pipeline.Kernel
{
    /// <summary>把旧 <see cref="IPipelineStage{TContext}"/> 适配成内核块。</summary>
    internal sealed class StageBlockAdapter<TContext> : IBlock
        where TContext : IPipelineContext
    {
        private readonly IPipelineStage<TContext> _stage;
        private readonly TContext _context;

        public StageBlockAdapter(IPipelineStage<TContext> stage, TContext context)
        {
            _stage = stage ?? throw new ArgumentNullException(nameof(stage));
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public string Name => _stage.Name;

        public string Description => _stage.Description;

        public BlockPolicy Policy => new()
        {
            IsFatal = true,
            CanExecute = _ => _stage.CanExecute(_context)
        };

        public async Task<BlockResult> ExecuteAsync(
            BlockContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await _stage.ExecuteAsync(_context, cancellationToken).ConfigureAwait(false);

                // E7：未写账本的 stage 视为成功；显式写失败才判失败。
                if (_context.IsStageExplicitlySet(_stage.Name)
                    && !_context.GetStageSuccess(_stage.Name))
                {
                    return BlockResult.Fail(
                        _context.GetStageError(_stage.Name)
                        ?? $"Stage '{_stage.Name}' failed.");
                }

                return BlockResult.Ok();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return BlockResult.Abort($"Stage '{_stage.Name}' cancelled.");
            }
            catch (Exception ex)
            {
                return BlockResult.Fail(ex.Message, ex);
            }
        }
    }
}
