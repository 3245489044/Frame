namespace Modules.Sync.Authority;

/// <summary>
/// 权威端 10Hz 决策循环（由 20Hz 模拟 tick 驱动）：推进 Sync / ATB / 4×PP，按固定顺序对 3 条协议规则做短路求值，
/// 结算敌前摇与保护，并产出 12 字段可见投影。
/// <para>
/// <b>一个决策 tick 的内部顺序（写死，不可换序）：</b>
/// <list type="number">
/// <item>应用本 tick 的接管输入（确定性排序，见 <see cref="SubmitGuard"/>）；</item>
/// <item>Sync 基础恢复 <c>r</c>（钳制到 <c>[0, S_max]</c>）；</item>
/// <item>ATB 充能（钳制到 <c>[0, AtbScale]</c>）；</item>
/// <item>PP 被动回复（按固定周期，逐槽钳制）；</item>
/// <item>计算本 tick 的可见前摇线索；</item>
/// <item>3 条规则自上而下短路求值（命中即停，最多 3 条）；</item>
/// <item>敌方伤害结算与保护裁决（猜中免伤 + 回补，猜错在窗口关闭后进硬直）；</item>
/// <item>投影捕获（由宿主在 tick 末调用 <see cref="CaptureVisible"/>）。</item>
/// </list>
/// </para>
/// <para>
/// <b>权威唯一裁决：</b>状态只由 <see cref="AdvanceSimTick"/> 推进；所有查询方法均不改状态，本类不做任何预测/回滚/插值
/// （预测属于客户端，MVP 里客户端也不做）。本类非线程安全，只允许由单一时钟线程驱动。
/// </para>
/// <para>
/// 契约依赖：不定义消息类型、投影结构、哈希函数、指标 id；投影写出只经 <see cref="IVisibleProjectionSink"/> 端口，
/// 真正的 <c>SyncStateProjection</c>/<c>Snapshot</c>/<c>StateHash</c> 由 SY-00 提供并在集成层组装。
/// </para>
/// </summary>
public sealed class AuthorityTickLoop
{
    private readonly AuthorityTickClock _clock;
    private readonly SyncBudget _budget;
    private readonly AtbTrack _atb;
    private readonly PpTrack _skills;
    private readonly GuardWindow _guard = new GuardWindow();
    private readonly EnemyTelegraphScript _enemy;
    private readonly int _hpMax;
    private readonly int _ruleLimit;

    private readonly List<PendingTakeover> _pending = new List<PendingTakeover>();
    private readonly List<PendingTakeover> _tickInputs = new List<PendingTakeover>();

    private long _nextSequence;
    private int _hp;
    private bool _extractActive;
    private long _stunUntilTick;
    private long _retreatUntilTick;
    private TelegraphCue _telegraph = TelegraphCue.None;

    /// <summary>构造决策循环。</summary>
    /// <param name="clock">tick 时钟（频率由宿主从契约注入）。</param>
    /// <param name="hpMax">血量上限（契约 <c>SnapshotHpScale</c>，宿主注入）。</param>
    /// <param name="atbMax">行动条上限（契约 <c>AtbScale</c>，宿主注入）。</param>
    /// <param name="skillSlots">技能槽数（契约 <c>SkillSlots</c>，宿主注入）。</param>
    /// <param name="ppPerSlotMax">单槽 PP 上限（provisional 数值由宿主从 <see cref="MvpTuning"/> 取）。</param>
    /// <param name="ruleLimit">本局生效的规则条数（1..3，供 ruleCount sweep；求值永远从槽 0 起）。</param>
    public AuthorityTickLoop(
        AuthorityTickClock clock,
        int hpMax,
        int atbMax,
        int skillSlots,
        int ppPerSlotMax,
        int ruleLimit)
    {
        if (clock is null)
        {
            throw new ArgumentNullException(nameof(clock));
        }

        if (hpMax <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(hpMax), hpMax, "血量上限必须为正。");
        }

        if (ruleLimit <= 0 || ruleLimit > RuleTable.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(ruleLimit), ruleLimit, "生效规则条数必须落在 1..规则表容量。");
        }

        _clock = clock;
        _budget = new SyncBudget(MvpTuning.SyncMax);
        _atb = new AtbTrack(atbMax);
        _skills = new PpTrack(skillSlots, ppPerSlotMax);
        _enemy = new EnemyTelegraphScript(MvpTuning.EnemyAttackIntervalTicks, MvpTuning.EnemyWindupTicks);
        _hpMax = hpMax;
        _hp = hpMax;
        _ruleLimit = ruleLimit;
    }

    /// <summary>tick 时钟（可读，不提供推进入口，推进只经本类的 <see cref="AdvanceSimTick"/>）。</summary>
    public AuthorityTickClock Clock => _clock;

    /// <summary>当前决策 tick。</summary>
    public long DecisionTick => _clock.DecisionTick;

    /// <summary>同步预算。</summary>
    public SyncBudget Budget => _budget;

    /// <summary>行动条。</summary>
    public AtbTrack Atb => _atb;

    /// <summary>四技能 PP。</summary>
    public PpTrack Skills => _skills;

    /// <summary>保护窗口。</summary>
    public GuardWindow Guard => _guard;

    /// <summary>当前血量（千分制）。</summary>
    public int Hp => _hp;

    /// <summary>血量上限。</summary>
    public int HpMax => _hpMax;

    /// <summary>撤离点是否激活（脚本设置；投影字段的来源）。</summary>
    public bool ExtractActive => _extractActive;

    /// <summary>最近一个决策 tick 求值到的可见前摇线索。</summary>
    public TelegraphCue Telegraph => _telegraph;

    /// <summary>最近一个决策 tick 命中的规则槽；未命中为 null。</summary>
    public RuleId? LastMatchedRule { get; private set; }

    /// <summary>最近一个决策 tick 执行的动作。</summary>
    public RuleAction LastAction { get; private set; }

    /// <summary>硬直结束 tick（含）；0 = 无硬直。</summary>
    public long StunUntilTick => _stunUntilTick;

    /// <summary>后撤保持结束 tick（含）；0 = 未后撤。</summary>
    public long RetreatUntilTick => _retreatUntilTick;

    /// <summary>保护免伤次数（剧本与摘要用，不是指标 id）。</summary>
    public int DamageAvoidedCount { get; private set; }

    /// <summary>实际挨打次数（剧本与摘要用，不是指标 id）。</summary>
    public int DamageTakenCount { get; private set; }

    /// <summary>投影对外可见的 <c>GuardUntilTick</c>：0 = 无窗口。</summary>
    public long VisibleGuardUntilTick => _guard.VisibleUntilTick;

    /// <summary>脚本设置撤离点激活状态（规则 2 的条件输入）。</summary>
    public void SetExtractActive(bool active)
    {
        _extractActive = active;
    }

    /// <summary>剧本初始化血量。</summary>
    public void ResetHp(int value)
    {
        _hp = value < 0 ? 0 : (value > _hpMax ? _hpMax : value);
    }

    /// <summary>
    /// 提交一次手动「预判保护」，在第 <paramref name="targetTick"/> 个决策 tick 生效（窗口按契约 0.5s = 5 tick）。
    /// <para>本调用只入队，真正的应用顺序与是否被接受由决策循环在目标 tick 裁决；权威端不做任何预测。</para>
    /// </summary>
    public void SubmitGuard(long targetTick)
    {
        Enqueue(targetTick, TakeoverRank.Guard, -1);
    }

    /// <summary>
    /// 提交一次手动「超频」：在第 <paramref name="targetTick"/> 个决策 tick 提前释放第 <paramref name="slot"/> 个技能，
    /// 消耗 Sync 与额外 PP，并把 ATB 清零。
    /// </summary>
    public void SubmitOvercharge(long targetTick, int slot)
    {
        if ((uint)slot >= (uint)_skills.SlotCount)
        {
            throw new ArgumentOutOfRangeException(nameof(slot), slot, "技能槽位越界。");
        }

        Enqueue(targetTick, TakeoverRank.Overcharge, slot);
    }

    /// <summary>
    /// 推进一个模拟 tick；跨越决策边界时执行一次完整决策推进。返回 true 表示本次推进执行了决策 tick。
    /// </summary>
    public bool AdvanceSimTick()
    {
        if (!_clock.AdvanceSimTick())
        {
            return false;
        }

        StepDecision();
        return true;
    }

    /// <summary>批量推进 <paramref name="simTicks"/> 个模拟 tick，返回执行的决策 tick 数。</summary>
    public int AdvanceSimTicks(int simTicks)
    {
        if (simTicks < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(simTicks), simTicks, "模拟 tick 推进数不得为负。");
        }

        int steps = 0;
        for (int i = 0; i < simTicks; i++)
        {
            if (_clock.AdvanceSimTick())
            {
                StepDecision();
                steps++;
            }
        }

        return steps;
    }

    /// <summary>
    /// 把一个决策 tick 的 12 字段可见投影送出入口端口。
    /// 这个结构<b>不</b>由本目录组装（契约类型归 SY-00）。
    /// </summary>
    public void CaptureVisible(IVisibleProjectionSink sink)
    {
        if (sink is null)
        {
            throw new ArgumentNullException(nameof(sink));
        }

        sink.Write(
            _clock.DecisionTick,
            _budget.Value,
            _budget.Max,
            _hp,
            _atb.Value,
            PpOrZero(0),
            PpOrZero(1),
            PpOrZero(2),
            PpOrZero(3),
            _guard.VisibleUntilTick,
            (byte)_telegraph,
            _extractActive);
    }

    // ---------------------------------------------------------------- 决策推进

    private void StepDecision()
    {
        long tick = _clock.DecisionTick;

        // 1. 接管输入（按确定顺序应用；预算不足的请求在此被权威裁决拒绝）
        ApplyTakeoversForTick(tick);

        // 2. Sync 恢复（钳制到 [0, S_max]）
        _budget.Regenerate(MvpTuning.SyncRegenPerDecisionTick);

        // 3. ATB 充能（钳制到 [0, AtbScale]；硬直只封协议动作，不封充能）
        _atb.Charge(MvpTuning.AtbChargePerDecisionTick);

        // 4. PP 被动回复（按固定周期，逐槽钳制；无回复的 tick 不改状态）
        if (tick > 0 && tick % MvpTuning.PpRegenIntervalTicks == 0)
        {
            _skills.RegenerateAll(MvpTuning.PpRegenPerSlot);
        }

        // 5. 本 tick 的可见前摇线索
        _telegraph = _enemy.CueAt(tick);

        // 6. 3 条规则短路求值
        EvaluateRules(tick);

        // 7. 伤害结算 + 保护裁决
        ResolveEnemyAttack(tick);

        // 8. 窗口过期 → 猜错硬直
        ExpireGuard(tick);
    }

    // ---------------------------------------------------------------- 接管输入

    private void Enqueue(long targetTick, TakeoverRank rank, int slot)
    {
        if (targetTick < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetTick), targetTick, "目标 tick 不得为负。");
        }

        _pending.Add(new PendingTakeover
        {
            TargetTick = targetTick,
            Sequence = _nextSequence++,
            Rank = rank,
            Slot = slot,
        });
    }

    /// <summary>
    /// 同一 tick 内的应用顺序（确定性，可复现）：
    /// 先按提交序号升序（提交顺序由会话按传输队列顺序保证），序号相同再按 槽类型 Guard &lt; Overcharge、再按槽位升序。
    /// 同一 (tick, 槽位) 只有首条生效，后续请求丢弃（R5 的权威侧实现）。
    /// </summary>
    private void ApplyTakeoversForTick(long tick)
    {
        _tickInputs.Clear();
        for (int i = 0; i < _pending.Count; i++)
        {
            if (_pending[i].TargetTick == tick)
            {
                _tickInputs.Add(_pending[i]);
            }
        }

        if (_tickInputs.Count > 1)
        {
            _tickInputs.Sort(ComparePending);
        }

        bool guardApplied = false;
        int overchargeMask = 0;

        for (int i = 0; i < _tickInputs.Count; i++)
        {
            PendingTakeover item = _tickInputs[i];
            if (item.Rank == TakeoverRank.Guard)
            {
                if (guardApplied)
                {
                    continue;
                }

                if (!_budget.TrySpend(MvpTuning.ManualGuardCost))
                {
                    continue;
                }

                _guard.Open(tick, MvpTuning.GuardWindowTicks, penaltyOnMiss: true);
                guardApplied = true;
                continue;
            }

            int bit = 1 << item.Slot;
            if ((overchargeMask & bit) != 0)
            {
                continue;
            }

            if (!_budget.CanSpend(MvpTuning.ManualOverchargeCost)
                || !_skills.CanConsume(item.Slot, MvpTuning.OverchargeExtraPp))
            {
                continue;
            }

            _budget.TrySpend(MvpTuning.ManualOverchargeCost);
            _skills.TryConsume(item.Slot, MvpTuning.OverchargeExtraPp);
            _atb.Clear();
            overchargeMask |= bit;
        }

        // 目标 tick 已过且尚未应用：按“迟到”丢弃（计数归会话/契约侧，权威侧不新增指标）。
        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            if (_pending[i].TargetTick < tick)
            {
                _pending.RemoveAt(i);
            }
        }
    }

    private static int ComparePending(PendingTakeover left, PendingTakeover right)
    {
        if (left.Sequence != right.Sequence)
        {
            return left.Sequence < right.Sequence ? -1 : 1;
        }

        if (left.Rank != right.Rank)
        {
            return left.Rank < right.Rank ? -1 : 1;
        }

        if (left.Slot != right.Slot)
        {
            return left.Slot < right.Slot ? -1 : 1;
        }

        return 0;
    }

    // ---------------------------------------------------------------- 规则求值

    /// <summary>
    /// 短路求值：从槽 0 起，最多 <c>min(ruleLimit, 3)</c> 条；第一条条件为真者执行动作并立即返回。
    /// 硬直期间整轮不求值（硬直是保护猜错的代价，MVP 用“不执行动作”表达）。
    /// </summary>
    private void EvaluateRules(long tick)
    {
        LastMatchedRule = null;
        LastAction = RuleAction.None;

        if (IsStunned(tick))
        {
            return;
        }

        int limit = _ruleLimit < RuleTable.Count ? _ruleLimit : RuleTable.Count;
        for (int i = 0; i < limit; i++)
        {
            RuleEntry rule = RuleTable.At(i);
            if (!ConditionHolds(rule, tick))
            {
                continue;
            }

            ExecuteAction(rule, tick);
            LastMatchedRule = rule.Id;
            LastAction = rule.Action;
            return; // 短路：后面的槽不再求值
        }
    }

    private bool ConditionHolds(RuleEntry rule, long tick)
    {
        switch (rule.Id)
        {
            case RuleId.TelegraphGuard:
                // 前摇可见 + 当前无窗口（避免同一段前摇重复扣预算）+ 付得起 c₁
                return _telegraph != TelegraphCue.None
                    && !_guard.IsOpen
                    && _budget.CanSpend(rule.Cost);

            case RuleId.LowHpRetreat:
                // 血量阈值 + 撤离激活 + 不在后撤保持中 + 付得起 c₂
                return _hp <= MvpTuning.RetreatHpThreshold
                    && _extractActive
                    && !IsRetreating(tick)
                    && _budget.CanSpend(rule.Cost);

            case RuleId.AtbSkill:
                // ATB 满 + 还有 PP + 不在后撤保持中 + 付得起 c₃
                return _atb.IsFull
                    && _skills.Total > 0
                    && !IsRetreating(tick)
                    && _budget.CanSpend(rule.Cost);

            default:
                return false;
        }
    }

    private void ExecuteAction(RuleEntry rule, long tick)
    {
        switch (rule.Action)
        {
            case RuleAction.Guard:
                // 条件已保证余额足够；自动保护猜错不罚硬直（MVP 不做“被骗长冷却”）
                _budget.TrySpend(rule.Cost);
                _guard.Open(tick, MvpTuning.GuardWindowTicks, penaltyOnMiss: false);
                break;

            case RuleAction.Retreat:
                _budget.TrySpend(rule.Cost);
                _retreatUntilTick = tick + MvpTuning.RetreatHoldTicks - 1;
                break;

            case RuleAction.Skill:
                int slot = FirstSlotWithPp();
                if (slot < 0)
                {
                    return; // 条件已保证 Total > 0，此处不可达
                }

                _budget.TrySpend(rule.Cost);
                _skills.TryConsume(slot, 1);
                _atb.Clear();
                break;
        }
    }

    private int FirstSlotWithPp()
    {
        for (int slot = 0; slot < _skills.SlotCount; slot++)
        {
            if (_skills[slot] > 0)
            {
                return slot;
            }
        }

        return -1;
    }

    // ---------------------------------------------------------------- 结算

    private void ResolveEnemyAttack(long tick)
    {
        if (!_enemy.ResolvesAt(tick))
        {
            return;
        }

        if (_guard.Covers(tick))
        {
            _guard.Absorb();
            _budget.Refund(MvpTuning.GuardHitSyncRefund);
            DamageAvoidedCount++;
            return;
        }

        DamageTakenCount++;
        long hp = (long)_hp - MvpTuning.EnemyAttackDamage;
        _hp = hp < 0 ? 0 : (int)hp;
    }

    private void ExpireGuard(long tick)
    {
        if (!_guard.ExpiredAt(tick))
        {
            return;
        }

        if (_guard.CloseAndReportMiss())
        {
            // 硬直区间：[tick+1, tick+12] 共 12 个决策 tick（本 tick 的规则已在结算前求值）
            _stunUntilTick = tick + MvpTuning.GuardMissStunTicks;
        }
    }

    private bool IsStunned(long tick)
    {
        return _stunUntilTick != 0 && tick <= _stunUntilTick;
    }

    private bool IsRetreating(long tick)
    {
        return _retreatUntilTick != 0 && tick <= _retreatUntilTick;
    }

    private int PpOrZero(int slot)
    {
        return slot < _skills.SlotCount ? _skills[slot] : 0;
    }

    // ---------------------------------------------------------------- 内部类型

    /// <summary>槽类型；序号即同 tick 内的次级排序键（Guard 先于 Overcharge）。</summary>
    private enum TakeoverRank : byte
    {
        Guard = 0,
        Overcharge = 1,
    }

    /// <summary>待应用的接管请求（权威内部队列项，不是协议消息类型）。</summary>
    private struct PendingTakeover
    {
        public long TargetTick;
        public long Sequence;
        public TakeoverRank Rank;
        public int Slot;
    }
}
