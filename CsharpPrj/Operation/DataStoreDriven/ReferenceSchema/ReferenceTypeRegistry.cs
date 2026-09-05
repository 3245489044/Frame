using System.Text.Json;
using Plate.DslMethod.Schema;

namespace Operation.DataStoreDriven.ReferenceSchema;

/// <summary>
/// 引用类型注册中心：从编译产物 <c>reference-schema.generated.json</c>（纯编译产物，源=reference-schema.dsl.md）加载
/// <see cref="ReferenceSchema"/>，构建以边名为键（大小写不敏感）的查表。实现 <see cref="IReferenceTypeRegistry"/>。
/// </summary>
public sealed class ReferenceTypeRegistry : IReferenceTypeRegistry
{
    private readonly Plate.DslMethod.Schema.ReferenceSchema _schema;
    private readonly Dictionary<string, ReferenceEdgeType> _edges;

    public ReferenceTypeRegistry(Plate.DslMethod.Schema.ReferenceSchema schema)
    {
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));
        _edges = new Dictionary<string, ReferenceEdgeType>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in schema.Edges)
            _edges.TryAdd(e.Name, e);
    }

    /// <summary>编译后的 Schema（含节点/边/约束全集）。</summary>
    public Plate.DslMethod.Schema.ReferenceSchema Schema => _schema;

    /// <summary>边查表（名 → 边声明，大小写不敏感）。</summary>
    public IReadOnlyDictionary<string, ReferenceEdgeType> Edges => _edges;

    /// <summary>从编译产物 JSON 文件加载。</summary>
    public static ReferenceTypeRegistry LoadFromFile(string jsonPath)
        => new(JsonSerializer.Deserialize<Plate.DslMethod.Schema.ReferenceSchema>(File.ReadAllText(jsonPath))!);

    /// <summary>从编译产物 JSON 字符串加载。</summary>
    public static ReferenceTypeRegistry LoadFromJson(string json)
        => new(JsonSerializer.Deserialize<Plate.DslMethod.Schema.ReferenceSchema>(json)!);

    public bool IsDeclared(string edgeName)
        => _edges.ContainsKey(edgeName);

    public bool IsDeprecated(string edgeName)
        => _edges.TryGetValue(edgeName, out var e) && e.Deprecated;

    public ReferenceEdgeType? FindEdge(string edgeName)
        => _edges.TryGetValue(edgeName, out var e) ? e : null;

    /// <summary>按节点类型 + 端口名解析端口类型签名（输入/输出侧；节点或端口未声明 → false）。</summary>
    public bool TryGetPortType(string nodeType, string port, bool isInput, out string type)
    {
        type = string.Empty;
        if (string.IsNullOrWhiteSpace(nodeType) || string.IsNullOrWhiteSpace(port)) return false;
        var node = _schema.Nodes.FirstOrDefault(n => string.Equals(n.Name, nodeType, StringComparison.OrdinalIgnoreCase));
        if (node is null) return false;
        var ports = isInput ? node.InputPorts : node.OutputPorts;
        if (ports.TryGetValue(port, out var t)) { type = t; return true; }
        return false;
    }
}
