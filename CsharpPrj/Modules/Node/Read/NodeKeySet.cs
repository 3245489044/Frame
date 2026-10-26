namespace Modules.Node.Read;

/// <summary>键集作用的层级锚点（运行态无关；层由调用方声明）。</summary>
public enum NodeKeySetScope
{
    /// <summary>任务作用域（task → node 归属边）。</summary>
    Task,

    /// <summary>历史作用域（history → task → node 派生并集）。</summary>
    History,

    /// <summary>节点作用域（单节点及其组合边）。</summary>
    Node,

    /// <summary>容器作用域（容器内直读）。</summary>
    Container,

    /// <summary>全量作用域（诊断/库页）。</summary>
    Full,
}

/// <summary>键集取得方式（数据；禁调用点 if 分支）。</summary>
public enum NodeKeySetOrigin
{
    Edge,
    Container,
    KeyPrefix,
    Full,
}

/// <summary>键集请求（层级侧锚点 = key；运行态无关）。</summary>
/// <param name="Scope">作用域。</param>
/// <param name="Key">锚点键（taskKey / historyId / nodeKey / container）。</param>
/// <param name="WorkflowId">工作流命名空间（端点归一用；可空 = 按端点面自解）。</param>
/// <param name="Limit">键集上限（排序后裁剪）。</param>
public sealed record NodeKeySetRequest(
    NodeKeySetScope Scope,
    string Key,
    string? WorkflowId = null,
    int? Limit = null);

/// <summary>键集结果：<see cref="Keys"/> 恒为「已归一 / 去重 / 键序」；<see cref="Reasons"/> 非空 ⇔ 空集或有被拒项。</summary>
public sealed record NodeKeySetResult(
    IReadOnlyList<string> Keys,
    NodeKeySetOrigin Origin,
    IReadOnlyList<string> Reasons)
{
    /// <summary>空键集（带原因；不静默）。</summary>
    public static NodeKeySetResult Empty(NodeKeySetOrigin origin, params string[] reasons)
        => new(Array.Empty<string>(), origin, reasons);

    /// <summary>可用但空（原因词必填）。</summary>
    public bool IsEmpty => Keys.Count == 0;
}

/// <summary>
/// 空集/拒收原因词**单源**（禁各处手写字符串；面板/上下文/树共用同一词表）。
/// </summary>
public static class NodeKeySetReasons
{
    /// <summary>无归属边（合法空，不清面）。</summary>
    public const string NoBinding = "no-binding";

    /// <summary>端口未装配 / 图不可用。</summary>
    public const string SourceUnavailable = "source-unavailable";

    /// <summary>外来族（history./session. 等）。</summary>
    public const string ForeignNamespace = "foreign-namespace";

    /// <summary>端点形态不可反解。</summary>
    public const string UnparsableKey = "unparsable-key";

    /// <summary>入参键为空。</summary>
    public const string EmptyKey = "empty-key";

    /// <summary>边目标节点行缺失（不伪造行）。</summary>
    public const string EdgeTargetMissing = "edge-target-missing";

    /// <summary>显式键集读时缺行（不伪造行）。</summary>
    public const string KeyMissing = "key-missing";
}

/// <summary>
/// 单次节点读的框架结果：键集 + 投影项 + 原因（三态由消费方映射到契约三态）。
/// <para>对外唯一形状仍是 <c>Agent.Contract.NodeDomain.NodeReadOutcome</c>；本类型不与其同名，避免跨层撞名。</para>
/// </summary>
/// <param name="DomainKey">读域。</param>
/// <param name="KeySet">键集结果（Keys/Origin/Reasons）。</param>
/// <param name="Items">投影项（键序）。</param>
/// <param name="Reasons">读原因（键集原因 + key-missing）。</param>
public sealed record NodeReadResult(
    string DomainKey,
    NodeKeySetResult KeySet,
    IReadOnlyList<Projection.NodeProjectionItem> Items,
    IReadOnlyList<string> Reasons)
{
    /// <summary>可用（含空）。</summary>
    public bool IsAvailable => !Reasons.Contains(NodeKeySetReasons.SourceUnavailable, StringComparer.Ordinal);
}
