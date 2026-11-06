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

    /// <summary>容器写模式受控词。</summary>
    public static readonly IReadOnlySet<string> WriteModes =
        new HashSet<string>(new[] { "replace", "append", "leaf" }, StringComparer.Ordinal);

    /// <summary>映射目标种类受控词。</summary>
    public static readonly IReadOnlySet<string> MappingKinds =
        new HashSet<string>(new[] { "container", "item", "cell" }, StringComparer.Ordinal);

    /// <summary>发射（结构不闭合 ⇒ 不产出 + 诊断）。</summary>
    /// <param name="compilation">声明编译结果。</param>
    public static UsharpSurfaceProgramResult Emit(UsharpCompilation compilation)
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
                    ["z"] = UsharpRecordJson.Str(record.Body, "z", "0"),
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

        foreach (var pair in carriers)
        {
            foreach (var cap in ((JsonObject)pair.Value!)["caps"]!.AsObject())
            {
                if (!primitiveSet.Contains(cap.Key))
                {
                    diagnostics.Add(Error(file, $"载体 '{pair.Key}' 的 caps 键 '{cap.Key}' 不在图元词表"));
                }

                var state = cap.Value?.GetValue<string>() ?? string.Empty;
                if (state != "supported" && state != "emulated" && state != "unsupported")
                {
                    diagnostics.Add(Error(file, $"载体 '{pair.Key}' 的 caps['{cap.Key}'] 非法：{state}（须 supported/emulated/unsupported）"));
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
}
