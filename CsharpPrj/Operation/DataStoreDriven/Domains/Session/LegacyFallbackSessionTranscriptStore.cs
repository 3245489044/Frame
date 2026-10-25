using System;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Core;
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
    private readonly FallbackReadThroughStore<SessionKey, ConversationLog> _fallback;

    public LegacyFallbackSessionTranscriptStore(
        ISessionTranscriptStore primary,
        IHistoryStore legacy)
    {
        _primary = primary ?? throw new ArgumentNullException(nameof(primary));
        _legacy = legacy ?? throw new ArgumentNullException(nameof(legacy));
        // U7：双读/双删收敛到通用回退装饰器（唯一实现）。
        _fallback = new FallbackReadThroughStore<SessionKey, ConversationLog>(
            primaryRead: (key, ct) => _primary.GetAsync(key, ct),
            fallbackRead: (key, ct) => _legacy.GetAsync(key.Value, ct),
            primaryDelete: (key, ct) => _primary.DeleteAsync(key, ct),
            fallbackDelete: (key, ct) => _legacy.DeleteAsync(key.Value, ct));
    }

    public Task InitializeAsync(CancellationToken ct = default) => _primary.InitializeAsync(ct);

    public Task<ConversationLog?> GetAsync(SessionKey key, CancellationToken ct = default)
        => _fallback.GetAsync(key, ct);

    public Task AppendProjectionAsync(SessionKey key, ConversationLog log, long watermark, CancellationToken ct = default)
        => _primary.AppendProjectionAsync(key, log, watermark, ct);

    public Task<bool> DeleteAsync(SessionKey key, CancellationToken ct = default)
        => _fallback.DeleteAsync(key, ct);

    /// <summary>消息数：与 <see cref="GetAsync"/> 同源（新表优先，未命中回退旧表）。</summary>
    public async Task<int> CountMessagesAsync(SessionKey key, CancellationToken ct = default)
    {
        var log = await GetAsync(key, ct).ConfigureAwait(false);
        return log?.Messages.Count ?? 0;
    }

    public async Task<bool> IsEmptyAsync(SessionKey key, CancellationToken ct = default)
        => await CountMessagesAsync(key, ct).ConfigureAwait(false) == 0;
}
