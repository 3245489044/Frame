using System.Collections.Concurrent;

namespace Strategy.Observability.Entity;

/// <summary>
/// 默认序号源：进程内 ConcurrentDictionary，同 scopeId+entityType 单调递增（E1）。
/// 仅引擎侧单一入口分配，不解释实体含义。
/// </summary>
public sealed class EntityIdFactory : IEntitySequenceSource
{
    private readonly ConcurrentDictionary<string, int> _seq = new(StringComparer.Ordinal);

    public EntityId New(string scopeId, string entityType)
    {
        var key = scopeId + "\u001f" + entityType;
        var seq = _seq.AddOrUpdate(key, 1, (_, c) => c + 1);
        return new EntityId(scopeId, entityType, seq);
    }
}
