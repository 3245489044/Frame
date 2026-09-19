namespace Strategy.Observability.Ports;

using DataCenter.Protocol.Standards;

/// <summary>
/// 规格编译（R2-2b）：标准目录投影（<see cref="DataCenter.Protocol.Standards.PortStandard"/> → <see cref="PortSpec"/>，
/// 唯一规格模型）与反射声明 → <see cref="PortSpec"/>。
/// <para>
/// 口诀（沿用下行器既有纪律）：**下行不抛不降级** —— 未迁移族（composed / scripted / delegate / frozen）产出
/// <see cref="PortSpec.Program"/> 为空且 <see cref="PortSpec.IsEvaluable"/> = false，由装配面按该判据择路
/// （路线 §2.7 I-4：禁种类分派）。
/// </para>
/// <para>
/// B5 内核退场：本件**不再自行解析 JSON**，入参为已上移标准层的 <see cref="PortStandard"/>（其 <see cref="PortImplStandard"/>
/// 是 <c>impl</c> 声明的结构化中立形态）。解析与校验由 <c>DataCenter.Standards.StandardCatalog.Load</c> / 结构门承担。
/// 旧 <c>PortLedger.Parse(json)</c> 入口自此移除。
/// </para>
/// </summary>
public static class PortSpecCompiler
{
    /// <summary>
    /// 标准条目 → 规格（<c>PortStandard.Impl</c> 为反射族 → <c>Program = [Call]</c>；
    /// 其余族 → 空 Program + IsEvaluable=false）。
    /// <para>
    /// SK-28：<c>impl</c> **缺省已在读取面被拒**（<c>StandardJson.NormalizeKind</c> 必填）；此处保留 <c>impl == null</c>
    /// 的形状兜底（模型可空），按**遗留** <c>delegate</c> 语义处理，不再是声明缺省。
    /// </para>
    /// </summary>
    public static PortSpec Compile(PortStandard standard)
    {
        ArgumentNullException.ThrowIfNull(standard);
        var impl = standard.Impl;

        var implRef = new PortImplRef(
            impl?.Kind ?? PortImplVocabulary.KindName.Delegate,
            impl?.Id,
            impl?.Target,
            impl?.Method,
            impl?.Reason,
            impl?.LegacyKind,
            impl?.ProductFields ?? string.Empty);

        if (!string.Equals(implRef.Kind, PortImplVocabulary.KindName.Reflective, StringComparison.Ordinal))
            return new PortSpec(standard.Key, implRef, Array.Empty<PortParam>(), Array.Empty<PortInstr>(), false);

        return Reflective(standard.Key, implRef, impl!.Target, impl.Method, impl.Args ?? Array.Empty<PortArgStandard>());
    }

    /// <summary>
    /// 反射声明 → 规格（装配面按已有声明择路用；不经标准目录往返，保持声明来源单一）。
    /// </summary>
    public static PortSpec FromReflective(string portKey, PortBindingSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var impl = new PortImplRef(PortImplVocabulary.KindName.Reflective, Target: spec.Target, Method: spec.Method);
        return Reflective(portKey, impl, spec.Target, spec.Method, spec.Args);
    }

    /// <summary>
    /// 反射族下行：<c>Program = [Call{Target, Method, Args}]</c>；<c>Params</c> ← <c>Args</c> 逐位
    /// （位序 = 声明序，名未知故为 null，取值声明经唯一转换表视图投影）。
    /// <para>
    /// 实参声明**不再中转**：标准族实参（<see cref="PortArgStandard"/>）即内核声明形态（H2 单源化），
    /// 旧 <c>MapArgs</c> 逐位复制通道随之退场（同字段集多份翻译 = 静默丢字段面）。
    /// </para>
    /// <para>目标类型 / 方法缺失（拼写漂移）→ 为空 Program（不可求值），由旧绑定类在装配期给出既有诊断。</para>
    /// </summary>
    private static PortSpec Reflective(
        string portKey, PortImplRef impl, string? target, string? method, IReadOnlyList<PortArgStandard> args)
    {
        var parameters = new PortParam[args.Count];
        for (var index = 0; index < args.Count; index++)
            parameters[index] = new PortParam(index, null, args[index].Options());

        var complete = !string.IsNullOrWhiteSpace(target) && !string.IsNullOrWhiteSpace(method);
        var program = complete
            ? new[] { new PortInstr(PortOp.Call, Target: target, Method: method, Args: args) }
            : Array.Empty<PortInstr>();

        return new PortSpec(portKey, impl, parameters, program, complete);
    }
}