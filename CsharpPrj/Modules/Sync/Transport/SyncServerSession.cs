using System;

namespace Modules.Sync.Transport;

/// <summary>会话生命周期：<c>Created → Handshaking → Active → Closed</c>；拒绝路径直达 <c>Closed</c>，不建会话。</summary>
public enum SyncSessionState
{
    /// <summary>已创建，尚未发起握手。</summary>
    Created = 0,

    /// <summary>握手中：只等 Hello 裁决，不接受任何输入。</summary>
    Handshaking = 1,

    /// <summary>握手通过，会话可用：接受输入、可推进 tick。</summary>
    Active = 2,

    /// <summary>已关闭：握手被拒、或主动关闭；任何输入都判 <see cref="SyncInputVerdict.RejectedNotActive"/>。</summary>
    Closed = 3,
}

/// <summary>
/// 服务端会话排序机：握手窗口 + 输入窗口 + 幂等键 + 确定性排序 + tick 推进的唯一编排点。
/// <para>单线程模型：只由权威循环线程独占调用，内部不加锁；与 <see cref="InMemorySyncTransport"/> 协作时，
/// 锁序固定为“会话逻辑在前、传输队列在后”，且绝不在持锁期间回调外部。</para>
/// <para>[契约依赖 · 等待 SY-00] 本类不定义任何消息类型、投影结构、哈希函数；版本/哈希以构造参数注入，
/// <c>InputWindowTicks</c> 以构造参数注入，<c>CommandId</c> 以稳定字符串键参与比较与幂等。</para>
/// </summary>
/// <typeparam name="TRuleSetHash">契约的规则集哈希值类型（只比较，不计算）。</typeparam>
public sealed class SyncServerSession<TRuleSetHash> where TRuleSetHash : IEquatable<TRuleSetHash>
{
    private readonly SyncHandshakeGate<TRuleSetHash> _handshake;
    private readonly SyncInputWindow _window;
    private readonly SyncIdempotencyWindow<string> _idempotency;
    private readonly SyncInputOrdering _ordering;
    private SyncSessionState _state = SyncSessionState.Created;

    /// <param name="inputWindowTicks">输入窗口宽度（含端点）；契约落地后由 <c>InputWindowTicks</c> 传入。</param>
    /// <param name="initialTick">初始决策 tick（从 0 起）。</param>
    /// <param name="handshake">握手校验器（版本 + 规则集哈希）。</param>
    public SyncServerSession(int inputWindowTicks, long initialTick, SyncHandshakeGate<TRuleSetHash> handshake)
    {
        ArgumentNullException.ThrowIfNull(handshake);

        _handshake = handshake;
        _window = new SyncInputWindow(inputWindowTicks, initialTick);
        _idempotency = new SyncIdempotencyWindow<string>(inputWindowTicks, initialTick);
        _ordering = new SyncInputOrdering(inputWindowTicks);
    }

    /// <summary>当前生命周期状态。</summary>
    public SyncSessionState State => _state;

    /// <summary>当前决策 tick。</summary>
    public long CurrentTick => _window.CurrentTick;

    /// <summary>输入窗口宽度（含端点）。</summary>
    public int InputWindowTicks => _window.WindowTicks;

    /// <summary>最近一次握手裁决；未握手为 null。</summary>
    public SyncHandshakeOutcome? LastHandshakeOutcome { get; private set; }

    /// <summary>握手是否已被拒绝（拒绝即关闭，且不建会话）。</summary>
    public bool IsRejected => _state == SyncSessionState.Closed
        && LastHandshakeOutcome is { } outcome
        && outcome != SyncHandshakeOutcome.Accepted;

    /// <summary>从 <c>Created</c> 进入 <c>Handshaking</c>；其它状态返回 false 且不改状态。</summary>
    public bool TryBeginHandshake()
    {
        if (_state != SyncSessionState.Created)
        {
            return false;
        }

        _state = SyncSessionState.Handshaking;
        return true;
    }

    /// <summary>
    /// 裁决一次 Hello。仅 <c>Handshaking</c> 可调用；其它状态下视为非法握手载荷，直接拒绝并关闭。
    /// 版本或规则集不匹配 → 返回值即拒绝原因，状态置 <c>Closed</c>，<b>不建会话</b>。
    /// </summary>
    public SyncHandshakeOutcome SubmitHello(int protocolVersion, TRuleSetHash ruleSetHash)
    {
        if (_state != SyncSessionState.Handshaking)
        {
            LastHandshakeOutcome = SyncHandshakeOutcome.Malformed;
            _state = SyncSessionState.Closed;
            return SyncHandshakeOutcome.Malformed;
        }

        var outcome = _handshake.Evaluate(protocolVersion, ruleSetHash);
        LastHandshakeOutcome = outcome;
        _state = outcome == SyncHandshakeOutcome.Accepted
            ? SyncSessionState.Active
            : SyncSessionState.Closed;
        return outcome;
    }

    /// <summary>主动关闭；重复关闭无副作用。</summary>
    public void Close() => _state = SyncSessionState.Closed;

    /// <summary>推进当前 tick（单调）；推进同时回收滑出窗口的幂等桶与排序桶。</summary>
    public void AdvanceTo(long tick)
    {
        if (_state != SyncSessionState.Active)
        {
            throw new InvalidOperationException("会话未激活，不能推进决策 tick。");
        }

        _window.AdvanceTo(tick);
        var current = _window.CurrentTick;
        _idempotency.AdvanceTo(current);
        _ordering.AdvanceTo(current);
    }

    /// <summary>
    /// 输入准入：窗口三段语义 → 同 tick 已消费 → 幂等 → 确定性排序（首条优先）。
    /// 返回的 <see cref="SyncInputVerdict"/> 即“为什么接受/为什么丢弃”，调用方据此上报观测端口。
    /// </summary>
    /// <param name="targetTick">输入目标决策 tick（契约消息的 Tick 字段）。</param>
    /// <param name="slotOrdinal">槽位序号（由契约的 <c>Kind</c> 映射；本层不定义 Kind 枚举）。</param>
    /// <param name="commandId">稳定幂等键文本（同一逻辑命令任何重试都相同）。</param>
    public SyncInputVerdict TryAcceptInput(long targetTick, int slotOrdinal, string commandId)
    {
        if (commandId is null)
        {
            throw new ArgumentNullException(nameof(commandId));
        }

        if (slotOrdinal < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(slotOrdinal), "槽位序号不能为负。");
        }

        if (_state != SyncSessionState.Active)
        {
            return SyncInputVerdict.RejectedNotActive;
        }

        var verdict = _window.Classify(targetTick);
        if (verdict != SyncInputVerdict.Accepted)
        {
            return verdict;
        }

        if (_ordering.IsTickConsumed(targetTick))
        {
            return SyncInputVerdict.RejectedSlotConsumed;
        }

        if (_idempotency.IsRegistered(targetTick, commandId))
        {
            return SyncInputVerdict.RejectedDuplicate;
        }

        var key = new SyncInputKey(targetTick, slotOrdinal, commandId);
        if (!_ordering.TryReserve(key, out var evicted))
        {
            return _ordering.IsTickConsumed(targetTick)
                ? SyncInputVerdict.RejectedSlotConsumed
                : SyncInputVerdict.RejectedOutranked;
        }

        if (evicted is not null)
        {
            // 被淘汰者从未真正应用，撤销其幂等登记，避免其重试被误判为重复。
            _idempotency.Revoke(evicted.Value.TargetTick, evicted.Value.CommandId);
        }

        _idempotency.TryRegister(targetTick, commandId);
        return SyncInputVerdict.Accepted;
    }

    /// <summary>
    /// 消费某 tick：按确定性全序输出该 tick 胜出的输入，并把该 tick 标记为已消费（之后同 tick 输入拒收）。
    /// 输出顺序 = 应用顺序，权威端据此保证“同 tick 首条优先”与到达顺序无关。
    /// </summary>
    public int DrainTickInputs(long tick, SyncInputKey[] sink)
    {
        ArgumentNullException.ThrowIfNull(sink);

        if (_state != SyncSessionState.Active)
        {
            return 0;
        }

        return _ordering.DrainTick(tick, sink);
    }
}
