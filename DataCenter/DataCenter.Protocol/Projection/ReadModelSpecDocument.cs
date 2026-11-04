using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace DataCenter.Protocol.Projection;

/// <summary>
/// 一条读模型声明（规格 + 实现绑定名）：<c>projector=</c> 是声明面的实现键，
/// 装配侧按注册表解析（禁按模型 id 猜实现）。
/// </summary>
/// <param name="Spec">读模型规格。</param>
/// <param name="Projector">投影器声明名（空 = 未声明，装配期 fail-fast）。</param>
public sealed record ReadModelDeclaration(ReadModelSpec Spec, string Projector);

/// <summary>
/// 读模型声明文档（<c>readmodel.spec.generated.json</c>）的**唯一解析点**。
/// <para>
/// 纪律：① 保声明序（等价门可逐字段比对）；② 缺段 = 空集（不伪造）；③ 形态非法 ⇒ 抛
/// （由装配侧可见化，禁静默丢弃半截声明）；④ 只做「声明 → 规格」投影，不绑定实现。
/// </para>
/// </summary>
public static class ReadModelSpecDocument
{
    /// <summary>解析读模型声明 JSON（空文本 ⇒ 空集）。</summary>
    /// <param name="json">发射器产物文本。</param>
    public static IReadOnlyList<ReadModelSpec> Parse(string? json)
        => ParseDeclarations(json).Select(d => d.Spec).ToArray();

    /// <summary>解析读模型声明（含 <c>projector=</c> 实现名；空文本 ⇒ 空集）。</summary>
    /// <param name="json">发射器产物文本。</param>
    public static IReadOnlyList<ReadModelDeclaration> ParseDeclarations(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<ReadModelDeclaration>();

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("读模型声明形态非法：根节点须为对象");
        }

        if (!root.TryGetProperty("readModels", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<ReadModelDeclaration>();
        }

        var declarations = new List<ReadModelDeclaration>();
        var index = 0;
        foreach (var element in array.EnumerateArray())
        {
            declarations.Add(ReadDeclaration(element, index));
            index++;
        }

        return declarations;
    }

    private static ReadModelDeclaration ReadDeclaration(JsonElement element, int index)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"读模型声明形态非法：readModels[{index}] 不是对象");
        }

        var modelId = ReadRequiredString(element, "id", $"readModels[{index}].id");
        var version = ParseVersion(ReadString(element, "version"), $"readModels[{index}].version");
        int? ttl = ReadInt32(element, "ttl");
        if (version == ReadModelVersionMode.Ttl && (ttl is null || ttl <= 0))
        {
            throw new InvalidOperationException(
                $"读模型声明形态非法：'{modelId}' 版本源=Ttl 但 ttl 缺失或非正");
        }

        var spec = ReadModelSpec.Create(
            modelId,
            ReadStringArray(element, "sources"),
            version,
            ttl,
            ReadStringArray(element, "args"));

        var projector = ReadString(element, "projector");
        if (string.IsNullOrWhiteSpace(projector))
        {
            throw new InvalidOperationException($"读模型声明形态非法：'{modelId}' 缺 projector=");
        }

        return new ReadModelDeclaration(spec, projector.Trim());
    }

    /// <summary>版本源词形 → 枚举（未登记词形 fail-fast，禁静默回落 None）。</summary>
    private static ReadModelVersionMode ParseVersion(string? wire, string location)
        => wire switch
        {
            "None" or "none" => ReadModelVersionMode.None,
            "Ttl" or "ttl" => ReadModelVersionMode.Ttl,
            "Event" or "event" => ReadModelVersionMode.Event,
            "GraphMembership" or "graph-membership" => ReadModelVersionMode.GraphMembership,
            "Declared" or "declared" => ReadModelVersionMode.Declared,
            _ => throw new InvalidOperationException($"读模型声明形态非法：{location} 未登记的版本源 '{wire}'"),
        };

    private static string ReadRequiredString(JsonElement element, string name, string location)
    {
        var value = ReadString(element, name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"读模型声明形态非法：{location} 缺失或为空");
        }

        return value;
    }

    private static string? ReadString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property)) return null;
        return property.ValueKind == JsonValueKind.String ? property.GetString() : null;
    }

    private static int? ReadInt32(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property)) return null;
        return property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value) ? value : null;
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        var values = new List<string>();
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String) continue;
            var value = item.GetString();
            if (!string.IsNullOrWhiteSpace(value)) values.Add(value);
        }

        return values;
    }
}
