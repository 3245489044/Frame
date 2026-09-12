using Share.StateManagement;
using Share.TaskStatusPool.Loops;
using Operation.StateBlock.Layering;

namespace Operation.StateBlock.Decision;

/// <summary>
/// 确定性核心只读视图（值类型，无任何写能力）—— 供导演/边界/骨架消费的 RO 投影（R8）。
/// </summary>
public readonly record struct CoreStateView(
    int BudgetLevel,
    int TokenValue,
    int TokenMax,
    bool Halt,
    int Round,
    EvalOutcome LastEval)
{
    public bool IsExhausted => TokenValue >= TokenMax;

    /// <summary>是否已熔断（预算打满或 halt）—— 阈值取 <see cref="FailureBudget.MaxLevel"/>（F-45：不再硬编码 3）。</summary>
    public bool IsTripped => BudgetLevel >= FailureBudget.MaxLevel || Halt;
}

/// <summary>
/// 确定性核心状态化的单一写口宿主 —— 把 Budget/Token/Flag/Round/LastEval 从内存字段改为 Center Session 层
/// （scope=任务）受管状态（R8 / StateRootSingleSource）。裁决器仍为纯函数（对内存 <see cref="ILoopState"/> 工作副本），
/// 仅 I/O 换状态层：<see cref="LoadWorking"/> 从 Center 还原工作副本，五步裁决在副本上推进，
/// 最后由 <see cref="Save"/>（唯一写口）提交回 Center。导演/边界持 <see cref="View"/> 只读投影。
/// 不复制裁决逻辑（R7，裁决唯一权威仍在 Share.TaskStatusPool.Loops）。
/// 对应文档 2026-09-04 StateHoldDriven §3.2 LoopStateStore / StateBackedLoopState。
/// <para>
/// 消费边界（F-66 边界化）：消费方 = StateHoldDriven 宿主线，Agent 侧零调用（Agent 运行期内核 =
/// <c>AgentTaskItem</c>；Agent 经 <c>DecisionCenter.Record</c> 只写审计快照）。跨仓消费方取证前**不得删除本类**。
/// </para>
/// </summary>
public sealed class LoopStateStore
{
    private readonly StateLayerScheme _scheme;
    private readonly string _scope;
    private readonly LayeredStoreClient _client;

    /// <param name="center">StateCenter（IStateCenter）。</param>
    /// <param name="scope">Session 层分区 scope（任务/会话标识）。</param>
    public LoopStateStore(IStateCenter center, string scope)
    {
        ArgumentNullException.ThrowIfNull(center);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        _scope = scope;
        _scheme = LoopKeys.BuildScheme();
        _client = new LayeredStoreClient(center, _scheme);
    }

    /// <summary>Session 分区 scope（任务/会话）。</summary>
    public string Scope => _scope;

    private LayeredStateKey K(string key) => _scheme.Session(_scope, key);

    /// <summary>Center 是否已有该任务的确定性核心记录（曾 Save 过）。</summary>
    public bool HasPersisted() => _client.Contains(K(LoopKeys.BudgetLevel));

    /// <summary>
    /// 从 Center 真源现读只读投影（导演/边界 RO 视图；无任何写能力，绝不触碰真源）。
    /// 未持久化时返回默认初始（budget=0, token=0, flag=clear, round=1, lastEval=None）。
    /// </summary>
    public CoreStateView View()
    {
        if (!HasPersisted()) return DefaultView();
        return new CoreStateView(
            _client.Read<int>(K(LoopKeys.BudgetLevel)),
            _client.Read<int>(K(LoopKeys.TokenValue)),
            _client.Read<int>(K(LoopKeys.TokenMax)),
            _client.Read<bool>(K(LoopKeys.FlagHalt)),
            _client.Read<int>(K(LoopKeys.Round)),
            ReadLastEval());
    }

    private static CoreStateView DefaultView() => new(0, 0, LoopKeys.DefaultTokenMax, false, 1, EvalOutcome.None);

    private EvalOutcome ReadLastEval()
        => Enum.TryParse<EvalOutcome>(_client.Read<string>(K(LoopKeys.LastEval)), ignoreCase: true, out var e) ? e : EvalOutcome.None;

    /// <summary>
    /// 还原内存工作副本（ILoopState），供五步裁决在副本上推进。确定性核心还原经
    /// <see cref="DeterministicCoreCopier.CopyFromValues"/>（快照还原唯一入口，避免私有 setter 直写）。
    /// 未持久化时为默认初始工作态；Save 前的一切修改不触 Center 真源（未提交语义）。
    /// </summary>
    public ILoopState LoadWorking()
    {
        var working = new LoopWorkingState();
        if (HasPersisted())
        {
            DeterministicCoreCopier.CopyFromValues(
                working,
                _client.Read<int>(K(LoopKeys.BudgetLevel)),
                _client.Read<int>(K(LoopKeys.TokenValue)),
                _client.Read<int>(K(LoopKeys.TokenMax)),
                _client.Read<bool>(K(LoopKeys.FlagHalt)));
            working.Round = _client.Read<int>(K(LoopKeys.Round));
            working.UserInput = _client.Read<string>(K(LoopKeys.UserInput));
            working.LastResultText = _client.Read<string>(K(LoopKeys.LastResultText));
            working.LastEval = ReadLastEval();
        }
        return working;
    }

    /// <summary>
    /// 确定性核心单一写口 —— 把工作副本持久回 Center Session 层（R8：仅 StateDrivenRoundLifecycle 内部调用）。
    /// </summary>
    public void Save(ILoopState working)
    {
        ArgumentNullException.ThrowIfNull(working);
        _client.Write(K(LoopKeys.BudgetLevel), working.Budget.Level);
        _client.Write(K(LoopKeys.TokenValue), working.Token.Value);
        _client.Write(K(LoopKeys.TokenMax), working.Token.Max);
        _client.Write(K(LoopKeys.FlagHalt), working.Flag.Halt);
        _client.Write(K(LoopKeys.Round), working.Round);
        _client.Write(K(LoopKeys.UserInput), working.UserInput);
        _client.Write(K(LoopKeys.LastResultText), working.LastResultText);
        _client.Write(K(LoopKeys.LastEval), working.LastEval.ToString());
    }

    /// <summary>重置确定性核心为默认初始（进入新任务的初始态语义）。</summary>
    public void Reset()
    {
        var w = new LoopWorkingState();
        Save(w);
    }
}

/// <summary>
/// 确定性核心内存工作副本（宿主侧 ILoopState 实现，无 Agent 依赖）。
/// 裁决器（BudgetEscalator/ProgressUpdater/TerminationDecider）对其实例推进；值仅经 Save 提交 Center。
/// </summary>
internal sealed class LoopWorkingState : ILoopState
{
    public FailureBudget Budget { get; } = new();
    public ProgressToken Token { get; } = new();
    public ControlFlag Flag { get; } = new();
    public int Round { get; set; } = 1;
    public string? UserInput { get; set; }
    public string? LastResultText { get; set; }
    public EvalOutcome LastEval { get; set; }
    public IDictionary<string, object> Data { get; } = new Dictionary<string, object>();
}
