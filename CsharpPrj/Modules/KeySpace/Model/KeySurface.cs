using System;
using System.Collections.Generic;
using System.Linq;

namespace Modules.KeySpace.Model;

/// <summary>
/// 键的表示面（同一实体在同一族内的不同字符串形态）。
/// <para>面之间只能经坐标转换，禁止跨面直接拼串；引用面与层级面是全局组合规则，逐族不重复声明。</para>
/// </summary>
public enum KeySurface
{
    /// <summary>族内身份（裸坐标渲染）。</summary>
    Identity,

    /// <summary>持久化行键（存储适配层使用）。</summary>
    Storage,

    /// <summary>图端点 Id（引用图适配层使用）。</summary>
    Endpoint,

    /// <summary>层级定位键（层次引擎使用）。</summary>
    Level,

    /// <summary>展示键（渲染层使用，不参与寻址）。</summary>
    Display,

    /// <summary>定位键（图端点/边的 <c>@{location}</c> 变体；持久化定位面，不参与身份寻址）。</summary>
    Location
}

/// <summary>
/// 单面声明：模板列表 [0] = 规范构造形态，其余 = 只读兼容形态（legacy/variant）。
/// </summary>
public sealed record KeySurfaceSpec(
    KeySurface Surface,
    IReadOnlyList<string> Templates,
    string? FamilyPrefix = null,
    IReadOnlyList<string>? ReadOnlyTemplates = null)
{
    /// <summary>规范模板（缺声明即定义错误，不静默降级）。</summary>
    public string CanonicalTemplate
        => Templates.Count > 0
            ? Templates[0]
            : throw new InvalidOperationException($"键面 {Surface} 未声明模板");

    /// <summary>是否可写（无规范模板 ⇒ 只读面）。</summary>
    public bool Writeable => Templates.Count > 0;

    /// <summary>只读兼容模板（存量形态；只反解、不构造）。</summary>
    public IReadOnlyList<string> CompatibleTemplates => ReadOnlyTemplates ?? Array.Empty<string>();
}

/// <summary>
/// 实体坐标：族 + 占位值袋（身份值与作用域值同袋，身份子集由族声明给出）。
/// </summary>
public sealed record KeyPoint(string Family, IReadOnlyDictionary<string, string> Values)
{
    /// <summary>空坐标（族与值袋均为空；不是任何真实实体）。</summary>
    public static KeyPoint Empty { get; } =
        new(string.Empty, new Dictionary<string, string>(StringComparer.Ordinal));

    /// <summary>按名构造坐标（空值不入袋，保持「缺席 ≠ 空串」）。</summary>
    public static KeyPoint Of(string family, params (string Name, string Value)[] values)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in values)
        {
            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(value))
            {
                map[name] = value;
            }
        }

        return new KeyPoint(family, map);
    }

    /// <summary>取值；缺席 ⇒ 空串（调用方须先判身份完整性）。</summary>
    public string Text(string name)
        => Values.TryGetValue(name, out var value) ? value : string.Empty;
}

/// <summary>
/// 带面标签的键（跨层唯一载体）：族 + 面 + 坐标 + 渲染值。
/// <para>相等性按渲染值与面标签；坐标用于面间转换（同一实体不同面值不同）。</para>
/// </summary>
public readonly record struct SurfaceKey(string Family, KeySurface Surface, KeyPoint Point, string Value)
{
    /// <summary>空键（未解析/未构造）。</summary>
    public static SurfaceKey Empty { get; } =
        new(string.Empty, KeySurface.Identity, KeyPoint.Empty, string.Empty);

    /// <summary>是否为空键。</summary>
    public bool IsEmpty => string.IsNullOrEmpty(Value);

    /// <summary>是否为指定面（跨面误用可见化）。</summary>
    public bool Is(KeySurface surface) => Surface == surface;
}
