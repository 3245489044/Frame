namespace Operation.DataStoreDriven.Relations;

/// <summary>违反种类（结构事实；<c>BudgetExceeded</c> 与 <c>Cycle</c> 不得共用出口）。</summary>
public enum RelationViolationKind
{
    Cycle = 0,
    BudgetExceeded = 1,
    SingleParent = 2,
    SingleTarget = 3,
    MissingEndpoint = 4,
}

/// <summary>一条不变量违反（不变量 id + 种类 + 结构事实理由）。</summary>
public sealed record RelationViolation(string InvariantId, RelationViolationKind Kind, string Reason);
