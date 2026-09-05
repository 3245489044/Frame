using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;

namespace Agent.Core.Tests.TestSupport
{
    /// <summary>测试用层级引用桩（IHierarchyReferenceManager 内存实现）：任务→节点键集固定返回。</summary>
    internal sealed class FakeHierarchy : IHierarchyReferenceManager
    {
        private readonly IReadOnlyList<string> _taskNodeKeys;
        private readonly IReadOnlyList<string>? _historyTaskKeys;

        public FakeHierarchy(IReadOnlyList<string> taskNodeKeys, IReadOnlyList<string>? historyTaskKeys = null)
        {
            _taskNodeKeys = taskNodeKeys ?? Array.Empty<string>();
            _historyTaskKeys = historyTaskKeys;
        }

        public Task EnsureHistoryAsync(string historyId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> DeleteHistoryAsync(string historyId, CancellationToken ct = default) => Task.FromResult(false);
        public Task AttachHistoryTaskAsync(string historyId, string taskKey, CancellationToken ct = default) => Task.CompletedTask;
        public Task DetachHistoryTaskAsync(string historyId, string taskKey, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<string>> GetHistoryTaskKeysAsync(string historyId, CancellationToken ct = default)
            => Task.FromResult(_historyTaskKeys ?? (IReadOnlyList<string>)Array.Empty<string>());
        public Task LinkTaskNodeAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct = default) => Task.CompletedTask;
        public Task UnlinkTaskNodeAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<string>> GetTaskNodeKeysAsync(string taskKey, string workflowId, CancellationToken ct = default)
            => Task.FromResult(_taskNodeKeys);
        public Task<IReadOnlyList<string>> GetHistoryNodeKeysAsync(string historyId, string workflowId, CancellationToken ct = default)
            => Task.FromResult(_taskNodeKeys);
    }
}
