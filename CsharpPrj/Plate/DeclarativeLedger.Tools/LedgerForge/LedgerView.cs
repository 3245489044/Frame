using System.Text.Json;

namespace BasicMethod.Declarative.Tools.LedgerForge;

/// <summary>台账分组（分片规则；键 / 前缀 / 文件全部来自数据视图，工具内不硬编码）。</summary>
internal sealed record LedgerGroup(string Name, string File, IReadOnlyList<string> Prefixes, IReadOnlyList<string> Keys);

/// <summary>
/// 台账数据视图（<c>Split-SpecPortLedger.data.json</c> 的只读投影）：路径 / 规则 / 分组 / 文案。
/// <para>默认定位 = 自程序集位置向上寻找数据视图文件（与守卫同法，免手工传参）。</para>
/// </summary>
internal sealed record LedgerView(
    string DataPath,
    string CentralTestRoot,
    string LedgerPath,
    string PartsDirectory,
    string MetaPath,
    string PortsProperty,
    string ShardCommentKey,
    IReadOnlyList<LedgerGroup> Groups,
    IReadOnlyDictionary<string, string> Messages)
{
    /// <summary>装载数据视图（<paramref name="explicitPath"/> 为空则自动定位）。</summary>
    internal static LedgerView Load(string? explicitPath)
    {
        var dataPath = explicitPath is { Length: > 0 } ? Path.GetFullPath(explicitPath) : Locate();
        if (!File.Exists(dataPath)) throw new FileNotFoundException($"数据视图缺失：{dataPath}");

        using var document = JsonDocument.Parse(File.ReadAllText(dataPath));
        var root = document.RootElement;
        var paths = root.GetProperty("paths");
        var rules = root.GetProperty("rules");

        var centralTestRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(dataPath)!, ".."));

        var messages = new Dictionary<string, string>(StringComparer.Ordinal);
        if (root.TryGetProperty("messages", out var messageNode) && messageNode.ValueKind == JsonValueKind.Object)
            foreach (var property in messageNode.EnumerateObject())
                messages[property.Name] = property.Value.GetString() ?? string.Empty;

        var groups = root.GetProperty("groups").EnumerateArray()
            .Select(group => new LedgerGroup(
                group.GetProperty("name").GetString() ?? string.Empty,
                group.GetProperty("file").GetString() ?? string.Empty,
                Strings(group, "prefixes"),
                Strings(group, "keys")))
            .ToArray();

        return new LedgerView(
            dataPath,
            centralTestRoot,
            Relative(paths, "ledger"),
            Relative(paths, "partsDir"),
            Path.Combine(Relative(paths, "partsDir"), paths.GetProperty("metaFile").GetString() ?? "_meta.json"),
            rules.GetProperty("portsProperty").GetString() ?? "ports",
            rules.GetProperty("shardCommentKey").GetString() ?? "$comment",
            groups,
            messages);

        string Relative(JsonElement pathsNode, string name)
            => Path.Combine(centralTestRoot,
                (pathsNode.GetProperty(name).GetString() ?? string.Empty).Replace('/', Path.DirectorySeparatorChar));
    }

    /// <summary>数据视图文案（<c>{0}</c>/<c>{1}</c> 为占位；与 PS 同源）。</summary>
    internal string Message(string name) => Messages.TryGetValue(name, out var text) ? text : $"<missing:{name}>";

    /// <summary>按数据视图定位（自程序集位置向上；找不到即抛，不猜路径）。</summary>
    private static string Locate()
    {
        const string Relative = @"Environment\CentralTest\Tools\Split-SpecPortLedger.data.json";
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, Relative);
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent!;
        }

        throw new InvalidOperationException(
            $"未能自 {AppContext.BaseDirectory} 向上定位数据视图（未找到 {Relative}）；请用 --data 显式指定");
    }

    private static IReadOnlyList<string> Strings(JsonElement owner, string name)
        => owner.TryGetProperty(name, out var node) && node.ValueKind == JsonValueKind.Array
            ? node.EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray()
            : [];
}
