using Strategy.Observability.Bus;
using Strategy.Observability.Entity;

namespace Usharp.Adapters.Observability;

/// <summary>
/// 步骤自动埋点：把 <c>[Flow]</c> 步骤声明的发射绑定（<c>emit=k1,k2</c>，经 <c>RoundSpec</c> 的
/// <c>RoundStep.Emits</c> 承载）在**执行时**自动转为观测活动 —— 编译器生成埋点，而非人工埋点。
/// <para>
/// 语义：<c>emit=</c> 首个 kind = 起始事件，第二个（可选）= 完成事件（缺省同起始分类）；
/// 活动经 <see cref="Activity"/> 补发 <c>elapsed_ms</c> 与 <c>stage</c>。入参用
/// <c>(stepKey, emits)</c> 而非 <c>RoundStep</c>：适配层不引骨架载体（依赖方向单向）。
/// </para>
/// <para>
/// 载荷约束：自动埋点只覆盖**无必填载荷**的生命周期事件；声明了 <c>required=</c> 的事件须由宿主在
/// 具体埋点处补载荷（否则内核校验按契约即抛，不静默降级）。
/// </para>
/// </summary>
public sealed class UsharpFlowObserver
{
    private readonly string _scopeId;
    private readonly string _entityType;
    private int _seq;

    /// <param name="scopeId">作用域键（对应 <c>[Obs] kind=scope</c> 声明）。</param>
    /// <param name="entityType">实体类型（须与事件契约的 <c>entity=</c> 一致）。</param>
    public UsharpFlowObserver(string scopeId, string entityType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityType);
        _scopeId = scopeId;
        _entityType = entityType;
    }

    /// <summary>
    /// 开始一个步骤的观测活动：<paramref name="emits"/> 为空 → 返回 null（该步未声明发射绑定，不埋点）。
    /// 调用方须显式终结（<c>Complete</c>/<c>Fail</c>/<c>Dispose</c>）。
    /// </summary>
    public Activity? Begin(string stepKey, IReadOnlyList<string> emits)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stepKey);
        ArgumentNullException.ThrowIfNull(emits);
        if (emits.Count == 0) return null;

        var entity = new EntityId(_scopeId, _entityType, Interlocked.Increment(ref _seq));
        var completion = emits.Count > 1 ? emits[1] : null;
        return Obs.Begin(entity, emits[0], completion);
    }

    /// <summary>以 <c>using</c> 语义包裹一个步骤：返回可释放句柄（未声明发射绑定 → 空句柄）。</summary>
    public IDisposable Step(string stepKey, IReadOnlyList<string> emits)
        => (IDisposable?)Begin(stepKey, emits) ?? NoopScope.Instance;

    private sealed class NoopScope : IDisposable
    {
        public static readonly NoopScope Instance = new();

        private NoopScope() { }

        public void Dispose() { }
    }
}
