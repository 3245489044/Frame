using System;
using System.Text.Json;
using Plate.DslMethod.Usharp;

namespace Operation.DataStoreDriven.Core;

/// <summary>负载读取单源（U4）：<c>ParsePayload</c> / <c>ReadString</c> / <c>TryGet</c>，取代 5+ 存储的私有副本。</summary>
public static class KvPayloadReader
{
    /// <summary>JSON 文本 → 中立值（非法 JSON ⇒ 不透明字符串标量，原样保留）。</summary>
    public static UsharpValue ParsePayload(string json)
    {
        try { return UsharpValueJson.Parse(json); }
        catch { return UsharpScalar.Str(json); }
    }

    /// <summary>记录字段 → 字符串（与历史根存储既有口径逐字一致）。</summary>
    public static string? ReadString(UsharpRecord body, string field)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!body.TryGet(field, out var value)) return null;
        var json = UsharpValueJson.ToJson(value);
        try { return JsonSerializer.Deserialize<string>(json); }
        catch { return json; }
    }

    /// <summary>记录字段 → 字符串（仅标量且非 Null；否则 null）。</summary>
    public static string? ReadScalarText(UsharpRecord body, string field)
        => body.TryGet(field, out var v) && v is UsharpScalar sc && sc.Kind != UsharpScalarKind.Null ? sc.Text : null;

    /// <summary>记录字段存在性 + 值读取。</summary>
    public static bool TryGet(UsharpRecord body, string field, out UsharpValue value)
        => body.TryGet(field, out value);
}
