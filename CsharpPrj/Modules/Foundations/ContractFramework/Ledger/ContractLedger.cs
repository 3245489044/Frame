using System.Text;
using System.Text.Json;
using ContractFramework.Abstractions;
using ContractFramework.Registry;

namespace ContractFramework.Ledger;

/// <summary>
/// 契约账本：裁决落盘（JSONL 追加）+ 对账报告（谁引用了谁 / 逐裁决可见化）。
/// <para>落盘为可选（<see cref="Path"/>=null ⇒ 仅内存，供测试与门禁读取 <see cref="Lines"/>）。</para>
/// </summary>
public sealed class ContractLedger
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly List<string> _lines = [];

    public ContractLedger(string? path = null)
    {
        Path = string.IsNullOrWhiteSpace(path) ? null : path;
    }

    /// <summary>落盘路径（<c>null</c> = 仅内存）。</summary>
    public string? Path { get; }

    /// <summary>已记录行（JSONL，逐裁决一条）。</summary>
    public IReadOnlyList<string> Lines => _lines;

    /// <summary>追加一次执行结果（逐契约一条 + 未判定补记）。</summary>
    public void Append(string label, ContractResult? result, DateTimeOffset? at = null)
    {
        if (result is null)
        {
            return;
        }

        var timestamp = (at ?? DateTimeOffset.UtcNow).ToString("O");
        foreach (var outcome in result.Outcomes)
        {
            var record = new
            {
                ts = timestamp,
                label,
                id = outcome.ContractId.Raw,
                tier = outcome.Tier?.ToString(),
                effect = outcome.Effect?.ToString(),
                verdict = outcome.Verdict.ToString(),
                violations = outcome.Violations.Count == 0 ? null : outcome.Violations.Select(v => new { v.Target, v.Detail }),
                reason = outcome.Reason,
            };
            _lines.Add(JsonSerializer.Serialize(record, SerializerOptions));
        }

        foreach (var unverifiable in result.Unverifiable)
        {
            var record = new
            {
                ts = timestamp,
                label,
                id = unverifiable.ContractId.Raw,
                verdict = nameof(ContractVerdict.Bottom),
                unverifiable = unverifiable.Reason,
            };
            _lines.Add(JsonSerializer.Serialize(record, SerializerOptions));
        }
    }

    /// <summary>对账报告（逐裁决文本 + 汇总计数）。</summary>
    public string Report()
    {
        var builder = new StringBuilder();
        builder.AppendLine("== 契约账本收官 ==");
        builder.AppendLine($"记录 {_lines.Count} 条（落盘：{Path ?? "(仅内存)"}）");
        var trueCount = _lines.Count(l => l.Contains("\"verdict\":\"True\"", StringComparison.Ordinal));
        var falseCount = _lines.Count(l => l.Contains("\"verdict\":\"False\"", StringComparison.Ordinal));
        var bottomCount = _lines.Count(l => l.Contains("\"verdict\":\"Bottom\"", StringComparison.Ordinal));
        builder.AppendLine($"T={trueCount} | F={falseCount} | ⊥={bottomCount}");
        foreach (var line in _lines)
        {
            builder.AppendLine("  " + line);
        }

        return builder.ToString();
    }

    /// <summary>把已记录行追加到 <see cref="Path"/>（无路径则空操作）。</summary>
    public void Flush()
    {
        if (Path is null || _lines.Count == 0)
        {
            return;
        }

        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.AppendAllLines(Path, _lines);
    }

    /// <summary>对账报告：谁引用了谁（按 <see cref="ContractRegistry"/> 反向引用面）。</summary>
    public static string ReferenceReport(ContractRegistry? registry)
    {
        if (registry is null)
        {
            return "(无注册表)";
        }

        var builder = new StringBuilder();
        builder.AppendLine("== 契约反查（谁引用了谁） ==");
        foreach (var contract in registry.Contracts)
        {
            var impl = contract.Host?.Impl ?? "(缺 host)";
            builder.AppendLine($"  {contract.Id.Raw} ← {impl} [{contract.Tier?.ToString() ?? "?"}/{contract.Effect?.ToString() ?? "?"}]");
        }

        if (registry.Conflicts.Count > 0)
        {
            builder.AppendLine("冲突：");
            foreach (var conflict in registry.Conflicts)
            {
                builder.AppendLine("  · " + conflict);
            }
        }

        return builder.ToString();
    }
}
