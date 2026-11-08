#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Share.Conversation.Rendering.Surface;

public sealed partial class UiDeclaration
{
    /// <summary>解析生成物（不可解析 ⇒ 抛；失败姿态由调用方声明）。</summary>
    /// <param name="json">生成物文本。</param>
    public static UiDeclaration Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        var diagnostics = new List<string>();
        var root = ConversationJson.Root(json);

        var fingerprint = ReadText(root, "fingerprint") ?? string.Empty;
        var slots = ParseSlots(root, diagnostics);
        var tokens = ParseTokens(root, diagnostics);
        var skins = ParseSkins(root, diagnostics);
        var parts = ParseParts(root, diagnostics);
        var carriers = ParseCarriers(root, diagnostics);

        var declared = slots.Count > 0 && tokens.Count > 0 && parts.Count > 0;
        if (!declared) diagnostics.Add("declaration-empty");
        if (carriers.Count == 0) diagnostics.Add("carriers-undeclared");

        return new UiDeclaration(slots, tokens, skins, parts, carriers, fingerprint, declared, diagnostics);
    }

    /// <summary>载体清单（单源 = [Table] UiCarrier；缺声明 ⇒ 空 + 可见诊断）。</summary>
    private static List<string> ParseCarriers(JsonElement root, List<string> diagnostics)
    {
        var carriers = new List<string>();
        if (!root.TryGetProperty("carriers", out var element) || element.ValueKind != JsonValueKind.Array) return carriers;

        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String) continue;
            var id = item.GetString();
            if (!string.IsNullOrWhiteSpace(id) && !carriers.Contains(id)) carriers.Add(id);
        }

        return carriers;
    }

    private static Dictionary<string, UiSlotSpec> ParseSlots(JsonElement root, List<string> diagnostics)
    {
        var slots = new Dictionary<string, UiSlotSpec>(StringComparer.Ordinal);
        if (!root.TryGetProperty("slots", out var element) || element.ValueKind != JsonValueKind.Object) return slots;

        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Add($"slot-shape:{property.Name}");
                continue;
            }

            var bindings = new Dictionary<string, UiSlotBinding>(StringComparer.Ordinal);
            foreach (var carrier in property.Value.EnumerateObject())
            {
                if (carrier.Value.ValueKind != JsonValueKind.Object)
                {
                    diagnostics.Add($"binding-shape:{property.Name}.{carrier.Name}");
                    continue;
                }

                bindings[carrier.Name] = new UiSlotBinding(
                    carrier.Name,
                    ReadText(carrier.Value, "bind") ?? "none",
                    ReadText(carrier.Value, "prop") ?? string.Empty,
                    ReadText(carrier.Value, "unit") ?? string.Empty);
            }

            slots[property.Name] = new UiSlotSpec(property.Name, bindings);
        }

        return slots;
    }

    private static Dictionary<string, UiTokenSpec> ParseTokens(JsonElement root, List<string> diagnostics)
    {
        var tokens = new Dictionary<string, UiTokenSpec>(StringComparer.Ordinal);
        if (!root.TryGetProperty("tokens", out var element) || element.ValueKind != JsonValueKind.Object) return tokens;

        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Add($"token-shape:{property.Name}");
                continue;
            }

            tokens[property.Name] = new UiTokenSpec(
                property.Name,
                ReadText(property.Value, "kind") ?? string.Empty,
                ReadText(property.Value, "value") ?? string.Empty);
        }

        return tokens;
    }

    private static Dictionary<string, UiSkinSpec> ParseSkins(JsonElement root, List<string> diagnostics)
    {
        var skins = new Dictionary<string, UiSkinSpec>(StringComparer.Ordinal);
        if (!root.TryGetProperty("skins", out var element) || element.ValueKind != JsonValueKind.Object) return skins;

        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Add($"skin-shape:{property.Name}");
                continue;
            }

            skins[property.Name] = new UiSkinSpec(
                property.Name,
                ReadText(property.Value, "extends"),
                ReadText(property.Value, "density") ?? string.Empty,
                ReadBool(property.Value, "default"),
                ReadMap(property.Value, "tokens", diagnostics, property.Name));
        }

        return skins;
    }

    private static Dictionary<string, UiPartSpec> ParseParts(JsonElement root, List<string> diagnostics)
    {
        var parts = new Dictionary<string, UiPartSpec>(StringComparer.Ordinal);
        if (!root.TryGetProperty("parts", out var element) || element.ValueKind != JsonValueKind.Object) return parts;

        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Add($"part-shape:{property.Name}");
                continue;
            }

            var states = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
            if (property.Value.TryGetProperty("state", out var stateElement) && stateElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var state in stateElement.EnumerateObject())
                {
                    if (state.Value.ValueKind != JsonValueKind.Object) continue;
                    states[state.Name] = ReadMap(state.Value, null, diagnostics, $"{property.Name}.{state.Name}");
                }
            }

            var carriers = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
            if (property.Value.TryGetProperty("carriers", out var carrierElement) && carrierElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var carrier in carrierElement.EnumerateObject())
                {
                    if (carrier.Value.ValueKind != JsonValueKind.Object) continue;
                    carriers[carrier.Name] = ReadMap(carrier.Value, null, diagnostics, $"{property.Name}.{carrier.Name}");
                }
            }

            var slotRefs = property.Value.TryGetProperty("slots", out var slotElement) && slotElement.ValueKind == JsonValueKind.Object
                ? ReadMap(slotElement, null, diagnostics, property.Name)
                : new Dictionary<string, string>(StringComparer.Ordinal);

            parts[property.Name] = new UiPartSpec(
                property.Name,
                ReadText(property.Value, "kind") ?? "role",
                slotRefs,
                states,
                carriers);
        }

        return parts;
    }

    private static string? ReadText(JsonElement element, string name)
        => element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    /// <summary>布尔属性（缺失 ⇒ false）。</summary>
    private static bool ReadBool(JsonElement element, string name)
        => element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.True;

    private static IReadOnlyDictionary<string, string> ReadMap(
        JsonElement element, string? name, List<string> diagnostics, string owner)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var source = element;
        if (name is not null)
        {
            if (!element.TryGetProperty(name, out source) || source.ValueKind != JsonValueKind.Object) return map;
        }

        foreach (var property in source.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String)
            {
                map[property.Name] = property.Value.GetString() ?? string.Empty;
            }
            else
            {
                diagnostics.Add($"value-shape:{owner}.{property.Name}");
            }
        }

        return map;
    }
}
