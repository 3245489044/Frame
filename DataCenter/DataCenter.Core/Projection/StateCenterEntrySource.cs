using DataCenter.Abstractions.Cells;
using DataCenter.Protocol.Projection;
using Operation.StateBlock.Core;
using Share.StateManagement;

namespace Operation.StateBlock.Projection;

/// <summary>
/// 状态枚举接缝的默认实现 —— 从 <see cref="StateCenter"/> 的端口真源（根单元 + 各 scope 分区子单元）
/// 读出分层条目；只读，不创建 scope（查询不产生副作用）。
/// </summary>
public sealed class StateCenterEntrySource : IStateEntrySource
{
    private static readonly DataTier[] AllTiers = [DataTier.Config, DataTier.Session, DataTier.Transient];

    private readonly IDataCell _root;
    private readonly IStateManager _manager;

    public StateCenterEntrySource(StateCenter center)
    {
        ArgumentNullException.ThrowIfNull(center);
        _root = center.Root;
        _manager = center;
    }

    public StateEntryScan Enumerate(string? scope)
    {
        var entries = new List<StateEntry>();
        var unparsed = 0;

        // 唯一分层真源有两处载体，二者互不可见，须一并枚举：
        // ① 根单元 —— StateCenter.Write 按「scope 是键的一段」写 ns:scope:key（解析为图坐标）；
        // ② scope 分区子单元 —— IStateManager 的 store API / BeginTransaction(scope) 写原始键
        //    （无 ns:scope: 前缀 → 计入 unparsedKeys 的显式缺口，不静默吞掉，见 StateEntryScan 契约）。
        // scope 过滤在条目级进行（不靠选单元）。
        Collect(_root, entries, ref unparsed);
        foreach (var name in _manager.GetScopeNames())
        {
            if (_manager.GetScope(name) is StateStore store)
                Collect(store.DataCell, entries, ref unparsed);
        }

        if (string.IsNullOrWhiteSpace(scope))
            return new StateEntryScan(entries, unparsed);

        var filteredEntries = entries
            .Where(e => string.Equals(e.Scope, scope, StringComparison.Ordinal))
            .ToList();

        // 不可解析键无 scope 归属，故不随过滤变化（缺口计数保持全局口径）
        return new StateEntryScan(filteredEntries, unparsed);
    }

    private static void Collect(IDataCell cell, List<StateEntry> sink, ref int unparsed)
    {
        foreach (var tier in AllTiers)
        {
            foreach (var entry in cell.GetEntries(tier))
            {
                if (!StateUriMapper.TrySplitFullKey(entry.Key, out var ns, out var entryScope, out var key))
                {
                    unparsed++;
                    continue;
                }

                sink.Add(new StateEntry(ns, entryScope, key, tier, entry.Value));
            }
        }
    }
}
