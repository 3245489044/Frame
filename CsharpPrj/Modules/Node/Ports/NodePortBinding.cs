using System.Text.Json.Nodes;
using Modules.Node.Diagnostics;

namespace Modules.Node.Ports;

public sealed record PortBindingResult(
    IReadOnlyDictionary<string, string> Ports,
    IReadOnlyList<NodeDiagnostic> Diagnostics,
    bool UsedInference);

/// <summary>宿主原生值形态的端口提取结果（值缝 V1：规则与 JSON 路径同一实现，值不经 JSON 往返）。</summary>
public sealed record PortValuesResult<TValue>(
    IReadOnlyDictionary<string, TValue> Ports,
    IReadOnlyList<NodeDiagnostic> Diagnostics,
    bool UsedInference);

/// <summary>特化提取器扩展点（按结果类型名命中；对齐既有"特化提取器注册表"语义）。</summary>
public interface INodePortExtractor
{
    string ResultTypeName { get; }

    PortBindingResult Extract(string nodeKey, string resultJson, IReadOnlyList<string> declaredPorts);
}

/// <summary>
/// 端口提取（G1 收编，<b>声明优先</b>）：准入规则唯一来源 = <see cref="NodePortRules"/>，本类只负责两条值投影路径——
/// <list type="bullet">
/// <item><see cref="Extract"/>：JSON 文本路径（解析 → 规则 → 值投影为 JSON 文本）；</item>
/// <item><see cref="ExtractValues{TValue}"/>：宿主原生值路径（规则同上；值由调用侧按端口名取出，保对象身份）。</item>
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
        if (!string.IsNullOrWhiteSpace(resultJson) && !NodeJson.TryParse(resultJson, out root, out _))
        {
            diagnostics.Add(new NodeDiagnostic(
                "port.parse-fallback",
                NodeDiagnosticSeverity.Info,
                "结果非 JSON：按简单值整存默认端口",
                nodeKey));
        }

        IReadOnlyList<PortMember>? members = root is JsonObject jsonObject ? MembersOf(jsonObject) : null;
        var ruleResult = NodePortRules.Decide(nodeKey, members, declared, diagnostics);
        var ports = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var decision in ruleResult.Decisions)
        {
            if (decision.Admission == PortAdmission.Default)
            {
                // 简单值（含非 JSON 原文）：整存默认端口 = 节点键
                ports[nodeKey] = root is null ? resultJson : NodeJson.ValueToText(root);
                continue;
            }

            if (root is JsonObject source && source.TryGetPropertyValue(decision.Port, out var value) && value is not null)
            {
                ports[decision.Port] = NodeJson.ValueToText(value);
            }
        }

        foreach (var diagnostic in ruleResult.Diagnostics)
        {
            _diagnostics.Report(diagnostic);
        }

        return new PortBindingResult(ports, ruleResult.Diagnostics, ruleResult.UsedInference);
    }

    /// <summary>
    /// 宿主原生值路径（值缝 V1）：准入规则与 <see cref="Extract"/> 完全相同（<see cref="NodePortRules.Decide"/>），
    /// 值由 <paramref name="valueOf"/> 按端口名取出（宿主保原生值/对象身份，禁 JSON 往返）；
    /// <paramref name="members"/> = <c>null</c> 即非对象结果 → 默认端口整存 <paramref name="defaultValue"/>。
    /// </summary>
    public PortValuesResult<TValue> ExtractValues<TValue>(
        string nodeKey,
        IReadOnlyList<PortMember>? members,
        Func<string, TValue> valueOf,
        TValue defaultValue,
        IReadOnlyList<string>? declaredPorts = null,
        StringComparer? memberComparer = null)
    {
        var ruleResult = NodePortRules.Decide(
            nodeKey,
            members,
            declaredPorts,
            prefixDiagnostics: null,
            memberComparer: memberComparer);

        var ports = new Dictionary<string, TValue>(StringComparer.Ordinal);

        foreach (var decision in ruleResult.Decisions)
        {
            if (decision.Admission == PortAdmission.DeclaredMissing)
            {
                // 缺口只可见化、不产槽位（对齐 JSON 路径：未命中/null 不入端口）
                continue;
            }

            ports[decision.Port] = decision.Admission == PortAdmission.Default
                ? defaultValue
                : valueOf(decision.Port);
        }

        foreach (var diagnostic in ruleResult.Diagnostics)
        {
            _diagnostics.Report(diagnostic);
        }

        return new PortValuesResult<TValue>(ports, ruleResult.Diagnostics, ruleResult.UsedInference);
    }

    private static IReadOnlyList<PortMember> MembersOf(JsonObject obj)
    {
        var members = new List<PortMember>(obj.Count);
        foreach (var pair in obj)
        {
            members.Add(new PortMember(pair.Key, pair.Value is not null));
        }

        return members;
    }
}
