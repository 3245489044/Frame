using System;
using System.Collections.Generic;
using System.Linq;
using Operation.HierarchyMethod.Judgment;
using Operation.HierarchyMethod.Nodes;
using Operation.HierarchyMethod.Nodes.Workflows;
using Share.Composition.Text;

namespace Operation.HierarchyMethod.Nodes.Execution;

/// <summary>
/// 声明契约判据（E1 步次闭合 / E2 入参越界 / E3 出参越界 / E4 声明端口缺失）**单点实现**：纯函数、无领域词。
/// <para>判据载体 = 键集合 / 步名集合（结构事实），不含执行者叙述；框架与 Agent 侧共用本实现（SingleImplementation）。</para>
/// </summary>
public static class RoundContractJudge
{
    private static readonly IJudgmentRule PortRule = new PortPresenceRule();
    private static readonly IJudgmentRule StepRule = new StepClosureRule();
    private static readonly IJudgmentRule UndeclaredInRule =
        new UndeclaredKeysRule(RoundFailureCodes.ExecUndeclaredInport, inports: true);
    private static readonly IJudgmentRule UndeclaredOutRule =
        new UndeclaredKeysRule(RoundFailureCodes.ExecUndeclaredOutport, inports: false);
    /// <summary>声明外键 = 实际键 − 声明键 − 豁免键（内部键）。空 = 闭合。</summary>
    public static IReadOnlyList<string> Undeclared(
        IEnumerable<string>? declared,
        IEnumerable<string> actual,
        IReadOnlyCollection<string>? exempt = null)
    {
        if (declared is null) return Array.Empty<string>();

        var input = new JudgmentInput(JudgmentScope.Static)
        {
            DeclaredPorts = declared.Where(static k => !string.IsNullOrWhiteSpace(k)).ToArray(),
            ActualOutportKeys = actual.Where(static k => !string.IsNullOrWhiteSpace(k)).ToArray(),
            ExemptKeys = exempt ?? Array.Empty<string>(),
        };

        return JudgmentEngine
            .Judge(input, new IJudgmentRule[] { UndeclaredOutRule })
            .SubjectsOf(RoundFailureCodes.ExecUndeclaredOutport);
    }

    /// <summary>E1：声明步骤中既未执行、也未「合法跳过」的步骤（升序；空 = 闭合）。</summary>
    public static IReadOnlyList<string> MissingSteps(
        IEnumerable<string> declaredSteps,
        IEnumerable<string> executed,
        IEnumerable<string>? skipped = null)
    {
        var input = new JudgmentInput(JudgmentScope.Round)
        {
            DeclaredSteps = declaredSteps.Where(static s => !string.IsNullOrWhiteSpace(s)).ToArray(),
            ExecutedSteps = executed?.ToArray() ?? Array.Empty<string>(),
            SkippedSteps = skipped?.ToArray() ?? Array.Empty<string>(),
        };

        return JudgmentEngine
            .Judge(input, new IJudgmentRule[] { StepRule })
            .SubjectsOf(RoundFailureCodes.ExecStepMissing);
    }

    /// <summary>E4：声明 outport 中未实际产出的端口（空 = 闭合）。</summary>
    /// <param name="declaredPorts">声明产出端口。</param>
    /// <param name="produced">实际产出端口。</param>
    /// <param name="nullablePorts">允许显式 null 的端口（缺省 null = null 亦算缺失，零回归）；非空集合内的端口只在「键不存在」时算缺失（E7）。</param>
    /// <param name="promptContract">
    /// 提示词端口契约（可选）：命中契约的端口改走 <see cref="TextJudge"/>（与前置门同一判据），
    /// 其余端口维持 <see cref="PortPresenceRule"/>。null ⇒ 全部走端口存在性判据（零回归）。
    /// </param>
    public static IReadOnlyList<string> MissingPorts(
        IEnumerable<string>? declaredPorts, IReadOnlyDictionary<string, object?> produced,
        IReadOnlyCollection<string>? nullablePorts = null,
        TextContract? promptContract = null)
    {
        ArgumentNullException.ThrowIfNull(produced);
        if (declaredPorts is null) return Array.Empty<string>();

        var declared = declaredPorts.Where(static p => !string.IsNullOrWhiteSpace(p)).ToArray();
        if (promptContract is null)
            return MissingPortsByPortRule(declared, produced, nullablePorts);

        // 提示词端口（契约命中）→ TextJudge（与 PromptPreflight 同判据：空白串视为未给出）。
        var promptPorts = declared
            .Where(p => promptContract.Ports.Any(port => string.Equals(port.Key.Value, p, StringComparison.Ordinal)))
            .ToArray();
        var missing = new List<string>();
        if (promptPorts.Length > 0)
        {
            var context = new SegmentContext(produced);
            var plan = TextPlan.FromPorts(promptContract, context, BlankSemantics.Absent);
            var verdict = TextJudge.Judge(plan, context, promptContract, BlankSemantics.Absent);
            foreach (var key in verdict.Missing)
                if (promptPorts.Contains(key.Value, StringComparer.Ordinal))
                    missing.Add(key.Value);
        }

        // 非提示词端口：维持端口存在性判据。
        var rest = declared.Where(p => !promptPorts.Contains(p, StringComparer.Ordinal)).ToArray();
        missing.AddRange(MissingPortsByPortRule(rest, produced, nullablePorts));
        return missing;
    }

    private static IReadOnlyList<string> MissingPortsByPortRule(
        IReadOnlyList<string> declared, IReadOnlyDictionary<string, object?> produced,
        IReadOnlyCollection<string>? nullablePorts)
    {
        var input = new JudgmentInput(JudgmentScope.Step)
        {
            DeclaredPorts = declared,
            Fact = new JudgmentFact(produced),
            Capability = new DeclarationCapability(
                NullablePorts: DeclarationCapability.Set(nullablePorts)),
        };

        return JudgmentEngine
            .Judge(input, new IJudgmentRule[] { PortRule })
            .SubjectsOf(RoundFailureCodes.ExecPortMissing);
    }

    /// <summary>
    /// 全量判定：<paramref name="judgeSteps"/>=false 时只判端口（失败轮不判步次，禁以缺步掩盖真实错误）。
    /// </summary>
    public static RoundContractVerdict Judge(
        RoundSpec spec,
        IReadOnlyList<NodeExecutionRecord> records,
        IEnumerable<string> executed,
        IReadOnlyList<string>? skipped,
        RoundContractOptions? options = null,
        bool judgeSteps = true)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(records);

        var opts = options ?? RoundContractOptions.Default;
        var declared = spec.Steps.Select(static s => s.EffectiveNodeId).ToArray();
        var executedKeys = new HashSet<string>(executed ?? Array.Empty<string>(), StringComparer.Ordinal);
        foreach (var record in records) executedKeys.Add(record.NodeKey);

        var missingSteps = judgeSteps
            ? MissingSteps(declared, executedKeys, skipped)
            : Array.Empty<string>();

        var byId = spec.Steps.ToDictionary(static s => s.EffectiveNodeId, StringComparer.Ordinal);
        var missingPorts = new List<string>();
        var undeclaredIn = new List<string>();
        var undeclaredOut = new List<string>();
        var exempt = opts.InternalKeys ?? Array.Empty<string>();

        foreach (var record in records)
        {
            if (!byId.TryGetValue(record.NodeKey, out var step)) continue;

            var portInput = new JudgmentInput(JudgmentScope.Step)
            {
                DeclaredPorts = step.ResultKeys,
                Fact = new JudgmentFact(record.OutputPorts),
                Capability = CapabilityOf(step),
            };

            foreach (var key in JudgmentEngine.Judge(portInput, new IJudgmentRule[] { PortRule })
                         .SubjectsOf(RoundFailureCodes.ExecPortMissing))
                missingPorts.Add($"{step.EffectiveNodeId}:[{key}]");

            if (record.InjectedKeys is { } injected)
            {
                var inInput = new JudgmentInput(JudgmentScope.Step)
                {
                    DeclaredInports = step.ParameterKeys,
                    ActualInportKeys = injected.ToArray(),
                    ExemptKeys = exempt,
                };

                foreach (var key in JudgmentEngine.Judge(inInput, new IJudgmentRule[] { UndeclaredInRule })
                             .SubjectsOf(RoundFailureCodes.ExecUndeclaredInport))
                    undeclaredIn.Add($"{step.EffectiveNodeId}:[{key}]");
            }

            var outInput = new JudgmentInput(JudgmentScope.Step)
            {
                DeclaredPorts = step.ResultKeys,
                ActualOutportKeys = record.OutputPorts.Keys.ToArray(),
                ExemptKeys = exempt,
            };

            foreach (var key in JudgmentEngine.Judge(outInput, new IJudgmentRule[] { UndeclaredOutRule })
                         .SubjectsOf(RoundFailureCodes.ExecUndeclaredOutport))
                undeclaredOut.Add($"{step.EffectiveNodeId}:[{key}]");
        }

        return new RoundContractVerdict(
            missingSteps, missingPorts,
            undeclaredIn.Distinct(StringComparer.Ordinal).OrderBy(static x => x, StringComparer.Ordinal).ToArray(),
            undeclaredOut.Distinct(StringComparer.Ordinal).OrderBy(static x => x, StringComparer.Ordinal).ToArray());
    }

    /// <summary>步骤级契约判定（执行边界上报 Verdict 事实；动作由轮级 Apply 统一裁决）。</summary>
    public static RoundContractVerdict JudgeStep(
        NodeDefinition node, IReadOnlyDictionary<string, object?> produced)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(produced);

        var input = new JudgmentInput(JudgmentScope.Step)
        {
            DeclaredPorts = node.ResultKeys,
            Fact = new JudgmentFact(produced),
            Capability = new DeclarationCapability(
                NullablePorts: DeclarationCapability.Set(node.NullablePorts),
                ConditionalPorts: DeclarationCapability.Set(node.ConditionalPorts)),
        };

        var verdict = JudgmentEngine.Judge(input, new IJudgmentRule[] { PortRule });
        return new RoundContractVerdict(
            Array.Empty<string>(),
            verdict.SubjectsOf(RoundFailureCodes.ExecPortMissing),
            Array.Empty<string>(),
            Array.Empty<string>());
    }

    private static DeclarationCapability CapabilityOf(RoundStep step)
        => new(
            NullablePorts: DeclarationCapability.Set(step.EffectiveNullablePorts),
            ConditionalPorts: DeclarationCapability.Set(step.EffectiveConditionalPorts));
}
