#nullable enable

namespace UiBackend.Abstractions;

/// <summary>
/// 写仲裁裁决（plate 本地枚举，映射自通用刷新框架的 AcceptDecision）：
/// <list type="bullet">
/// <item><see cref="Apply"/> —— 可落地（触 DOM）。</item>
/// <item><see cref="Skip"/> —— 指纹未变（内容与上次相同），不触 DOM。</item>
/// <item><see cref="Stale"/> —— 版本过期（date 后的旧快照），不触 DOM。</item>
/// </list>
/// </summary>
public enum WriteVerdict
{
    Apply,
    Skip,
    Stale,
}

/// <summary>
/// 写仲裁端口（L0 契约，宿主注入）——每 slot 单调版本 + baseline 过期即丢 + 指纹去重。
/// <para>依赖倒置：plate 只依赖本抽象；实现由宿主（组合根）用通用刷新框架
/// <c>Strategy.Refresh.Ports.IRefreshStore</c> 适配注入（plate 零上行依赖）。</para>
/// <para>语义（不可协商）：<c>version &lt; baseline</c> ⇒ <see cref="WriteVerdict.Stale"/>；
/// <c>fingerprint == last</c> ⇒ <see cref="WriteVerdict.Skip"/>；其余 ⇒ <see cref="WriteVerdict.Apply"/>。
/// 仅当实际落地成功后才 <see cref="Commit"/> 推进 baseline。</para>
/// </summary>
public interface IWriteArbiter
{
    /// <summary>单调分配该 slot 的下一个版本号（同一状态源，唯一分配处）。</summary>
    long NextVersion(string slotKey);

    /// <summary>按判定规则给出版本/指纹裁决（判定处唯一）。</summary>
    WriteVerdict Decide(string slotKey, long version, string fingerprint);

    /// <summary>落地成功后推进 baseline 与 last 指纹（仅此一路写状态）。</summary>
    void Commit(string slotKey, long version, string fingerprint);
}

