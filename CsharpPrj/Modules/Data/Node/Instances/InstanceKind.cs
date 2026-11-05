using Modules.Node.Diagnostics;

namespace Modules.Node.Instances;

/// <summary>实例保留策略（G-1：承载粒度与保留由声明决定，不由代码分叉）。</summary>
public enum InstanceRetention
{
    /// <summary>常驻：不自动封存（会话根等长生命周期实例）。</summary>
    Keep,

    /// <summary>关闭即封存（只读；I3：Sealed 之后写拒绝）。</summary>
    SealAfterClose,

    /// <summary>封存后归档（可迁出承载，读侧仍可反解）。</summary>
    ArchiveAfterSeal,
}

/// <summary>
/// 实例类型声明（G-1）—— 把「同一实例机制在不同 Kind 的差异」收敛为一行数据：
/// Kind 名 / 身份模板 / Placement 集 / 承载 Store 键 / 级联规则 / 保留策略 / 运行态面标记。
/// <para>
/// 纪律（SingleImplementation）：Kind 的**装配逻辑只有一份**（上层装配器），各 Kind 只提供本记录；
/// 框架不感知任何具体 Kind 名（禁领域词，见 <c>Check-NodeFramework.ps1</c> H 段）。
/// </para>
/// <para>
/// 承载不在此内联：<see cref="StoreKind"/> 是**键**，实现由宿主按该键注入（守住框架零依赖，K6）。
/// </para>
/// </summary>
/// <param name="Kind">实例类型名（声明面字符串；大小写敏感，Ordinal 比较）。</param>
/// <param name="IdentityTemplate">身份模板引用（<c>IdentitySpec</c> 的 id；实例身份 = 该模板的求值结果）。</param>
/// <param name="PlacementIds">该 Kind 的位置 id 集（位置声明的引用，不内联位置内容）。</param>
/// <param name="StoreKind">承载 Store 键（宿主按此键解析 <see cref="IInstanceStore"/> 实现）。</param>
/// <param name="Cascades">级联规则集（父 Disposed 时的处置依据；空 = 不级联）。</param>
/// <param name="Retention">保留策略。</param>
/// <param name="RuntimeBound">是否含运行态内存面（非位置载体；仅作读侧 overlay 源）。</param>
/// <param name="Note">说明注记（诊断/文档用，非判据）。</param>
public sealed record InstanceKindSpec(
    string Kind,
    string IdentityTemplate,
    IReadOnlyList<string> PlacementIds,
    string StoreKind,
    IReadOnlyList<CascadeRule> Cascades,
    InstanceRetention Retention,
    bool RuntimeBound = false,
    string? Note = null);

/// <summary>
/// 实例类型注册表（G-1）—— 装配期写入、运行期只读；重复注册即 fail-fast（禁第二份 Kind 语义）。
/// <para>注入式（禁进程静态）：由组合根构造并注入实例装配器；<see cref="Freeze"/> 之后一切注册被拒。</para>
/// </summary>
public sealed class InstanceKindRegistry
{
    private readonly Dictionary<string, InstanceKindSpec> _kinds = new(StringComparer.Ordinal);
    private readonly INodeDiagnosticsSink _diagnostics;
    private bool _frozen;

    public InstanceKindRegistry(INodeDiagnosticsSink? diagnostics = null)
        => _diagnostics = diagnostics ?? NullDiagnosticsSink.Instance;

    /// <summary>是否已冻结（运行期只读门；冻结后注册一律拒绝）。</summary>
    public bool IsFrozen => _frozen;

    /// <summary>已注册 Kind 数。</summary>
    public int Count => _kinds.Count;

    /// <summary>已注册 Kind 集（序 = 注册序不稳定，按序数排序后返回）。</summary>
    public IReadOnlyList<string> Kinds => _kinds.Keys.OrderBy(static k => k, StringComparer.Ordinal).ToArray();

    /// <summary>冻结注册表（装配相位末调用一次；此后注册被拒并可见化）。</summary>
    public void Freeze()
    {
        if (_frozen)
            return;

        _frozen = true;
        _diagnostics.Report(new NodeDiagnostic(
            "instance.kind-registry-frozen",
            NodeDiagnosticSeverity.Info,
            $"实例类型注册表已冻结（{_kinds.Count} 类）",
            "instance"));
    }

    /// <summary>注册 Kind 声明（冻结后 / 重复 Kind ⇒ 抛；缺口不静默）。</summary>
    public void Register(InstanceKindSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentException.ThrowIfNullOrWhiteSpace(spec.Kind);

        if (_frozen)
            throw new InvalidOperationException($"实例类型注册表已冻结，禁止注册 '{spec.Kind}'（装配期写入、运行期只读）。");

        if (!_kinds.TryAdd(spec.Kind, spec))
            throw new InvalidOperationException($"实例类型重复注册: {spec.Kind}（Kind 语义唯一）。");

        _diagnostics.Report(new NodeDiagnostic(
            "instance.kind-registered",
            NodeDiagnosticSeverity.Info,
            $"实例类型已注册（位置 {spec.PlacementIds.Count} / 级联 {spec.Cascades.Count} / 保留 {spec.Retention}）",
            spec.Kind));
    }

    /// <summary>尝试取 Kind 声明（未注册 ⇒ false，调用方负责可见化）。</summary>
    public bool TryGet(string? kind, out InstanceKindSpec? spec)
    {
        spec = null;
        return !string.IsNullOrWhiteSpace(kind) && _kinds.TryGetValue(kind, out spec);
    }

    /// <summary>取 Kind 声明；未注册 ⇒ 抛（装配期显式失败，不给静默兜底）。</summary>
    public InstanceKindSpec Require(string kind)
        => TryGet(kind, out var spec)
            ? spec!
            : throw new ArgumentException($"未注册的实例类型 '{kind}'（Kind 清单唯一权威 = InstanceKindRegistry）", nameof(kind));
}
