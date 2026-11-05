using System.Globalization;
using System.Text.Json;

namespace Modules.Observe.Metrics;

/// <summary>
/// 两个输出端：Console 片段（并入既有帧统计行，不新增第二行）与 JSON 落盘。
/// 路径优先级：<c>ENGINE_METRICS_DIR</c> → 仓库根 <c>Runtime\Data\Metrics</c> → 进程目录 <c>Metrics</c>。
/// </summary>
public static class MetricSinks
{
    public const string Schema = "engine-metrics-v1";

    public static string FormatConsole(MetricSnapshot snapshot)
    {
        long solid = 0;
        double tickP95 = 0, heightP95 = 0;
        long visible = 0, resident = 0, entities = 0;

        foreach (var counter in snapshot.Counters)
            if (counter.Name == MetricCatalog.NameOf(MetricId.CollideCellReads))
                solid = counter.Value;

        foreach (var histogram in snapshot.Histograms)
        {
            if (histogram.Name == MetricCatalog.NameOf(MetricId.SimTick)) tickP95 = histogram.P95Ms;
            else if (histogram.Name == MetricCatalog.NameOf(MetricId.HeightQuery)) heightP95 = histogram.P95Ms;
        }

        foreach (var gauge in snapshot.Gauges)
        {
            if (gauge.Name == MetricCatalog.NameOf(MetricId.ChunkVisible)) visible = gauge.Last;
            else if (gauge.Name == MetricCatalog.NameOf(MetricId.ChunkResident)) resident = gauge.Last;
            else if (gauge.Name == MetricCatalog.NameOf(MetricId.ChunkEntities)) entities = gauge.Last;
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            "metrics: solid={0} tick.p95={1:F2}ms vis={2} resident={3} entities={4} height.p95={5:F2}ms",
            solid, tickP95, visible, resident, entities, heightP95);
    }

    public static string WriteJson(MetricSnapshot snapshot, string? directory = null)
    {
        string targetDir = directory ?? ResolveDirectory();
        Directory.CreateDirectory(targetDir);
        // 运行隔离：秒级时间戳 + 进程号，避免并发/同秒 flush 覆盖同一份快照。
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string fileName = $"engine-{stamp}-{Environment.ProcessId}.json";
        string path = Path.Combine(targetDir, fileName);
        int suffix = 1;
        while (File.Exists(path))
            path = Path.Combine(targetDir, $"engine-{stamp}-{Environment.ProcessId}-{suffix++}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(snapshot, JsonOptions));
        return path;
    }

    public static string ResolveDirectory()
    {
        var explicitDir = Environment.GetEnvironmentVariable("ENGINE_METRICS_DIR");
        if (!string.IsNullOrWhiteSpace(explicitDir))
            return explicitDir;

        var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
        if (repoRoot is not null)
            return Path.Combine(repoRoot, "Runtime", "Data", "Metrics");

        return Path.Combine(AppContext.BaseDirectory, "Metrics");
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string? FindRepoRoot(string start)
    {
        var dir = new DirectoryInfo(start);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "Projects"))
                && Directory.Exists(Path.Combine(dir.FullName, "Frame", "CsharpPrj")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }
}
