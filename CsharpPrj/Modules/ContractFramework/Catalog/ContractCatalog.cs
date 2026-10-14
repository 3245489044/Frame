using System.Text.Json;
using ContractFramework.Abstractions;

namespace ContractFramework.Catalog;

/// <summary>
/// 声明加载问题（解析失败 / 跨文件 Id 重复 / 文件缺失）——可见化，不静默吞。
/// </summary>
public sealed record ContractCatalogIssue(string Kind, string Detail)
{
    public override string ToString() => $"[{Kind}] {Detail}";
}

/// <summary>
/// 契约目录：加载 + 合并多文件 + 去重（跨契约 Id 唯一）。
/// <para>单一真源 = <c>contracts.json</c>；本类是 C# 侧唯一加载入口（PowerShell 侧同读一份文件）。</para>
/// </summary>
public sealed class ContractCatalog
{
    private readonly List<ContractDescriptor> _descriptors;
    private readonly List<ContractCatalogIssue> _issues;
    private readonly List<string> _sources;

    private ContractCatalog(
        List<ContractDescriptor> descriptors,
        List<ContractCatalogIssue> issues,
        List<string> sources)
    {
        _descriptors = descriptors;
        _issues = issues;
        _sources = sources;
    }

    /// <summary>全部契约声明（跨文件合并后）。</summary>
    public IReadOnlyList<ContractDescriptor> Descriptors => _descriptors;

    /// <summary>加载问题清单。</summary>
    public IReadOnlyList<ContractCatalogIssue> Issues => _issues;

    /// <summary>声明的来源文件清单。</summary>
    public IReadOnlyList<string> Sources => _sources;

    /// <summary>空目录（框架侧零具体契约时的初值）。</summary>
    public static ContractCatalog Empty { get; } = new([], [], []);

    /// <summary>加载单个 <c>contracts.json</c>。</summary>
    public static ContractCatalog Load(string contractsJsonPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contractsJsonPath);
        var issues = new List<ContractCatalogIssue>();
        var document = ReadDocument(contractsJsonPath, issues);
        if (document is null)
        {
            return new ContractCatalog([], issues, []);
        }

        var merged = Merge([document]);
        var allIssues = new List<ContractCatalogIssue>(issues);
        allIssues.AddRange(merged.Issues);
        return new ContractCatalog([.. merged.Descriptors], allIssues, [.. merged.Sources]);
    }

    /// <summary>加载仓库级索引并逐个加载声明文件。</summary>
    /// <param name="indexPath"><c>contracts.index.json</c> 路径。</param>
    /// <param name="repoRoot">声明文件路径的解析基准（缺省 = 索引所在目录）。</param>
    public static ContractCatalog LoadIndex(string indexPath, string? repoRoot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(indexPath);
        var issues = new List<ContractCatalogIssue>();
        var index = ReadIndex(indexPath, issues);
        if (index is null)
        {
            return new ContractCatalog([], issues, []);
        }

        var root = repoRoot ?? Path.GetDirectoryName(Path.GetFullPath(indexPath)) ?? ".";
        var documents = new List<ContractsDocument>();
        foreach (var relative in index.Files)
        {
            if (string.IsNullOrWhiteSpace(relative))
            {
                continue;
            }

            var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            var document = ReadDocument(full, issues);
            if (document is not null)
            {
                documents.Add(document);
            }
        }

        var merged = Merge(documents);
        var allIssues = new List<ContractCatalogIssue>(issues);
        allIssues.AddRange(merged.Issues);
        var sources = new List<string>(merged.Sources) { indexPath };
        return new ContractCatalog([.. merged.Descriptors], allIssues, sources);
    }

    /// <summary>合并多份文档（跨文件 Id 去重：重复保留首见并记冲突）。</summary>
    public static ContractCatalog Merge(IEnumerable<ContractsDocument> documents)
    {
        var descriptors = new List<ContractDescriptor>();
        var issues = new List<ContractCatalogIssue>();
        var sources = new List<string>();
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var document in documents ?? [])
        {
            var source = document.Source ?? string.Empty;
            if (source.Length > 0 && !sources.Contains(source))
            {
                sources.Add(source);
            }

            foreach (var entry in document.Contracts ?? [])
            {
                var descriptor = Map(entry, source);
                var key = descriptor.Id.Raw;
                if (key.Length == 0)
                {
                    descriptors.Add(descriptor);
                    issues.Add(new ContractCatalogIssue("MissingId", $"{source}: 条目缺 id（判 ⊥）"));
                    continue;
                }

                if (seen.TryGetValue(key, out var firstSource))
                {
                    issues.Add(new ContractCatalogIssue(
                        "DuplicateId",
                        $"{key}: 在 {firstSource} 与 {source} 重复声明（保留首见；Id 须跨文件唯一）"));
                    continue;
                }

                seen[key] = source;
                descriptors.Add(descriptor);
            }
        }

        return new ContractCatalog(descriptors, issues, sources);
    }

    /// <summary>按 Id 精确查找（原始串）。</summary>
    public ContractDescriptor? Find(ContractId id)
    {
        var raw = id?.Raw ?? string.Empty;
        return _descriptors.FirstOrDefault(d => string.Equals(d.Id.Raw, raw, StringComparison.Ordinal));
    }

    /// <summary>按层级筛选。</summary>
    public IEnumerable<ContractDescriptor> ByTier(ContractTier tier)
        => _descriptors.Where(d => d.Tier == tier);

    /// <summary>按不变量族筛选（<c>Family</c> 精确匹配）。</summary>
    public IEnumerable<ContractDescriptor> ByFamily(string? family)
        => _descriptors.Where(d => string.Equals(d.Id.Family, family, StringComparison.Ordinal));

    private static ContractsDocument? ReadDocument(string path, List<ContractCatalogIssue> issues)
    {
        if (!File.Exists(path))
        {
            issues.Add(new ContractCatalogIssue("MissingFile", path));
            return null;
        }

        try
        {
            var json = File.ReadAllText(path);
            var document = JsonSerializer.Deserialize<ContractsDocument>(json, ContractsDocument.SerializerOptions);
            if (document is null)
            {
                issues.Add(new ContractCatalogIssue("EmptyDocument", path));
                return null;
            }

            document.Source = path;
            return document;
        }
        catch (Exception ex)
        {
            issues.Add(new ContractCatalogIssue("ParseError", $"{path}: {ex.Message}"));
            return null;
        }
    }

    private static ContractsIndexDocument? ReadIndex(string path, List<ContractCatalogIssue> issues)
    {
        if (!File.Exists(path))
        {
            issues.Add(new ContractCatalogIssue("MissingIndex", path));
            return null;
        }

        try
        {
            var json = File.ReadAllText(path);
            var index = JsonSerializer.Deserialize<ContractsIndexDocument>(json, ContractsDocument.SerializerOptions);
            if (index is null)
            {
                issues.Add(new ContractCatalogIssue("EmptyIndex", path));
                return null;
            }

            index.Source = path;
            return index;
        }
        catch (Exception ex)
        {
            issues.Add(new ContractCatalogIssue("IndexParseError", $"{path}: {ex.Message}"));
            return null;
        }
    }

    private static ContractDescriptor Map(ContractsDocument.ContractEntry entry, string source)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var tier = TryParseTier(entry.Tier, out var parsedTier) ? parsedTier : (ContractTier?)null;
        var effect = TryParseEffect(entry.Effect, out var parsedEffect) ? parsedEffect : (ContractEffect?)null;
        var carrier = entry.Carrier is null ? null : ContractCarrier.Create(entry.Carrier.Kind, entry.Carrier.Facts);
        var host = entry.Host is null ? null : ContractHostRef.Create(entry.Host.Kind, entry.Host.Impl);
        var evidence = entry.Evidence is null
            ? null
            : ContractEvidence.Create(entry.Evidence.RecomputeCmd, entry.Evidence.Positive, entry.Evidence.Negative);

        return new ContractDescriptor(
            ContractId.Parse(entry.Id), entry.Semantics, carrier, tier, effect, host, evidence,
            entry.Invariant, entry.Title, source);
    }

    private static bool TryParseTier(string? text, out ContractTier tier)
        => Enum.TryParse(text, ignoreCase: true, out tier) && Enum.IsDefined(tier);

    private static bool TryParseEffect(string? text, out ContractEffect effect)
        => Enum.TryParse(text, ignoreCase: true, out effect) && Enum.IsDefined(effect);
}
