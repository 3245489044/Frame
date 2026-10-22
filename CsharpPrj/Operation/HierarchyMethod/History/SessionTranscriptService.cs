using System;
using System.Threading;
using System.Threading.Tasks;
using Operation.HierarchyMethod.Abstractions;
using Share.Foundations.Diagnostics;
using Share.Conversation;
using Share.Conversation.Ids;
using Share.Conversation.Ports;

namespace Operation.HierarchyMethod.History;

/// <summary>
/// 会话日志聚拢点（C3/C4）：唯一写入口 = append projection；唯一读入口 = transcript store。
/// 不再触碰 ur_histories；旧日志双读由 store 内部完成并可见化。
/// </summary>
public sealed class SessionTranscriptService
{
    private readonly ISessionTranscriptStore _store;
    private readonly IDiagnosticsSink _diagnostics;

    public SessionTranscriptService(ISessionTranscriptStore store, IDiagnosticsSink? diagnostics = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _diagnostics = diagnostics ?? NullDiagnosticsSink.Instance;
    }

    public Task<ConversationLog?> GetAsync(string sessionKey, CancellationToken ct = default)
    {
        if (!SessionKey.TryCreate(sessionKey, out var key)) return Task.FromResult<ConversationLog?>(null);
        return _store.GetAsync(key, ct);
    }

    public async Task AppendAsync(string sessionKey, ConversationLog log, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(log);
        if (!SessionKey.TryCreate(sessionKey, out var key))
        {
            _diagnostics.Report("SessionTranscript_invalid_key", new { sessionKey });
            return;
        }

        try
        {
            await _store.AppendProjectionAsync(key, log, log.NextRound, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _diagnostics.Report("SessionTranscript_append_failed", new { sessionKey, error = ex.Message });
            throw;
        }
    }
}
