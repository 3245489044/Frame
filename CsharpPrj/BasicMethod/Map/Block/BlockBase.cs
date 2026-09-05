
#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BasicMethod.Block
{

    public abstract class BlockBase : IBlock
    {
        public string Name { get; }

        public string Description { get; }

        protected BlockBase(string name, string? description = null)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Description = description ?? string.Empty;
        }

        public abstract Task<BlockResult> ExecuteAsync(BlockContext context, CancellationToken cancellationToken = default);
    }


    public class DelegateBlock : BlockBase
    {
        private readonly Func<BlockContext, CancellationToken, Task<BlockResult>> _executeFunc;

        public DelegateBlock(
            string name,
            Func<BlockContext, CancellationToken, Task<BlockResult>> executeFunc,
            string? description = null)
            : base(name, description)
        {
            _executeFunc = executeFunc ?? throw new ArgumentNullException(nameof(executeFunc));
        }

        public DelegateBlock(
            string name,
            Func<BlockContext, BlockResult> executeFunc,
            string? description = null)
            : base(name, description)
        {
            ArgumentNullException.ThrowIfNull(executeFunc);
            _executeFunc = (ctx, ct) => Task.FromResult(executeFunc(ctx));
        }

        public override Task<BlockResult> ExecuteAsync(BlockContext context, CancellationToken cancellationToken = default)
            => _executeFunc(context, cancellationToken);
    }


    public abstract class ConditionalBlockBase : BlockBase, IBlockCondition
    {
        protected ConditionalBlockBase(string name, string? description = null)
            : base(name, description) { }

        public abstract bool ShouldExecute(BlockContext context);
    }


    public abstract class RetryBlockBase : BlockBase, IBlockRetry
    {
        public int MaxRetries { get; set; } = 3;

        public int RetryDelayMs { get; set; } = 500;

        protected RetryBlockBase(string name, string? description = null)
            : base(name, description) { }

        public virtual bool ShouldRetry(BlockContext context, BlockResult result, int attempt)
            => !result.IsSuccess && attempt < MaxRetries;
    }


    public abstract class DiagnosticBlockBase : BlockBase, IBlockDiagnosticProvider
    {
        protected DiagnosticBlockBase(string name, string? description = null)
            : base(name, description) { }

        public virtual bool PreDiagnostic(BlockContext context) => true;

        public virtual void PostDiagnostic(BlockContext context, BlockResult result) { }
    }
}
