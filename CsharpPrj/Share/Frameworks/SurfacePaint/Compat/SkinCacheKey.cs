#nullable enable

using Share.SurfacePaint;

namespace Share.SurfacePaint.Compat;

/// <summary>缓存键门面（薄层）：实现单源 = <see cref="SurfaceCacheKey"/>。</summary>
public static class SkinCacheKey
{
    /// <summary>全量缓存键。</summary>
    public static string Build(
        string fingerprint, int width, int height, int dpi,
        string? lightToken, int backdropVersion, int assetGeneration)
        => SurfaceCacheKey.Build(fingerprint, width, height, dpi, lightToken, backdropVersion, assetGeneration);

    /// <summary>材质指纹。</summary>
    public static string Fingerprint(string materialId, string? fallbackUsed, string paramDigest)
        => SurfaceCacheKey.Fingerprint(materialId, fallbackUsed, paramDigest);

    /// <summary>层参数摘要。</summary>
    public static string ParamDigest(System.Collections.Generic.IReadOnlyList<Share.Conversation.Rendering.Surface.ResolvedLayer> layers)
        => SurfaceCacheKey.ParamDigest(layers);
}
