using System;
using System.Collections.Generic;

namespace Strategy.Catalog.Model;

/// <summary>
/// 分类轴定义（声明式）—— 「按什么口径把平铺条目切成目录」。
/// <para>
/// 轴是数据而非代码：同一批条目可同时挂在多个轴上（前缀轴 / 目录轴 / 模块轴 / 技能轴 / 层级轴…），
/// 框架按轴分组，杜绝各域各自硬编码"取第一个下划线前缀"这类口径分叉。
/// </para>
/// </summary>
/// <param name="Id">轴标识（source 内唯一，跨轴引用用；大小写不敏感）。</param>
/// <param name="Display">轴的显示名（如「域」「函数库」「层级」；缺省回退 <see cref="Id"/>）。</param>
/// <param name="CaseSensitive">轴取值是否大小写敏感（缺省 false：同值不同大小写合并为一组）。</param>
public sealed record CatalogAxis(string Id, string Display, bool CaseSensitive = false)
{
    /// <summary>轴显示名（空则回退 Id，统一空值兜底入口）。</summary>
    public string EffectiveDisplay => CatalogText.Or(Display, Id);

    /// <summary>按名构造（常用轴；显示名与 Id 相同）。</summary>
    public static CatalogAxis Of(string id, string? display = null, bool caseSensitive = false)
        => new(id, CatalogText.Or(display, id), caseSensitive);
}

/// <summary>
/// 条目的轴取值（条目 → 轴的挂载事实）：<see cref="Value"/> 为分组键，<see cref="Display"/> 为人类可读名。
/// </summary>
/// <param name="AxisId">轴标识（对应 <see cref="CatalogAxis.Id"/>）。</param>
/// <param name="Value">轴取值（分组键；空值条目不入该轴分组）。</param>
/// <param name="Display">取值显示名（缺省回退 <see cref="Value"/>）。</param>
public sealed record CatalogAxisValue(string AxisId, string Value, string? Display = null)
{
    /// <summary>取值显示名（空则回退 Value）。</summary>
    public string EffectiveDisplay => CatalogText.Or(Display, Value);

    /// <summary>按名构造。</summary>
    public static CatalogAxisValue Of(string axisId, string value, string? display = null) => new(axisId, value, display);
}

/// <summary>
/// 轴值派生助手（**唯一**口径实现）—— 各 source 只调用这里的静态方法，禁自行 split/截断。
/// </summary>
public static class CatalogAxes
{
    /// <summary>无轴取值条目的归集值（不静默丢弃；与空串区分）。</summary>
    public const string MiscValue = "misc";

    /// <summary>
    /// 前缀轴口径：取条目名**首个** <c>'_'</c> 之前的片段；无前缀 / 首字符即分隔符 → <see cref="MiscValue"/>。
    /// <para>对齐既有工具面 / 函数库口径（<c>Math_</c> / <c>Func_</c> / <c>Activation_</c> …）。</para>
    /// </summary>
    public static string PrefixOf(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return MiscValue;
        var n = name.Trim();
        var i = n.IndexOf('_');
        return i <= 0 ? MiscValue : n.Substring(0, i);
    }

    /// <summary>前缀轴取值（一次性构造 <see cref="CatalogAxisValue"/>）。</summary>
    public static CatalogAxisValue PrefixValue(string name, string axisId = "prefix")
        => CatalogAxisValue.Of(axisId, PrefixOf(name));

    /// <summary>
    /// 目录轴口径：取相对路径的**首段**（文件夹 = 分类；无分隔符 → <see cref="MiscValue"/>）。
    /// 支持 <c>/</c> 与 <c>\</c> 两种分隔符（对齐「模板目录树」口径）。
    /// </summary>
    public static string FolderOf(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return MiscValue;
        var p = relativePath.Trim().Replace('\\', '/');
        var i = p.IndexOf('/');
        var head = i < 0 ? p : p.Substring(0, i);
        return string.IsNullOrWhiteSpace(head) ? MiscValue : head;
    }

    /// <summary>目录轴取值（一次性构造）。</summary>
    public static CatalogAxisValue FolderValue(string relativePath, string axisId = "folder")
        => CatalogAxisValue.Of(axisId, FolderOf(relativePath));

    /// <summary>按名构造一组轴取值（便捷；跳过空轴 Id / 空取值的项）。</summary>
    public static IReadOnlyList<CatalogAxisValue> Values(params (string AxisId, string? Value)[] pairs)
    {
        var list = new List<CatalogAxisValue>();
        foreach (var (axisId, value) in pairs)
        {
            if (string.IsNullOrWhiteSpace(axisId) || string.IsNullOrWhiteSpace(value)) continue;
            list.Add(CatalogAxisValue.Of(axisId, value!));
        }
        return list;
    }
}

/// <summary>
/// 显示名兜底（**单一实现**）—— 「空 / 全空白则回退」全工程只此一处，各模型 / 源不得再抄变体。
/// </summary>
internal static class CatalogText
{
    /// <summary>取 <paramref name="value"/>；为空或全空白时回退 <paramref name="fallback"/>。</summary>
    public static string Or(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value!;
}
