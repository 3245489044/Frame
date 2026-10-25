using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Share.Conversation.Aggregates;
using Share.Conversation.Ids;
using Share.Conversation.Ports;

namespace Operation.HierarchyMethod.History;

/// <summary>
/// 历史根目录唯一实现：**只认 root store**（结构事实 = 根注册表行）；旧日志表不再参与根判定。
/// 过滤空键 / 去重 / 排序在此单点完成，UI / API / 会话页 / 树读取全部委托本类。
/// </summary>
public sealed class HistoryRootCatalog : IHistoryRootCatalog
{
    private readonly IHistoryRootStore _roots;

    /// <summary>root store 为必填（未装配即构造期失败，禁「全表即根」的静默伪根）。</summary>
    public HistoryRootCatalog(IHistoryRootStore roots)
        => _roots = roots ?? throw new ArgumentNullException(nameof(roots));

    /// <summary>最近一次根枚举的可见化原因：<c>ok</c> / <c>root-store-empty</c>（空树与装配失败不再同形）。</summary>
    public string? LastResolveReason { get; private set; }

    /// <summary>最近一次根枚举的原始根数（归一前）。</summary>
    public int LastRootCount { get; private set; }

    public async Task<IReadOnlyList<HistoryRoot>> ListAsync(CancellationToken ct = default)
    {
        var roots = await _roots.ListRootsAsync(ct).ConfigureAwait(false);
        LastRootCount = roots?.Count ?? 0;
        LastResolveReason = LastRootCount == 0 ? "root-store-empty" : "ok";
        return Normalize(roots ?? Array.Empty<HistoryRoot>());
    }

    public Task<bool> ExistsAsync(HistoryKey key, CancellationToken ct = default)
        => key.IsEmpty ? Task.FromResult(false) : _roots.ExistsAsync(key, ct);

    private static IReadOnlyList<HistoryRoot> Normalize(IEnumerable<HistoryRoot> roots)
        => roots
            .Where(r => !r.Key.IsEmpty)
            .GroupBy(r => r.Key.Value, StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderBy(r => r.Key.Value, StringComparer.Ordinal)
            .ToList();
}
