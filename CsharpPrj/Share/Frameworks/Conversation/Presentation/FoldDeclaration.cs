#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;
using Share.Conversation.Rendering;

namespace Share.Conversation.Presentation;

/// <summary>折叠词声明条目（词 = 产品策略；本类型只承载数据）。</summary>
/// <param name="Id">声明词（规范化小写）。</param>
/// <param name="Mode">折叠模式。</param>
/// <param name="OpenLatest">收起态下是否打开最新轮。</param>
/// <param name="Supported">该词是否已实现（false ⇒ 可见诊断，禁静默等同 none）。</param>
/// <param name="SuppressHeader">是否抑制段头（视图声明 `grouping=none`；词表产出，代码禁字面量）。</param>
public sealed record FoldModeSpec(string Id, RoundFoldMode Mode, bool OpenLatest, bool Supported, bool SuppressHeader = false);

/// <summary>折叠解析结果（策略 + 诊断；诊断非空 ⇒ 调用方须可见化）。</summary>
/// <param name="Policy">折叠策略。</param>
/// <param name="UnknownWord">未声明的词（null = 已声明）。</param>
/// <param name="Degradation">降级原因诊断码（null = 正常）。</param>
public sealed record FoldResolution(FoldPolicy Policy, string? UnknownWord, string? Degradation);

/// <summary>
/// **折叠声明**（产品策略单源：<c>ui/fold.json</c>）：词表 → 模式/默认态，视图只引用词。
/// <para>未知词与未实现词均返回诊断（<c>unknown-fold-word</c> / <c>unsupported-fold-word</c>），
/// 禁静默降级为「不折叠」。</para>
/// </summary>
public sealed record FoldDeclaration(IReadOnlyDictionary<string, FoldModeSpec> Modes, string DefaultMode)
{
    /// <summary>未知词诊断码。</summary>
    public const string UnknownWordCode = "unknown-fold-word";

    /// <summary>未实现词诊断码。</summary>
    public const string UnsupportedWordCode = "unsupported-fold-word";

    /// <summary>扁平化词（不分组、不产段头；声明面 <c>fold.json:flat</c>）。</summary>
    public const string FlatWord = "flat";

    /// <summary>空声明（声明不可用）：任何词都视为未知。</summary>
    public static readonly FoldDeclaration Empty = new(
        new Dictionary<string, FoldModeSpec>(StringComparer.Ordinal),
        string.Empty);

    /// <summary>声明是否可用（词表非空）。</summary>
    public bool IsDeclared => Modes.Count > 0;

    /// <summary>解析声明词（缺省词 → <see cref="DefaultMode"/>；未知/未实现 ⇒ 诊断）。</summary>
    /// <param name="word">视图声明词（null/空 ⇒ 取缺省词）。</param>
    public FoldResolution Resolve(string? word)
    {
        if (!IsDeclared)
        {
            return new FoldResolution(
                FoldPolicy.Unresolved(word, "fold-declaration-missing"),
                null,
                "fold-declaration-missing");
        }

        var effective = string.IsNullOrWhiteSpace(word) ? DefaultMode : word!.Trim().ToLowerInvariant();
        if (effective.Length == 0)
        {
            return new FoldResolution(FoldPolicy.None, null, "fold-declaration-missing");
        }

        if (!Modes.TryGetValue(effective, out var spec))
        {
            return new FoldResolution(
                FoldPolicy.Unresolved(effective, UnknownWordCode),
                effective,
                UnknownWordCode);
        }

        var policy = new FoldPolicy(spec.Mode, spec.OpenLatest, spec.Id, SuppressHeader: spec.SuppressHeader);
        if (spec.Supported)
        {
            return new FoldResolution(policy, null, null);
        }

        var degradation = UnsupportedWordCode + ":" + spec.Id;
        return new FoldResolution(
            new FoldPolicy(spec.Mode, spec.OpenLatest, spec.Id, degradation, spec.SuppressHeader),
            null,
            degradation);
    }

    /// <summary>
    /// 解析声明 JSON。
    /// 形制：<c>{ "modes": { "&lt;词&gt;": { "mode": "closed|open|none", "openLatest": true, "supported": true } }, "default": "&lt;词&gt;" }</c>
    /// </summary>
    /// <param name="json">声明 JSON 文本。</param>
    public static FoldDeclaration Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var modes = new Dictionary<string, FoldModeSpec>(StringComparer.Ordinal);
        if (root.TryGetProperty("modes", out var modeMap) && modeMap.ValueKind == JsonValueKind.Object)
        {
            foreach (var entry in modeMap.EnumerateObject())
            {
                var id = entry.Name.Trim().ToLowerInvariant();
                if (id.Length == 0 || entry.Value.ValueKind != JsonValueKind.Object) continue;

                var mode = ParseMode(ReadString(entry.Value, "mode"));
                var openLatest = !entry.Value.TryGetProperty("openLatest", out var openLatestElement)
                                 || openLatestElement.ValueKind != JsonValueKind.False;
                var supported = !entry.Value.TryGetProperty("supported", out var supportedElement)
                                || supportedElement.ValueKind != JsonValueKind.False;
                var suppressHeader = entry.Value.TryGetProperty("suppressHeader", out var suppressElement)
                                     && suppressElement.ValueKind == JsonValueKind.True;
                modes[id] = new FoldModeSpec(id, mode, openLatest, supported, suppressHeader);
            }
        }

        var defaultMode = ReadString(root, "default")?.Trim().ToLowerInvariant() ?? string.Empty;
        return new FoldDeclaration(modes, defaultMode);
    }

    private static RoundFoldMode ParseMode(string? word)
        => word?.Trim().ToLowerInvariant() switch
        {
            "closed" => RoundFoldMode.Closed,
            "open" => RoundFoldMode.Open,
            _ => RoundFoldMode.None,
        };

    private static string? ReadString(JsonElement element, string name)
        => element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
}
