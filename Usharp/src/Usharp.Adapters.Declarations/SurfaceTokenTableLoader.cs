#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Usharp.Emit;

namespace Usharp.Adapters.Declarations;

/// <summary>
/// token/皮肤来源装载（I/O 边界）：<c>ui.generated.json</c> → <see cref="SurfaceTokenTable"/>。
/// <para>文件缺失 ⇒ null（不做值替换，保持旧行为）；解析失败 ⇒ null + <c>error</c>（可见化，禁静默回退）。</para>
/// </summary>
internal static class SurfaceTokenTableLoader
{
    /// <summary>装载 token 表。</summary>
    /// <param name="path">产物路径（<c>ui.generated.json</c>）。</param>
    /// <param name="error">失败原因（成功或文件缺失 ⇒ null）。</param>
    public static SurfaceTokenTable? Load(string? path, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path!)) return null;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path!));
            var root = document.RootElement;
            var baseTokens = ReadTokens(root, "tokens");
            var skins = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);

            if (root.TryGetProperty("skins", out var skinsNode) && skinsNode.ValueKind == JsonValueKind.Object)
            {
                var declared = new Dictionary<string, (string Extends, Dictionary<string, string> Tokens)>(StringComparer.Ordinal);
                foreach (var skin in skinsNode.EnumerateObject())
                {
                    var extends = skin.Value.ValueKind == JsonValueKind.Object
                                  && skin.Value.TryGetProperty("extends", out var parent)
                                  && parent.ValueKind == JsonValueKind.String
                        ? parent.GetString() ?? string.Empty
                        : string.Empty;
                    var tokens = skin.Value.ValueKind == JsonValueKind.Object
                        ? ReadTokens(skin.Value, "tokens")
                        : new Dictionary<string, string>(StringComparer.Ordinal);
                    declared[skin.Name] = (extends, new Dictionary<string, string>(tokens, StringComparer.Ordinal));
                }

                foreach (var name in declared.Keys)
                {
                    skins[name] = MergeSkin(name, declared, new HashSet<string>(StringComparer.Ordinal));
                }
            }

            return new SurfaceTokenTable(baseTokens, skins);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            error = $"token 表不可解析：{path}（{ex.Message}）";
            return null;
        }
    }

    /// <summary>皮肤 extends 链合并（父先子后；环 ⇒ 停止展开，不抛）。</summary>
    private static IReadOnlyDictionary<string, string> MergeSkin(
        string name,
        IReadOnlyDictionary<string, (string Extends, Dictionary<string, string> Tokens)> declared,
        HashSet<string> visiting)
    {
        var merged = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!declared.TryGetValue(name, out var skin) || !visiting.Add(name)) return merged;

        if (!string.IsNullOrWhiteSpace(skin.Extends))
        {
            foreach (var pair in MergeSkin(skin.Extends, declared, visiting)) merged[pair.Key] = pair.Value;
        }

        foreach (var pair in skin.Tokens) merged[pair.Key] = pair.Value;
        visiting.Remove(name);
        return merged;
    }

    /// <summary>读 <c>{token: {value}}</c>（兼容直接字符串值）。</summary>
    private static IReadOnlyDictionary<string, string> ReadTokens(JsonElement root, string name)
    {
        var tokens = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!root.TryGetProperty(name, out var node) || node.ValueKind != JsonValueKind.Object) return tokens;

        foreach (var token in node.EnumerateObject())
        {
            if (token.Value.ValueKind == JsonValueKind.String)
            {
                tokens[token.Name] = token.Value.GetString() ?? string.Empty;
                continue;
            }

            if (token.Value.ValueKind != JsonValueKind.Object
                || !token.Value.TryGetProperty("value", out var value)) continue;

            tokens[token.Name] = value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? string.Empty,
                JsonValueKind.Number => value.GetRawText(),
                _ => string.Empty,
            };
        }

        return tokens;
    }
}
