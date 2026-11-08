#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Share.Conversation.Rendering.Surface;

/// <summary>
/// 表面程序（生成物 <c>surface.generated.json</c>，哨兵 <c>surface-v1</c>）：容器 / 映射 / 外观三段的**唯一读取模型**。
/// <para>
/// 真源 = <c>Config\ui\surface.ushar + appearance.ushar</c>；本类零载体字面量、零 I/O（文本由装配方读入）。
/// 不可解析 / 未装配 ⇒ <see cref="Empty"/>（可见降级，禁静默伪造）。
/// </para>
/// </summary>
public sealed class SurfaceProgramDeclaration
{
    /// <summary>产物哨兵。</summary>
    public const string SchemaName = "surface-v1";

    /// <summary>空程序（未装配）：容器/材质一律缺失，解析方走可见降级。</summary>
    public static readonly SurfaceProgramDeclaration Empty = new(
        string.Empty,
        string.Empty,
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal),
        new Dictionary<string, SurfaceContainerSpec>(StringComparer.Ordinal),
        new Dictionary<string, SurfaceMappingSpec>(StringComparer.Ordinal),
        Array.Empty<string>(),
        Array.Empty<string>(),
        new Dictionary<string, SurfaceLayerSpec>(StringComparer.Ordinal),
        new Dictionary<string, SurfaceMaterialSpec>(StringComparer.Ordinal),
        new Dictionary<string, SurfaceProfileSpec>(StringComparer.Ordinal),
        new Dictionary<string, SurfaceCarrierSpec>(StringComparer.Ordinal),
        new Dictionary<string, SurfaceAssetSpec>(StringComparer.Ordinal),
        new Dictionary<string, string>(StringComparer.Ordinal),
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal),
        declared: false);

    private SurfaceProgramDeclaration(
        string schema,
        string fingerprint,
        IReadOnlyDictionary<string, IReadOnlyList<string>> views,
        IReadOnlyDictionary<string, SurfaceContainerSpec> containers,
        IReadOnlyDictionary<string, SurfaceMappingSpec> mappings,
        IReadOnlyList<string> primitives,
        IReadOnlyList<string> states,
        IReadOnlyDictionary<string, SurfaceLayerSpec> layers,
        IReadOnlyDictionary<string, SurfaceMaterialSpec> materials,
        IReadOnlyDictionary<string, SurfaceProfileSpec> profiles,
        IReadOnlyDictionary<string, SurfaceCarrierSpec> carriers,
        IReadOnlyDictionary<string, SurfaceAssetSpec> assets,
        IReadOnlyDictionary<string, string> settings,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> tokens,
        bool declared)
    {
        Schema = schema;
        Fingerprint = fingerprint;
        Views = views;
        Containers = containers;
        Mappings = mappings;
        Primitives = primitives;
        States = states;
        Layers = layers;
        Materials = materials;
        Profiles = profiles;
        Carriers = carriers;
        Assets = assets;
        Settings = settings;
        Tokens = tokens;
        IsDeclared = declared;
    }

    /// <summary>产物哨兵值。</summary>
    public string Schema { get; }

    /// <summary>声明指纹（外观缓存与写槽键的失效依据）。</summary>
    public string Fingerprint { get; }

    /// <summary>是否来自可用声明。</summary>
    public bool IsDeclared { get; }

    /// <summary>视图 → 容器键列表（顺序即结构序）。</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Views { get; }

    /// <summary>容器（view:key → 规格）。</summary>
    public IReadOnlyDictionary<string, SurfaceContainerSpec> Containers { get; }

    /// <summary>结构映射（id → 规格）。</summary>
    public IReadOnlyDictionary<string, SurfaceMappingSpec> Mappings { get; }

    /// <summary>图元词表。</summary>
    public IReadOnlyList<string> Primitives { get; }

    /// <summary>状态词表（单源）。</summary>
    public IReadOnlyList<string> States { get; }

    /// <summary>层（name → 规格）。</summary>
    public IReadOnlyDictionary<string, SurfaceLayerSpec> Layers { get; }

    /// <summary>材质（name → 规格）。</summary>
    public IReadOnlyDictionary<string, SurfaceMaterialSpec> Materials { get; }

    /// <summary>外观档（name → 规格；extends 未合并）。</summary>
    public IReadOnlyDictionary<string, SurfaceProfileSpec> Profiles { get; }

    /// <summary>载体能力（id → 规格）。</summary>
    public IReadOnlyDictionary<string, SurfaceCarrierSpec> Carriers { get; }

    /// <summary>资源（id → 规格；层 <c>params.asset</c> 引用 id，解析期换成 <c>src</c>）。</summary>
    public IReadOnlyDictionary<string, SurfaceAssetSpec> Assets { get; }

    /// <summary>表面设置（name → value；<c>active_profile</c> 为默认外观档，声明式、可运行期覆盖）。</summary>
    public IReadOnlyDictionary<string, string> Settings { get; }

    /// <summary>
    /// 表面令牌（token → 档位 → 值）。档位键 = Profile 的 <c>values.ui_skin</c>；<c>default</c> 缺省值兜底。
    /// 层参数在发射期已落地为 active_profile 的字面值；切档时按本表重新取值（两档同键两值）。
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Tokens { get; }

    /// <summary>默认外观档（未声明 ⇒ 空字符串；解析方不得硬编码替代）。</summary>
    public string ActiveProfile
        => Settings.TryGetValue("active_profile", out var value) ? value : string.Empty;

    /// <summary>取容器（未声明 ⇒ null）。</summary>
    /// <param name="view">视图键。</param>
    /// <param name="key">DOM 名。</param>
    public SurfaceContainerSpec? Container(string view, string key)
        => view is not null && key is not null && Containers.TryGetValue(view + ":" + key, out var spec) ? spec : null;

    /// <summary>
    /// 展开外观档（extends 链合并：子覆盖父）。未声明 ⇒ null；环 ⇒ 截断并保留已合并部分。
    /// </summary>
    /// <param name="profileId">档名。</param>
    public SurfaceProfileSpec? EffectiveProfile(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId) || !Profiles.TryGetValue(profileId, out var leaf)) return null;

        var materials = new Dictionary<string, string>(StringComparer.Ordinal);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var chain = new List<SurfaceProfileSpec>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var current = leaf;
        while (current is not null && visited.Add(current.Name))
        {
            chain.Add(current);
            current = current.Extends.Length > 0 && Profiles.TryGetValue(current.Extends, out var parent) ? parent : null;
        }

        for (var i = chain.Count - 1; i >= 0; i--)
        {
            foreach (var pair in chain[i].Materials) materials[pair.Key] = pair.Value;
            foreach (var pair in chain[i].Values) values[pair.Key] = pair.Value;
        }

        return new SurfaceProfileSpec(leaf.Name, leaf.Extends, values, materials);
    }

    /// <summary>解析生成物（不可解析/哨兵不符 ⇒ <see cref="Empty"/>；禁抛给渲染路径）。</summary>
    /// <param name="json">生成物文本。</param>
    public static SurfaceProgramDeclaration Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!string.Equals(Str(root, "schema"), SchemaName, StringComparison.Ordinal)) return Empty;

        var views = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        if (root.TryGetProperty("views", out var viewsElement) && viewsElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var view in viewsElement.EnumerateObject())
            {
                if (view.Value.ValueKind != JsonValueKind.Object) continue;
                views[view.Name] = StrList(view.Value, "containers");
            }
        }

        var containers = new Dictionary<string, SurfaceContainerSpec>(StringComparer.Ordinal);
        if (root.TryGetProperty("containers", out var containersElement) && containersElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var entry in containersElement.EnumerateObject())
            {
                if (entry.Value.ValueKind != JsonValueKind.Object) continue;
                var container = new SurfaceContainerSpec(
                    Str(entry.Value, "key"),
                    Str(entry.Value, "view"),
                    Str(entry.Value, "owner"),
                    Str(entry.Value, "role"),
                    Str(entry.Value, "slot"),
                    Str(entry.Value, "mapping"),
                    Str(entry.Value, "write"),
                    Str(entry.Value, "lifecycle"),
                    Str(entry.Value, "parent"),
                    Str(entry.Value, "region"),
                    StrMap(entry.Value, "appearance"));
                containers[container.Id] = container;
            }
        }

        var mappings = new Dictionary<string, SurfaceMappingSpec>(StringComparer.Ordinal);
        if (root.TryGetProperty("mappings", out var mappingsElement) && mappingsElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var entry in mappingsElement.EnumerateObject())
            {
                if (entry.Value.ValueKind != JsonValueKind.Object) continue;
                mappings[entry.Name] = new SurfaceMappingSpec(
                    entry.Name,
                    Str(entry.Value, "target"),
                    Str(entry.Value, "kind"),
                    NestedMap(entry.Value, "carriers"));
            }
        }

        var primitives = Array.Empty<string>();
        var states = Array.Empty<string>();
        var layers = new Dictionary<string, SurfaceLayerSpec>(StringComparer.Ordinal);
        var materials = new Dictionary<string, SurfaceMaterialSpec>(StringComparer.Ordinal);
        var profiles = new Dictionary<string, SurfaceProfileSpec>(StringComparer.Ordinal);
        var carriers = new Dictionary<string, SurfaceCarrierSpec>(StringComparer.Ordinal);
        var assets = new Dictionary<string, SurfaceAssetSpec>(StringComparer.Ordinal);
        IReadOnlyDictionary<string, string> settings = new Dictionary<string, string>(StringComparer.Ordinal);
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> tokens =
            new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);

        if (root.TryGetProperty("appearance", out var appearance) && appearance.ValueKind == JsonValueKind.Object)
        {
            primitives = StrArray(appearance, "primitives");
            states = StrArray(appearance, "states");

            if (appearance.TryGetProperty("layers", out var layersElement) && layersElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var entry in layersElement.EnumerateObject())
                {
                    if (entry.Value.ValueKind != JsonValueKind.Object) continue;
                    layers[entry.Name] = new SurfaceLayerSpec(
                        entry.Name,
                        Str(entry.Value, "primitive"),
                        Int(entry.Value, "z"),
                        StrMap(entry.Value, "params"));
                }
            }

            if (appearance.TryGetProperty("materials", out var materialsElement) && materialsElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var entry in materialsElement.EnumerateObject())
                {
                    if (entry.Value.ValueKind != JsonValueKind.Object) continue;
                    materials[entry.Name] = new SurfaceMaterialSpec(
                        entry.Name, Str(entry.Value, "fallback"), StrMap(entry.Value, "layers"))
                    {
                        Decor = Str(entry.Value, "decor"),
                    };
                }
            }

            if (appearance.TryGetProperty("profiles", out var profilesElement) && profilesElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var entry in profilesElement.EnumerateObject())
                {
                    if (entry.Value.ValueKind != JsonValueKind.Object) continue;
                    profiles[entry.Name] = new SurfaceProfileSpec(
                        entry.Name, Str(entry.Value, "extends"), StrMap(entry.Value, "values"), StrMap(entry.Value, "materials"));
                }
            }

            if (appearance.TryGetProperty("carriers", out var carriersElement) && carriersElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var entry in carriersElement.EnumerateObject())
                {
                    if (entry.Value.ValueKind != JsonValueKind.Object) continue;
                    carriers[entry.Name] = new SurfaceCarrierSpec(
                        entry.Name, Str(entry.Value, "dialect"), StrMap(entry.Value, "caps"));
                }
            }

            if (appearance.TryGetProperty("assets", out var assetsElement) && assetsElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var entry in assetsElement.EnumerateObject())
                {
                    if (entry.Value.ValueKind != JsonValueKind.Object) continue;
                    assets[entry.Name] = new SurfaceAssetSpec(
                        entry.Name,
                        Str(entry.Value, "kind"),
                        Str(entry.Value, "src"),
                        Str(entry.Value, "slice"),
                        Str(entry.Value, "repeat"),
                        Str(entry.Value, "anchor"));
                }
            }

            settings = StrMap(appearance, "settings");
            tokens = NestedMap(appearance, "tokens");
        }

        return new SurfaceProgramDeclaration(
            SchemaName, Str(root, "fingerprint"), views, containers, mappings,
            primitives, states, layers, materials, profiles, carriers, assets, settings, tokens,
            declared: containers.Count > 0);
    }

    private static string Str(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static int Int(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
           && value.TryGetInt32(out var number)
            ? number
            : 0;

    private static IReadOnlyList<string> StrList(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString() ?? string.Empty)
                .Where(item => item.Length > 0)
                .ToArray()
            : Array.Empty<string>();

    private static string[] StrArray(JsonElement element, string name) => StrList(element, name).ToArray();

    private static IReadOnlyDictionary<string, string> StrMap(JsonElement element, string name)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object) return map;

        foreach (var pair in value.EnumerateObject())
        {
            map[pair.Name] = pair.Value.ValueKind switch
            {
                JsonValueKind.String => pair.Value.GetString() ?? string.Empty,
                JsonValueKind.Number => pair.Value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => string.Empty,
            };
        }

        return map;
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> NestedMap(JsonElement element, string name)
    {
        var map = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object) return map;

        foreach (var pair in value.EnumerateObject())
        {
            if (pair.Value.ValueKind != JsonValueKind.Object) continue;
            map[pair.Name] = pair.Value.EnumerateObject().ToDictionary(
                prop => prop.Name,
                prop => prop.Value.ValueKind == JsonValueKind.String
                    ? prop.Value.GetString() ?? string.Empty
                    : prop.Value.ValueKind == JsonValueKind.Number
                        ? prop.Value.GetRawText()
                        : string.Empty,
                StringComparer.Ordinal);
        }

        return map;
    }
}
