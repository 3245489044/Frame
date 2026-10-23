namespace Operation.HierarchyMethod.Judgment;

/// <summary>判定作用域（同一引擎多处调用，不复制实现）。</summary>
[Flags]
public enum JudgmentScope
{
    None = 0,
    Step = 1,
    Round = 2,
    Static = 4,
    Preflight = 8,
    All = Step | Round | Static | Preflight,
}
