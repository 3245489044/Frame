#nullable enable

using Share.SurfacePaint;

namespace FrontEndDriven.WinForms.Paint;

/// <summary>缓存键门面（薄层）：实现单源 = <see cref="SurfaceCacheKey"/>（Share.SurfacePaint）。</summary>
internal static class SkinCacheKey
{
    internal static string Build(
        string fingerprint, int width, int height, int dpi,
        string? lightToken, int backdropVersion, int assetGeneration)
        => SurfaceCacheKey.Build(fingerprint, width, height, dpi, lightToken, backdropVersion, assetGeneration);

    internal static string Fingerprint(string materialId, string? fallbackUsed, string paramDigest)
        => SurfaceCacheKey.Fingerprint(materialId, fallbackUsed, paramDigest);

    internal static string ParamDigest(System.Collections.Generic.IReadOnlyList<Share.Conversation.Rendering.Surface.ResolvedLayer> layers)
        => SurfaceCacheKey.ParamDigest(layers);
}