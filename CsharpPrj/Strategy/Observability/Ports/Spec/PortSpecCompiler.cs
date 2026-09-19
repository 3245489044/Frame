namespace Strategy.Observability.Ports;

using DataCenter.Protocol.Standards;

/// <summary>
/// 规格编译（R2-2b）：标准目录投影（<see cref="DataCenter.Protocol.Standards.PortStandard"/> → <see cref="PortSpec"/>，
/// 唯一规格模型）与反射声明 → <see cref="PortSpec"/>。
/// <para>
/// 口诀（沿用下行器既有纪律）：**下行不抛不降级** —— 未迁移族（scripted / delegate / frozen）产出
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
    /// 标准条目 → 规格（<c>reflective</c> 族 → <c>Program = [Call]</c>；<c>composed</c> 族 →
    /// <c>[Dep…, Call…, Project…]</c>；其余族 → 空 Program + IsEvaluable=false）。
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

        if (string.Equals(implRef.Kind, PortImplVocabulary.KindName.Reflective, StringComparison.Ordinal))
            return Reflective(standard.Key, implRef, impl!.Target, impl.Method, impl.Args ?? Array.Empty<PortArgStandard>());

        if (string.Equals(implRef.Kind, PortImplVocabulary.KindName.Composed, StringComparison.Ordinal))
            return Composed(standard, implRef);

        if (string.Equals(implRef.Kind, PortImplVocabulary.KindName.Scripted, StringComparison.Ordinal))
            return Scripted(standard, implRef);

        return new PortSpec(standard.Key, implRef, Array.Empty<PortParam>(), Array.Empty<PortInstr>(), false);
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

    /// <summary>
    /// 组合族下行（J2-1/J2-2）：<c>Program = [Dep…, Call…, Project…]</c>（声明序 = 执行序）——
    /// <c>Dep</c> 按声明序写依赖槽、<c>Call</c> 以引用名写槽并登记结果、<c>Project</c> 逐字段投影。
    /// <para>
    /// 结构性判据（与旧绑定类构造同口径，**不抛不降级**）：无调用 / 调用引用名重复 / 无投影 →
    /// 空 <c>Program</c> + <c>IsEvaluable = false</c>，由装配面回退旧绑定类（既有诊断与 Unbound 记账不变）。
    /// </para>
    /// <para>形参表为空：组合族的输入面是位置实参（<c>IPortInput</c>），不经反射族形参表。</para>
    /// </summary>
    private static PortSpec Composed(PortStandard standard, PortImplRef implRef)
    {
        var impl = standard.Impl!;
        var deps = impl.Deps ?? Array.Empty<DepStandard>();
        var calls = impl.Calls ?? Array.Empty<CallStandard>();
        var project = impl.Project ?? Array.Empty<ComposedFieldStandard>();

        var names = new HashSet<string>(StringComparer.Ordinal);
        var unique = calls.All(call => names.Add(call.As));

        if (calls.Count == 0 || project.Count == 0 || !unique)
            return new PortSpec(standard.Key, implRef, Array.Empty<PortParam>(), Array.Empty<PortInstr>(), false);

        var program = new List<PortInstr>(deps.Count + calls.Count + project.Count);

        foreach (var dep in deps)
            program.Add(new PortInstr(PortOp.Dep, Operand: dep));

        foreach (var call in calls)
            program.Add(new PortInstr(
                PortOp.Call, Out: call.As, Target: call.Target, Method: call.Method, Dep: call.Dep, Operand: call));

        foreach (var field in project)
            program.Add(new PortInstr(PortOp.Project, Operand: field));

        return new PortSpec(standard.Key, implRef, Array.Empty<PortParam>(), program, true);
    }

    /// <summary>
    /// 脚本族下行（J2-8）：<c>Program = Steps(script)</c>（步骤指令序列）+ **签名面**引用
    /// （载体 / 输入绑定 / 依赖 / 投影 / 作用域 / 场景 —— 一律 ① 标准记录引用，零运行期成员、零同形副本）。
    /// <para>
    /// 可求值判据（与旧绑定类构造同口径，**不抛不降级**）：有行为声明
    /// （script / project / 延迟步骤 / issue 捕获体 / 值载体表达式 / 场景）→ <c>IsEvaluable = true</c>；
    /// 无行为声明 → 空 <c>Program</c> + false（由旧绑定类给既有诊断，装配期 fail-safe 不变）。
    /// </para>
    /// </summary>
    private static PortSpec Scripted(PortStandard standard, PortImplRef implRef)
    {
        var impl = standard.Impl!;
        var deliver = impl.Deliver ?? new DeliverStandard();
        var program = Steps(impl.Script ?? Array.Empty<StepStandard>());
        var project = impl.ScriptProject ?? Array.Empty<FieldExprStandard>();

        var behaves = program.Count > 0 || project.Count > 0 || deliver.Capture is not null
            || deliver.DeferSteps is { Count: > 0 } || deliver.Value is not null || impl.Scenarios is not null;

        if (!behaves)
            return new PortSpec(standard.Key, implRef, Array.Empty<PortParam>(), Array.Empty<PortInstr>(), false);

        return new PortSpec(
            standard.Key, implRef, Array.Empty<PortParam>(), program, true,
            Deliver: deliver,
            Binds: impl.Binds,
            Deps: impl.Deps,
            ScriptProject: impl.ScriptProject,
            Scopes: impl.Scopes,
            Scenarios: impl.Scenarios);
    }

    /// <summary>
    /// 脚本步骤声明 → 指令序列（**通用框架的单向映射**；<c>Loop</c> 体递归编译）：
    /// 形态判别式（<c>new</c> / <c>invoke</c> / <c>foreach</c>）映射为操作码，步骤修饰
    /// （<c>When</c> / <c>Collects</c>）随指令同体承载（不另立指令，语义由步骤执行单源消费）。
    /// <para>
    /// 形态未声明的步骤 → **无操作数指令**（执行期按既有文案 fail-fast，不静默）；
    /// <c>Loop</c> 不写槽（循环变量由执行期入栈管理，<c>Out</c> 仅为形态保留位）。
    /// </para>
    /// </summary>
    internal static IReadOnlyList<PortInstr> Steps(IReadOnlyList<StepStandard> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);

        var program = new List<PortInstr>(steps.Count);
        foreach (var step in steps) program.Add(Step(step));

        return program;
    }

    /// <summary>单步映射（声明 → 指令；形态判别式 ↔ 操作码）。</summary>
    private static PortInstr Step(StepStandard step)
    {
        if (step.New is { } construct)
            return new PortInstr(
                PortOp.New, Out: construct.As, Operand: construct, When: step.When, Collects: step.Collects);

        if (step.Invoke is { } invoke)
            return new PortInstr(
                PortOp.Call, Out: invoke.As, Target: invoke.Target, Method: invoke.Method,
                Dep: invoke.Dep, Operand: invoke, When: step.When, Collects: step.Collects);

        if (step.Foreach is { } loop)
            return new PortInstr(
                PortOp.Loop, Operand: loop, Body: Steps(loop.Do ?? Array.Empty<StepStandard>()),
                When: step.When, Collects: step.Collects);

        return new PortInstr(PortOp.New, When: step.When, Collects: step.Collects);
    }
}