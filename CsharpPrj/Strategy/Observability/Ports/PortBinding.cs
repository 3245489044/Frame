namespace Strategy.Observability.Ports;

using DataCenter.Protocol.Standards;

/// <summary>
/// 端口绑定（A2 装配收敛 · R5-1/R5-2）：端口键 + 唯一规格 + 实现归属（派生自 <see cref="Spec.Impl"/>）。
/// <para>
/// 绑定创建责任**自持**：按实现归属 <see cref="Kind"/>（不透明字符串，非种类枚举）数据驱动自建绑定实例
/// （<see cref="TryConstruct"/>），**不做种类穷举分派**。装配统一由
/// <see cref="PortComposer"/> 完成：本类只回答「给定声明能否构造出绑定，失败原因是什么」。
/// </para>
/// <para>
/// 构造全部收口到本类的静态工厂（<see cref="Frozen"/>/<see cref="Reflective"/>/
/// <see cref="FromComposed"/>/<see cref="FromScripted"/>/<see cref="Delegate"/>/<see cref="Unbound"/>，
/// 下称「计划构造点 = 本文件」）：上层只产出声明，不再各自择绑定类。
/// 冻结只登记不绑定（结构型端口转守卫面）；<see cref="Unbound"/> = 声明末落实现（fail-safe 记账）。
/// </para>
/// </summary>
public sealed record PortBinding(
    string PortKey,
    PortSpec Spec,
    PortBindingSpec? DeclSpec = null,
    ComposedBindingSpec? Composed = null,
    ScriptedBindingSpec? Scripted = null,
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
    /// 返回 <c>null</c> 并置 <paramref name="failReason"/> = 声明不完整 / 未落实现 / 未知种类（装配面据此 fail-safe 记 Unbound）。
    /// </summary>
    public IPortBinding? TryConstruct(out string? failReason)
    {
        var kind = Kind;

        if (kind == PortImplVocabulary.KindName.Reflective)
        {
            // 规格可下行 → 求值引擎；否则回退旧绑定类（R3-1 唯一择路，非种类分派）。
            if (DeclSpec is null)
            {
                failReason = "声明 reflective 绑定但缺少绑定声明（PortBindingSpec）";
                return null;
            }

            var spec = Spec.IsEvaluable ? Spec : PortSpecCompiler.FromReflective(PortKey, DeclSpec);
            failReason = null;

            if (spec.IsEvaluable) return new PortEvaluatorBinding(spec, null, Injection);

            PortBindingFallbackProbe.Record(PortKey, kind);   // 批 0 计量：回退旧绑定类（批 1 后本分支消失）
            return new ReflectiveBinding(DeclSpec);
        }

        if (kind == PortImplVocabulary.KindName.Composed)
        {
            if (Composed is null)
            {
                failReason = "声明 composed 绑定但缺少组合声明（ComposedBindingSpec）";
                return null;
            }

            // 规格已下行（计划构造点传入）→ 求值引擎；未下行（结构性判据不满足）→ 回退旧绑定类（既有诊断不变）。
            failReason = null;

            if (Spec.IsEvaluable) return new PortEvaluatorBinding(Spec, null, Injection);

            PortBindingFallbackProbe.Record(PortKey, kind);   // 批 0 计量：回退旧绑定类（批 1 后本分支消失）
            return new ComposedBinding(Composed);
        }

        if (kind == PortImplVocabulary.KindName.Scripted)
        {
            if (Scripted is null)
            {
                failReason = "声明 scripted 绑定但缺少脚本声明（ScriptedBindingSpec）";
                return null;
            }

            // 规格已下行（计划构造点传入）→ 求值引擎（执行单源）；未下行（无行为声明）→ 回退旧绑定类（既有诊断不变）。
            failReason = null;

            if (Spec.IsEvaluable) return new PortEvaluatorBinding(Spec, null, Injection);

            PortBindingFallbackProbe.Record(PortKey, kind);   // 批 0 计量：回退旧绑定类（批 1 后本分支消失）
            return new ScriptedBinding(Scripted);
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

    /// <summary>构造绑定并断言成功（旧替身面回填等「已知可建」场景用）；不可建即抛可定位异常。</summary>
    public IPortBinding RequireBinding()
        => TryConstruct(out var reason) ?? throw new InvalidOperationException(reason ?? "绑定声明不完整");

    /// <summary>冻结：只登记不绑定（理由缺省 = 只登记不绑定）。</summary>
    public static PortBinding Frozen(string portKey, string reason)
        => new(portKey, Shell(portKey, PortImplVocabulary.KindName.Frozen, null, reason));

    /// <summary>反射：按绑定声明下行规格（target/method/args 驱动；不可求值回退旧绑定类）。</summary>
    public static PortBinding Reflective(string portKey, PortBindingSpec spec)
        => new(portKey, PortSpecCompiler.FromReflective(portKey, spec), DeclSpec: spec);

    /// <summary>
    /// 组合：携带组合声明（calls/project/deps + 工厂表）与**已下行规格**
    /// （<paramref name="spec"/> = 计划构造点传入的 <c>PortSpecCompiler.Compile</c> 产物；null = 未下行）。
    /// </summary>
    public static PortBinding FromComposed(
        string portKey, ComposedBindingSpec composed, PortSpec? spec = null, PortInjection? injection = null)
        => new(
            portKey,
            spec ?? Shell(portKey, PortImplVocabulary.KindName.Composed, null),
            Composed: composed,
            Injection: injection);

    /// <summary>脚本：携带脚本声明（bind/deps/script/project/deliver + 工厂表）。</summary>
    public static PortBinding FromScripted(
        string portKey, ScriptedBindingSpec scripted, PortSpec? spec = null, PortInjection? injection = null)
        => new(
            portKey,
            spec ?? Shell(portKey, PortImplVocabulary.KindName.Scripted, null),
            Scripted: scripted,
            Injection: injection);

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