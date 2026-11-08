#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Share.Conversation.Rendering.Surface;

/// <summary>
/// 读路径事实声明（<c>nodes/read-paths.json</c> 的 <c>paths[].facts</c> 合并面）。
/// <para>facts 字段名 = 帧字段袋键名；作为「字段口径」的通用键源之一。</para>
/// </summary>
public sealed class ReadPathFactsDeclaration
{
    /// <summary>空声明（未装配）：不参与适用性裁决。</summary>
    public static readonly ReadPathFactsDeclaration Empty =
        new(new HashSet<string>(StringComparer.Ordinal), declared: false);

    private readonly HashSet<string> _facts;

    private ReadPathFactsDeclaration(HashSet<string> facts, bool declared)
    {
        _facts = facts;
        IsDeclared = declared;
    }

    /// <summary>是否来自可用声明。</summary>
    public bool IsDeclared { get; }

    /// <summary>已声明事实键集。</summary>
    public IReadOnlyCollection<string> Facts => _facts;

    /// <summary>是否声明了该键。</summary>
    /// <param name="key">帧字段键。</param>
    public bool Contains(string key) => key is { Length: > 0 } && _facts.Contains(key);

    /// <summary>解析声明 JSON（<c>read-paths-v1</c>）；结构不符 ⇒ 空声明（失败姿态由调用方决定）。</summary>
    /// <param name="json">声明文本。</param>
    public static ReadPathFactsDeclaration Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = JsonDocument.Parse(json);
        var facts = new HashSet<string>(StringComparer.Ordinal);
        if (document.RootElement.TryGetProperty("paths", out var paths) && paths.ValueKind == JsonValueKind.Array)
        {
            foreach (var path in paths.EnumerateArray())
            {
                if (path.ValueKind != JsonValueKind.Object) continue;
                if (!path.TryGetProperty("facts", out var list) || list.ValueKind != JsonValueKind.Array) continue;
                foreach (var item in list.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } key) facts.Add(key);
                }
            }
        }

        return new ReadPathFactsDeclaration(facts, declared: facts.Count > 0);
    }
}
