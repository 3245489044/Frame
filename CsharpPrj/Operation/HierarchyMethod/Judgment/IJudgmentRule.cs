using System.Collections.Generic;

namespace Operation.HierarchyMethod.Judgment;

/// <summary>判定规则端口：新增判定 = 注册规则，不改引擎（Open-Closed）。</summary>
public interface IJudgmentRule
{
    string Name { get; }

    /// <summary>数值大者先评估（对齐既有匹配策略约定）。</summary>
    int Specificity { get; }

    JudgmentScope AppliesTo { get; }

    IReadOnlyList<JudgmentFinding> Evaluate(JudgmentInput input);
}
