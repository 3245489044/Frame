namespace Strategy.Observability.Ports;

using DataCenter.Protocol.Standards;
using Strategy.Observability.Diagnostics;

/// <summary>
/// 端口绑定（A2 装配收敛 · R5-1/R5-2；批 1 收敛）：端口键 + **唯一规格** + 实现归属（派生自 <see cref="Spec.Impl"/>）。
/// <para>
/// 绑定创建责任**自持**：按实现归属 <see cref="Kind"/>（不透明字符串，非种类枚举）数据驱动自建绑定实例
/// （<see cref="TryConstruct"/>），**不做种类穷举分派**。装配统一由
/// <see cref="PortComposer"/> 完成：本类只回答「给定声明能否构造出绑定，失败原因是什么」。
/// </para>
/// <para>
/// 批 1 收敛：③ 绑定声明家族（<c>*BindingSpec</c>）退场后，本类**只承载唯一规格**
/// （<see cref="PortSpec"/>）—— 声明驱动族（reflective / composed / scripted）一律经求值引擎；
/// 规格不可求值即按 <see cref="PortSpec.TerminalReason"/> 抛出**既有诊断**（异常型与文案逐字不变），
/// 由装配面转记 Unbound（fail-safe 记账口径不变）。
/// </para>
/// <para>冻结只登记不绑定（结构型端口转守卫面）；<see cref="Unbound"/> = 声明末落实现（fail-safe 记账）。</para>
/// </summary>
public sealed record PortBinding(
    string PortKey,
    PortSpec Spec,
    Func<IPortBinding>? Factory = null,
    string? Reason = null,
    PortInjection? Injection = null)
{
    /// <summary>实现归属（派生自规格；携带只读驱动元数据 kind/id/target/method/reason）。</summary>
    public PortImplRef Impl => Spec.Impl;

    /// <summary>绑定方式（实现归属 kind 字符串：<c>reflective</c>/<c>composed</c>/<c>scripted</c>/<c>delegate</c>/<c>frozen</c>）。</summary>
    public string Kind => Impl.Kind;

    /// <summary>
    /// 单点绑定构造（唯一绑定类构建处）：按 <see cref="Kind"/> 数据驱动（if/else 判据，禁种类穷举）自建绑定实例；
    /// 返回 <c>null</c> 并置 <paramref name="failReason"/> = 未落实现 / 未知种类（装配面据此 fail-safe 记 Unbound）。
    /// <para>声明驱动族不可求值 → 抛 <see cref="PortBindingViolationException"/>（既有诊断逐字），由装配面记账。</para>
    /// </summary>
    public IPortBinding? TryConstruct(out string? failReason)
    {
        var kind = Kind;

        if (kind is PortImplVocabulary.KindName.Reflective
            or PortImplVocabulary.KindName.Composed
            or PortImplVocabulary.KindName.Scripted)
        {
            if (!Spec.IsEvaluable)
                throw new PortBindingViolationException(
                    PortKey,
                    Spec.TerminalReason
                    ?? $"端口 '{PortKey}' 的规格不可求值但未给出原因（编译器缺陷：须置 TerminalReason）");

            failReason = null;
            return new PortEvaluatorBinding(Spec, null, Injection);
        }

        if (kind == PortImplVocabulary.KindName.Delegate)
        {
            if (Factory is null)
            {
                failReason = Reason ?? "声明 delegate 绑定但未提供实现工厂";
                return null;
            }

            failReason = null;
            return Factory();
        }

        failReason = $"未知绑定计划种类 '{kind}'";
        return null;
    }

    /// <summary>构造绑定并断言成功（已知可建场景用）；不可建即抛可定位异常。</summary>
    public IPortBinding RequireBinding()
        => TryConstruct(out var reason) ?? throw new InvalidOperationException(reason ?? "绑定声明不完整");

    /// <summary>冻结：只登记不绑定（理由缺省 = 只登记不绑定）。</summary>
    public static PortBinding Frozen(string portKey, string reason)
        => new(portKey, Shell(portKey, PortImplVocabulary.KindName.Frozen, null, reason));

    /// <summary>
    /// 声明驱动族：承载**唯一规格**（reflective / composed / scripted 一律经求值引擎）。
    /// 规格不可求值者不在装配期静默建壳 —— <see cref="TryConstruct"/> 按 <see cref="PortSpec.TerminalReason"/> fail-fast。
    /// </summary>
    public static PortBinding FromSpec(string portKey, PortSpec spec, PortInjection? injection = null)
        => new(portKey, spec ?? throw new ArgumentNullException(nameof(spec)), Injection: injection);

    /// <summary>委派：携带实现工厂（构建即得绑定实例）。</summary>
    public static PortBinding Delegate(string portKey, string? id, Func<IPortBinding> factory)
        => new(portKey, Shell(portKey, PortImplVocabulary.KindName.Delegate, id), Factory: factory);

    /// <summary>未绑定：声明给定原因（fail-safe 记账入口；工厂缺省 = null → 装配记 Unbound）。</summary>
    public static PortBinding Unbound(string portKey, string? id, string reason)
        => new(portKey, Shell(portKey, PortImplVocabulary.KindName.Delegate, id, reason), Reason: reason);

    /// <summary>壳规格：未迁移族（composed/scripted/delegate/frozen）的规格形态 —— 空 Program + IsEvaluable=false（PortSpecCompiler 下行不抛不降级）。</summary>
    private static PortSpec Shell(string portKey, string kind, string? id, string? reason = null)
        => new(
            portKey,
            new PortImplRef(kind, Id: id, Reason: reason),
            Array.Empty<PortParam>(),
            Array.Empty<PortInstr>(),
            false);
}