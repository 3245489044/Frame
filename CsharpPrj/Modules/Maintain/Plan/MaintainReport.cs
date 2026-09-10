#nullable enable

namespace Modules.Maintain.Plan
{
    /// <summary>维护/迁移/状态操作种类。</summary>
    public enum MaintainOperationKind
    {
        Upsert,
        Remove,
        Merge,
        Prune,
        Rename,
        Remap,
        Snapshot,
        Revert,
    }

    /// <summary>单条变更记录（统一审计载体）。</summary>
    public sealed record ChangeEntry(
        MaintainOperationKind Kind,
        string? OldKey = null,
        string? NewKey = null,
        string? StateId = null,
        string? Message = null)
    {
        public DateTime CreatedAt { get; } = DateTime.UtcNow;
    }

    /// <summary>
    /// 维护/迁移管线执行结果：登记每次操作的变更条目并聚合统计。
    /// 取代 <c>MergeResult</c> 在门面层的直接使用，作为唯一审计/回滚载体。
    /// </summary>
    public sealed class MaintainReport
    {
        private readonly List<ChangeEntry> _entries = new();

        public IReadOnlyList<ChangeEntry> Entries => _entries;

        public void Add(ChangeEntry entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            _entries.Add(entry);
        }

        public void AddPair(MaintainOperationKind kind, string? oldKey, string? newKey, string? message = null)
            => _entries.Add(new ChangeEntry(kind, oldKey, newKey, Message: message));

        public int Count(MaintainOperationKind kind)
            => _entries.Count(e => e.Kind == kind);

        public int Renamed => Count(MaintainOperationKind.Rename);
        public int Remapped => Count(MaintainOperationKind.Remap);
        public int Removed => Count(MaintainOperationKind.Remove);
        public int Merged => Count(MaintainOperationKind.Merge);
        public int Pruned => Count(MaintainOperationKind.Prune);
        public int Upserted => Count(MaintainOperationKind.Upsert);
    }
}