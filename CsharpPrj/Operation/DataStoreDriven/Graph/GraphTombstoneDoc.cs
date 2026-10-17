namespace Operation.DataStoreDriven.Graph;

/// <summary>墓碑类别受控词（帧库通用，无业务语义）。</summary>
public static class GraphTombstoneKinds
{
    /// <summary>关系声明墓碑。</summary>
    public const string Declaration = "declaration";

    /// <summary>引用边墓碑。</summary>
    public const string Edge = "edge";

    /// <summary>端点墓碑。</summary>
    public const string Endpoint = "endpoint";
}

/// <summary>
/// 删除墓碑：记录「这条声明 / 边 / 端点已被用户删除」，阻止 seed / reconcile / rebuild 重建。
/// <para>写入顺序恒为「先墓碑后物理删除」；墓碑写失败 ⇒ 删除失败（fail-closed）。</para>
/// </summary>
public sealed record GraphTombstoneDoc
{
    /// <summary>类别（<see cref="GraphTombstoneKinds"/>）。</summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>原键（诊断用）。</summary>
    public string TargetKey { get; init; } = string.Empty;

    /// <summary>所属工作流（可空）。</summary>
    public string? WorkflowId { get; init; }

    /// <summary>源端类型（可空）。</summary>
    public string? SourceType { get; init; }

    /// <summary>源端 Id（可空）。</summary>
    public string? Source { get; init; }

    /// <summary>源端端口（可空）。</summary>
    public string? SourcePort { get; init; }

    /// <summary>目标端类型（可空）。</summary>
    public string? TargetType { get; init; }

    /// <summary>目标端 Id（可空）。</summary>
    public string? Target { get; init; }

    /// <summary>目标端端口（可空）。</summary>
    public string? TargetPort { get; init; }

    /// <summary>关系类型（可空）。</summary>
    public string? RefType { get; init; }

    /// <summary>关联 KeyMapping 键（可空）。</summary>
    public string? KeyMappingRef { get; init; }

    /// <summary>创建时刻（UTC ISO-8601）。</summary>
    public string CreatedAt { get; init; } = string.Empty;

    /// <summary>删除原因（可见化文本；不参与判据）。</summary>
    public string? Reason { get; init; }
}
