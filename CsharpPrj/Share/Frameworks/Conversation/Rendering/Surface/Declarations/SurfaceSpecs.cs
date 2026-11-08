#nullable enable

using System.Collections.Generic;

namespace Share.Conversation.Rendering.Surface;

/// <summary>容器规格（结构寻址单位）：<c>view:key</c> 为全局主键（DOM 名只在所属视图内唯一）。</summary>
/// <param name="Key">DOM 名（<c>data-name</c>）。</param>
/// <param name="View">所属视图键。</param>
/// <param name="Owner">所有者视图（必须等于 <paramref name="View"/>）。</param>
/// <param name="Role">角色（region/stream/shell/item/overlay/composer）。</param>
/// <param name="Slot">结构槽（main/seg/head/subhead/aside/overlay/composer/content/stream）。</param>
/// <param name="Mapping">结构映射 id。</param>
/// <param name="Write">写模式（replace/append/leaf）。</param>
/// <param name="Lifecycle">生命周期（on-demand/persistent）。</param>
/// <param name="Parent">父容器键（空 = 视图根直属）。</param>
/// <param name="Region">关联 region kind（可空）。</param>
/// <param name="Appearance">容器级外观槽 → 材质 id。</param>
public sealed record SurfaceContainerSpec(
    string Key,
    string View,
    string Owner,
    string Role,
    string Slot,
    string Mapping,
    string Write,
    string Lifecycle,
    string Parent,
    string Region,
    IReadOnlyDictionary<string, string> Appearance)
{
    /// <summary>全局主键（view:key）。</summary>
    public string Id => View + ":" + Key;

    /// <summary>是否流容器（append-only）。</summary>
    public bool IsStream => string.Equals(Role, "stream", System.StringComparison.Ordinal);
}

/// <summary>容器 → 载体结构映射（唯一桥）。</summary>
/// <param name="Id">映射 id。</param>
/// <param name="Target">目标语义（container/item/cell/槽名）。</param>
/// <param name="Kind">种类（container/item/cell）。</param>
/// <param name="Carriers">载体 id → 结构属性（tag/cls/control/prefix…）。</param>
public sealed record SurfaceMappingSpec(
    string Id,
    string Target,
    string Kind,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Carriers)
{
    /// <summary>该载体的结构属性（未声明 ⇒ null：调用方可见降级）。</summary>
    /// <param name="carrierId">载体 id。</param>
    public IReadOnlyDictionary<string, string>? Carrier(string carrierId)
        => carrierId is not null && Carriers.TryGetValue(carrierId, out var props) ? props : null;
}

/// <summary>一层图元（z 序由解析期排序）。</summary>
/// <param name="Name">层名。</param>
/// <param name="Primitive">图元（fill/gradient/stroke/shadow/backdrop/specular…）。</param>
/// <param name="Z">叠放序。</param>
/// <param name="Params">参数（value 可为 token 名，解析期换值）。</param>
public sealed record SurfaceLayerSpec(
    string Name, string Primitive, int Z, IReadOnlyDictionary<string, string> Params);

/// <summary>材质 = 层引用（角色 → 层名）+ fallback（载体做不到时的回退材质）。</summary>
/// <param name="Name">材质名。</param>
/// <param name="Fallback">回退材质（空 = 无）。</param>
/// <param name="Layers">层角色 → 层名。</param>
public sealed record SurfaceMaterialSpec(
    string Name, string Fallback, IReadOnlyDictionary<string, string> Layers)
{
    /// <summary>装饰能力声明原文（<c>decor</c> 列；空 = 未声明）。解析成能力位在 <see cref="SurfaceResolver"/>。</summary>
    public string Decor { get; init; } = string.Empty;
}

/// <summary>外观档（Profile）：继承链 + 值覆盖 + 语义槽 → 材质映射。</summary>
/// <param name="Name">档名。</param>
/// <param name="Extends">父档（空 = 无）。</param>
/// <param name="Values">token 覆盖。</param>
/// <param name="Materials">语义槽 → 材质 id。</param>
public sealed record SurfaceProfileSpec(
    string Name,
    string Extends,
    IReadOnlyDictionary<string, string> Values,
    IReadOnlyDictionary<string, string> Materials);

/// <summary>载体能力：图元三态（supported/emulated/unsupported）。</summary>
/// <param name="Id">载体 id。</param>
/// <param name="Dialect">方言（css/gdi/plain…）。</param>
/// <param name="Caps">图元 → 三态。</param>
public sealed record SurfaceCarrierSpec(
    string Id, string Dialect, IReadOnlyDictionary<string, string> Caps)
{
    /// <summary>该图元在本载体的三态（未声明 ⇒ unsupported，可见降级）。</summary>
    /// <param name="primitive">图元名。</param>
    [System.Obsolete("改用 CapabilityMatrix.SpecOf(carrier, channel, primitive)（通道/值域感知）；本方法下一版本移除。")]
    public string Capability(string primitive)
        => primitive is not null && Caps.TryGetValue(primitive, out var state) ? state : "unsupported";
}

/// <summary>资源声明（载体无关相对路径；九宫格/填充语义由载体投影器解释）。</summary>
/// <param name="Id">资源 id（层 <c>params.asset</c> 引用它）。</param>
/// <param name="Kind">资源种类（texture/decal）。</param>
/// <param name="Src">载体无关相对路径（禁绝对路径）。</param>
/// <param name="Slice">九宫格切片宽度（空/0 = 不平铺九宫格）。</param>
/// <param name="Repeat">平铺方式（repeat/no-repeat）。</param>
/// <param name="Anchor">锚点（background-position 语义）。</param>
public sealed record SurfaceAssetSpec(
    string Id, string Kind, string Src, string Slice, string Repeat, string Anchor);

