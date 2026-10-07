using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;
using Share.Conversation;

namespace Operation.DataStoreDriven.Core;

/// <summary>
/// 内存会话历史存储（<see cref="IHistoryStore"/>）：ur_history 的进程内承载；非 Sqlite 后端与测试使用，进程重启即失。
/// <para>
/// 只做「真实契约的内存落点」，不承担业务语义：不抛异常、缺键返回 null / 空列表，与 NotFound 判定解耦
/// （由被测组件决定）；与 <see cref="Sqlite.SqliteHistoryStore"/> 同契约、同口径。
/// </para>
/// </summary>
public sealed class InMemoryHistoryStore : IHistoryStore
{
    private readonly Dictionary<string, ConversationLog> _logs = new(StringComparer.Ordinal);

    /// <summary>已落库会话日志（键 = sessionId；读取视图，供对账 / 种子）。</summary>
    public IReadOnlyDictionary<string, ConversationLog> Logs => _logs;

    /// <summary>直接植入会话日志（不经 <see cref="UpsertAsync"/> 的种子路径）。</summary>
    public void Put(string sessionId, ConversationLog log) => _logs[sessionId] = log;

    /// <summary>会话是否已落库。</summary>
    public bool Contains(string sessionId) => _logs.ContainsKey(sessionId);

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task UpsertAsync(string sessionId, ConversationLog log, CancellationToken ct = default)
    {
        _logs[sessionId] = log;
        return Task.CompletedTask;
    }

    public Task<ConversationLog?> GetAsync(string sessionId, CancellationToken ct = default)
        => Task.FromResult(_logs.TryGetValue(sessionId, out var log) ? log : null);

    public Task<IReadOnlyList<string>> ListHistoryIdsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>(_logs.Keys.ToList());

    public Task<bool> DeleteAsync(string sessionId, CancellationToken ct = default)
        => Task.FromResult(_logs.Remove(sessionId));
}
