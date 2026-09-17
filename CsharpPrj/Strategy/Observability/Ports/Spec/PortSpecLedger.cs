namespace Strategy.Observability.Ports;

/// <summary>
/// 规格下行器（R2-2b）：台账 JSON / 反射声明 → <see cref="PortSpec"/>（唯一规格模型）。
/// <para>
/// 口诀：**下行不抛不降级** —— 未迁移族（composed / scripted / delegate / frozen）产出
/// <see cref="PortSpec.Program"/> 为空且 <see cref="PortSpec.IsEvaluable"/> = false，由装配面按该判据择路
/// （路线 §2.7 I-4：禁种类分派）。
/// </para>
/// <para>
/// 与 R2-2a 的衔接：<see cref="PortLedgerEntry"/>（声明视图）在迁移期为未迁移族的数据载体；
/// <see cref="PortSpec"/> 为终态形态，R2-3 时前者收窄为后者的投影。
/// </para>
/// </summary>
public static class PortSpecLedger
{
    /// <summary>台账 JSON → 规格序列（文件序 = 声明序；不含 kinds 校验，非法形态由 <see cref="PortLedger"/> 抛出）。</summary>
    public static IReadOnlyList<PortSpec> Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        var specs = new List<PortSpec>();
        foreach (var entry in PortLedger.Parse(json))
            specs.Add(FromEntry(entry));

        return specs;
    }

    /// <summary>
    /// 反射声明 → 规格（装配面按已有声明择路用；不经 JSON 往返，保持声明来源单一）。
    /// </summary>
    public static PortSpec FromReflective(string portKey, PortBindingSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var impl = new PortImplRef("reflective", Target: spec.Target, Method: spec.Method);
        return Reflective(portKey, impl, spec.Target, spec.Method, spec.Args);
    }

    /// <summary>声明视图 → 规格（未迁移族：空 Program + IsEvaluable=false）。</summary>
    private static PortSpec FromEntry(PortLedgerEntry entry)
    {
        var impl = new PortImplRef(
            entry.Kind, entry.Id, entry.Target, entry.Method, entry.Reason, entry.LegacyKind, entry.ProductFields);

        if (!string.Equals(entry.Kind, "reflective", StringComparison.Ordinal))
            return new PortSpec(entry.PortKey, impl, Array.Empty<PortParam>(), Array.Empty<PortInstr>(), false);

        return Reflective(entry.PortKey, impl, entry.Target, entry.Method, entry.Args);
    }

    /// <summary>
    /// 反射族下行：<c>Program = [Call{Target, Method, Args}]</c>；<c>Params</c> ← <c>Args</c> 逐位
    /// （位序 = 声明序，名未知故为 null，取值声明经 R2-1 的唯一转换表投影）。
    /// <para>目标类型 / 方法缺失（拼写漂移）→ 为空 Program（不可求值），由旧绑定类在装配期给出既有诊断。</para>
    /// </summary>
    private static PortSpec Reflective(
        string portKey, PortImplRef impl, string? target, string? method, IReadOnlyList<PortArgSpec> args)
    {
        var parameters = new PortParam[args.Count];
        for (var index = 0; index < args.Count; index++)
            parameters[index] = new PortParam(index, null, args[index].Options);

        var complete = !string.IsNullOrWhiteSpace(target) && !string.IsNullOrWhiteSpace(method);
        var program = complete
            ? new[] { new PortInstr(PortOp.Call, Target: target, Method: method, Args: args) }
            : Array.Empty<PortInstr>();

        return new PortSpec(portKey, impl, parameters, program, complete);
    }
}
