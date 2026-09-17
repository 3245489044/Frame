using System.Text.Json;

namespace BasicMethod.Declarative.Tools.LedgerForge;

/// <summary>
/// <c>check</c>：只读**语义对拍**（分片内存重组 ↔ 台账产物）——
/// (a) 非 <c>ports</c> 元数据段成员数与逐段文本相等；(b) 键集相等；(c) 逐键条目文本相等。
/// <para>键序不参与比较（分组只改变次序，不改变语义）；文案 / 分组 / 路径全部取自数据视图。</para>
/// </summary>
internal static class CheckCommand
{
    internal static int Run(LedgerView view)
    {
        if (!Directory.Exists(view.PartsDirectory) || !File.Exists(view.MetaPath))
        {
            Console.WriteLine(string.Format(view.Message("notSplitYet"), view.PartsDirectory));
            return 0;
        }

        var composed = Compose(view);
        var ledger = ReadLedger(view);

        // (a) 非 ports 元数据段
        if (ledger.Meta.Count != composed.Meta.Count)
            throw new InvalidOperationException(string.Format(
                view.Message("checkMetaDiff"), $"成员数 {ledger.Meta.Count} ≠ {composed.Meta.Count}"));

        foreach (var (name, text) in ledger.Meta)
        {
            if (!composed.Meta.TryGetValue(name, out var other))
                throw new InvalidOperationException(string.Format(view.Message("checkMetaDiff"), name));

            if (!string.Equals(text, other, StringComparison.Ordinal))
                throw new InvalidOperationException(string.Format(view.Message("checkMetaDiff"), name));
        }

        // (b) 键集相等
        Console.WriteLine(string.Format(view.Message("shardTotal"), composed.Ports.Count));

        var missing = ledger.Ports.Keys.Where(key => !composed.Ports.ContainsKey(key)).ToArray();
        var extra = composed.Ports.Keys.Where(key => !ledger.Ports.ContainsKey(key)).ToArray();
        if (missing.Length != 0 || extra.Length != 0)
            throw new InvalidOperationException(string.Format(
                view.Message("checkKeyMismatch"), missing.Length, extra.Length,
                string.Join(", ", missing.Take(10)), string.Join(", ", extra.Take(10))));

        // (c) 逐键条目文本相等（规范化压缩口径）
        var diffs = ledger.Ports
            .Where(pair => !string.Equals(pair.Value, composed.Ports[pair.Key], StringComparison.Ordinal))
            .Select(pair => pair.Key)
            .ToArray();
        if (diffs.Length != 0)
            throw new InvalidOperationException(string.Format(
                view.Message("checkEntryDiff"), diffs.Length, string.Join(", ", diffs.Take(10))));

        Console.WriteLine(string.Format(view.Message("checkOk"), ledger.Ports.Count, view.Groups.Count));
        return 0;
    }

    /// <summary>分片内存重组：<c>_meta.json</c>（非 ports 段）+ 各组分片按**组序**取端口体。</summary>
    private static (Dictionary<string, string> Meta, Dictionary<string, string> Ports) Compose(LedgerView view)
    {
        var meta = new Dictionary<string, string>(StringComparer.Ordinal);
        using (var document = JsonDocument.Parse(File.ReadAllText(view.MetaPath)))
            foreach (var property in document.RootElement.EnumerateObject())
                meta[property.Name] = LedgerJson.Minify(property.Value);

        var ports = new Dictionary<string, string>(StringComparer.Ordinal);
        var owner = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var group in view.Groups)
        {
            var shardPath = Path.Combine(view.PartsDirectory, group.File);
            if (!File.Exists(shardPath))
                throw new InvalidOperationException(string.Format(view.Message("missingShard"), shardPath, group.Name));

            using var document = JsonDocument.Parse(File.ReadAllText(shardPath));
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, view.ShardCommentKey, StringComparison.Ordinal)) continue;

                if (owner.TryGetValue(property.Name, out var previous))
                    throw new InvalidOperationException(string.Format(
                        view.Message("duplicateInParts"), property.Name, previous));

                owner[property.Name] = group.Name;
                ports[property.Name] = LedgerJson.Minify(property.Value);
            }
        }

        return (meta, ports);
    }

    /// <summary>台账产物读取（<c>ports</c> 段 → 端口体；其余顶层成员 → 元数据段）。</summary>
    private static (Dictionary<string, string> Meta, Dictionary<string, string> Ports) ReadLedger(LedgerView view)
    {
        if (!File.Exists(view.LedgerPath)) throw new FileNotFoundException($"台账缺失：{view.LedgerPath}");

        using var document = JsonDocument.Parse(File.ReadAllText(view.LedgerPath));
        var root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty(view.PortsProperty, out var section)
            || section.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException(string.Format(
                view.Message("missingPorts"), view.LedgerPath, view.PortsProperty));

        var ports = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in section.EnumerateObject())
            ports[property.Name] = LedgerJson.Minify(property.Value);

        var meta = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
            if (!string.Equals(property.Name, view.PortsProperty, StringComparison.Ordinal))
                meta[property.Name] = LedgerJson.Minify(property.Value);

        return (meta, ports);
    }
}
