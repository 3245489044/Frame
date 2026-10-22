#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BasicMethod.Block
{
    /// <summary>
    /// 新内核管线：装配 + 单一执行器。装配错误 fail-fast（E14）。
    /// </summary>
    public class BlockPipeline
    {
        protected readonly List<IBlock> _blocks;
        private readonly HashSet<string> _names = new(StringComparer.Ordinal);

        public BlockPipeline(string name, string? description = null)
            : this(name, description, null)
        {
        }

        internal BlockPipeline(string name, string? description, List<IBlock>? storage)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Description = description ?? string.Empty;
            _blocks = storage ?? new List<IBlock>();

            foreach (var block in _blocks)
                _names.Add(block.Name);
        }

        public string Name { get; }

        public string Description { get; }

        public IReadOnlyList<IBlock> Blocks => _blocks.AsReadOnly();

        public virtual BlockPipeline Add(IBlock block)
        {
            ArgumentNullException.ThrowIfNull(block);
            EnsureUniqueName(block.Name);
            _blocks.Add(block);
            _names.Add(block.Name);
            return this;
        }

        public BlockPipeline Add<T>() where T : IBlock, new()
            => Add(new T());

        /// <summary>兼容命名：等价于 <see cref="Add"/>。</summary>
        public BlockPipeline AddBlock(IBlock block) => Add(block);

        public BlockPipeline InsertBefore(string name, IBlock block)
        {
            ArgumentNullException.ThrowIfNull(block);
            ArgumentException.ThrowIfNullOrWhiteSpace(name);

            var index = FindIndex(name);
            if (index < 0)
                throw new KeyNotFoundException($"Cannot find block '{name}' to insert before.");

            EnsureUniqueName(block.Name);
            _blocks.Insert(index, block);
            _names.Add(block.Name);
            return this;
        }

        public BlockPipeline InsertAfter(string name, IBlock block)
        {
            ArgumentNullException.ThrowIfNull(block);
            ArgumentException.ThrowIfNullOrWhiteSpace(name);

            var index = FindIndex(name);
            if (index < 0)
                throw new KeyNotFoundException($"Cannot find block '{name}' to insert after.");

            EnsureUniqueName(block.Name);
            _blocks.Insert(index + 1, block);
            _names.Add(block.Name);
            return this;
        }

        public BlockPipeline Remove(string name)
        {
            var index = FindIndex(name);
            if (index >= 0)
            {
                _names.Remove(_blocks[index].Name);
                _blocks.RemoveAt(index);
            }
            return this;
        }

        public void Clear()
        {
            _blocks.Clear();
            _names.Clear();
        }

        public Task<BlockRunResult> RunAsync(
            BlockContext? context = null,
            CancellationToken cancellationToken = default)
            => BlockRunner.Shared.RunAsync(_blocks, context, defaultIsFatal: true, cancellationToken);

        internal Task<BlockRunResult> RunLegacyAsync(
            BlockContext? context,
            bool defaultIsFatal,
            CancellationToken cancellationToken)
            => BlockRunner.Shared.RunAsync(_blocks, context, defaultIsFatal, cancellationToken);

        private int FindIndex(string name)
        {
            for (var i = 0; i < _blocks.Count; i++)
            {
                if (string.Equals(_blocks[i].Name, name, StringComparison.Ordinal))
                    return i;
            }
            return -1;
        }

        private void EnsureUniqueName(string name)
        {
            if (_names.Contains(name))
                throw new InvalidOperationException($"Duplicate block name '{name}'.");
        }
    }
}
