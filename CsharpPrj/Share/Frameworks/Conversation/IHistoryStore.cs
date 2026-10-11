using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Share.Conversation;

/// <summary>
/// 会话历史容器存储端口 —— 以 <see cref="ConversationLog"/> 为统一存储模型（History 空壳已废弃，无往返）。
/// 写入整条会话日志（幂等 upsert，SnapshotJson = ConversationLog 全量序列化）；读取返回 ConversationLog。
/// </summary>
public interface IHistoryStore
{
    /// <summary>幂等建表（ur_histories）。</summary>
    Task InitializeAsync(CancellationToken ct = default);

    /// <summary>幂等写整条会话日志（会话 ID 维度 upsert）。</summary>
    Task UpsertAsync(string sessionId, ConversationLog log, CancellationToken ct = default);

    /// <summary>按会话 ID 读取会话日志；不存在返回 null。</summary>
    Task<ConversationLog?> GetAsync(string sessionId, CancellationToken ct = default);

    /// <summary>列出全部会话 ID（供 UI 会话下拉）。</summary>
    Task<IReadOnlyList<string>> ListHistoryIdsAsync(CancellationToken ct = default);

    /// <summary>有界枚举会话 ID（<paramref name="limit"/> ≤ 0 ⇒ 全量）—— E5：全表无界的**下推出口**。
    /// 缺省实现 = 全量后 Take（不改既有实现/测试替身）；存储实现宜覆写为下推（SQL LIMIT）。</summary>
    async Task<IReadOnlyList<string>> ListHistoryIdsAsync(int limit, CancellationToken ct = default)
    {
        var all = await ListHistoryIdsAsync(ct).ConfigureAwait(false);
        return limit > 0 ? all.Take(limit).ToList() : all;
    }

    /// <summary>删除会话（级联清其引用）。返回是否命中删除。</summary>
    Task<bool> DeleteAsync(string sessionId, CancellationToken ct = default);
}
