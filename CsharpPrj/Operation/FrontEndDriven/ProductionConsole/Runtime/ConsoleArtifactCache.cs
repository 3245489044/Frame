using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using FrontEndDriven.ProductionConsole.Console;

namespace FrontEndDriven.ProductionConsole.Runtime;

/// <summary>
/// 产物物化缓存（I/O 从命令路由下沉至此）：终态任务不可变 ⇒ 自动刷新 tick 不重复读盘/base64；
/// 上限 64 后整体清空（简单有界，避免无限增长）。
/// </summary>
public sealed class ConsoleArtifactCache
{
    private readonly IArtifactPublisher _publisher;
    private readonly Dictionary<string, IReadOnlyList<ConsoleArtifactView>> _cache = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public ConsoleArtifactCache(IArtifactPublisher publisher)
        => _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));

    /// <summary>物化任务产物为可渲染视图（文本/JSON 小文件内联；终态结果缓存）。</summary>
    public async Task<IReadOnlyList<ConsoleArtifactView>> MaterializeAsync(JobSnapshot job, CancellationToken ct = default)
    {
        if (job.Artifacts.Count == 0) return Array.Empty<ConsoleArtifactView>();

        var cacheKey = $"{job.JobId}|{job.Status}|{job.Artifacts.Count}";
        if (job.IsTerminal)
        {
            lock (_gate)
                if (_cache.TryGetValue(cacheKey, out var cached)) return cached;
        }

        var views = new List<ConsoleArtifactView>(job.Artifacts.Count);
        foreach (var artifact in job.Artifacts)
        {
            var url = await _publisher.MaterializeAsync(artifact, ct).ConfigureAwait(false);
            string? text = null;
            if (artifact.Kind is ArtifactKind.Text or ArtifactKind.Json
                && artifact.Bytes <= 64 * 1024
                && File.Exists(artifact.Path))
            {
                try { text = await File.ReadAllTextAsync(artifact.Path, ct).ConfigureAwait(false); }
                catch { /* 读取失败保留路径 */ }
            }
            views.Add(new ConsoleArtifactView { Spec = artifact, Url = url, Text = text });
        }

        if (job.IsTerminal)
        {
            lock (_gate)
            {
                if (_cache.Count >= 64) _cache.Clear();
                _cache[cacheKey] = views;
            }
        }
        return views;
    }
}
