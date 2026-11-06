using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Modules.Parsing.Invocation;

/// <summary>调用面声明（内存形态）：语法形状 + 目标命名空间 + 参数白名单。</summary>
public sealed record InvocationSpec(
    string Action,
    string? SubAction,
    NameSpaceId TargetSpace,
    IReadOnlyList<string>? Args = null,
    bool AllowProbeArgs = false,
    IReadOnlyDictionary<string, NameSpaceId>? ArgSpaces = null,
    int MinTargetTokens = 1)
{
    /// <summary>参数键是否被本命令接受（声明键 ∪ 探测参数 ∪ 具名参数空间）。</summary>
    public bool AcceptsArg(string key)
        => (!string.IsNullOrWhiteSpace(key))
           && ((Args is not null && Args.Contains(key, StringComparer.OrdinalIgnoreCase))
               || (AllowProbeArgs && !IsReservedArg(key))
               || (ArgSpaces is not null && ArgSpaces.ContainsKey(key)));

    /// <summary>命令级保留键（其余键对 probe 类命令即探测函数参数）。</summary>
    private static bool IsReservedArg(string key)
        => key is "timeout" or "interval" or "output" or "failWhen" or "failContains"
            or "expectedContains" or "contains" or "modifier" or "wait" or "critical" or "style";

    public string DescribeArgs()
    {
        var declared = Args is { Count: > 0 } ? string.Join(",", Args) : "<none>";
        return AllowProbeArgs ? declared + ",<probe args>" : declared;
    }
}

/// <summary>声明文件根（字段名对齐 <c>invocation.specs.json</c>）。</summary>
public sealed class InvocationSpecDocument
{
    [JsonPropertyName("$schema")]
    public string? Schema { get; set; }

    /// <summary>命令级默认预算（ms）；供命令层读取同一数值（不另写缺省常量）。</summary>
    [JsonPropertyName("defaultTimeoutMs")]
    public int? DefaultTimeoutMs { get; set; }

    [JsonPropertyName("specs")]
    public List<InvocationSpecEntry> Specs { get; set; } = new();
}

/// <summary>单条声明（缺失即「声明不完整」，由加载期判红）。</summary>
public sealed class InvocationSpecEntry
{
    [JsonPropertyName("action")]
    public string? Action { get; set; }

    [JsonPropertyName("subAction")]
    public string? SubAction { get; set; }

    [JsonPropertyName("targetSpace")]
    public string? TargetSpace { get; set; }

    [JsonPropertyName("args")]
    public List<string>? Args { get; set; }

    [JsonPropertyName("allowProbeArgs")]
    public bool? AllowProbeArgs { get; set; }

    [JsonPropertyName("argSpaces")]
    public Dictionary<string, string>? ArgSpaces { get; set; }

    [JsonPropertyName("minTargetTokens")]
    public int? MinTargetTokens { get; set; }
}
