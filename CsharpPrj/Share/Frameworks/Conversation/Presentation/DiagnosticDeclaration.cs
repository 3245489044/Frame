#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Share.Conversation.Presentation;

/// <summary>诊断码声明条目（码 → 严重度 + 文案键）。</summary>
/// <param name="Code">诊断码（结构事实）。</param>
/// <param name="Severity">严重度（info/warn/error）。</param>
/// <param name="TextKey">文案键（宿主 loc 坐标；null = 由码自身可见呈现）。</param>
public sealed record DiagnosticSpec(string Code, string Severity, string? TextKey);

/// <summary>
/// **诊断码声明**（产品策略单源：<c>ui/diagnostics.json</c>）：
/// 渲染链只使用声明中的码，未声明码 ⇒ 可见回退（码即文案，严重度 warn），禁静默吞。
/// </summary>
public sealed record DiagnosticDeclaration(IReadOnlyDictionary<string, DiagnosticSpec> Codes)
{
    /// <summary>空声明。</summary>
    public static readonly DiagnosticDeclaration Empty =
        new(new Dictionary<string, DiagnosticSpec>(StringComparer.Ordinal));

    /// <summary>声明是否可用。</summary>
    public bool IsDeclared => Codes.Count > 0;

    /// <summary>取声明（未声明 ⇒ 可见回退：码即文案，severity=warn）。</summary>
    /// <param name="code">诊断码。</param>
    public DiagnosticSpec Describe(string code)
        => Codes.TryGetValue(code, out var spec) ? spec : new DiagnosticSpec(code, "warn", null);

    /// <summary>
    /// 解析声明 JSON。形制：<c>{ "codes": { "&lt;码&gt;": { "severity": "warn", "textKey": "…" } } }</c>
    /// </summary>
    /// <param name="json">声明 JSON 文本。</param>
    public static DiagnosticDeclaration Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var root = ConversationJson.Root(json);

        var codes = new Dictionary<string, DiagnosticSpec>(StringComparer.Ordinal);
        if (!root.TryGetProperty("codes", out var codeMap) || codeMap.ValueKind != JsonValueKind.Object)
        {
            return new DiagnosticDeclaration(codes);
        }

        foreach (var entry in codeMap.EnumerateObject())
        {
            var code = entry.Name.Trim();
            if (code.Length == 0) continue;

            var severity = "warn";
            string? textKey = null;
            if (entry.Value.ValueKind == JsonValueKind.Object)
            {
                if (entry.Value.TryGetProperty("severity", out var severityElement)
                    && severityElement.ValueKind == JsonValueKind.String)
                {
                    severity = (severityElement.GetString() ?? "warn").Trim();
                }

                if (entry.Value.TryGetProperty("textKey", out var keyElement)
                    && keyElement.ValueKind == JsonValueKind.String)
                {
                    textKey = keyElement.GetString();
                }
            }

            codes[code] = new DiagnosticSpec(code, severity, textKey);
        }

        return new DiagnosticDeclaration(codes);
    }
}
