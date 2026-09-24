using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Modules.Node.Diagnostics;

namespace Modules.Node.Ports;

public sealed record PortBindingResult(
    IReadOnlyDictionary<string, string> Ports,
    IReadOnlyList<NodeDiagnostic> Diagnostics,
    bool UsedInference);

/// <summary>特化提取器扩展点（按结果类型名命中；对齐既有"特化提取器注册表"语义）。</summary>
public interface INodePortExtractor
{
    string ResultTypeName { get; }

    PortBindingResult Extract(string nodeKey, string resultJson, IReadOnlyList<string> declaredPorts);
}

/// <summary>
/// 端口提取（G1 收编，<b>声明优先</b>）：
/// <list type="number">
/// <item>声明端口定向命中（<c>ResultKeys</c>）；</item>
/// <item>未声明 → 顶层属性名兜底，但<b>必须产 <c>port.inferred</c> 诊断</b>（治 P12：禁静默反射猜端口）；</item>
/// <item>简单值/非对象 → 默认端口 = 节点键整存；</item>
/// <item>null 值/端口跳过，缺失声明产 <c>port.declared-missing</c> 可见化（治 P15）。</item>
/// </list>
/// 不解析键映射、不做类型转换。
/// </summary>
public sealed class NodePortBinding
{
    private readonly Dictionary<string, INodePortExtractor> _extractors = new(StringComparer.Ordinal);
    private readonly INodeDiagnosticsSink _diagnostics;

    public NodePortBinding(INodeDiagnosticsSink? diagnostics = null, IEnumerable<INodePortExtractor>? extractors = null)
    {
        _diagnostics = diagnostics ?? NullDiagnosticsSink.Instance;

        foreach (var extractor in extractors ?? Array.Empty<INodePortExtractor>())
        {
            Register(extractor);
        }
    }

    public IReadOnlyCollection<string> RegisteredTypeNames => _extractors.Keys.ToArray();

    public void Register(INodePortExtractor extractor) => _extractors[extractor.ResultTypeName] = extractor;

    public PortBindingResult Extract(
        string nodeKey,
        string resultJson,
        IReadOnlyList<string>? declaredPorts = null,
        string? resultTypeName = null)
    {
        var declared = declaredPorts ?? Array.Empty<string>();
        var diagnostics = new List<NodeDiagnostic>();

        if (resultTypeName is not null && _extractors.TryGetValue(resultTypeName, out var extractor))
        {
            var specialized = extractor.Extract(nodeKey, resultJson, declared);
            foreach (var diagnostic in specialized.Diagnostics)
            {
                _diagnostics.Report(diagnostic);
            }

            return specialized;
        }

        JsonNode? root = null;
        if (!string.IsNullOrWhiteSpace(resultJson))
        {
            try
            {
                root = JsonNode.Parse(resultJson, documentOptions: new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                });
            }
            catch (JsonException)
            {
                diagnostics.Add(new NodeDiagnostic(
                    "port.parse-fallback",
                    NodeDiagnosticSeverity.Info,
                    "结果非 JSON：按简单值整存默认端口",
                    nodeKey));
            }
        }

        var ports = new Dictionary<string, string>(StringComparer.Ordinal);

        if (root is JsonObject obj)
        {
            if (declared.Count > 0)
            {
                foreach (var port in declared)
                {
                    if (obj.TryGetPropertyValue(port, out var value) && value is not null)
                    {
                        ports[port] = ToPortValue(value);
                    }
                    else
                    {
                        diagnostics.Add(new NodeDiagnostic(
                            "port.declared-missing",
                            NodeDiagnosticSeverity.Info,
                            $"声明端口 '{port}' 在结果中未命中或为 null（缺口可见化，不静默跳过）",
                            nodeKey));
                    }
                }
            }
            else
            {
                foreach (var pair in obj)
                {
                    if (pair.Value is null)
                    {
                        continue;
                    }

                    ports[pair.Key] = ToPortValue(pair.Value);
                    diagnostics.Add(new NodeDiagnostic(
                        "port.inferred",
                        NodeDiagnosticSeverity.Warning,
                        $"未声明 ResultKeys：端口 '{pair.Key}' 由结果结构推断（跨模块复用请显式声明）",
                        nodeKey));
                }
            }
        }
        else
        {
            // 简单值（含非 JSON 原文）：整存默认端口 = 节点键
            ports[nodeKey] = root is null ? resultJson : ToPortValue(root);
            diagnostics.Add(new NodeDiagnostic(
                "port.default",
                NodeDiagnosticSeverity.Info,
                "无命名端口：结果整存于默认端口（端口名 = 节点键）",
                nodeKey));
        }

        foreach (var diagnostic in diagnostics)
        {
            _diagnostics.Report(diagnostic);
        }

        return new PortBindingResult(ports, diagnostics, UsedInference: declared.Count == 0);
    }

    private static string ToPortValue(JsonNode node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text)
            ? text
            : node.ToJsonString(new JsonSerializerOptions { WriteIndented = false });

    public static string ToJsonLiteral(string value) =>
        JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = false });

    public static string ToJsonNumber(long value) => value.ToString(CultureInfo.InvariantCulture);
}
