using System.Collections.Concurrent;
using System.Reflection;
using BasicMethod.Declarative.StructuralWalk;
using DataCenter.Protocol.Standards;

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
    ScenarioBodyStandard? Scenario = null)
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
}

/// <summary>
/// impl 声明树的**唯一**「父→子」下钻规则与唯一遍历入口（前序 / 深度上限由零依赖骨架承担）。
/// <para>分工：表达式子槽 = 契约层表 <see cref="PortImplVocabulary.ExprSlots"/>（反射取值，零逐成员手写）；
/// 遍历算法 = <c>BasicMethod.Declarative.StructuralWalk</c>（显式栈 + 深度上限，禁各写递归）。</para>
/// <para>口径 = 迁移前两套规则的**并集**：取值来源侧（C8/C11）与步骤侧（C10）自此同源同域——
/// 步骤侧原先「看不到委托体内 / 链式体内步骤」的盲区随之闭合（对拍差异须在提交说明登记）。</para>
/// <para>深度上限 128：既防程序化构造的自引用图，也让截断可见（截断不回传违规，等同不判）。</para>
/// </summary>
internal static class StandardNodeWalk
{
    private static readonly StructuralWalkOptions Options = new(MaxDepth: 128);

    private static readonly ConcurrentDictionary<string, PropertyInfo> ExprProps = new(StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, ValueSourceStandard> EmptyBinds =
        new Dictionary<string, ValueSourceStandard>(StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, ScenarioBodyStandard> EmptyScenarios =
        new Dictionary<string, ScenarioBodyStandard>(StringComparer.Ordinal);

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

    /// <summary>唯一「父→子」实现：每种节点恰一处展开，禁另处再写递归。</summary>
    private static IEnumerable<StandardNode> Children(StandardNode node)
    {
        switch (node)
        {
            case { Impl: { } impl }:
                foreach (var bind in (impl.Binds ?? EmptyBinds).Values) yield return StandardNode.Of(bind);
                foreach (var depArg in ComposedArgs(impl.Deps)) yield return StandardNode.Of(AsValueSource(depArg));
                foreach (var callArg in ComposedArgs(impl.Calls)) yield return StandardNode.Of(AsValueSource(callArg));
                foreach (var scriptStep in impl.Script ?? []) yield return StandardNode.Of(scriptStep);
                foreach (var scopeNode in impl.Scopes ?? []) yield return StandardNode.Of(scopeNode);
                if (impl.Deliver is { } implDeliver) yield return StandardNode.Of(implDeliver);
                foreach (var scenarioNode in (impl.Scenarios?.Cases ?? EmptyScenarios).Values) yield return StandardNode.Of(scenarioNode);
                break;

            case { Expr: { } expr }:
                foreach (var child in ExprChildren(expr)) yield return child;
                break;

            case { Source: { } source }:
                if (source.Rows is { } rows) yield return StandardNode.Of(rows);
                if (source.Items is { } items) yield return StandardNode.Of(items);
                if (source.RowValue is { } rowValue) yield return StandardNode.Of(rowValue);
                if (source.Filter is { } filter) yield return StandardNode.Of(filter);
                if (source.Construct is { } construct) yield return StandardNode.Of(construct);
                foreach (var delegateStep in source.Delegate?.Steps ?? []) yield return StandardNode.Of(delegateStep);
                break;

            case { Step: { } step }:
                if (step.When is { } when) yield return StandardNode.Of(when);
                if (step.New is { } newStep) yield return StandardNode.Of(newStep);
                if (step.Invoke is { } invoke) yield return StandardNode.Of(invoke);
                if (step.Foreach is { } foreachStep)
                {
                    if (foreachStep.Items is { } foreachItems) yield return StandardNode.Of(foreachItems);
                    if (foreachStep.Where is { } where) yield return StandardNode.Of(where);
                    foreach (var body in foreachStep.Do ?? []) yield return StandardNode.Of(body);
                }
                break;
            case { New: { } newNode }:
                foreach (var newArg in newNode.Args ?? []) yield return StandardNode.Of(newArg);
                foreach (var chainNode in newNode.Chain ?? [])
                {
                    yield return StandardNode.Of(chainNode);
                    foreach (var chainArg in chainNode.Args ?? []) yield return StandardNode.Of(chainArg);
                }
                foreach (var init in newNode.Init ?? [])
                    if (init.Value is { } initValue) yield return StandardNode.Of(initValue);
                break;

            case { Invoke: { } invokeNode }:
                foreach (var invokeArg in invokeNode.Args ?? []) yield return StandardNode.Of(invokeArg);
                if (invokeNode.Value is { } invokeValue) yield return StandardNode.Of(invokeValue);
                break;

            case { Chain: { } chainOnly }:
                foreach (var chainOnlyArg in chainOnly.Args ?? []) yield return StandardNode.Of(chainOnlyArg);
                break;

            case { Scope: { } scopeScope }:
                if (scopeScope.Invoke is { } scopeInvoke) yield return StandardNode.Of(scopeInvoke);
                foreach (var scopeFile in scopeScope.Files ?? [])
                {
                    if (scopeFile.From is { } fileFrom) yield return StandardNode.Of(fileFrom);
                    if (scopeFile.Content is { } fileContent) yield return StandardNode.Of(fileContent);
                    if (scopeFile.Batch is { } fileBatch) yield return StandardNode.Of(fileBatch);
                    if (scopeFile.NameExpr is { } fileName) yield return StandardNode.Of(fileName);
                }
                foreach (var scopeSave in scopeScope.Save ?? [])
                    if (scopeSave.Set is { } saveSet) yield return StandardNode.Of(saveSet);
                foreach (var openStep in scopeScope.Open ?? []) yield return StandardNode.Of(openStep);
                foreach (var closeStep in scopeScope.Close ?? []) yield return StandardNode.Of(closeStep);
                break;

            case { Deliver: { } deliverNode }:
                if (deliverNode.Value is { } deliverValue) yield return StandardNode.Of(deliverValue);
                foreach (var deferredStep in deliverNode.DeferSteps ?? []) yield return StandardNode.Of(deferredStep);
                if (deliverNode.Capture is { } capture)
                {
                    yield return StandardNode.Of(capture.Call);
                    if (capture.When is { } captureWhen) yield return StandardNode.Of(captureWhen);
                }
                break;

            case { Scenario: { } scenarioBody }:
                foreach (var scenarioBind in (scenarioBody.Binds ?? EmptyBinds).Values) yield return StandardNode.Of(scenarioBind);
                foreach (var scenarioDepArg in ComposedArgs(scenarioBody.Deps)) yield return StandardNode.Of(AsValueSource(scenarioDepArg));
                foreach (var scenarioStep in scenarioBody.Script ?? []) yield return StandardNode.Of(scenarioStep);
                foreach (var scenarioScope in scenarioBody.Scopes ?? []) yield return StandardNode.Of(scenarioScope);
                if (scenarioBody.Deliver is { } scenarioDeliver) yield return StandardNode.Of(scenarioDeliver);
                break;


        }
    }

    /// <summary>表达式子槽展开：表驱动（<see cref="PortImplVocabulary.ExprSlots"/>）+ 反射取值，禁逐成员手写。</summary>
    private static IEnumerable<StandardNode> ExprChildren(ExprStandard expr)
    {
        foreach (var slot in PortImplVocabulary.ExprSlots)
        {
            if (!slot.IsChild) continue;

            var value = SlotValue(expr, slot.Member);
            switch (slot.Kind)
            {
                case ExprSlotKind.One:
                    if (value is ExprStandard one) yield return StandardNode.Of(one);
                    break;

                case ExprSlotKind.Many:
                    if (value is IReadOnlyList<ExprStandard> many)
                        foreach (var item in many) yield return StandardNode.Of(item);
                    break;

                case ExprSlotKind.Parts:
                    if (value is IReadOnlyList<ConcatPartStandard> parts)
                        foreach (var part in parts)
                        {
                            yield return StandardNode.Of(part.Expr);
                            if (part.When is { } gate) yield return StandardNode.Of(gate);
                        }
                    break;

                case ExprSlotKind.Source:
                    if (value is ValueSourceStandard source) yield return StandardNode.Of(source);
                    break;

                case ExprSlotKind.Invoke:
                    if (value is InvokeStandard invoke) yield return StandardNode.Of(invoke);
                    break;

                case ExprSlotKind.Chain:
                    if (value is ChainStandard chain) yield return StandardNode.Of(chain);
                    break;
            }
        }
    }

    /// <summary>按成员名取值（反射缓存）：成员缺失即抛——表与声明漂移必须当场可见（禁静默少下钻）。</summary>
    private static object? SlotValue(ExprStandard expr, string member)
        => ExprProps.GetOrAdd(member, name =>
                typeof(ExprStandard).GetProperty(name, BindingFlags.Public | BindingFlags.Instance)
                ?? throw new StandardViolationException(
                    $"ExprSlots 表与 ExprStandard 声明漂移：成员 '{member}' 不存在（须同步 PortImplVocabulary.ExprSlots）"))
            .GetValue(expr);

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

