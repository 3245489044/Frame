
#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BasicMethod.Block
{

    public interface IBlock
    {
        string Name { get; }

        string Description { get; }

        Task<BlockResult> ExecuteAsync(BlockContext context, CancellationToken cancellationToken = default);
    }


    public interface IBlockPipeline
    {
        string Name { get; }

        string Description { get; }

        IReadOnlyList<IBlock> Blocks { get; }

        IBlockPipeline AddBlock(IBlock block);

        IBlockPipeline AddBlock<T>() where T : IBlock, new();

        IBlockPipeline InsertBefore(string name, IBlock block);

        IBlockPipeline InsertAfter(string name, IBlock block);

        IBlockPipeline Remove(string name);

        void Clear();

        Task<PipelineResult> RunAsync(BlockContext? context = null, CancellationToken cancellationToken = default);
    }


    public interface IBlockDiagnosticProvider
    {
        bool PreDiagnostic(BlockContext context);

        void PostDiagnostic(BlockContext context, BlockResult result);
    }


    public interface IBlockCondition
    {
        bool ShouldExecute(BlockContext context);
    }


    public interface IBlockRetry
    {
        int MaxRetries { get; }

        int RetryDelayMs { get; }

        bool ShouldRetry(BlockContext context, BlockResult result, int attempt);
    }
}
