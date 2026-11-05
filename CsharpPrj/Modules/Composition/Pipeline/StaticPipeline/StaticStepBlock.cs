#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using BasicMethod.Block;

namespace Modules.Pipeline.StaticPipeline
{
    /// <summary>把静态步骤适配成内核块（钩子异常独立记账，E6）。</summary>
    internal sealed class StaticStepBlock<TContext> : IBlock
    {
        private readonly Step<TContext> _step;
        private readonly TContext _context;
        private readonly Action<TContext>? _onBeforeStep;
        private readonly Action<TContext, string>? _onAfterStep;
        private readonly Action<TContext, string, Exception>? _onStepError;

        public StaticStepBlock(
            Step<TContext> step,
            TContext context,
            Action<TContext>? onBeforeStep,
            Action<TContext, string>? onAfterStep,
            Action<TContext, string, Exception>? onStepError)
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

        public Task<BlockResult> ExecuteAsync(
            BlockContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                _onBeforeStep?.Invoke(_context);
                _step.Execute(_context);
            }
            catch (Exception ex)
            {
                TryNotifyError(context, ex);
                return Task.FromResult(BlockResult.Fail(ex.Message, ex));
            }

            TryNotifyAfter(context);
            return Task.FromResult(BlockResult.Ok());
        }

        private void TryNotifyError(BlockContext context, Exception ex)
        {
            if (_onStepError is null)
                return;

            try
            {
                _onStepError(_context, Name, ex);
            }
            catch (Exception hookEx)
            {
                context.ReportHookFailure($"{Name}.onStepError", hookEx);
            }
        }

        private void TryNotifyAfter(BlockContext context)
        {
            if (_onAfterStep is null)
                return;

            try
            {
                _onAfterStep(_context, Name);
            }
            catch (Exception hookEx)
            {
                context.ReportHookFailure($"{Name}.onAfterStep", hookEx);
            }
        }
    }
}
