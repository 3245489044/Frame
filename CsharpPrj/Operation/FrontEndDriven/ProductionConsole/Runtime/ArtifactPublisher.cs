using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FrontEndDriven.ProductionConsole.Runtime;

/// <summary>
/// 产物物化器：小文件内联 data URI，大文件走宿主 URL（避免音频 base64 膨胀）。
/// <list type="bullet">
/// <item>≤ InlineThreshold → data URI（图片/文本默认路径，零宿主依赖）；</item>
/// <item>&gt; 阈值且宿主可用 → host URL；</item>
/// <item>宿主缺失且超过 MaxInlineWithoutHost → 失败（明确原因，不静默）。</item>
/// </list>
/// </summary>
public sealed class ArtifactPublisher : IArtifactPublisher
{
    private readonly IProductionArtifactHost? _host;
    private readonly long _inlineThresholdBytes;
    private readonly long _maxInlineWithoutHost;

    public ArtifactPublisher(
        IProductionArtifactHost? host = null,
        long inlineThresholdBytes = 256 * 1024,
        long maxInlineWithoutHost = 2 * 1024 * 1024)
    {
        _host = host;
        _inlineThresholdBytes = Math.Max(0, inlineThresholdBytes);
        _maxInlineWithoutHost = Math.Max(_inlineThresholdBytes, maxInlineWithoutHost);
    }

    public async Task<ArtifactUrl> MaterializeAsync(ArtifactSpec artifact, CancellationToken ct = default)
    {
        if (artifact is null) return ArtifactUrl.Failed("产物为空");
        if (string.IsNullOrWhiteSpace(artifact.Path) || !File.Exists(artifact.Path))
            return ArtifactUrl.Failed($"产物不存在: {artifact.Path}");

        long bytes;
        try { bytes = new FileInfo(artifact.Path).Length; }
        catch (Exception ex) { return ArtifactUrl.Failed($"产物不可读: {ex.Message}"); }

        var mime = string.IsNullOrWhiteSpace(artifact.Mime) ? ArtifactMime.For(artifact.Path) : artifact.Mime!;

        if (bytes <= _inlineThresholdBytes || _host is null)
        {
            if (bytes > _maxInlineWithoutHost)
                return ArtifactUrl.Failed($"产物 {bytes} 字节超过内联上限 {_maxInlineWithoutHost}（未启用产物宿主）", bytes);
            return await InlineAsync(artifact.Path, mime, bytes, ct).ConfigureAwait(false);
        }

        var url = _host.ResolveUrl(artifact.Path);
        if (!string.IsNullOrWhiteSpace(url)) return ArtifactUrl.Host(url!, bytes);

        // 宿主声明可用但解析失败：小文件仍可内联；大文件明确失败
        if (bytes <= _maxInlineWithoutHost)
            return await InlineAsync(artifact.Path, mime, bytes, ct).ConfigureAwait(false);

        return ArtifactUrl.Failed($"产物宿主未能解析路径: {artifact.Path}", bytes);
    }

    private static async Task<ArtifactUrl> InlineAsync(string path, string mime, long bytes, CancellationToken ct)
    {
        try
        {
            var data = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
            return ArtifactUrl.Data($"data:{mime};base64,{System.Convert.ToBase64String(data)}", bytes);
        }
        catch (Exception ex)
        {
            return ArtifactUrl.Failed($"产物读取失败: {ex.Message}", bytes);
        }
    }
}
