namespace Modules.Sync.Authority;

/// <summary>
/// 可见状态投影出口（端口，不是投影结构）。
/// <para>
/// 权威端只负责把每个决策 tick 的 12 个可见值按**契约冻结顺序**送出去；
/// 真正的 <c>SyncStateProjection</c> / <c>Snapshot</c> / <c>StateHash</c> 属于 SY-00 的契约类型，
/// 本目录不定义、不组装、不序列化、不哈希。
/// </para>
/// <para>
/// 参数顺序与契约 §4.3 的 12 字段顺序一一对应：
/// <c>Tick / Sync / Smax / Hp / Atb / Pp0 / Pp1 / Pp2 / Pp3 / GuardUntilTick / Telegraph / ExtractActive</c>。
/// </para>
/// </summary>
public interface IVisibleProjectionSink
{
    /// <summary>写出一条可见状态投影（每决策 tick 恰好一次）。</summary>
    /// <param name="tick">决策 tick（不是挂钟时间）。</param>
    /// <param name="sync">当前同步预算。</param>
    /// <param name="syncMax">同步上限 <c>S_max</c>。</param>
    /// <param name="hp">血量（千分制，契约 <c>SnapshotHpScale</c>）。</param>
    /// <param name="atb">行动条（千分制，契约 <c>AtbScale</c>）。</param>
    /// <param name="pp0">槽 0 剩余 PP。</param>
    /// <param name="pp1">槽 1 剩余 PP。</param>
    /// <param name="pp2">槽 2 剩余 PP。</param>
    /// <param name="pp3">槽 3 剩余 PP。</param>
    /// <param name="guardUntilTick">保护窗口结束 tick；0 = 无窗口。</param>
    /// <param name="telegraph">可见线索字节（取值见 <see cref="TelegraphCue"/>）。</param>
    /// <param name="extractActive">撤离点是否激活。</param>
    void Write(
        long tick,
        int sync,
        int syncMax,
        int hp,
        int atb,
        int pp0,
        int pp1,
        int pp2,
        int pp3,
        long guardUntilTick,
        byte telegraph,
        bool extractActive);
}
