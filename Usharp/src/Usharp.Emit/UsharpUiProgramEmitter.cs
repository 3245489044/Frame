#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Plate.DslMethod.Usharp;
using Usharp.Semantics;
using Usharp.Syntax;

namespace Usharp.Emit;

/// <summary>UI 个性化声明发射产物（<c>ui-v1</c> + 指纹 + 诊断）。</summary>
public sealed record UsharpUiProgramResult(
    string? Json, string? Fingerprint, IReadOnlyList<UsharpDiagnostic> Diagnostics) : UsharpEmitResultBase(Diagnostics);

/// <summary>
/// 单一 UI 声明程序发射器：一个编译单元 → 一个产物。
/// <para>
/// 记录前缀（唯一声明入口，四类记录同属一个程序）：
/// <c>ui_token_*</c>（语义值）／<c>ui_slot_*</c>（槽位 → 各载体绑定）／
/// <c>ui_skin_*</c>（皮肤 → token 覆盖）／<c>ui_part_*</c>（渲染单元 → 元素 + 槽位 + 状态）。
/// </para>
/// <para>纪律：发射器不含任何载体名（载体 id = 嵌套记录字段名）；不做语义校验（判据归门禁）。</para>
/// </summary>
public static class UsharpUiProgramEmitter
{
    /// <summary>产物哨兵。</summary>
    public const string Schema = "ui-v1";

    /// <summary>语义值记录前缀。</summary>
    public const string TokenPrefix = "ui_token_";

    /// <summary>槽位记录前缀。</summary>
    public const string SlotPrefix = "ui_slot_";

    /// <summary>皮肤记录前缀。</summary>
    public const string SkinPrefix = "ui_skin_";

    /// <summary>渲染单元记录前缀。</summary>
    public const string PartPrefix = "ui_part_";

    /// <summary>发射（声明缺项 ⇒ 不产出 + 诊断）。</summary>
    /// <param name="compilation">声明编译结果。</param>
    public static UsharpUiProgramResult Emit(UsharpCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        var diagnostics = new List<UsharpDiagnostic>();
        var tokens = new JsonObject();
        var slots = new JsonObject();
        var skins = new JsonObject();
        var parts = new JsonObject();

        foreach (var record in compilation.Records)
        {
            if (record.RecordId.StartsWith(TokenPrefix, StringComparison.Ordinal))
            {
                var name = Name(record, TokenPrefix, "name");
                var node = new JsonObject { ["value"] = Str(record.Body, "value", string.Empty) };
                var kind = StrOrNull(record.Body, "kind");
                if (kind is not null) node["kind"] = kind;
                tokens[name] = node;
            }
            else if (record.RecordId.StartsWith(SlotPrefix, StringComparison.Ordinal))
            {
                var name = Name(record, SlotPrefix, "name");
                var carriers = new JsonObject();
                foreach (var pair in record.Body.Fields)
                {
                    if (string.Equals(pair.Key, "name", StringComparison.Ordinal)) continue;
                    if (pair.Value is UsharpRecord binding) carriers[pair.Key] = ScalarProps(binding);
                }

                slots[name] = carriers;
            }
            else if (record.RecordId.StartsWith(SkinPrefix, StringComparison.Ordinal))
            {
                var name = Name(record, SkinPrefix, "name");
                var node = new JsonObject();
                var extends = StrOrNull(record.Body, "extends");
                if (extends is not null) node["extends"] = extends;
                var density = StrOrNull(record.Body, "density");
                if (density is not null) node["density"] = density;
                node["tokens"] = Rec(record.Body, "tokens") is { } overrides ? ScalarProps(overrides) : new JsonObject();
                skins[name] = node;
            }
            else if (record.RecordId.StartsWith(PartPrefix, StringComparison.Ordinal))
            {
                var name = Name(record, PartPrefix, "name");
                parts[name] = new JsonObject
                {
                    ["kind"] = Str(record.Body, "kind", "role"),
                    ["slots"] = Rec(record.Body, "slots") is { } slotRefs ? ScalarProps(slotRefs) : new JsonObject(),
                    ["state"] = NestedProps(Rec(record.Body, "state")),
                    ["carriers"] = NestedProps(Rec(record.Body, "carriers")),
                };
            }
        }

        var file = compilation.Documents.FirstOrDefault()?.FilePath ?? "(memory)";
        if (tokens.Count == 0 || slots.Count == 0 || parts.Count == 0)
        {
            diagnostics.Add(UsharpDiagnostic.Error(UsharpCodes.UiDeclarationNotProducible,
                "UI 个性化声明不可产出：缺 [Record] ui_token_* / ui_slot_* / ui_part_*（单一程序四类记录）",
                file, 1));
            return new UsharpUiProgramResult(null, null, diagnostics);
        }

        var body = new JsonObject
        {
            ["schema"] = Schema,
            ["slots"] = slots,
            ["tokens"] = tokens,
            ["skins"] = skins,
            ["parts"] = parts,
        };

        var compact = body.ToJsonString();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(compact))).ToLowerInvariant();
        var document = JsonNode.Parse(compact)!.AsObject();
        document.Insert(1, "fingerprint", hash);

        return new UsharpUiProgramResult(document.ToJsonString(), hash, diagnostics);
    }

    /// <summary>记录键（显式 name 字段优先，缺省取记录 id 后缀）。</summary>
    private static string Name(UsharpRecordDecl record, string prefix, string field)
        => Str(record.Body, field, record.RecordId[prefix.Length..]);

    private static string Str(UsharpRecord record, string name, string fallback)
        => record.TryGet(name, out var value) && value is UsharpScalar { Kind: not UsharpScalarKind.Null } scalar
            ? scalar.Text
            : fallback;

    private static string? StrOrNull(UsharpRecord record, string name)
        => record.TryGet(name, out var value) && value is UsharpScalar { Kind: not UsharpScalarKind.Null } scalar
            ? scalar.Text
            : null;

    private static UsharpRecord? Rec(UsharpRecord record, string name)
        => record.TryGet(name, out var value) && value is UsharpRecord nested ? nested : null;

    /// <summary>标量字段 → JSON 对象（布尔/整数保持原类型）。</summary>
    private static JsonObject ScalarProps(UsharpRecord record)
    {
        var node = new JsonObject();
        foreach (var pair in record.Fields)
        {
            if (pair.Value is UsharpScalar { Kind: not UsharpScalarKind.Null } scalar)
            {
                node[pair.Key] = ScalarNode(scalar);
            }
        }

        return node;
    }

    /// <summary>一层嵌套记录（state → { 状态 → { 槽位 → 值 } }；carriers → { 载体 → {...} }）。</summary>
    private static JsonObject NestedProps(UsharpRecord? record)
    {
        var node = new JsonObject();
        if (record is null) return node;

        foreach (var pair in record.Fields)
        {
            node[pair.Key] = pair.Value switch
            {
                UsharpRecord nested => ScalarProps(nested),
                UsharpScalar { Kind: not UsharpScalarKind.Null } scalar => ScalarNode(scalar),
                _ => null,
            };
        }

        return node;
    }

    private static JsonNode? ScalarNode(UsharpScalar scalar)
        => scalar.Kind switch
        {
            UsharpScalarKind.Bool => JsonValue.Create(string.Equals(scalar.Text, "true", StringComparison.OrdinalIgnoreCase)),
            UsharpScalarKind.Int when int.TryParse(scalar.Text, out var number) => JsonValue.Create(number),
            UsharpScalarKind.Null => null,
            _ => JsonValue.Create(scalar.Text),
        };
}

