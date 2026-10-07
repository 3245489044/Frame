using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using BasicMethod.Declarative.StructuralWalk;
using DataCenter.Protocol.Standards;
using DataCenter.Protocol.Standards.Descriptors;
using MemberDescriptor = DataCenter.Protocol.Standards.Descriptors.MemberDescriptor;

namespace DataCenter.Standards;

/// <summary>
/// impl 声明树的统一节点视图（判别式联合：恰一字段非空）。
/// <para>用途：让「取值来源全域」与「步骤全域」共用一份下钻规则与一次遍历，取代迁移前两套分头手写的递归。</para>
/// </summary>
internal readonly record struct StandardNode(
    PortImplStandard? Impl = null,
    ExprStandard? Expr = null,
    ValueSourceStandard? Source = null,
    StepStandard? Step = null,
    NewStandard? New = null,
    InvokeStandard? Invoke = null,
    ChainStandard? Chain = null,
    ScopeStandard? Scope = null,
    DeliverStandard? Deliver = null,
    ScenarioBodyStandard? Scenario = null,
    DelegateStandard? Delegate = null,
    InitStandard? Init = null,
    SaveStandard? Save = null,
    FileStandard? File = null,
    ConcatPartStandard? ConcatPart = null)
{
    internal static StandardNode Of(PortImplStandard v) => new(Impl: v);
    internal static StandardNode Of(ExprStandard v) => new(Expr: v);
    internal static StandardNode Of(ValueSourceStandard v) => new(Source: v);
    internal static StandardNode Of(StepStandard v) => new(Step: v);
    internal static StandardNode Of(NewStandard v) => new(New: v);
    internal static StandardNode Of(InvokeStandard v) => new(Invoke: v);
    internal static StandardNode Of(ChainStandard v) => new(Chain: v);
    internal static StandardNode Of(ScopeStandard v) => new(Scope: v);
    internal static StandardNode Of(DeliverStandard v) => new(Deliver: v);
    internal static StandardNode Of(ScenarioBodyStandard v) => new(Scenario: v);
    internal static StandardNode Of(DelegateStandard v) => new(Delegate: v);
    internal static StandardNode Of(InitStandard v) => new(Init: v);
    internal static StandardNode Of(SaveStandard v) => new(Save: v);
    internal static StandardNode Of(FileStandard v) => new(File: v);
    internal static StandardNode Of(ConcatPartStandard v) => new(ConcatPart: v);
}

/// <summary>
/// impl 声明树的**唯一**「父→子」下钻规则与唯一遍历入口（前序 / 深度上限由零依赖骨架承担）。
/// <para>
/// 下钻规则唯一 = SSD（<see cref="StandardDescriptors"/>）的 <see cref="MemberDescriptor.IsChild"/>（<c>Child</c>/<c>Children</c>/<c>KeyedMap</c>）
/// —— 常规层逐成员反射取值、按 <c>ChildLayer</c> 的承载模型造节点（见 <see cref="Expand"/>），禁逐成员手写；
/// 子层模型无节点工厂者视为**叶**（不下钻）。
/// </para>
/// <para>
/// **显式特例（登记，非静默）**：① 组合族实参以**取值来源形态**合成参与遍历（只取 <c>Dep</c>/<c>Index</c>/<c>Key</c>——组合实参无 <c>from</c>，故不触发 C8，见 <see cref="AsValueSource"/>）；
/// ② **扁平多模型**层（<c>step</c> 四形态 + <c>foreach</c> 子层、<c>deliver.capture</c> 内联）逐字段手写；
/// ③ **未登记层** <c>scenarioBody</c>（<c>Scenario.Cases</c> 的子层，无独立描述符）逐案展开。
/// </para>
/// <para>
/// 分工：遍历算法 = <c>BasicMethod.Declarative.StructuralWalk</c>（显式栈 + 深度上限，禁各写递归）。
/// 深度上限 128：既防程序化构造的自引用图，也让截断可见（截断不回传违规，等同不判）。
/// </para>
/// </summary>
internal static class StandardNodeWalk
{
    private static readonly StructuralWalkOptions Options = new(MaxDepth: 128);

    private static readonly ConcurrentDictionary<(Type Model, string Member), PropertyInfo> Properties = new();

    private static readonly IReadOnlyDictionary<string, ValueSourceStandard> EmptyBinds =
        new Dictionary<string, ValueSourceStandard>(StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, ScenarioBodyStandard> EmptyScenarios =
        new Dictionary<string, ScenarioBodyStandard>(StringComparer.Ordinal);

    /// <summary>子层承载模型 → 标准节点工厂；未登记者 = 叶（不下钻）。</summary>
    private static readonly IReadOnlyDictionary<Type, Func<object, StandardNode>> NodeFactories =
        new Dictionary<Type, Func<object, StandardNode>>
        {
            [typeof(PortImplStandard)] = value => StandardNode.Of((PortImplStandard)value),
            [typeof(ExprStandard)] = value => StandardNode.Of((ExprStandard)value),
            [typeof(ValueSourceStandard)] = value => StandardNode.Of((ValueSourceStandard)value),
            [typeof(StepStandard)] = value => StandardNode.Of((StepStandard)value),
            [typeof(NewStandard)] = value => StandardNode.Of((NewStandard)value),
            [typeof(InvokeStandard)] = value => StandardNode.Of((InvokeStandard)value),
            [typeof(ChainStandard)] = value => StandardNode.Of((ChainStandard)value),
            [typeof(ScopeStandard)] = value => StandardNode.Of((ScopeStandard)value),
            [typeof(DeliverStandard)] = value => StandardNode.Of((DeliverStandard)value),
            [typeof(ScenarioBodyStandard)] = value => StandardNode.Of((ScenarioBodyStandard)value),
            [typeof(DelegateStandard)] = value => StandardNode.Of((DelegateStandard)value),
            [typeof(InitStandard)] = value => StandardNode.Of((InitStandard)value),
            [typeof(SaveStandard)] = value => StandardNode.Of((SaveStandard)value),
            [typeof(FileStandard)] = value => StandardNode.Of((FileStandard)value),
            [typeof(ConcatPartStandard)] = value => StandardNode.Of((ConcatPartStandard)value),
        };

    /// <summary>前序遍历 impl 声明树（唯一入口）。</summary>
    internal static StructuralWalkStats Visit(PortImplStandard impl, Action<StandardNode> visit)
        => StructuralWalk.Visit(StandardNode.Of(impl), Children, visit, Options);

    /// <summary>前序遍历全部 <see cref="ValueSourceStandard"/>（bind / 实参 / 表达式源 / 行集 / 委实体），供 C8/C11。</summary>
    internal static void VisitValueSources(PortImplStandard impl, Action<ValueSourceStandard> visit)
        => Visit(impl, node =>
        {
            if (node.Source is { } source) visit(source);
        });

    /// <summary>前序遍历全部 <see cref="StepStandard"/>（脚本 / 作用域开闭 / 延迟动作 / 场景 / 委托体内），供 C10。</summary>
    internal static void VisitSteps(PortImplStandard impl, Action<StepStandard> visit)
        => Visit(impl, node =>
        {
            if (node.Step is { } step) visit(step);
        });

    /// <summary>唯一「父→子」实现（常规层 = SSD 的 <c>IsChild</c> 驱动；特例见类注记）。</summary>
    private static IEnumerable<StandardNode> Children(StandardNode node)
    {
        switch (node)
        {
            case { Impl: { } impl }:
                foreach (var child in Expand(StandardDescriptors.Impl, impl, ImplSpecials)) yield return child;
                break;

            case { Expr: { } expr }:
                foreach (var child in Expand(StandardDescriptors.Expr, expr)) yield return child;
                break;

            case { Source: { } source }:
                foreach (var child in Expand(StandardDescriptors.ValueSource, source)) yield return child;
                break;

            case { Delegate: { } delegateNode }:
                foreach (var child in Expand(StandardDescriptors.Delegate, delegateNode)) yield return child;
                break;

            case { Init: { } init }:
                foreach (var child in Expand(StandardDescriptors.Init, init)) yield return child;
                break;

            case { Save: { } save }:
                foreach (var child in Expand(StandardDescriptors.Save, save)) yield return child;
                break;

            case { File: { } file }:
                foreach (var child in Expand(StandardDescriptors.File, file)) yield return child;
                break;

            case { ConcatPart: { } part }:
                foreach (var child in Expand(StandardDescriptors.ConcatPart, part)) yield return child;
                break;

            case { New: { } newNode }:
                foreach (var child in Expand(StandardDescriptors.New, newNode)) yield return child;
                break;

            case { Invoke: { } invokeNode }:
                foreach (var child in Expand(StandardDescriptors.Invoke, invokeNode)) yield return child;
                break;

            case { Chain: { } chainNode }:
                foreach (var child in Expand(StandardDescriptors.Chain, chainNode)) yield return child;
                break;

            case { Scope: { } scopeNode }:
                foreach (var child in Expand(StandardDescriptors.Scope, scopeNode)) yield return child;
                break;

            case { Deliver: { } deliverNode }:
                foreach (var child in Expand(StandardDescriptors.Deliver, deliverNode, DeliverSpecials)) yield return child;
                break;

            // ── 特例：扁平多模型 / 未登记层（显式登记，见类注记）─────────────────────────
            case { Step: { } step }:
                foreach (var child in StepChildren(step)) yield return child;
                break;

            case { Scenario: { } scenarioBody }:
                foreach (var child in ScenarioChildren(scenarioBody)) yield return child;
                break;
        }
    }

    /// <summary>
    /// SSD 驱动的子节点展开：逐 <see cref="MemberDescriptor.IsChild"/> 成员（声明序）反射取值；
    /// 子层承载模型无节点工厂 ⇒ 叶（不下钻）；<paramref name="memberSpecials"/> 覆盖无工厂的特例成员（合成实参 / 内联载体）。
    /// </summary>
    private static IEnumerable<StandardNode> Expand(
        LayerDescriptor layer,
        object model,
        IReadOnlyDictionary<string, Func<object, IEnumerable<StandardNode>>>? memberSpecials = null)
    {
        foreach (var member in layer.Members)
        {
            if (!member.IsChild) continue;

            if (memberSpecials is not null && memberSpecials.TryGetValue(member.Member, out var special))
            {
                foreach (var child in special(model)) yield return child;
                continue;
            }

            foreach (var child in MemberChildren(layer, member, model)) yield return child;
        }
    }

    /// <summary>单成员子节点（形态：<c>Child</c> / <c>Children</c> / <c>KeyedMap</c> 取映射值；子层无工厂 ⇒ 叶）。</summary>
    private static IEnumerable<StandardNode> MemberChildren(LayerDescriptor layer, MemberDescriptor member, object model)
    {
        var value = PropertyOf(layer.Model, member.Member).GetValue(model);
        if (value is null) yield break;

        if (member.Kind == MemberKind.Child)
        {
            if (ChildOf(value) is { } single) yield return single;
            yield break;
        }

        if (value is not IEnumerable items) yield break;

        foreach (var item in items)
        {
            var child = member.Kind == MemberKind.KeyedMap ? MapValue(item) : item;
            if (child is not null && ChildOf(child) is { } node) yield return node;
        }
    }

    /// <summary>键值映射项的 <c>Value</c>（反射取用，兼容任何 <c>IReadOnlyDictionary</c> 实现）。</summary>
    private static object? MapValue(object pair)
        => pair.GetType().GetProperty("Value")?.GetValue(pair);

    /// <summary>子对象 → 标准节点（承载模型未登记工厂 ⇒ null = 叶）。</summary>
    private static StandardNode? ChildOf(object child)
        => NodeFactories.TryGetValue(child.GetType(), out var factory) ? factory(child) : null;

    /// <summary>按承载模型取值（反射缓存）：成员缺失即抛——SSD 与声明漂移必须当场可见（禁静默少下钻）。</summary>
    private static PropertyInfo PropertyOf(Type model, string member)
        => Properties.GetOrAdd((model, member), key =>
            key.Model.GetProperty(key.Member, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new StandardViolationException(
                $"SSD 与声明漂移：{key.Model.Name} 无成员 '{key.Member}'（须同步 StandardDescriptors）"));

    /// <summary>特例成员（无节点工厂）：组合族实参合成取值来源；场景主体逐案展开。</summary>
    private static readonly IReadOnlyDictionary<string, Func<object, IEnumerable<StandardNode>>> ImplSpecials =
        new Dictionary<string, Func<object, IEnumerable<StandardNode>>>(StringComparer.Ordinal)
        {
            [nameof(PortImplStandard.Deps)] = model =>
                ComposedArgs(((PortImplStandard)model).Deps).Select(arg => StandardNode.Of(AsValueSource(arg))),
            [nameof(PortImplStandard.Calls)] = model =>
                ComposedArgs(((PortImplStandard)model).Calls).Select(arg => StandardNode.Of(AsValueSource(arg))),
            [nameof(PortImplStandard.Scenarios)] = model => ScenarioCases(((PortImplStandard)model).Scenarios),
        };

    /// <summary>特例成员（无节点工厂）：<c>deliver.capture</c> 内联展开（扁平多模型）。</summary>
    private static readonly IReadOnlyDictionary<string, Func<object, IEnumerable<StandardNode>>> DeliverSpecials =
        new Dictionary<string, Func<object, IEnumerable<StandardNode>>>(StringComparer.Ordinal)
        {
            [nameof(DeliverStandard.Capture)] = model => CaptureChildren(((DeliverStandard)model).Capture),
        };

    /// <summary>场景表的逐案主体（开放映射 <c>scenarios.cases</c> → <c>scenarioBody</c>）。</summary>
    private static IEnumerable<StandardNode> ScenarioCases(ScenarioStandard? scenarios)
    {
        foreach (var body in (scenarios?.Cases ?? EmptyScenarios).Values) yield return StandardNode.Of(body);
    }

    /// <summary>issue 承载捕获（<c>deliver.capture</c>：调用体 + 门控）。</summary>
    private static IEnumerable<StandardNode> CaptureChildren(DeliverCaptureStandard? capture)
    {
        if (capture is null) yield break;

        yield return StandardNode.Of(capture.Call);
        if (capture.When is { } when) yield return StandardNode.Of(when);
    }

    /// <summary>特例层 <c>step</c>（扁平多模型：四形态 + <c>when</c> 修饰 + <c>foreach</c> 子层）。</summary>
    private static IEnumerable<StandardNode> StepChildren(StepStandard step)
    {
        if (step.When is { } when) yield return StandardNode.Of(when);
        if (step.New is { } newStep) yield return StandardNode.Of(newStep);
        if (step.Invoke is { } invoke) yield return StandardNode.Of(invoke);
        if (step.Foreach is { } foreachStep)
        {
            if (foreachStep.Items is { } items) yield return StandardNode.Of(items);
            if (foreachStep.Where is { } where) yield return StandardNode.Of(where);
            foreach (var body in foreachStep.Do ?? []) yield return StandardNode.Of(body);
        }
    }

    /// <summary>特例层 <c>scenarioBody</c>（未登记层；逐案主体展开）。</summary>
    private static IEnumerable<StandardNode> ScenarioChildren(ScenarioBodyStandard body)
    {
        foreach (var bind in (body.Binds ?? EmptyBinds).Values) yield return StandardNode.Of(bind);
        foreach (var depArg in ComposedArgs(body.Deps)) yield return StandardNode.Of(AsValueSource(depArg));
        foreach (var step in body.Script ?? []) yield return StandardNode.Of(step);
        foreach (var scope in body.Scopes ?? []) yield return StandardNode.Of(scope);
        if (body.Deliver is { } deliver) yield return StandardNode.Of(deliver);
    }

    /// <summary>组合族实参以取值来源形态参与遍历（只取其 Dep 伴生字段——组合实参无 from，故不触发 C8）。</summary>
    private static ValueSourceStandard AsValueSource(ComposedArgStandard arg)
        => new(Dep: arg.Dep, Index: arg.ArgIndex, Key: arg.Key);

    private static IEnumerable<ComposedArgStandard> ComposedArgs(IReadOnlyList<DepStandard>? deps)
    {
        foreach (var dep in deps ?? [])
            foreach (var arg in dep.Args ?? [])
                yield return arg;
    }

    private static IEnumerable<ComposedArgStandard> ComposedArgs(IReadOnlyList<CallStandard>? calls)
    {
        foreach (var call in calls ?? [])
            foreach (var arg in call.Args ?? [])
                yield return arg;
    }
}
