#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Plate.DslMethod.Usharp;
using Usharp.Semantics;
using Usharp.Syntax;

namespace Usharp.Emit;

/// <summary>表面程序发射产物（<c>surface-v1</c> + 指纹 + 诊断）。</summary>
public sealed record UsharpSurfaceProgramResult(
    string? Json, string? Fingerprint, IReadOnlyList<UsharpDiagnostic> Diagnostics) : UsharpEmitResultBase(Diagnostics);

/// <summary>
/// 表面程序发射器：容器 / 映射 / 视图 → 一个产物（<c>surface-v1</c>）。
/// <para>
/// 记录前缀：<c>surface_container_*</c>（容器：结构寻址单位）／<c>surface_mapping_*</c>（容器→载体结构映射）／
/// <c>surface_view_*</c>（视图 → 容器键列表）。
/// </para>
/// <para>
/// 容器标识 = <c>view:key</c>：DOM 名 <c>key</c> 只在所属视图内唯一（现状 <c>region-seg</c> 同属 history/task/node），
/// 故以「视图 + 键」作全局主键；<c>owner</c> 必须等于 <c>view</c>。
/// </para>
/// <para>纪律：发射器不含载体字面量（载体 id 只作嵌套记录字段名）；结构完整性判据在此闭合（缺映射/写模式非法 ⇒ 不产出）。</para>
/// </summary>
public static class UsharpSurfaceProgramEmitter
{
    public const string Schema = "surface-v1";
    public const string ContainerPrefix = "surface_container_";
    public const string MappingPrefix = "surface_mapping_";
    public const string ViewPrefix = "surface_view_";
    public const string PrimitivePrefix = "surface_primitive_";
    public const string LayerPrefix = "surface_layer_";
    public const string MaterialPrefix = "surface_material_";
    public const string ProfilePrefix = "surface_profile_";
    public const string CarrierPrefix = "surface_carrier_";
    public const string StatePrefix = "surface_state_";
    public const string AssetPrefix = "surface_asset_";
    public const string SettingPrefix = "surface_setting_";

    /// <summary>容器写模式受控词。</summary>
    public static readonly IReadOnlySet<string> WriteModes =
        new HashSet<string>(new[] { "replace", "append", "leaf" }, StringComparer.Ordinal);

    /// <summary>映射目标种类受控词。</summary>
    public static readonly IReadOnlySet<string> MappingKinds =
        new HashSet<string>(new[] { "container", "item", "cell" }, StringComparer.Ordinal);

    /// <summary>发射（结构不闭合 ⇒ 不产出 + 诊断）。</summary>
    /// <param name="compilation">声明编译结果。</param>
    /// <param name="tokens">token/皮肤表（null ⇒ 不做值替换，与历史产物等价）。</param>
    public static UsharpSurfaceProgramResult Emit(UsharpCompilation compilation, SurfaceTokenTable? tokens = null)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        var file = compilation.Documents.FirstOrDefault()?.FilePath ?? "(memory)";
        var diagnostics = new List<UsharpDiagnostic>();
        var mappings = new JsonObject();
        var containers = new JsonObject();
        var views = new JsonObject();
        var primitives = new JsonArray();
        var states = new JsonArray();
        var layers = new JsonObject();
        var materials = new JsonObject();
        var profiles = new JsonObject();
        var carriers = new JsonObject();
        var assets = new JsonObject();
        var settings = new JsonObject();

        foreach (var record in compilation.Records)
        {
            if (record.RecordId.StartsWith(MappingPrefix, StringComparison.Ordinal))
            {
                var id = UsharpRecordJson.Str(record.Body, "id", record.RecordId[MappingPrefix.Length..]);
                var kind = UsharpRecordJson.Str(record.Body, "kind", "container");
                if (!MappingKinds.Contains(kind))
                {
                    diagnostics.Add(Error(file, $"映射 '{id}' 的 kind 非法：{kind}（须 ∈ {{container,item,cell}}）"));
                    continue;
                }

                mappings[id] = new JsonObject
                {
                    ["id"] = id,
                    ["target"] = UsharpRecordJson.Str(record.Body, "target", id),
                    ["kind"] = kind,
                    ["carriers"] = Carriers(record.Body),
                };
            }
            else if (record.RecordId.StartsWith(SettingPrefix, StringComparison.Ordinal))
            {
                var name = UsharpRecordJson.Str(record.Body, "name", record.RecordId[SettingPrefix.Length..]);
                settings[name] = UsharpRecordJson.Str(record.Body, "value", string.Empty);
            }
            else if (record.RecordId.StartsWith(ContainerPrefix, StringComparison.Ordinal))
            {
                var key = UsharpRecordJson.Str(record.Body, "key", record.RecordId[ContainerPrefix.Length..]);
                var view = UsharpRecordJson.Str(record.Body, "view", string.Empty);
                var owner = UsharpRecordJson.Str(record.Body, "owner", view);
                var write = UsharpRecordJson.Str(record.Body, "write", "replace");
                var mapping = UsharpRecordJson.Str(record.Body, "mapping", string.Empty);

                if (view.Length == 0 || owner.Length == 0)
                {
                    diagnostics.Add(Error(file, $"容器 '{key}' 缺 view/owner（结构寻址不闭合）"));
                    continue;
                }

                if (!string.Equals(view, owner, StringComparison.Ordinal))
                {
                    diagnostics.Add(Error(file, $"容器 '{key}' 的 owner='{owner}' ≠ view='{view}'（所有者唯一）"));
                    continue;
                }

                if (!WriteModes.Contains(write))
                {
                    diagnostics.Add(Error(file, $"容器 '{view}:{key}' 的 write 非法：{write}（须 ∈ {{replace,append,leaf}}）"));
                    continue;
                }

                var node = new JsonObject
                {
                    ["key"] = key,
                    ["view"] = view,
                    ["owner"] = owner,
                    ["role"] = UsharpRecordJson.Str(record.Body, "role", "region"),
                    ["slot"] = UsharpRecordJson.Str(record.Body, "slot", "content"),
                    ["mapping"] = mapping,
                    ["write"] = write,
                    ["lifecycle"] = UsharpRecordJson.Str(record.Body, "lifecycle", "on-demand"),
                    ["parent"] = UsharpRecordJson.Str(record.Body, "parent", string.Empty),
                    ["region"] = UsharpRecordJson.Str(record.Body, "region", string.Empty),
                };

                if (record.Body.TryGet("appearance", out var appearance) && appearance is UsharpRecord slots)
                {
                    node["appearance"] = UsharpRecordJson.Props(slots);
                }

                var id = view + ":" + key;
                if (containers.ContainsKey(id))
                {
                    diagnostics.Add(Error(file, $"容器标识重复：{id}（view:key 必须唯一）"));
                    continue;
                }

                containers[id] = node;
            }
            else if (record.RecordId.StartsWith(ViewPrefix, StringComparison.Ordinal))
            {
                var view = UsharpRecordJson.Str(record.Body, "view", record.RecordId[ViewPrefix.Length..]);
                views[view] = new JsonObject
                {
                    ["containers"] = UsharpRecordJson.Array(UsharpRecordJson.StrList(record.Body, "containers")),
                };
            }
            else if (record.RecordId.StartsWith(PrimitivePrefix, StringComparison.Ordinal))
            {
                primitives.Add(JsonValue.Create(UsharpRecordJson.Str(record.Body, "name", record.RecordId[PrimitivePrefix.Length..])));
            }
            else if (record.RecordId.StartsWith(StatePrefix, StringComparison.Ordinal))
            {
                states.Add(JsonValue.Create(UsharpRecordJson.Str(record.Body, "name", record.RecordId[StatePrefix.Length..])));
            }
            else if (record.RecordId.StartsWith(LayerPrefix, StringComparison.Ordinal))
            {
                var name = UsharpRecordJson.Str(record.Body, "name", record.RecordId[LayerPrefix.Length..]);
                layers[name] = new JsonObject
                {
                    ["name"] = name,
                    ["primitive"] = UsharpRecordJson.Str(record.Body, "primitive", string.Empty),
                    // z 发数字：读取侧按 JSON 数字解析层序，发字符串会让所有层 z 退化为 0（描边被填充覆盖）。
                    ["z"] = LayerZ(record.Body, diagnostics, file, name),
                    ["params"] = Nested(record.Body, "params"),
                };
            }
            else if (record.RecordId.StartsWith(MaterialPrefix, StringComparison.Ordinal))
            {
                var name = UsharpRecordJson.Str(record.Body, "name", record.RecordId[MaterialPrefix.Length..]);
                materials[name] = new JsonObject
                {
                    ["name"] = name,
                    ["fallback"] = UsharpRecordJson.Str(record.Body, "fallback", string.Empty),
                    ["layers"] = Nested(record.Body, "layers"),
                };
            }
            else if (record.RecordId.StartsWith(ProfilePrefix, StringComparison.Ordinal))
            {
                var name = UsharpRecordJson.Str(record.Body, "name", record.RecordId[ProfilePrefix.Length..]);
                profiles[name] = new JsonObject
                {
                    ["name"] = name,
                    ["extends"] = UsharpRecordJson.Str(record.Body, "extends", string.Empty),
                    ["values"] = Nested(record.Body, "values"),
                    ["materials"] = Nested(record.Body, "materials"),
                };
            }
            else if (record.RecordId.StartsWith(AssetPrefix, StringComparison.Ordinal))
            {
                var id = UsharpRecordJson.Str(record.Body, "id", record.RecordId[AssetPrefix.Length..]);
                var kind = UsharpRecordJson.Str(record.Body, "kind", "texture");
                var src = UsharpRecordJson.Str(record.Body, "src", string.Empty);
                if (kind != "texture" && kind != "decal")
                {
                    diagnostics.Add(Error(file, $"资源 '{id}' 的 kind 非法：{kind}（须 ∈ {{texture,decal}}）"));
                    continue;
                }

                if (src.Length == 0 || System.IO.Path.IsPathRooted(src) || src.Contains(':'))
                {
                    diagnostics.Add(Error(file, $"资源 '{id}' 的 src 必须为载体无关相对路径（禁绝对路径）：'{src}'"));
                    continue;
                }

                assets[id] = new JsonObject
                {
                    ["id"] = id,
                    ["kind"] = kind,
                    ["src"] = src,
                    ["slice"] = UsharpRecordJson.Str(record.Body, "slice", string.Empty),
                    ["repeat"] = UsharpRecordJson.Str(record.Body, "repeat", "repeat"),
                    ["anchor"] = UsharpRecordJson.Str(record.Body, "anchor", "top-left"),
                };
            }
            else if (record.RecordId.StartsWith(CarrierPrefix, StringComparison.Ordinal))
            {
                var id = UsharpRecordJson.Str(record.Body, "id", record.RecordId[CarrierPrefix.Length..]);
                carriers[id] = new JsonObject
                {
                    ["id"] = id,
                    ["dialect"] = UsharpRecordJson.Str(record.Body, "dialect", string.Empty),
                    ["caps"] = Nested(record.Body, "caps"),
                };
            }
        }

        // token 值落地（发射期）：层参数换字面值；档位 → values.ui_skin → 皮肤覆盖。
        if (tokens is not null) ApplyTokens(layers, profiles, settings, tokens, diagnostics, file);

        if (containers.Count == 0)
        {
            diagnostics.Add(Error(file, "表面程序不可产出：缺 [Record] surface_container_*（容器是唯一结构寻址单位）"));
            return new UsharpSurfaceProgramResult(null, null, diagnostics);
        }

        if (mappings.Count == 0)
        {
            diagnostics.Add(Error(file, "表面程序不可产出：缺 [Record] surface_mapping_*（映射是容器与载体的唯一桥）"));
            return new UsharpSurfaceProgramResult(null, null, diagnostics);
        }

        // 容器 → 映射闭合；视图 → 容器闭合（结构事实，可外部复算）。
        foreach (var pair in containers)
        {
            var node = (JsonObject)pair.Value!;
            var mapping = node["mapping"]?.GetValue<string>() ?? string.Empty;
            if (mapping.Length == 0 || !mappings.ContainsKey(mapping))
            {
                diagnostics.Add(Error(file, $"容器 '{pair.Key}' 的 mapping='{mapping}' 不存在（映射闭合失败）"));
            }
        }

        foreach (var pair in views)
        {
            var view = pair.Key;
            var declared = ((JsonObject)pair.Value!)["containers"]!.AsArray()
                .Select(n => n?.GetValue<string>() ?? string.Empty)
                .Where(k => k.Length > 0)
                .ToArray();
            if (declared.Length == 0)
            {
                diagnostics.Add(Error(file, $"视图 '{view}' 未声明任何容器"));
                continue;
            }

            foreach (var key in declared)
            {
                if (!containers.ContainsKey(view + ":" + key))
                {
                    diagnostics.Add(Error(file, $"视图 '{view}' 引用了不存在的容器 '{key}'"));
                }
            }
        }

        // 外观闭合：层→图元 / 材质→层+fallback / Profile→材质 / 载体 caps 词表（结构事实）。
        var primitiveSet = new HashSet<string>(primitives.Select(p => p?.GetValue<string>() ?? string.Empty), StringComparer.Ordinal);
        foreach (var pair in layers)
        {
            var primitive = ((JsonObject)pair.Value!)["primitive"]?.GetValue<string>() ?? string.Empty;
            if (!primitiveSet.Contains(primitive))
            {
                diagnostics.Add(Error(file, $"层 '{pair.Key}' 的 primitive='{primitive}' 未在图元词表声明"));
            }
        }

        foreach (var pair in materials)
        {
            var node = (JsonObject)pair.Value!;
            foreach (var layerRef in node["layers"]!.AsObject().Select(p => p.Value?.GetValue<string>() ?? string.Empty))
            {
                if (layerRef.Length > 0 && !layers.ContainsKey(layerRef))
                {
                    diagnostics.Add(Error(file, $"材质 '{pair.Key}' 引用了未声明层 '{layerRef}'"));
                }
            }

            var fallback = node["fallback"]?.GetValue<string>() ?? string.Empty;
            if (fallback.Length > 0 && !materials.ContainsKey(fallback))
            {
                diagnostics.Add(Error(file, $"材质 '{pair.Key}' 的 fallback='{fallback}' 未声明"));
            }
        }

        foreach (var pair in profiles)
        {
            var node = (JsonObject)pair.Value!;
            var extends = node["extends"]?.GetValue<string>() ?? string.Empty;
            if (extends.Length > 0 && !profiles.ContainsKey(extends))
            {
                diagnostics.Add(Error(file, $"Profile '{pair.Key}' 的 extends='{extends}' 未声明"));
            }

            foreach (var materialRef in node["materials"]!.AsObject().Select(p => p.Value?.GetValue<string>() ?? string.Empty))
            {
                if (materialRef.Length > 0 && !materials.ContainsKey(materialRef))
                {
                    diagnostics.Add(Error(file, $"Profile '{pair.Key}' 引用了未声明材质 '{materialRef}'"));
                }
            }
        }

        if (settings["active_profile"]?.GetValue<string>() is { Length: > 0 } activeProfile
            && !profiles.ContainsKey(activeProfile))
        {
            diagnostics.Add(Error(file, $"active_profile='{activeProfile}' 未在 profiles 声明（禁悬空默认档）"));
        }

        foreach (var pair in carriers)
        {
            foreach (var cap in ((JsonObject)pair.Value!)["caps"]!.AsObject())
            {
                // 能力键文法：<primitive>（默认绘制通道）或 <channel>_<primitive>（写面细分）。
                var separator = cap.Key.IndexOf('_');
                var channel = separator <= 0 ? "draw" : cap.Key[..separator];
                var primitive = separator <= 0 ? cap.Key : cap.Key[(separator + 1)..];
                if (channel is not ("draw" or "property" or "css" or "text"))
                {
                    diagnostics.Add(Error(file, $"载体 '{pair.Key}' 的 caps 键 '{cap.Key}' 通道前缀 '{channel}' 非法（须 draw/property/css/text）"));
                }

                if (!primitiveSet.Contains(primitive))
                {
                    diagnostics.Add(Error(file, $"载体 '{pair.Key}' 的 caps 键 '{cap.Key}' 不在图元词表"));
                }

                var state = cap.Value?.GetValue<string>() ?? string.Empty;
                if (state != "supported" && state != "supported-opaque" && state != "emulated" && state != "unsupported")
                {
                    diagnostics.Add(Error(file, $"载体 '{pair.Key}' 的 caps['{cap.Key}'] 非法：{state}（须 supported/supported-opaque/emulated/unsupported）"));
                }
            }
        }

        if (diagnostics.Count > 0) return new UsharpSurfaceProgramResult(null, null, diagnostics);

        var body = new JsonObject
        {
            ["schema"] = Schema,
            ["views"] = views,
            ["containers"] = containers,
            ["mappings"] = mappings,
            ["appearance"] = new JsonObject
            {
                ["primitives"] = primitives,
                ["states"] = states,
                ["layers"] = layers,
                ["materials"] = materials,
                ["profiles"] = profiles,
                ["carriers"] = carriers,
                ["assets"] = assets,
                ["settings"] = settings,
            },
        };

        var compact = body.ToJsonString();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(compact))).ToLowerInvariant();
        var document = JsonNode.Parse(compact)!.AsObject();
        document.Insert(1, "fingerprint", hash);

        return new UsharpSurfaceProgramResult(document.ToJsonString(), hash, diagnostics);
    }

    /// <summary>一层嵌套标量记录 → JSON 对象（params/layers/values/materials/caps）。</summary>
    private static JsonObject Nested(UsharpRecord record, string name)
        => record.TryGet(name, out var value) && value is UsharpRecord nested
            ? UsharpRecordJson.Props(nested)
            : new JsonObject();

    /// <summary>mapping.carriers → { 载体 id: { 属性: 值 } }（properties 归一：element → tag）。</summary>
    private static JsonObject Carriers(UsharpRecord record)
    {
        var node = new JsonObject();
        if (!record.TryGet("carriers", out var value) || value is not UsharpRecord carriers) return node;

        foreach (var pair in carriers.Fields)
        {
            if (pair.Value is not UsharpRecord carrier) continue;
            var props = UsharpRecordJson.Props(carrier);
            if (props.TryGetPropertyValue("element", out var element))
            {
                props.Remove("element");
                props["tag"] = element;
            }

            node[pair.Key] = props;
        }

        return node;
    }

    private static UsharpDiagnostic Error(string file, string message)
        => UsharpDiagnostic.Error(UsharpCodes.UiDeclarationNotProducible, message, file, 1);

    /// <summary>参与色值落地的图元（其余图元用 asset / 纯数值）。</summary>
    private static readonly HashSet<string> ColorPrimitives =
        new(StringComparer.Ordinal) { "fill", "gradient", "stroke", "shadow", "innerShadow", "backdrop", "specular" };

    /// <summary>层参数换字面值 + 色值落地校验（未解析且非字面色值 ⇒ 诊断）。</summary>
    private static void ApplyTokens(
        JsonObject layers,
        JsonObject profiles,
        JsonObject settings,
        SurfaceTokenTable tokens,
        List<UsharpDiagnostic> diagnostics,
        string file)
    {
        var profile = settings["active_profile"]?.GetValue<string>();
        var skin = EffectiveValue(profiles, profile, "ui_skin");
        if (!string.IsNullOrWhiteSpace(skin) && !tokens.Skins.ContainsKey(skin!))
        {
            diagnostics.Add(Error(file, $"Profile '{profile}' 的 ui_skin='{skin}' 未在皮肤表声明（禁悬空皮肤）"));
            return;
        }

        var values = tokens.For(skin);
        foreach (var pair in layers)
        {
            if (pair.Value is not JsonObject layer || layer["params"] is not JsonObject parameters) continue;

            foreach (var parameter in parameters.ToList())
            {
                var raw = parameter.Value?.GetValue<string>();
                if (raw is null) continue;
                parameters[parameter.Key] = ResolveValue(raw, values);
            }

            var primitive = layer["primitive"]?.GetValue<string>() ?? string.Empty;
            if (!ColorPrimitives.Contains(primitive)) continue;

            var value = parameters["value"]?.GetValue<string>() ?? string.Empty;
            foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (IsLiteralValue(part) || values.ContainsKey(part)) continue;
                diagnostics.Add(Error(file, $"层 '{pair.Key}' 的色值无法落地：{part}（非字面色值且非已声明 token）"));
                break;
            }
        }
    }

    /// <summary>按逗号分段换 token（gradient 多色值；其余参数原样单值）。</summary>
    private static string ResolveValue(string raw, IReadOnlyDictionary<string, string> values)
    {
        if (values.TryGetValue(raw, out var direct)) return direct;

        var parts = raw.Split(',');
        if (parts.Length < 2) return raw;

        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i].Trim();
            if (values.TryGetValue(part, out var value)) parts[i] = value;
        }

        return string.Join(",", parts);
    }

    /// <summary>档位值沿 <c>extends</c> 链合并（父先子后；子覆盖父）。</summary>
    private static string? EffectiveValue(JsonObject profiles, string? profileName, string key)
    {
        if (string.IsNullOrWhiteSpace(profileName)) return null;

        var chain = new List<string>();
        string? current = profileName;
        for (var guard = 0; guard < 16 && !string.IsNullOrWhiteSpace(current); guard++)
        {
            if (profiles[current!] is not JsonObject node) break;
            chain.Add(current!);
            current = node["extends"]?.GetValue<string>();
        }

        for (var i = chain.Count - 1; i >= 0; i--)
        {
            if (profiles[chain[i]] is JsonObject node
                && node["values"] is JsonObject values
                && values[key]?.GetValue<string>() is { Length: > 0 } value)
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>字面值判定（#hex / 数值 / 非标识符形态如 rgb(...)；不承担色名解析）。</summary>
    private static bool IsLiteralValue(string text)
    {
        if (text.Length == 0) return false;
        if (text[0] == '#')
        {
            return text.Length is 4 or 7 or 9 && text.Skip(1).All(Uri.IsHexDigit);
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) return true;
        return !IsIdentifier(text);
    }

    private static bool IsIdentifier(string text)
        => (char.IsLetter(text[0]) || text[0] == '_')
           && text.All(c => char.IsLetterOrDigit(c) || c == '_');

    /// <summary>层 z（发 JSON 数字：读取侧按数字解析层序）。</summary>
    private static JsonNode LayerZ(
        UsharpRecord record, List<UsharpDiagnostic> diagnostics, string file, string name)
    {
        var raw = UsharpRecordJson.Str(record, "z", "0");
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var z))
        {
            return JsonValue.Create(z)!;
        }

        diagnostics.Add(Error(file, $"层 '{name}' 的 z 非法：{raw}（须整数）"));
        return JsonValue.Create(0)!;
    }
}
