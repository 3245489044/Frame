#nullable enable

using Modules.Maintain.Ports;

namespace Modules.Maintain.Workspace
{
    /// <summary>
    /// 通用内存状态存储：以字典维护 <c>状态 id → 快照</c>，供测试与演示使用。
    /// 生产场景可实现 <see cref="IMaintainStateStore{TData}"/> 背靠数据库 / ur_config 等。
    /// </summary>
    public sealed class InMemoryStateStore<TData> : IMaintainStateStore<TData>
    {
        private readonly Dictionary<string, IReadOnlyList<TData>> _states = new();

        public string Save(IReadOnlyList<TData> snapshot, string revision)
        {
            var stateId = Guid.NewGuid().ToString("N");
            _states[stateId] = snapshot;
            return stateId;
        }

        public IReadOnlyList<TData>? Restore(string stateId)
            => _states.TryGetValue(stateId, out var s) ? s : null;

        public void Delete(string stateId)
            => _states.Remove(stateId);
    }
}