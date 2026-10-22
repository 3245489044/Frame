#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BasicMethod.Block
{
    /// <summary>
    /// 旧管线公共签名兼容壳：内部委托 <see cref="BlockRunner"/>（唯一执行循环）。
    /// 保留 <see cref="FailFast"/> 旧语义：false = 非致命失败继续执行。
    /// </summary>
    [Obsolete("使用 BlockPipeline（返回 BlockRunResult）。本类型仅作一个版本周期的兼容壳。")]
    public class ExecutionPipeline : IBlockPipeline
    {
        protected readonly List<IBlock> _blocks = new();
        private readonly BlockPipeline _kernel;

        public ExecutionPipeline(string name, string? description = null)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Description = description ?? string.Empty;
            _kernel = new BlockPipeline(Name, Description, _blocks);
        }

        public string Name { get; }

        public string Description { get; }

        public IReadOnlyList<IBlock> Blocks => _blocks.AsReadOnly();

        /// <summary>旧语义：true = 致命失败短路；false = 失败继续（默认）。</summary>
        public bool FailFast { get; set; }

        public IBlockPipeline AddBlock(IBlock block)
        {
            _kernel.Add(block);
            return this;
        }

        public IBlockPipeline AddBlock<T>() where T : IBlock, new()
        {
            _kernel.Add(new T());
            return this;
        }

        public IBlockPipeline InsertBefore(string name, IBlock block)
        {
            _kernel.InsertBefore(name, block);
            return this;
        }

        public IBlockPipeline InsertAfter(string name, IBlock block)
        {
            _kernel.InsertAfter(name, block);
            return this;
        }

        public IBlockPipeline Remove(string name)
        {
            _kernel.Remove(name);
            return this;
        }

        public void Clear() => _kernel.Clear();

        public async Task<PipelineResult> RunAsync(
            BlockContext? context = null,
            CancellationToken cancellationToken = default)
        {
            var result = await _kernel
                .RunLegacyAsync(context, defaultIsFatal: FailFast, cancellationToken)
                .ConfigureAwait(false);

            return PipelineResult.From(result);
        }
    }
}
