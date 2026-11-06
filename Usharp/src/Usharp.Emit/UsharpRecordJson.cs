#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using FrameworkAbstractions.Serialization;
using Plate.DslMethod.Usharp;

namespace Usharp.Emit;

/// <summary>记录值 → JSON 的取值助手（发射器共用；只读，不做语义校验）。</summary>
internal static class UsharpRecordJson
{
    internal static string Str(UsharpRecord record, string name, string fallback)
        => record.TryGet(name, out var v) && v is UsharpScalar { Kind: not UsharpScalarKind.Null } s
            ? s.Text : fallback;

    internal static string? StrOrNull(UsharpRecord record, string name)
        => record.TryGet(name, out var v) && v is UsharpScalar { Kind: not UsharpScalarKind.Null } s
            ? s.Text : null;

    internal static bool Bool(UsharpRecord record, string name, bool fallback)
        => record.TryGet(name, out var v) && v is UsharpScalar { Kind: UsharpScalarKind.Bool } s
            ? string.Equals(s.Text, "true", StringComparison.OrdinalIgnoreCase) : fallback;

    internal static IReadOnlyList<string> StrList(UsharpRecord record, string name)
    {
        var list = new List<string>();
        if (record.TryGet(name, out var v) && v is UsharpList items)
        {
            foreach (var item in items.Items)
            {
                if (item is UsharpScalar { Kind: UsharpScalarKind.String } s && s.Text.Length > 0)
                {
                    list.Add(s.Text);
                }
            }
        }

        return list;
    }

    internal static UsharpList? List(UsharpRecord record, string name)
        => record.TryGet(name, out var v) && v is UsharpList items ? items : null;

    internal static JsonArray Array(IReadOnlyList<string> values) => JsonCollections.Array(values);

    internal static JsonObject Props(UsharpRecord record)
    {
        var node = new JsonObject();
        foreach (var pair in record.Fields)
        {
            if (pair.Value is UsharpScalar { Kind: not UsharpScalarKind.Null } scalar)
            {
                var flag = BoolText(scalar);
                node[pair.Key] = flag.HasValue
                    ? JsonValue.Create(flag.Value)
                    : JsonValue.Create(scalar.Text);
            }
        }

        return node;
    }

    private static bool? BoolText(UsharpScalar scalar)
        => scalar.Kind == UsharpScalarKind.Bool
            ? string.Equals(scalar.Text, "true", StringComparison.OrdinalIgnoreCase)
            : null;
}
