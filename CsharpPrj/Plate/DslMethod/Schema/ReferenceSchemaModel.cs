using Plate.DslMethod.Model;
using Plate.DslMethod.Parsing;

namespace Plate.DslMethod.Schema;

/// <summary>资源节点类型（映射 UniversalReference <c>NodeType</c> / <c>ResourceIdentifier.ResourceType</c>）。</summary>
public sealed record ResourceNodeType(
    string Name,
    string? Display,
    string? Color,
    string? Icon,
    int SortWeight,
    int Version,
    bool Deprecated,
    string? ReplacedBy,
    string SourceFile,
    int LineNumber)
{
    /// <summary>输入端口类型签名（<c>[Node] inports</c> 字段，port:type 逗号列表）。默认空（未声明端口类型约束）。</summary>
    public IReadOnlyDictionary<string, string> InputPorts { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>输出端口类型签名（<c>[Node] outports</c> 字段，port:type 逗号列表）。默认空（未声明端口类型约束）。</summary>
    public IReadOnlyDictionary<string, string> OutputPorts { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>从 <c>[Node]</c> 声明解析；非节点声明或无名返回 null。</summary>
    public static ResourceNodeType? From(DslDeclaration d)
    {
        if (string.IsNullOrWhiteSpace(d.Name)) return null;
        var (dep, replaced) = StringValueParser.ParseDep(d.Field("dep"));
        return new ResourceNodeType(
            d.Name,
            StringValueParser.NullIfEmpty(d.Field("display")),
            StringValueParser.NullIfEmpty(d.Field("color")),
            StringValueParser.NullIfEmpty(d.Field("icon")),
            StringValueParser.IntValue(d.Field("sort"), 0),
            StringValueParser.IntValue(d.Field("version"), 1),
            dep,
            replaced,
            d.SourceFile,
            d.LineNumber)
        {
            InputPorts = ParsePorts(d.Field("inports")),
            OutputPorts = ParsePorts(d.Field("outports")),
        };
    }

    /// <summary>
    /// 解析端口类型签名字段（<c>inports</c>/<c>outports</c>）：<c>port:type</c> 逗号列表；
    /// 端口类型签名 = 引用 Schema 的节点属性，供 <c>WorkflowCompiler</c> 在调用点静态校验实参/形参类型（对齐 C# CS0029）。
    /// 未声明类型（仅 <c>port</c> 无 <c>:type</c>）以空串登记（视为无类型约束）。
    /// </summary>
    public static IReadOnlyDictionary<string, string> ParsePorts(string? field)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in StringValueParser.SplitList(field, ','))
        {
            var i = item.IndexOf(':');
            var port = i > 0 ? item[..i].Trim() : item.Trim();
            var type = i > 0 ? item[(i + 1)..].Trim() : string.Empty;
            if (port.Length > 0 && !result.ContainsKey(port)) result[port] = type;
        }
        return result;
    }
}

/// <summary>引用类型（映射 UniversalReference <c>ReferenceType</c> 词汇表 / <c>EdgeType</c>）。</summary>
public sealed record ReferenceEdgeType(
    string Name,
    string? Source,
    string? Target,
    double Strength,
    string? Display,
    int Version,
    bool Deprecated,
    string? ReplacedBy,
    string SourceFile,
    int LineNumber)
{
    /// <summary>从 <c>[Edge]</c> 声明解析；非边声明或无名返回 null。</summary>
    public static ReferenceEdgeType? From(DslDeclaration d)
    {
        if (string.IsNullOrWhiteSpace(d.Name)) return null;
        var (dep, replaced) = StringValueParser.ParseDep(d.Field("dep"));
        return new ReferenceEdgeType(
            d.Name,
            StringValueParser.NullIfEmpty(d.Field("src")),
            StringValueParser.NullIfEmpty(d.Field("tgt")),
            StringValueParser.DoubleValue(d.Field("strength"), 1.0),
            StringValueParser.NullIfEmpty(d.Field("display")),
            StringValueParser.IntValue(d.Field("version"), 1),
            dep,
            replaced,
            d.SourceFile,
            d.LineNumber);
    }
}

/// <summary>配对约束（映射 UniversalReference <c>ValidationRule</c>/<c>FilterRule</c>）：某引用类型的合法源/目标节点类型与上限。</summary>
public sealed record ReferenceConstraint(
    string Edge,
    string SrcType,
    List<string> TgtTypes,
    double? MaxCount,
    bool Unbounded,
    string SourceFile,
    int LineNumber)
{
    /// <summary>从 <c>[Constraint]</c> 声明解析；非约束声明或无名返回 null。</summary>
    public static ReferenceConstraint? From(DslDeclaration d)
    {
        if (string.IsNullOrWhiteSpace(d.Name)) return null;
        var src = d.Field("srcType");
        var tgt = d.Field("tgtTypes");
        var tgtTypes = StringValueParser.SplitList(tgt, ',');
        var (max, unbounded) = StringValueParser.ParseMax(d.Field("max"));

        return new ReferenceConstraint(
            d.Name,
            StringValueParser.NullIfEmpty(src) ?? "any",
            tgtTypes,
            max,
            unbounded,
            d.SourceFile,
            d.LineNumber);
    }
}

/// <summary>编译后的引用 Schema（纯数据 DTO，可经 System.Text.Json 序列化 → 运行时消费）。</summary>
public sealed class ReferenceSchema
{
    public string Version { get; set; } = "1.0";
    public List<ResourceNodeType> Nodes { get; set; } = new();
    public List<ReferenceEdgeType> Edges { get; set; } = new();
    public List<ReferenceConstraint> Constraints { get; set; } = new();
}