using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Share.Conversation.Ids;

namespace Share.Conversation.Facts;

/// <summary>
/// 事实存储端口（方案 §2.3）——**只增不丢**（F2：无 update / delete API）；读侧按轮键取全轮。
/// <para>
/// 实现（按装配选择）：<c>JsonlSessionFactStore</c>（默认，人可读可取证）；
/// <c>SqliteSessionFactStore</c>（P2-10，SQL 聚合 / GUI 查询）；<c>CompositeSessionFactStore</c>（P2-10，双写 + 降级可见）。
/// </para>
/// </summary>
public interface ISessionFactStore
{
    /// <summary>追加一条事实（同文档内序号由 Writer 保证单调）。</summary>
    /// <param name="fact">事实条目。</param>
    /// <param name="ct">取消令牌。</param>
    Task AppendAsync(FactEnvelope fact, CancellationToken ct = default);

    /// <summary>批量追加（顺序即落盘顺序；同文档内序号仍由 Writer 分配）。</summary>
    /// <param name="facts">事实条目序列。</param>
    /// <param name="ct">取消令牌。</param>
    Task AppendManyAsync(IReadOnlyList<FactEnvelope> facts, CancellationToken ct = default);

    /// <summary>按归属键取该轮全部事实（跨 worker 归并由摄取器完成，读侧永不感知 worker）。</summary>
    /// <param name="key">归属键（轮级）。</param>
    /// <param name="ct">取消令牌。</param>
    Task<IReadOnlyList<FactEnvelope>> LoadAsync(FactId key, CancellationToken ct = default);

    /// <summary>列出已有文档键（倒序：最近轮在前；供取证与 GUI 列表）。</summary>
    /// <param name="limit">最多返回条数（&lt;=0 = 不限）。</param>
    /// <param name="ct">取消令牌。</param>
    Task<IReadOnlyList<FactId>> ListAsync(int limit = 50, CancellationToken ct = default);
}
