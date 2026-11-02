namespace Share.NodeIo;

/// <summary>
/// 载荷契约（**中立 JSON 字段名**；零语言语义）：声明面给出字段拼写与必填面，
/// 写侧写入前校验、读侧解析共用同一实例。
/// </summary>
/// <param name="Id">契约 id（位置声明 <c>payload-schema</c>）。</param>
/// <param name="PartitionField">分区键字段名（须与位置键容器一致）。</param>
/// <param name="RoundsField">轮分组字段名（可空 = 扁平帧数组）。</param>
/// <param name="RoundField">轮对象内轮号字段名。</param>
/// <param name="FramesField">帧数组字段名。</param>
/// <param name="SeqField">帧序字段名。</param>
/// <param name="KindField">帧类别字段名。</param>
/// <param name="RequiredRootFields">根必填字段（缺一即拒写）。</param>
public sealed record NodePayloadSchema(
    string Id,
    string PartitionField = "taskKey",
    string? RoundsField = "rounds",
    string? RoundField = "round",
    string? FramesField = "frames",
    string? SeqField = "seq",
    string? KindField = "kind",
    IReadOnlyList<string>? RequiredRootFields = null)
{
    /// <summary>缺省契约（与既有 Stream 载荷口径一致：<c>{taskKey,rounds:[{round,frames:[…]}]}</c>）。</summary>
    public static NodePayloadSchema Default { get; } = new(
        "stream-default",
        RequiredRootFields: new[] { "taskKey" });

    public IReadOnlyList<string> RequiredOrEmpty => RequiredRootFields ?? Array.Empty<string>();
}

/// <summary>载荷契约注册表（声明装载产物；未命中 ⇒ 缺省契约，不伪造领域语义）。</summary>
public sealed class NodePayloadSchemaRegistry
{
    private readonly Dictionary<string, NodePayloadSchema> _schemas = new(StringComparer.Ordinal);

    public IReadOnlyCollection<NodePayloadSchema> All => _schemas.Values.ToArray();

    public void Register(NodePayloadSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        if (string.IsNullOrWhiteSpace(schema.Id))
        {
            throw new ArgumentException("载荷契约 id 不可为空", nameof(schema));
        }

        _schemas[schema.Id] = schema;
    }

    /// <summary>按 id 取契约；空 id / 未注册 ⇒ <see cref="NodePayloadSchema.Default"/>（零回归）。</summary>
    public NodePayloadSchema Resolve(string? id)
        => string.IsNullOrWhiteSpace(id)
            ? NodePayloadSchema.Default
            : _schemas.TryGetValue(id!, out var schema) ? schema : NodePayloadSchema.Default;
}
