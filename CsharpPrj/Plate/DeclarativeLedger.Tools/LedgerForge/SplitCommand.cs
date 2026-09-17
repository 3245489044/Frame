using System.Text.Json;
using BasicMethod.Declarative.Ledger;

namespace BasicMethod.Declarative.Tools.LedgerForge;

/// <summary>单个分片的计划产物（组名 / 文件名 / 路径 / 文本 / 键数）。</summary>
internal sealed record ShardPlan(string Group, string File, string Path, string Text, int Count);

/// <summary>分片计划（<c>_meta.json</c> 文本 + 各组分片 + 总键数）。</summary>
internal sealed record SplitPlan(string MetaText, IReadOnlyList<ShardPlan> Shards, int Total);

/// <summary>
/// <c>split</c>：产物 → 分片（键归属 = <c>keys</c> 精确键优先 → <c>prefixes</c> 首段匹配；
/// 未命中 / 重叠 / 键数与登记不符**即抛**，防静默漏键）。
/// 缺省 **dry-run**：与磁盘分片逐字节比较；<c>--write</c> 才写（先建目录）。
/// </summary>
internal static class SplitCommand
{
    /// <summary>规划分片产物（含全部归属与计数校验）；不落盘。</summary>
    internal static SplitPlan Plan(LedgerView view)
    {
        using var ledger = LedgerDocument.FromLedger(view);

        if (ledger.Ports.Count != view.ExpectedTotal)
            throw new InvalidOperationException(string.Format(
                view.Message("totalMismatch"), ledger.Ports.Count, view.ExpectedTotal));

        var assignment = Assign(view, ledger.Ports);
        var shards = new List<ShardPlan>();

        foreach (var group in view.Groups)
        {
            var members = new List<(string Name, JsonElement Value)>
            {
                (view.ShardCommentKey,
                    JsonSerializer.SerializeToElement(string.Format(view.Message("shardComment"), group.Name))),
            };

            foreach (var port in ledger.Ports)
                if (string.Equals(assignment[port.Name], group.Name, StringComparison.Ordinal))
                    members.Add((port.Name, port.Value));

            var count = members.Count - 1;
            var expected = view.ExpectedCount(group.Name);
            if (count != expected)
                throw new InvalidOperationException(string.Format(
                    view.Message("shardCountMismatch"), group.Name, count, expected));

            shards.Add(new ShardPlan(
                group.Name, group.File, Path.Combine(view.PartsDirectory, group.File),
                PsJsonText.WriteMembers(members), count));
        }

        return new SplitPlan(ledger.RenderMeta(), shards, ledger.Ports.Count);
    }

    /// <summary>分片字节对拍（<c>_meta.json</c> + 各组，空 = 逐字节一致）。</summary>
    internal static IReadOnlyList<string> Drift(LedgerView view) => Drift(view, Plan(view));

    internal static int Run(LedgerView view, bool write)
    {
        var plan = Plan(view);

        if (!write)
        {
            var problems = Drift(view, plan);
            if (problems.Count == 0)
            {
                Console.WriteLine(string.Format(view.Message("forgeSplitOk"), view.Groups.Count, plan.Total));
                return 0;
            }

            foreach (var problem in problems) Console.Error.WriteLine(problem);
            Console.Error.WriteLine(view.Message("forgeDryRunHint"));
            return 1;
        }

        Directory.CreateDirectory(view.PartsDirectory);
        LedgerBytes.Write(view.MetaPath, plan.MetaText);
        foreach (var shard in plan.Shards)
        {
            LedgerBytes.Write(shard.Path, shard.Text);
            Console.WriteLine(string.Format(view.Message("perShard"), shard.Group, shard.Count, shard.File));
        }

        Console.WriteLine(string.Format(
            view.Message("splitDone"), view.Groups.Count, plan.Total, view.ExpectedTotal));
        return 0;
    }

    private static IReadOnlyList<string> Drift(LedgerView view, SplitPlan plan)
    {
        var problems = new List<string>();

        var metaDrift = LedgerBytes.Drift(view, view.MetaPath, plan.MetaText);
        if (metaDrift is not null) problems.Add(metaDrift);

        foreach (var shard in plan.Shards)
        {
            var drift = LedgerBytes.Drift(view, shard.Path, shard.Text);
            if (drift is not null) problems.Add(drift);
        }

        return problems;
    }

    /// <summary>键 → 组（<c>keys</c> 精确键优先；未命中 / 重叠 / 重复命中即抛）。</summary>
    private static Dictionary<string, string> Assign(LedgerView view, IReadOnlyList<LedgerDocument.Member> ports)
    {
        var keys = ports.Select(port => port.Name).ToHashSet(StringComparer.Ordinal);
        var assignment = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var group in view.Groups)
            foreach (var key in group.Keys)
            {
                if (!keys.Contains(key))
                    throw new InvalidOperationException(string.Format(
                        view.Message("unknownExactKey"), key, group.Name));
                if (!assignment.TryAdd(key, group.Name))
                    throw new InvalidOperationException(string.Format(
                        view.Message("duplicateKey"), key, assignment[key], group.Name));
            }

        foreach (var port in ports)
        {
            if (assignment.ContainsKey(port.Name)) continue;

            var segment = port.Name.Split('.')[0];
            var hits = view.Groups
                .Where(group => group.Prefixes.Contains(segment, StringComparer.Ordinal))
                .ToArray();

            if (hits.Length == 0)
                throw new InvalidOperationException(string.Format(
                    view.Message("unmatchedKey"), port.Name, segment));
            if (hits.Length > 1)
                throw new InvalidOperationException(string.Format(
                    view.Message("overlapKey"), port.Name, string.Join(" / ", hits.Select(hit => hit.Name))));

            assignment[port.Name] = hits[0].Name;
        }

        return assignment;
    }
}
