using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Modules.Node.Identity;
using Share.Conversation;
using Share.Conversation.Aggregates;
using Share.Conversation.Ids;
using Share.Conversation.Ports;

namespace Operation.DataStoreDriven.Core;

/// <summary>内存历史根注册表（测试替身；与 Sqlite 实现同契约）。</summary>
public sealed class InMemoryHistoryRootStore : IHistoryRootStore
{
    private readonly Dictionary<string, HistoryRoot> _roots = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, HistoryRoot> Roots => _roots;

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<IReadOnlyList<HistoryRoot>> ListRootsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<HistoryRoot>>(_roots.Values.OrderBy(r => r.Key.Value, StringComparer.Ordinal).ToList());

    public Task<bool> ExistsAsync(HistoryKey key, CancellationToken ct = default)
        => Task.FromResult(!key.IsEmpty && _roots.ContainsKey(Canonical(key).Value));

    public Task<HistoryRoot?> GetAsync(HistoryKey key, CancellationToken ct = default)
        => Task.FromResult(!key.IsEmpty && _roots.TryGetValue(Canonical(key).Value, out var root) ? root : null);

    public Task CreateAsync(HistoryRoot root, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(root);
        // D-01=A：内存替身与 Sqlite 同契约（HistoryKey 规范面 = 裸身份；行键只在存储边界出现）。
        var key = Canonical(root.Key);
        var name = string.Equals(root.Name, root.Key.Value, StringComparison.Ordinal) ? key.Value : root.Name;
        _roots[key.Value] = root with { Key = key, Name = name };
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(HistoryKey key, CancellationToken ct = default)
        => Task.FromResult(!key.IsEmpty && _roots.Remove(Canonical(key).Value));

    /// <summary>身份归一（单点；空/仅前缀 ⇒ 原样，保持「未命中 ≠ 不存在」）。</summary>
    private static HistoryKey Canonical(HistoryKey key)
    {
        if (key.IsEmpty) return key;
        var identity = NodeEndpointIds.NormalizeHistoryId(key.Value);
        return identity.Length == 0 ? key : HistoryKey.Create(identity);
    }
}

/// <summary>内存会话注册表（测试替身）。</summary>
public sealed class InMemorySessionStore : ISessionStore
{
    private readonly Dictionary<string, SessionRecord> _sessions = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, SessionRecord> Sessions => _sessions;

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<SessionRecord?> GetAsync(SessionKey key, CancellationToken ct = default)
        => Task.FromResult(!key.IsEmpty && _sessions.TryGetValue(key.Value, out var s) ? s : null);

    public Task<IReadOnlyList<SessionRecord>> ListByOwnerAsync(SessionOwner owner, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<SessionRecord>>(_sessions.Values
            .Where(s => s.Owner.Kind == owner.Kind && string.Equals(s.Owner.Key, owner.Key, StringComparison.Ordinal))
            .ToList());

    public Task CreateAsync(SessionRecord session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        _sessions[session.Key.Value] = session;
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(SessionKey key, CancellationToken ct = default)
        => Task.FromResult(!key.IsEmpty && _sessions.Remove(key.Value));
}

/// <summary>内存会话日志（测试替身）。</summary>
public sealed class InMemorySessionTranscriptStore : ISessionTranscriptStore
{
    private readonly Dictionary<string, ConversationLog> _logs = new(StringComparer.Ordinal);

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<ConversationLog?> GetAsync(SessionKey key, CancellationToken ct = default)
        => Task.FromResult(!key.IsEmpty && _logs.TryGetValue(key.Value, out var log) ? log : null);

    public Task AppendProjectionAsync(SessionKey key, ConversationLog log, long watermark, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(log);
        _logs[key.Value] = log;
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(SessionKey key, CancellationToken ct = default)
        => Task.FromResult(!key.IsEmpty && _logs.Remove(key.Value));

    public Task<int> CountMessagesAsync(SessionKey key, CancellationToken ct = default)
        => Task.FromResult(!key.IsEmpty && _logs.TryGetValue(key.Value, out var log) ? log.Messages.Count : 0);

    public async Task<bool> IsEmptyAsync(SessionKey key, CancellationToken ct = default)
        => await CountMessagesAsync(key, ct).ConfigureAwait(false) == 0;
}
