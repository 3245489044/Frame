using System.Collections.Generic;
using Modules.KeySpace.Diagnostics;
using Operation.HierarchyMethod.Nodes.Execution;

namespace Operation.HierarchyMethod.Judgment;

/// <summary>默认规则集（组合根可替换；新增判定 = 追加一条注册）。</summary>
public static class JudgmentRules
{
    public static IReadOnlyList<IJudgmentRule> Default { get; } = new IJudgmentRule[]
    {
        new CapabilityDeclaredRule(),
        new PortPresenceRule(),
        new AliasCoverageRule(),
        new StepClosureRule(),
        new UndeclaredKeysRule(RoundFailureCodes.ExecUndeclaredInport, inports: true),
        new UndeclaredKeysRule(RoundFailureCodes.ExecUndeclaredOutport, inports: false),
    };
}

