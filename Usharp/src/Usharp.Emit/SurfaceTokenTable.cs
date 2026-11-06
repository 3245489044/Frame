#nullable enable

using System;
using System.Collections.Generic;

namespace Usharp.Emit;

/// <summary>
/// 发射期 token 表（来源 <c>ui.generated.json</c> 的 <c>tokens</c> + <c>skins</c>）。
/// <para>用途：把声明里的 token 名（<c>surface_plain</c>）在**发射期**换成字面值，产物自包含；
/// 运行期不再依赖 token 表（三处 <c>SurfaceResolver.Resolve(tokens:null)</c> 无需改动）。</para>
/// <para>纯数据、零 IO；文件装载在 Adapters 边界完成。</para>
/// </summary>
/// <param name="Base">基础 token 表（token 名 → 值）。</param>
/// <param name="Skins">皮肤覆盖表（皮肤名 → 覆盖 token）；<c>extends</c> 已在装载期合并。</param>
public sealed record SurfaceTokenTable(
    IReadOnlyDictionary<string, string> Base,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Skins)
{
    /// <summary>空表（无 token 来源）。</summary>
    public static readonly SurfaceTokenTable Empty = new(
        new Dictionary<string, string>(StringComparer.Ordinal),
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal));

    /// <summary>按皮肤取生效表（空/未声明 ⇒ 基础表）。</summary>
    /// <param name="skin">皮肤 id（来自档位 <c>values.ui_skin</c>）。</param>
    public IReadOnlyDictionary<string, string> For(string? skin)
    {
        if (string.IsNullOrWhiteSpace(skin) || !Skins.TryGetValue(skin!, out var overlay)) return Base;

        var merged = new Dictionary<string, string>(Base, StringComparer.Ordinal);
        foreach (var pair in overlay) merged[pair.Key] = pair.Value;
        return merged;
    }
}
