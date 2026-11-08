#nullable enable

using System;

namespace Share.SurfacePaint;

/// <summary>
/// 缓存键内核（K4）：静态底图失效的唯一判据，纯函数、无宿主状态。
/// <para>键 = 材质指纹 + 尺寸档 + DPI 档 + 静态光 token + 背后快照版本 + 资产代。
/// 资产代由 <see cref="SkinPaintEngine.AssetGeneration"/> 提供（换根/换装载器/清缓存即递增），
/// 防止「换根后旧底图继续贴旧贴图」的假命中。</para>
/// </summary>
public static class SurfaceCacheKey
{
    /// <summary>构建缓存键（null/空项不进键）。</summary>
    /// <param name="fingerprint">材质指纹（含层参数摘要）。</param>
    /// <param name="width">位图宽。</param>
    /// <param name="height">位图高。</param>
    /// <param name="dpi">设备 DPI。</param>
    /// <param name="lightToken">静态光 token（无光 ⇒ null）。</param>
    /// <param name="backdropVersion">背后快照版本（无快照 ⇒ 0）。</param>
    /// <param name="assetGeneration">资产代（<see cref="SkinPaintEngine.AssetGeneration"/>）。</param>
    public static string Build(
        string fingerprint, int width, int height, int dpi,
        string? lightToken, int backdropVersion, int assetGeneration)
    {
        var key = fingerprint + ":" + width + "x" + height + "@dpi" + dpi + "|assets:" + assetGeneration;
        if (!string.IsNullOrEmpty(lightToken)) key += "|light:" + lightToken;
        if (backdropVersion > 0) key += "|bg:" + backdropVersion;
        return key;
    }

    /// <summary>材质指纹：材质 id + 回退材质 + 层参数摘要（同材质换色必须失效）。</summary>
    /// <param name="materialId">材质 id。</param>
    /// <param name="fallbackUsed">实际回退材质。</param>
    /// <param name="paramDigest">层参数摘要。</param>
    public static string Fingerprint(string materialId, string? fallbackUsed, string paramDigest)
        => materialId + "|" + (fallbackUsed ?? string.Empty) + "|" + paramDigest;

    /// <summary>层参数摘要（角色/图元/z/全部参数；按键序，禁枚举顺序当语义）。</summary>
    /// <param name="layers">层栈。</param>
    public static string ParamDigest(System.Collections.Generic.IReadOnlyList<Share.Conversation.Rendering.Surface.ResolvedLayer> layers)
        => Share.Conversation.Rendering.Surface.SurfaceKeyCanonical.ParamDigest(layers);
}
