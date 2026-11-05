namespace Modules.Sync.Contracts;

/// <summary>
/// 同步协议 v1 的冻结常量与 tick 语义（唯一声明点）。
/// <para>
/// <b>tick 语义：</b><c>SimHz = 20</c>、<c>DecisionHz = 10</c>；<c>DecisionTick = SimTick / 2</c>，从 0 单调递增。
/// 所有消息的 <c>Tick</c> 字段一律是<b>决策 tick</c>，不是挂钟时间、不是渲染帧号。
/// </para>
/// <para>
/// <b>一个决策 tick 的固定执行顺序（不可换序）：</b>
/// <list type="number">
/// <item>应用本 tick 的输入/接管（按 <c>TargetTick → Kind → CommandId</c> 的确定性全序）；</item>
/// <item>Sync 恢复；</item>
/// <item>ATB 充能；</item>
/// <item>PP 被动回复；</item>
/// <item>生成可见前摇线索；</item>
/// <item>3 条规则自上而下短路求值；</item>
/// <item>伤害结算与保护裁决；</item>
/// <item>生成快照并下发（同 tick 恰好一条）。</item>
/// </list>
/// 顺序不一致的违约表现：双端 <c>StateHash</c> 在首个分歧 tick 起持续不一致（<c>sync.hashMismatch</c> 增长）。
/// </para>
/// <para>
/// <b>禁止：</b>协议内核不得使用 <c>DateTime</c>、<c>Guid</c>、<c>Random</c>、<c>Environment.TickCount</c>；
/// 需要耗时只允许 <c>Stopwatch.GetTimestamp()</c>（观测用，不参与协议语义）。
/// </para>
/// </summary>
public static class SyncProtocol
{
    /// <summary>协议版本。唯一声明点；握手两端必须相等（不等即 <see cref="RejectReason.VersionMismatch"/>）。</summary>
    public const int ProtocolVersion = 1;

    /// <summary>输入窗口宽度（含端点）：<c>current &lt;= TargetTick &lt;= current + InputWindowTicks</c>。</summary>
    public const int InputWindowTicks = 2;

    /// <summary>规则表最大条数；短路求值最多遍历该条数。</summary>
    public const int MaxRules = 3;

    /// <summary>技能槽位数（投影固定 4 个 PP 字段）。</summary>
    public const int SkillSlots = 4;

    /// <summary>进程内传输单方向队列容量（满即拒绝入队，不无界增长）。</summary>
    public const int TransportQueueCapacity = 1024;

    /// <summary>血量千分制上限（投影 <c>Hp</c> 的合法域 <c>[0, 1000]</c>）。</summary>
    public const int SnapshotHpScale = 1000;

    /// <summary>行动条千分制上限（投影 <c>AtB</c> 的合法域 <c>[0, 1000]</c>）。</summary>
    public const int AtbScale = 1000;

    /// <summary>模拟频率（Hz）：20Hz 连续推进层。</summary>
    public const int SimHz = 20;

    /// <summary>决策频率（Hz）：10Hz 离散裁决层，快照与输入都以它为单位。</summary>
    public const int DecisionHz = 10;

    /// <summary>每决策 tick 推进的模拟步数（<c>SimHz / DecisionHz</c>）。</summary>
    public const int SimTicksPerDecisionTick = SimHz / DecisionHz;

    /// <summary>保护窗口宽度（决策 tick）：0.5s × 10Hz = 5。</summary>
    public const int GuardWindowTicks = 5;
}
