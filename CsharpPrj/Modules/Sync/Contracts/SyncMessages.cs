namespace Modules.Sync.Contracts;

/// <summary>消息方向：决定允许出现的位置（上行只发 C→S，下行只发 S→C）。</summary>
public enum SyncMessageDirection : byte
{
    /// <summary>客户端 → 权威端。</summary>
    ClientToServer = 0,

    /// <summary>权威端 → 客户端。</summary>
    ServerToClient = 1,
}

/// <summary>
/// 消息家族：v1 只有这 7 条。<b>新增消息只能追加到末尾</b>，既有编号语义不变。
/// <para>违约表现：两端 <see cref="SyncMessageKind"/> 编号不一致时，接收端会把“未知类型”当畸形载荷，
/// 握手 <see cref="RejectReason.Malformed"/> 或会话拒收即暴露。</para>
/// </summary>
public enum SyncMessageKind : byte
{
    /// <summary>C→S 握手：只协商，不携带状态。</summary>
    Hello = 0,

    /// <summary>S→C 建会话：回协议版本、规则集哈希、双频与当前 tick。</summary>
    Welcome = 1,

    /// <summary>S→C 拒绝：不建会话。</summary>
    Reject = 2,

    /// <summary>C→S 输入帧（MVP 允许全 0）。</summary>
    InputFrame = 3,

    /// <summary>C→S 手动接管（保护 / 超频）。</summary>
    TakeoverCommand = 4,

    /// <summary>S→C 全量状态快照：每个决策 tick 一条。</summary>
    Snapshot = 5,

    /// <summary>C→S 快照确认：只统计，不阻塞 tick。</summary>
    SnapshotAck = 6,
}

/// <summary>手动接管种类：MVP 只有两项；序号即同 tick 内的次级排序键。</summary>
public enum TakeoverKind : byte
{
    /// <summary>预判保护（开保护窗口，扣 Sync）。</summary>
    Guard = 0,

    /// <summary>超频（提前释放技能，扣 Sync 与额外 PP，清 ATB）。</summary>
    Overcharge = 1,
}

/// <summary>C→S 握手（<see cref="SyncMessageKind.Hello"/>）。</summary>
public sealed record Hello(long Tick, int ProtocolVersion, string ClientBuild);

/// <summary>S→C 建会话（<see cref="SyncMessageKind.Welcome"/>）。</summary>
public sealed record Welcome(
    long SessionId,
    int ProtocolVersion,
    ulong RulesetHash,
    int SimHz,
    int DecisionHz,
    long ServerTick);

/// <summary>S→C 拒绝（<see cref="SyncMessageKind.Reject"/>）；<b>不建会话</b>。</summary>
public sealed record Reject(RejectReason Reason);

/// <summary>C→S 输入帧；MVP 允许 <c>MoveX/MoveZ=0</c>、<c>AttackHeld=false</c>（不驱动移动也能跑闭环）。</summary>
public sealed record InputFrame(
    long TargetTick,
    long Sequence,
    int MoveX,
    int MoveZ,
    bool AttackHeld);

/// <summary>
/// C→S 手动接管。<c>CommandId</c> 是单调序号（由客户端本地分配，重试必须复用），
/// 幂等键 = <c>(TargetTick, CommandId)</c>；禁止 <c>Guid</c> / <c>Random</c>。
/// </summary>
public sealed record TakeoverCommand(long TargetTick, long CommandId, TakeoverKind Kind);

/// <summary>
/// S→C 全量快照：每个决策 tick一条，<c>Projection</c> 已含 <see cref="SyncStateProjection.Tick"/>。
/// <para>校验尺寸 = 8 + 50 + 8 = 66 字节；<see cref="StateHash"/> 覆盖 <c>Projection</c> 的编码，不覆盖信封。</para>
/// </summary>
public sealed class Snapshot
{
    /// <summary>本快照对应的决策 tick（R1：一条快照一个 tick）。</summary>
    public long Tick { get; init; }

    /// <summary>12 字段可见投影。</summary>
    public SyncStateProjection Projection { get; init; } = null!;

    /// <summary>权威端算出的状态哈希（客户端应按同一口径重算并比对）。</summary>
    public StateHash StateHash { get; init; }
}

/// <summary>C→S 快照确认；<see cref="SyncMessageKind.SnapshotAck"/>。<c>AppliedHash</c> 必须等于客户端实际应用后重算的哈希。</summary>
public sealed record SnapshotAck(long Tick, StateHash AppliedHash);

/// <summary>消息字段布局的一行：字段名、类型标记、固定字节宽度。</summary>
public readonly record struct SyncMessageFieldSpec(string Name, string Type, int Width);

/// <summary>一条消息的布局：类型编号、名字、方向、字段表。</summary>
public readonly record struct SyncMessageSpec(
    SyncMessageKind Kind,
    string Name,
    SyncMessageDirection Direction,
    IReadOnlyList<SyncMessageFieldSpec> Fields);

/// <summary>
/// 消息家族的唯一布局表（枚举 + 表，不是 if/switch 链）。
/// <para>
/// <b>编码规则：</b>小端、字段按表顺序连续排列、无填充、无对齐尾巴；
/// 整数用 <c>BinaryPrimitives</c> 的 little-endian 原语写，<c>bool</c> 恒为 1 字节 <c>0/1</c>；
/// 字符串 <c>ClientBuild</c> 为 UTF-8，长度上限 32 字节（超限算畸形）。
/// </para>
/// <para>
/// <b>契约测试口径：</b>H01 断言每条消息 <c>decode(encode(x))</c> 字段相等且再编码逐字节相等；
/// 两端字段顺序不一致时，该断言在首个错位字段处暴露，而不是靠线上行为碰运气。
/// </para>
/// </summary>
public static class SyncMessageLayout
{
    /// <summary><c>ClientBuild</c> 的 UTF-8 字节上限（超限即 <see cref="RejectReason.Malformed"/>）。</summary>
    public const int ClientBuildMaxBytes = 32;

    /// <summary>7 条消息的字段布局（顺序即 <see cref="SyncMessageKind"/> 编号）。</summary>
    public static readonly IReadOnlyList<SyncMessageSpec> All = new SyncMessageSpec[]
    {
        new(SyncMessageKind.Hello, "Hello", SyncMessageDirection.ClientToServer, new SyncMessageFieldSpec[]
        {
            new("Tick", "int64", 8),
            new("ProtocolVersion", "int32", 4),
            new("ClientBuild", "utf8(<=32)", ClientBuildMaxBytes),
        }),
        new(SyncMessageKind.Welcome, "Welcome", SyncMessageDirection.ServerToClient, new SyncMessageFieldSpec[]
        {
            new("SessionId", "int64", 8),
            new("ProtocolVersion", "int32", 4),
            new("RulesetHash", "uint64", 8),
            new("SimHz", "int32", 4),
            new("DecisionHz", "int32", 4),
            new("ServerTick", "int64", 8),
        }),
        new(SyncMessageKind.Reject, "Reject", SyncMessageDirection.ServerToClient, new SyncMessageFieldSpec[]
        {
            new("Reason", "uint8", 1),
        }),
        new(SyncMessageKind.InputFrame, "InputFrame", SyncMessageDirection.ClientToServer, new SyncMessageFieldSpec[]
        {
            new("TargetTick", "int64", 8),
            new("Sequence", "int64", 8),
            new("MoveX", "int32", 4),
            new("MoveZ", "int32", 4),
            new("AttackHeld", "uint8(bool)", 1),
        }),
        new(SyncMessageKind.TakeoverCommand, "TakeoverCommand", SyncMessageDirection.ClientToServer, new SyncMessageFieldSpec[]
        {
            new("TargetTick", "int64", 8),
            new("CommandId", "int64", 8),
            new("Kind", "uint8", 1),
        }),
        new(SyncMessageKind.Snapshot, "Snapshot", SyncMessageDirection.ServerToClient, new SyncMessageFieldSpec[]
        {
            new("Tick", "int64", 8),
            new("Projection", "SyncStateProjection", SyncStateProjectionLayout.EncodedBytes),
            new("StateHash", "uint64", 8),
        }),
        new(SyncMessageKind.SnapshotAck, "SnapshotAck", SyncMessageDirection.ClientToServer, new SyncMessageFieldSpec[]
        {
            new("Tick", "int64", 8),
            new("AppliedHash", "uint64", 8),
        }),
    };

    /// <summary>按 <see cref="SyncMessageKind"/> 取布局（越界即抛，禁止静默回退）。</summary>
    public static SyncMessageSpec Spec(SyncMessageKind kind)
    {
        int index = (int)kind;
        if ((uint)index >= (uint)All.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知消息类型。");
        }

        return All[index];
    }
}
