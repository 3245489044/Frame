using System;
using System.Collections.Generic;
using System.Linq;
using Modules.Node.Spec;
using Operation.DataStoreDriven.Abstractions;
using Operation.DataStoreDriven.ReferenceSchema;
using Operation.HierarchyMethod.Nodes.Circuit;
using Operation.HierarchyMethod.Nodes.Execution;
using Operation.HierarchyMethod.Nodes.Workflows;
using Share.CircuitFramework.Compiler;
using UniversalWorkflowFramework.Core;

namespace Operation.HierarchyMethod.Nodes.Relations;

/// <summary>
/// 工作流引用编译结果 —— 对齐 C# 编译器的「错误/警告/信息」三分输出。仅供 WorkflowCompiler 产出。
/// </summary>
public sealed class WorkflowCompileResult
{
    /// <summary>是否存在编译错误（IsValid=false）。</summary>
    public bool IsValid { get; private set; } = true;

    /// <summary>是否存在编译告警（不阻断执行，提示风险）。</summary>
    public bool HasWarnings { get; private set; }

    public IReadOnlyList<string> Errors => _errors;
    public IReadOnlyList<string> Warnings => _warnings;
    public IReadOnlyList<string> Info => _info;

    private readonly List<string> _errors = new();
    private readonly List<string> _warnings = new();
    private readonly List<string> _info = new();

    public int ErrorCount => _errors.Count;
    public int WarningCount => _warnings.Count;

    public void AddError(string message) { _errors.Add(message); IsValid = false; }

    public void AddWarning(string message) { _warnings.Add(message); HasWarnings = true; }

    public void AddInfo(string message) => _info.Add(message);

    public string GenerateReport()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"工作流引用编译: 错误 {ErrorCount} / 警告 {WarningCount}");
        foreach (var e in _errors) sb.AppendLine("  ❌ " + e);
        foreach (var w in _warnings) sb.AppendLine("  ⚠️ " + w);
        foreach (var i in _info) sb.AppendLine("  ℹ️ " + i);
        return sb.ToString();
    }
}

/// <summary>
/// 工作流编译器（仅编排，LLM-free）：把工作流引用关系（组合 + 控制流 + 端口类型契约）投影为图，依次执行既有校验器：
///   · DSL 悬空/重复/dep（复用 DslMethod ReferenceSchemaRegistry.Validate）
///   · 图完整性/悬空/重复节点（复用 GraphValidator.ValidateIntegrity，对齐 CS0103）
///   · 调用环检测（复用 CircularDependencyDetector.Analyze，对齐死循环风险）
///   · 孤立/未使用/缺失引用（复用 ReferenceDiagnostics.RunFullDiagnostics，对齐 CS0168 等）
///   · 端口类型不匹配（复用引用 Schema 端口类型签名，实参 vs 形参，对齐 CS0029）
/// 设计原则（SingleImplementation / TH-12）：本类只编排，算法全部委托既有组件，零新增校验逻辑、零 LLM；
/// 可空装配：registry/dslSchema 未装配时对应检查跳过并返回 Info 诊断（UnavailableDiagnostics 语义）。
/// 与 WorkflowReferenceStaticizer 同源消费 NodeRelation.DeriveWorkflowControlFlowEdges，仅只读校验，
/// 不写边、不产生编译产物（不依赖/不生成 reference-schema.generated.json）。
/// </summary>
public static class WorkflowCompiler
{
    /// <summary>编译一份工作流骨架。输入可空：无步骤 → 空诊断；registry/dslSchema 未装配 → 跳过对应检查（可空守卫）。</summary>
    public static WorkflowCompileResult Compile(
        RoundSpec? spec,
        IReferenceTypeRegistry? registry = null,
        Plate.DslMethod.Schema.ReferenceSchemaRegistry? dslSchema = null)
    {
        var result = new WorkflowCompileResult();
        if (spec is null || spec.Steps is not { Count: > 0 })
        {
            result.AddInfo($"工作流 '{spec?.Name}' 无步骤，跳过编译检查。");
            return result;
        }

        // ① DSL 声明校验（悬空边端/悬空约束/重复/dep 目标）—— 复用 DslMethod 校验逻辑
        if (dslSchema is not null)
            foreach (var issue in dslSchema.Validate())
                result.AddWarning($"[DSL] {issue.Message}");

        var (keys, edges) = WorkflowReferenceStaticizer.DeriveParts(spec);
        var nodeIds = keys.Select(k => NodeRelation.NodeId(spec.Name, k)).ToList();
        var callEdges = edges
            .Where(e => string.Equals(e.RefType, WorkflowReferenceTypes.Calls, StringComparison.Ordinal))
            .Select(e => (e.SourceId, e.TargetId))
            .ToList();

        // ②③ 链校验（悬空 / 重复 / 环 / 入口出口）—— 唯一算法出处 = 框架 NodeSpecValidator.ValidateChains
        //      （治 P19：语言级/框架级不得各写一套链判据；SingleImplementation / TH-12）
        CheckChain(result, spec, nodeIds);

        // ④ 邻接表构建 + 引用诊断（孤立/未使用）
        var deps = BuildAdjacency(callEdges);
        CheckDiagnostics(result, nodeIds, callEdges, deps);

        // ⑤ 端口类型签名（实参端口类型 ≠ 形参端口类型）—— 对齐 CS0029；registry 未装配 → 跳过
        if (registry is null)
            result.AddInfo("引用 Schema 注册表未装配，跳过端口类型检查（CS0029）。");
        else
            CheckPortTypes(result, spec, keys, registry);

        // ⑥ 框架结构权威可见化（P3 收敛）：见 CheckCircuitStructure。
        CheckCircuitStructure(result, spec);

        return result;
    }

    /// <summary>
    /// 框架结构权威（P3 收敛）：RoundSpec → 配方（<see cref="RoundFlowSpecMapper"/>）→ 框架唯一编译入口
    /// <see cref="CircuitCompiler.TryCompile"/>；结构性判红（产物唯一 / 缺失产物 / 环 / 拓扑 / 端口）以**告警**叠加可见化 ——
    /// 不改本结果**参考域** Error 契约（执行期由框架 <c>RoundFlowCircuitRegistry</c> 以 failFast 强判；本处仅静态对账）。
    /// 投影/编译异常按告警可见化（best-effort，不抛）。
    /// </summary>
    private static void CheckCircuitStructure(WorkflowCompileResult result, RoundSpec spec)
    {
        try
        {
            var recipe = RoundFlowSpecMapper.MapToRecipe(spec);
            if (CircuitCompiler.TryCompile(recipe, out _, out var diagnostics)) return;
            foreach (var d in diagnostics)
                result.AddWarning($"[电路结构 {d.Code}] {d.Message}");
        }
        catch (Exception ex)
        {
            result.AddWarning($"电路结构投影未完成（{ex.Message}）。");
        }
    }

    /// <summary>
    /// 链校验（悬空 / 重复 / 环 / 入口出口）—— 委托框架唯一算法 <see cref="NodeSpecValidator"/>.ValidateChains（治 P19）。
    /// 既有 <see cref="WorkflowCompileResult"/> 口径保持：环 → 告警（文案逐字）；悬空/重复/缺口 → 错误（对齐 CS0103）。
    /// </summary>
    private static void CheckChain(WorkflowCompileResult result, RoundSpec spec, IReadOnlyList<string> nodeIds)
    {
        var chain = BuildChainDocument(spec, nodeIds);
        // requireEntry=false 保既有口径（不强制入口）；requireExit=true 对齐既有「链须有终点」图完整性口径。
        var report = NodeSpecValidator.ValidateChainDocuments(new[] { chain }, requireEntry: false, requireExit: true);
        foreach (var violation in report.Violations)
        {
            if (string.Equals(violation.Code, "spec.chain-cycle", StringComparison.Ordinal))
                result.AddWarning($"检测到调用环（死循环风险）: {StripPrefix(violation.Message, "链存在环: ")}");
            else
                result.AddError($"图完整性校验失败: {violation.Message}");
        }
    }

    /// <summary>把线性 <see cref="RoundSpec"/> 步骤映射为框架链声明（稳定节点资源 Id 为链键）。</summary>
    private static NodeChainDocument BuildChainDocument(RoundSpec spec, IReadOnlyList<string> nodeIds)
    {
        var nodes = new List<NodeChainNode>(nodeIds.Count);
        for (var i = 0; i < nodeIds.Count; i++)
        {
            var step = spec.Steps[i];
            nodes.Add(new NodeChainNode(
                nodeIds[i],
                i + 1 < nodeIds.Count ? nodeIds[i + 1] : null,
                step.ParameterKeys,
                step.ResultKeys,
                IsEntry: i == 0,
                IsExit: i == nodeIds.Count - 1));
        }
        return new NodeChainDocument(spec.Name, nodes);
    }

    private static string StripPrefix(string message, string prefix)
        => message.StartsWith(prefix, StringComparison.Ordinal) ? message.Substring(prefix.Length) : message;

    private static void CheckDiagnostics(WorkflowCompileResult result, List<string> nodeIds, List<(string, string)> callEdges, Dictionary<string, List<string>> deps)
    {
        var allRefs = new Dictionary<string, IEnumerable<string>>(StringComparer.Ordinal);
        foreach (var id in nodeIds)
            allRefs[id] = deps.TryGetValue(id, out var list) ? list : Enumerable.Empty<string>();
        var used = new HashSet<string>(callEdges.Select(e => e.Item2), StringComparer.Ordinal);

        var dr = ReferenceDiagnostics.RunFullDiagnostics(allRefs, id => allRefs[id], () => used);
        foreach (var e in dr.Errors) result.AddError(e);
        foreach (var w in dr.Warnings) result.AddWarning(w);
        if (dr.OrphanedReferences > 0) result.AddWarning($"孤立引用 {dr.OrphanedReferences} 处。");
        if (dr.UnusedReferences > 0) result.AddWarning($"未使用引用 {dr.UnusedReferences} 处。");
    }

    private static void CheckPortTypes(WorkflowCompileResult result, RoundSpec spec, IReadOnlyList<string> keys, IReferenceTypeRegistry registry)
    {
        // 静态 N2N 实参绑定：下游 ParameterKey 由上游 ResultKey 产出（对齐运行期 NodeToNode 溯源，纯函数推导，零 LLM）。
        for (var i = 0; i < spec.Steps.Count; i++)
        {
            var step = spec.Steps[i];
            if (step.ParameterKeys is not { Count: > 0 }) continue;
            foreach (var param in step.ParameterKeys)
            {
                // 收集式输入端口（nodes.*）为整包标记，无类型签名：跳过 N2N 类型校验（不产 Info 噪音）。
                if (NodeExecutionKeys.IsCollectToken(param)) continue;
                for (var j = 0; j < i; j++)
                {
                    var up = spec.Steps[j];
                    if (up.ResultKeys is not { Count: > 0 } ||
                        !up.ResultKeys.Contains(param, StringComparer.Ordinal)) continue;

                    var srcResolved = registry.TryGetPortType(NodeResourceTypes.Node, param, isInput: false, out var srcType);
                    var tgtResolved = registry.TryGetPortType(NodeResourceTypes.Node, param, isInput: true, out var tgtType);
                    if (!srcResolved || !tgtResolved)
                    {
                        result.AddInfo($"端口 '{param}' 未声明类型签名（{keys[j]} → {keys[i]}），跳过类型校验。");
                        continue;
                    }
                    if (!string.Equals(srcType, tgtType, StringComparison.OrdinalIgnoreCase))
                        result.AddError($"类型不匹配（CS0029）: {keys[j]}.{param}:{srcType} → {keys[i]}.{param}:{tgtType}。");
                }
            }
        }
    }

    private static Dictionary<string, List<string>> BuildAdjacency(List<(string Source, string Target)> edges)
    {
        var deps = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (source, target) in edges)
        {
            if (!deps.TryGetValue(source, out var list)) deps[source] = list = new List<string>();
            list.Add(target);
        }
        return deps;
    }
}
