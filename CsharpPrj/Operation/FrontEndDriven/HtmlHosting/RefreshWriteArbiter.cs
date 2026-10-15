#nullable enable

using System;
using Share.RefreshFramework;
using UiBackend.Abstractions;

namespace FrontEndDriven.HtmlHosting;

/// <summary>
/// 写仲裁适配器（L3 组合根）——把通用刷新框架单一状态源 <see cref="IRefreshStore"/> 适配为 plate 的
/// <see cref="IWriteArbiter"/> 端口（依赖倒置：plate 只依赖抽象，零上行引用）。
/// <para>写落地决策上移框架：<c>Stale</c>（版本过期）/ <c>Skip</c>（指纹未变）一律不触 DOM，
/// 消除「双生产者各持快照交替覆盖」（region/tree ping-pong）。</para>
/// </summary>
public sealed class RefreshWriteArbiter : IWriteArbiter
{
    private readonly IRefreshStore _store;

    public RefreshWriteArbiter(IRefreshStore store) => _store = store ?? throw new ArgumentNullException(nameof(store));

    /// <inheritdoc />
    public long NextVersion(string slotKey) => _store.NextVersion(slotKey);

    /// <inheritdoc />
    public WriteVerdict Decide(string slotKey, long version, string fingerprint)
        => _store.Decide(slotKey, version, fingerprint) switch
        {
            AcceptDecision.Apply => WriteVerdict.Apply,
            AcceptDecision.Skip => WriteVerdict.Skip,
            _ => WriteVerdict.Stale,
        };

    /// <inheritdoc />
    public void Commit(string slotKey, long version, string fingerprint) => _store.Commit(slotKey, version, fingerprint);
}
