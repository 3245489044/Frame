#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using BasicMethod.Block;

namespace Modules.Pipeline.StaticPipeline
{
    /// <summary>异步静态步骤内核适配块。</summary>
    internal sealed class StaticAsyncStepBlock<TContext> : IBlock
    {
        private readonly AsyncStep<TContext> _step;
        private readonly TContext _context;
        private readonly Func<TContext, Task>? _onBeforeStep;
        private readonly Func<TContext, string, Task>? _onAfterStep;
        private readonly Func<TContext, string, Exception, Task>? _onStepError;

        public StaticAsyncStepBlock(
            AsyncStep<TContext> step,
            TContext context,
            Func<TContext, Task>? onBeforeStep,
            Func<TContext, string, Task>? onAfterStep,
            Func<TContext, string, Exception, Task>? onStepError)
        {
            _step = step;
            _context = context;
            _onBeforeStep = onBeforeStep;
            _onAfterStep = onAfterStep;
            _onStepError = onStepError;
        }

        public string Name => _step.Name;

        public string Description => _step.Name;

        public BlockPolicy Policy => new()
        {
            IsFatal = _step.IsFatal,
            CanExecute = _step.CanExecute is null ? null : _ => _step.CanExecute(_context)
        };

        public async Task<BlockResult> ExecuteAsync(
            BlockContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                if (_onBeforeStep is not null)
                    await _onBeforeStep(_context).ConfigureAwait(false);

                await _step.Execute(_context).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return BlockResult.Abort($"Step '{Name}' cancelled.");
            }
            catch (Exception ex)
            {
                await TryNotifyErrorAsync(context, ex).ConfigureAwait(false);
                return BlockResult.Fail(ex.Message, ex);
            }

            await TryNotifyAfterAsync(context).ConfigureAwait(false);
            return BlockResult.Ok();
        }

        private async Task TryNotifyErrorAsync(BlockContext context, Exception ex)
        {
            if (_onStepError is null)
                return;

            try
            {
                await _onStepError(_context, Name, ex).ConfigureAwait(false);
            }
            catch (Exception hookEx)
            {
                context.ReportHookFailure($"{Name}.onStepError", hookEx);
            }
        }

        private async Task TryNotifyAfterAsync(BlockContext context)
        {
            if (_onAfterStep is null)
                return;

            try
            {
                await _onAfterStep(_context, Name).ConfigureAwait(false);
            }
            catch (Exception hookEx)
            {
                context.ReportHookFailure($"{Name}.onAfterStep", hookEx);
            }
        }
    }
}
