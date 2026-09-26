using Modules.Node.Diagnostics;

namespace Modules.Node.Instances;

/// <summary>
/// 实例承载阶段（G-4）—— 受控词唯一来源（禁散落字面量）。
/// <para>语义链：<c>Created → Open → Active → Sealed → Archived → Disposed</c>；<c>Sealed</c> 之后只读（I3）。</para>
/// </summary>
public enum InstanceStage
{
    /// <summary>已创建（承载已定位/已打开，但尚未进入活动写入面）。</summary>
    Created,

    /// <summary>已打开（句柄就位；可写）。</summary>
    Open,

    /// <summary>活动中（装配完成、正在被消费；可写）。</summary>
    Active,

    /// <summary>已封存（只读；写调用被拒并可见化）。</summary>
    Sealed,

    /// <summary>已归档（只读；可迁出承载，身份仍可反解）。</summary>
    Archived,

    /// <summary>已处置（终态；句柄释放）。</summary>
    Disposed,
}

/// <summary>跨实例边类别（G-5）—— 决定父实例处置时是否级联。</summary>
public enum InstanceCascadeKind
{
    /// <summary>持有：父处置时级联处置子实例（包含关系）。</summary>
    Holds,

    /// <summary>引用：父处置**不**级联（边仅为只读关系；多对多保留为边）。</summary>
    Refs,

    /// <summary>派生：随源存亡（派生视图不独立持有生命周期）。</summary>
    Derived,
}

/// <summary>一条级联规则：子 Kind + 级联类别（G-1 声明面的一条）。</summary>
/// <param name="Kind">子实例 Kind。</param>
/// <param name="Cascade">级联类别。</param>
public sealed record CascadeRule(string Kind, InstanceCascadeKind Cascade)
{
    /// <summary>父处置时是否级联处置该 Kind（<see cref="InstanceCascadeKind.Holds"/> 才级联）。</summary>
    public bool CascadesOnDispose => Cascade == InstanceCascadeKind.Holds;
}

/// <summary>
/// 实例生命周期（G-4）—— 转移表 + 可写判定 + 级联裁决（纯函数，无状态：状态由调用方持有）。
/// <para>非法跃迁 = 架构级错误 ⇒ 抛（装配期/运行期显式失败，不静默改状态）。</para>
/// </summary>
public static class InstanceLifecycle
{
    private static readonly Dictionary<InstanceStage, InstanceStage[]> Transitions = new()
    {
        [InstanceStage.Created] = new[] { InstanceStage.Open, InstanceStage.Disposed },
        [InstanceStage.Open] = new[] { InstanceStage.Active, InstanceStage.Sealed, InstanceStage.Disposed },
        [InstanceStage.Active] = new[] { InstanceStage.Open, InstanceStage.Sealed, InstanceStage.Disposed },
        [InstanceStage.Sealed] = new[] { InstanceStage.Archived, InstanceStage.Disposed },
        [InstanceStage.Archived] = new[] { InstanceStage.Disposed },
        [InstanceStage.Disposed] = Array.Empty<InstanceStage>(),
    };

    /// <summary>全部受控词（序 = 阶段链序；诊断/文档用）。</summary>
    public static IReadOnlyList<InstanceStage> Stages { get; } = new[]
    {
        InstanceStage.Created, InstanceStage.Open, InstanceStage.Active,
        InstanceStage.Sealed, InstanceStage.Archived, InstanceStage.Disposed,
    };

    /// <summary>终态判定（<c>Disposed</c> 无后继）。</summary>
    public static bool IsTerminal(InstanceStage stage) => Transitions[stage].Length == 0;

    /// <summary>可写判定（I3：<c>Open</c>/<c>Active</c> 可写；其余一律只读）。</summary>
    public static bool IsWritable(InstanceStage stage) =>
        stage is InstanceStage.Open or InstanceStage.Active;

    /// <summary>合法跃迁判定（同态 = 幂等 no-op，视为合法）。</summary>
    public static bool IsLegal(InstanceStage from, InstanceStage to) =>
        from == to || Transitions[from].Contains(to);

    /// <summary>跃迁目标（非法 ⇒ 抛；消息含合法目标集，便于定位）。</summary>
    public static InstanceStage Next(InstanceStage from, InstanceStage to)
    {
        if (IsLegal(from, to))
            return to;

        throw new InvalidOperationException(
            $"非法实例承载跃迁: {from} → {to}（{from} 的合法目标: {string.Join(", ", Transitions[from])}）");
    }

    /// <summary>级联裁决：父实例 <c>Disposed</c> 时，该边类别是否级联处置子实例。</summary>
    public static bool CascadesOnDispose(InstanceCascadeKind kind) => kind == InstanceCascadeKind.Holds;
}
