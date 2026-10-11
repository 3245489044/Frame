namespace Operation.HierarchyMethod.Tasks;

/// <summary>
/// 归档轮次的**单源裁决**（通用）：归档轮次一律取**调用方轮次**（= 会话管线种子轮次，同时驱动消息键与载荷 round），
/// 从而消除「归档自推（已归档 max+1）vs 管线种子」的轮次漂移。
/// <para>
/// 仅当调用方**无会话上下文**（<c>request.Round ≤ 0</c>：直连调用/无会话归属）时才回退归档派生，
/// 且回退必须**可见化**（<see cref="SourceOf"/> 标出来源），禁静默自推。
/// </para>
/// </summary>
public static class RoundSourcePolicy
{
    /// <summary>来源=调用方轮次（会话管线单源）。</summary>
    public const string SourceRequest = "request";

    /// <summary>来源=归档派生（无会话上下文的可见化回退）。</summary>
    public const string SourceArchiveDerived = "archive-derived";

    /// <summary>裁决归档轮次：调用方轮次优先；≤0 才用归档派生值。</summary>
    public static int Resolve(int requestRound, int archiveDerived) =>
        requestRound > 0 ? requestRound : archiveDerived;

    /// <summary>轮次来源标签（供可见化：回退路径不得静默）。</summary>
    public static string SourceOf(int requestRound) =>
        requestRound > 0 ? SourceRequest : SourceArchiveDerived;
}
