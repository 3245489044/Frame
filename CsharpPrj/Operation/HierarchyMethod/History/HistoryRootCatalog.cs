using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Share.Conversation;
using Share.Conversation.Aggregates;
using Share.Conversation.Ids;
using Share.Conversation.Ports;

namespace Operation.HierarchyMethod.History;

/// <summary>
/// 历史根目录唯一实现（DUP-1）：root store 优先；旧 ur_histories 行只作迁移期回退。
/// 过滤空键 / 去重 / 排序在此单点完成，UI / API / 会话页 / 树读取全部委托本类。
/// </summary>
public sealed class HistoryRootCatalog : IHistoryRootCatalog
{
    private readonly IHistoryRootStore? _roots;
    private readonly IHistoryStore? _legacy;
    private readonly ILegacyHistoryClassifier _classifier;

    public HistoryRootCatalog(
        IHistoryRootStore? roots,
        IHistoryStore? legacy = null,
        ILegacyHistoryClassifier? classifier = null)
    {
        _roots = roots;
        _legacy = legacy;
        _classifier = classifier ?? NullLegacyHistoryClassifier.Instance;
    }

    public async Task<IReadOnlyList<HistoryRoot>> ListAsync(CancellationToken ct = default)
    {
        if (_roots is not null)
        {
            var roots = await _roots.ListRootsAsync(ct).ConfigureAwait(false);
            return Normalize(roots);
        }

        if (_legacy is null) return Array.Empty<HistoryRoot>();

        var ids = await _legacy.ListHistoryIdsAsync(ct).ConfigureAwait(false);
        var list = new List<HistoryRoot>();
        foreach (var id in ids)
        {
            if (string.IsNullOrWhiteSpace(id) || !_classifier.IsLegacyRoot(id)) continue;
            list.Add(HistoryRoot.Create(HistoryKey.Create(id), id, HistoryKind.Imported, "legacy"));
        }
        return Normalize(list);
    }

    public async Task<bool> ExistsAsync(HistoryKey key, CancellationToken ct = default)
    {
        if (key.IsEmpty) return false;
        if (_roots is not null)
            return await _roots.ExistsAsync(key, ct).ConfigureAwait(false);
        if (_legacy is null) return false;
        var ids = await _legacy.ListHistoryIdsAsync(ct).ConfigureAwait(false);
        return ids.Any(id => string.Equals(id, key.Value, StringComparison.Ordinal));
    }

    private static IReadOnlyList<HistoryRoot> Normalize(IEnumerable<HistoryRoot> roots)
        => roots
            .Where(r => !r.Key.IsEmpty)
            .GroupBy(r => r.Key.Value, StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderBy(r => r.Key.Value, StringComparer.Ordinal)
            .ToList();
}
