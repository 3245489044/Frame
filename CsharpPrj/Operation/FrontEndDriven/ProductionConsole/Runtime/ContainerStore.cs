using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FrontEndDriven.ProductionConsole.Runtime;

/// <summary>
/// 文件历史存储（JSONL 追加 + 内存查询 + 容量轮转）。单文件、单写者锁；损坏行跳过不抛。
/// <para>用途：控制台历史/一稿一痕核对；插件只需给定路径（如 stores/console/runs.jsonl）。</para>
/// </summary>
public sealed class ContainerStore : IProductionContainer
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly object _gate = new();
    private readonly string _path;
    private readonly int _maxEntries;
    private int _writes;

    public ContainerStore(string filePath, int maxEntries = 5000)
    {
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("历史文件路径为空", nameof(filePath));
        _path = System.IO.Path.GetFullPath(filePath);
        _maxEntries = Math.Max(50, maxEntries);
    }

    public string FilePath => _path;

    public void Append(JobSnapshot snapshot)
    {
        if (snapshot is null) return;
        lock (_gate)
        {
            var dir = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);

            File.AppendAllText(_path, JsonSerializer.Serialize(snapshot, Json) + Environment.NewLine);
            if (++_writes % 32 == 0) TrimLocked();
        }
    }

    public PagedResult<JobSnapshot> Query(ContainerQuery query)
    {
        query ??= new ContainerQuery();
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize <= 0 ? 20 : query.PageSize, 1, 200);

        var all = ReadAll();
        var filtered = all.Where(s => Matches(s, query)).ToList();
        var items = filtered
            .OrderByDescending(s => s.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return new PagedResult<JobSnapshot>
        {
            Items = items,
            Total = filtered.Count,
            Page = page,
            PageSize = pageSize,
        };
    }

    public JobSnapshot? LastByInputHash(string inputHash)
    {
        if (string.IsNullOrWhiteSpace(inputHash)) return null;
        var all = ReadAll();
        return all.LastOrDefault(s => string.Equals(s.InputHash, inputHash, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 按 jobId 删除历史行（带锁 + 临时文件原子替换）。返回删除行数；损坏行保留不动。
    /// 用于测试/运维清理产物对应的历史，避免“文件已删、历史仍可查”。
    /// </summary>
    public int Remove(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId)) return 0;

        lock (_gate)
        {
            if (!File.Exists(_path)) return 0;

            var kept = new List<string>();
            var removed = 0;
            foreach (var line in File.ReadLines(_path))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    kept.Add(line);
                    continue;
                }

                try
                {
                    var snapshot = JsonSerializer.Deserialize<JobSnapshot>(line, Json);
                    if (snapshot is not null
                        && string.Equals(snapshot.JobId, jobId, StringComparison.OrdinalIgnoreCase))
                    {
                        removed++;
                        continue;
                    }
                }
                catch
                {
                    // 损坏行不参与匹配，原样保留
                }

                kept.Add(line);
            }

            if (removed == 0) return 0;

            var temp = _path + ".tmp";
            File.WriteAllLines(temp, kept);
            File.Move(temp, _path, overwrite: true);
            return removed;
        }
    }

    private static bool Matches(JobSnapshot snapshot, ContainerQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.CapabilityId)
            && !string.Equals(snapshot.CapabilityId, query.CapabilityId, StringComparison.OrdinalIgnoreCase))
            return false;
        if (query.Status is { } status && snapshot.Status != status) return false;
        if (query.From is { } from && snapshot.CreatedAt < from) return false;
        if (query.To is { } to && snapshot.CreatedAt > to) return false;
        if (!string.IsNullOrWhiteSpace(query.Text))
        {
            var text = query.Text.Trim();
            var haystack = snapshot.JobId + " " + (snapshot.InputHash ?? "") + " " + snapshot.CapabilityId + " "
                         + string.Join(" ", snapshot.Params.Select(kv => $"{kv.Key}={kv.Value}"));
            if (haystack.IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0) return false;
        }
        return true;
    }

    private List<JobSnapshot> ReadAll()
    {
        lock (_gate)
        {
            if (!File.Exists(_path)) return new List<JobSnapshot>();
            var result = new List<JobSnapshot>();
            foreach (var line in File.ReadLines(_path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var snapshot = JsonSerializer.Deserialize<JobSnapshot>(line, Json);
                    if (snapshot is not null) result.Add(snapshot);
                }
                catch
                {
                    // 损坏行（半写/版本漂移）跳过，不影响其余历史
                }
            }
            return result;
        }
    }

    /// <summary>超容量（1.2 倍水位）时保留最近 <see cref="_maxEntries"/> 条（原子替换）。</summary>
    private void TrimLocked()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var lines = File.ReadAllLines(_path);
            var keep = (int)Math.Ceiling(_maxEntries * 1.2);
            if (lines.Length <= keep) return;

            var retained = lines.Skip(lines.Length - _maxEntries).ToArray();
            var temp = _path + ".tmp";
            File.WriteAllLines(temp, retained);
            File.Move(temp, _path, overwrite: true);
        }
        catch
        {
            // 轮转失败不影响追加（下次再试）
        }
    }
}
