using System.Text.Json;
using System.Text.RegularExpressions;

namespace BasicMethod.Declarative.Tools.LedgerForge;

/// <summary>
/// <c>Convert-SpecKindToPortKey.data.json</c> 的只读投影（路径 / 冻结映射 / 改写规则 / 文案）。
/// <para>
/// 与既有 PowerShell 工具**同一真源**（本件零内联领域串）；缺失 / 非法即 fail-fast。
/// 仓库根由数据视图位置**上溯定位**（须含 <c>Environment\</c> 与 <c>Docs\</c>），不猜路径。
/// </para>
/// </summary>
internal sealed record SpecKindView(
    string DataPath,
    string RepoRoot,
    string AgentTestDirectory,
    string LedgerPath,
    string CasesDirectoryName,
    string SourceExtension,
    IReadOnlyList<string> ExcludedKinds,
    Regex StepLine,
    string LineSeparatorPattern,
    IReadOnlyDictionary<string, string> LegacyMap,
    IReadOnlyDictionary<string, string> Messages)
{
    /// <summary>数据视图文案（<c>{0}</c> 占位由调用方格式化）。</summary>
    internal string Message(string name, params object[] args)
    {
        var text = Messages.TryGetValue(name, out var value) ? value : $"<missing:{name}>";
        return args.Length == 0 ? text : string.Format(text, args);
    }

    /// <summary>
    /// 旧 kind → 端口键映射：**冻结映射优先**（数据视图 <c>legacyMap</c>）；缺失时回退读台账
    /// <c>impl.legacyKind</c>（历史兼容路径，撞键即记入 <paramref name="duplicates"/>）。
    /// </summary>
    internal IReadOnlyDictionary<string, string> PortKeyMap(out IReadOnlyList<string> duplicates)
    {
        var found = new List<string>();
        duplicates = found;

        if (LegacyMap.Count != 0) return LegacyMap;

        if (!File.Exists(LedgerPath)) throw new FileNotFoundException(Message("errLedgerMissing", LedgerPath));

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        using var ledger = JsonDocument.Parse(File.ReadAllText(LedgerPath));
        if (!ledger.RootElement.TryGetProperty("ports", out var ports) || ports.ValueKind != JsonValueKind.Object)
            return map;

        foreach (var port in ports.EnumerateObject())
        {
            var legacy = ReadLegacy(port.Value);
            if (string.IsNullOrWhiteSpace(legacy)) continue;

            if (map.TryGetValue(legacy, out var previous))
            {
                found.Add($"{legacy} → {previous} / {port.Name}");
                continue;
            }

            map[legacy] = port.Name;
        }

        return map;
    }


    /// <summary>数据视图装载（<paramref name="explicitPath"/> 为空则自程序集位置向上定位）。</summary>
    internal static SpecKindView Load(string? explicitPath)
    {
        var dataPath = explicitPath is { Length: > 0 } ? Path.GetFullPath(explicitPath) : Locate();
        if (!File.Exists(dataPath)) throw new FileNotFoundException($"数据视图缺失：{dataPath}");

        using var document = JsonDocument.Parse(File.ReadAllText(dataPath));
        var root = document.RootElement;
        var paths = root.GetProperty("paths");
        var rules = root.GetProperty("rules");
        var repoRoot = ResolveRepoRoot(Path.GetDirectoryName(dataPath)!);

        var legacyMap = new Dictionary<string, string>(StringComparer.Ordinal);
        if (root.TryGetProperty("legacyMap", out var legacy) && legacy.ValueKind == JsonValueKind.Object)
            foreach (var entry in legacy.EnumerateObject())
            {
                if (entry.Name.StartsWith('$')) continue;
                if (entry.Value.GetString() is { Length: > 0 } target) legacyMap[entry.Name] = target;
            }

        var messages = new Dictionary<string, string>(StringComparer.Ordinal);
        if (root.TryGetProperty("messages", out var messageNode) && messageNode.ValueKind == JsonValueKind.Object)
            foreach (var property in messageNode.EnumerateObject())
                messages[property.Name] = property.Value.GetString() ?? string.Empty;

        var excluded = new List<string>();
        if (rules.TryGetProperty("excludedKindTokens", out var excludedNode) && excludedNode.ValueKind == JsonValueKind.Array)
            foreach (var item in excludedNode.EnumerateArray())
                if (item.GetString() is { Length: > 0 } token) excluded.Add(token);

        return new SpecKindView(
            dataPath,
            repoRoot,
            Relative(paths, "agentTestDir"),
            Relative(paths, "ledger"),
            paths.GetProperty("casesDir").GetString() ?? "Cases",
            rules.GetProperty("sourceExtension").GetString() ?? ".specs",
            excluded,
            new Regex(rules.GetProperty("stepLine").GetString()
                ?? throw new InvalidOperationException("rules.stepLine 缺失")),
            rules.GetProperty("lineSeparatorPattern").GetString() ?? "(?<=\\n)",
            legacyMap,
            messages);

        string Relative(JsonElement pathsNode, string name)
            => Path.Combine(repoRoot, (pathsNode.GetProperty(name).GetString() ?? string.Empty)
                .Replace('/', Path.DirectorySeparatorChar));
    }

    private static string? ReadLegacy(JsonElement port)
    {
        if (port.ValueKind != JsonValueKind.Object) return null;
        if (port.TryGetProperty("impl", out var impl) && impl.ValueKind == JsonValueKind.Object
            && impl.TryGetProperty("legacyKind", out var inner)) return inner.GetString();

        return port.TryGetProperty("legacyKind", out var outer) ? outer.GetString() : null;
    }

    /// <summary>仓库根（自数据视图所在目录上溯；须含 <c>Environment</c> 与 <c>Docs</c>）。</summary>
    private static string ResolveRepoRoot(string start)
    {
        var directory = new DirectoryInfo(start);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "Environment"))
                && Directory.Exists(Path.Combine(directory.FullName, "Docs")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"无法自 {start} 上溯定位仓库根（须含 Environment/ 与 Docs/）");
    }

    private static string Locate()
    {
        const string Relative = @"Environment\CentralTest\Tools\Convert-SpecKindToPortKey.data.json";
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, Relative);
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"未能自 {AppContext.BaseDirectory} 向上定位数据视图（未找到 {Relative}）；请用 --data 显式指定");
    }
}
