using System;
using System.Threading;
using System.Threading.Tasks;
using Share.Conversation;
using Share.Conversation.Ids;
using Share.Conversation.Ports;

namespace Operation.DataStoreDriven.Sqlite;

/// <summary>
/// 迁移期唯一双读点（DUP-6）：新表优先，未命中回退旧 ur_histories；写入只走新表。
/// 上层（block / projection / pipeline / view）不得再各自写回退逻辑。
/// </summary>
public sealed class LegacyFallbackSessionTranscriptStore : ISessionTranscriptStore
{
    private readonly ISessionTranscriptStore _primary;
    private readonly IHistoryStore _legacy;

    public LegacyFallbackSessionTranscriptStore(
        ISessionTranscriptStore primary,
        IHistoryStore legacy)
    {
        _primary = primary ?? throw new ArgumentNullException(nameof(primary));
        _legacy = legacy ?? throw new ArgumentNullException(nameof(legacy));
    }

    public Task InitializeAsync(CancellationToken ct = default) => _primary.InitializeAsync(ct);

    public async Task<ConversationLog?> GetAsync(SessionKey key, CancellationToken ct = default)
    {
        var log = await _primary.GetAsync(key, ct).ConfigureAwait(false);
        if (log is not null) return log;

        return await _legacy.GetAsync(key.Value, ct).ConfigureAwait(false);
    }

    public Task AppendProjectionAsync(SessionKey key, ConversationLog log, long watermark, CancellationToken ct = default)
        => _primary.AppendProjectionAsync(key, log, watermark, ct);

    public async Task<bool> DeleteAsync(SessionKey key, CancellationToken ct = default)
    {
        var removed = await _primary.DeleteAsync(key, ct).ConfigureAwait(false);
        // 迁移期：新表未命中时旧表行也要清，避免“删除后仍从回退读到”。
        var legacyRemoved = await _legacy.DeleteAsync(key.Value, ct).ConfigureAwait(false);
        return removed || legacyRemoved;
    }
}
