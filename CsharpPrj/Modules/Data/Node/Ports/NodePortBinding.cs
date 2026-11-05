using System.Text.Json.Nodes;
using Modules.Node.Diagnostics;

namespace Modules.Node.Ports;

public sealed record PortBindingResult(
    IReadOnlyDictionary<string, string> Ports,
    IReadOnlyList<NodeDiagnostic> Diagnostics,
    bool UsedInference)
{
    /// <summary>端口存在性三态（值路径/JSON 路径同口径）。</summary>
    public IReadOnlyDictionary<string, PortPresence> Presence { get; init; } =
        new Dictionary<string, PortPresence>(StringComparer.Ordinal);

    /// <summary>声明过但实现完全没给出的端口（与显式 null 可分）。</summary>
    public IReadOnlyCollection<string> MissingDeclared { get; init; } = Array.Empty<string>();
}

/// <summary>宿主原生值形态的端口提取结果（值缝 V1：规则与 JSON 路径同一实现，值不经 JSON 往返）。</summary>
public sealed record PortValuesResult<TValue>(
    IReadOnlyDictionary<string, TValue> Ports,
    IReadOnlyList<NodeDiagnostic> Diagnostics,
    bool UsedInference)
{
    /// <summary>端口存在性三态（显式 null 留痕）。</summary>
    public IReadOnlyDictionary<string, PortPresence> Presence { get; init; } =
        new Dictionary<string, PortPresence>(StringComparer.Ordinal);

    /// <summary>声明过但实现完全没给出的端口（与显式 null 可分）。</summary>
    public IReadOnlyCollection<string> MissingDeclared { get; init; } = Array.Empty<string>();
}

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
        var presence = new Dictionary<string, PortPresence>(StringComparer.Ordinal);
        var missingDeclared = new List<string>();

        foreach (var decision in ruleResult.Decisions)
        {
            presence[decision.Port] = decision.Presence;

            if (decision.Admission == PortAdmission.Default)
            {
                // 简单值（含非 JSON 原文）：整存默认端口 = 节点键
                ports[nodeKey] = root is null ? resultJson : NodeJson.ValueToText(root);
                presence[nodeKey] = PortPresence.Value;
                continue;
            }

            if (decision.Admission == PortAdmission.DeclaredMissing)
            {
                missingDeclared.Add(decision.Port);
                continue;
            }

            if (decision.Admission == PortAdmission.DeclaredNull)
            {
                // 显式 null 留痕：不产文本槽位，但 presence 可判（JSON 路径不静默压平）。
                continue;
            }

            if (root is JsonObject source && source.TryGetPropertyValue(decision.Port, out var value) && value is not null)
            {
                ports[decision.Port] = NodeJson.ValueToText(value);
                presence[decision.Port] = PortPresence.Value;
            }
        }

        foreach (var diagnostic in ruleResult.Diagnostics)
        {
            _diagnostics.Report(diagnostic);
        }

        return new PortBindingResult(ports, ruleResult.Diagnostics, ruleResult.UsedInference)
        {
            Presence = presence,
            MissingDeclared = missingDeclared,
        };
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
        var presence = new Dictionary<string, PortPresence>(StringComparer.Ordinal);
        var missingDeclared = new List<string>();

        foreach (var decision in ruleResult.Decisions)
        {
            presence[decision.Port] = decision.Presence;

            if (decision.Admission == PortAdmission.DeclaredMissing)
            {
                // 缺席：不产槽位，但记入 MissingDeclared（与显式 null 可分）。
                missingDeclared.Add(decision.Port);
                continue;
            }

            if (decision.Admission == PortAdmission.DeclaredNull)
            {
                // 显式 null：值槽位由调用侧按 presence 投影（泛型 TValue 不保证可空）。
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

        return new PortValuesResult<TValue>(ports, ruleResult.Diagnostics, ruleResult.UsedInference)
        {
            Presence = presence,
            MissingDeclared = missingDeclared,
        };
    }

    private static IReadOnlyList<PortMember> MembersOf(JsonObject obj)
    {
        var members = new List<PortMember>(obj.Count);
        foreach (var pair in obj)
        {
            members.Add(new PortMember(pair.Key, pair.Value is not null)
            {
                Presence = pair.Value is null ? PortPresence.ExplicitNull : PortPresence.Value,
            });
        }

        return members;
    }
}
