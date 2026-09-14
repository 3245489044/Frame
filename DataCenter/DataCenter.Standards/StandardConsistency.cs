using DataCenter.Protocol.Standards;

namespace DataCenter.Standards;

/// <summary>六条机器判据的可配参数（判据本身不可配；此处只给取值域与上一快照）。</summary>
public sealed record StandardConsistencyOptions
{
    /// <summary>C4 的跨切字段登记表（归一化后比较：去 <c>_</c>/<c>-</c> 且忽略大小写）。</summary>
    public IReadOnlyList<string> CrossCutFields { get; init; } =
        ["runId", "roundId", "sessionId", "execId", "traceId", "exec_result"];

    /// <summary>C5 的白名单（如产品名 / 宿主：数据项生产方可非端口）。</summary>
    public IReadOnlyList<string> ProducerWhitelist { get; init; } = [];

    /// <summary>C6 的上一快照（键 = <c>段:键</c> → version；null = 无基线，跳过版本比对）。</summary>
    public IReadOnlyDictionary<string, int>? PreviousVersions { get; init; }
}

/// <summary>
/// 标准一致性六条机器判据（设计文档 §3.4）：C1 键唯一 / C2 <c>ports.emits ⊆ events</c> /
/// C3 <c>events.carries ⊆ data</c> / C4 跨切字段 ⊆ data / C5 <c>data.producer ⊆ ports ∪ 白名单</c> /
/// C6 版本单调（与上一快照比对）。
/// <para>
/// 纪律：判据**只此一处**、失败即阻断；数据不合规时改数据，**禁改判据**（对齐上位方案「契约即断言」）。
/// </para>
/// </summary>
public static class StandardConsistency
{
    /// <summary>跑 C1–C6，返回全部违规（空 = 全绿）。</summary>
    public static IReadOnlyList<string> Check(IStandardCatalog catalog, StandardConsistencyOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        options ??= new StandardConsistencyOptions();

        var violations = new List<string>();
        var portKeys = new HashSet<string>(catalog.Ports.Select(p => p.Key), StringComparer.Ordinal);
        var dataKeysNormalized = new HashSet<string>(catalog.Data.Select(d => Normalize(d.Key)), StringComparer.Ordinal);
        var eventKeys = new HashSet<string>(catalog.Events.Select(e => e.Key), StringComparer.Ordinal);

        CheckKeyUniqueness(catalog, violations);
        CheckEmits(catalog, eventKeys, violations);
        CheckCarries(catalog, dataKeysNormalized, violations);
        CheckCrossCutFields(catalog, dataKeysNormalized, options, violations);
        CheckProducers(catalog, portKeys, options, violations);
        CheckVersionMonotonic(catalog, options, violations);

        return violations;
    }

    /// <summary>跑 C1–C6，有违规即抛（一次报全，便于一次修数据）。</summary>
    public static void EnsureValid(IStandardCatalog catalog, StandardConsistencyOptions? options = null)
    {
        var violations = Check(catalog, options);
        if (violations.Count == 0) return;

        throw new StandardViolationException(
            $"标准一致性校验失败（{violations.Count} 条；改数据不改判据）：" +
            Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", violations),
            violations);
    }

    /// <summary>C1：四本台账各自键唯一（Ordinal；JSON 层重复键被静默合并，须扫原始文本）。</summary>
    private static void CheckKeyUniqueness(IStandardCatalog catalog, List<string> violations)
    {
        foreach (var (name, json) in RawTexts(catalog))
            foreach (var duplicate in StandardJson.DuplicateKeys(json))
                violations.Add($"C1 键不唯一：{name} 中同一对象内重复键 '{duplicate}'");

        ReportDuplicates("ports", catalog.Ports.Select(p => p.Key), violations);
        ReportDuplicates("data", catalog.Data.Select(d => d.Key), violations);
        ReportDuplicates("events", catalog.Events.Select(e => e.Key), violations);
        ReportDuplicates("fakes", catalog.Fakes.Select(f => f.PortKey), violations);
    }

    /// <summary>C2：<c>ports[x].emits</c> ⊆ 事件键集。</summary>
    private static void CheckEmits(IStandardCatalog catalog, HashSet<string> eventKeys, List<string> violations)
    {
        foreach (var port in catalog.Ports)
            foreach (var emitted in port.Emits)
                if (!eventKeys.Contains(emitted))
                    violations.Add($"C2 声明了不存在的事件：ports[{port.Key}].emits 含 '{emitted}'（事件台账未登记）");
    }

    /// <summary>C3：<c>events[x].carries</c> ⊆ 数据键集。</summary>
    private static void CheckCarries(IStandardCatalog catalog, HashSet<string> dataKeysNormalized, List<string> violations)
    {
        foreach (var evt in catalog.Events)
            foreach (var carried in evt.Carries)
                if (!dataKeysNormalized.Contains(Normalize(carried)))
                    violations.Add($"C3 事件携带未登记数据项：events[{evt.Key}].carries 含 '{carried}'（data 段未登记）");
    }

    /// <summary>C4：端口契约中出现的跨切字段 ⊆ 数据键集（端口契约与数据标准不得脱节）。</summary>
    private static void CheckCrossCutFields(
        IStandardCatalog catalog, HashSet<string> dataKeysNormalized, StandardConsistencyOptions options, List<string> violations)
    {
        var crossCut = new HashSet<string>(options.CrossCutFields.Select(Normalize), StringComparer.Ordinal);

        foreach (var port in catalog.Ports)
        {
            var fields = port.Inputs.Select(i => i.Name).Concat(port.OutFields);
            foreach (var field in fields)
                if (crossCut.Contains(Normalize(field)) && !dataKeysNormalized.Contains(Normalize(field)))
                    violations.Add($"C4 跨切字段未登记数据标准：ports[{port.Key}] 字段 '{field}'（data 段未登记）");
        }
    }

    /// <summary>C5：<c>data[x].producer</c>（若声明）⊆ 端口键集 ∪ 白名单。</summary>
    private static void CheckProducers(
        IStandardCatalog catalog, HashSet<string> portKeys, StandardConsistencyOptions options, List<string> violations)
    {
        var whitelist = new HashSet<string>(options.ProducerWhitelist, StringComparer.Ordinal);

        foreach (var item in catalog.Data)
            foreach (var producer in item.Producers)
                if (!portKeys.Contains(producer) && !whitelist.Contains(producer))
                    violations.Add($"C5 数据项无生产方：data[{item.Key}].producer = '{producer}'（既非端口键亦不在白名单）");
    }

    /// <summary>C6：同键 version 只增不减（与上一快照比对；无基线则跳过）。</summary>
    private static void CheckVersionMonotonic(
        IStandardCatalog catalog, StandardConsistencyOptions options, List<string> violations)
    {
        if (options.PreviousVersions is not { } previous) return;

        foreach (var (scope, key, version) in Versioned(catalog))
            if (previous.TryGetValue($"{scope}:{key}", out var last) && version < last)
                violations.Add($"C6 版本回退：{scope}[{key}].version = {version} < 上一快照 {last}（只增不减）");
    }

    private static IEnumerable<(string Scope, string Key, int Version)> Versioned(IStandardCatalog catalog)
    {
        foreach (var port in catalog.Ports) yield return ("ports", port.Key, port.Version);
        foreach (var item in catalog.Data) yield return ("data", item.Key, item.Version);
        foreach (var evt in catalog.Events) yield return ("events", evt.Key, evt.Version);
        foreach (var fake in catalog.Fakes) yield return ("fakes", fake.PortKey, fake.Version);
    }

    /// <summary>原始文本视图（供 C1 在原始文本层扫重复键）。</summary>
    private static IEnumerable<(string Name, string Json)> RawTexts(IStandardCatalog catalog)
    {
        yield return ("ports.json", catalog.PortsJson);
        if (catalog.EventsJson is { } events) yield return ("events.json", events);
        if (catalog is StandardCatalog { FakesJson: { } fakes }) yield return ("fakes.json", fakes);
    }

    private static void ReportDuplicates(string section, IEnumerable<string> keys, List<string> violations)
    {
        foreach (var duplicate in keys.GroupBy(k => k, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key))
            violations.Add($"C1 键不唯一：{section} 段重复键 '{duplicate}'");
    }

    /// <summary>归一化（去 <c>_</c>/<c>-</c> 后统一大写）：跨切字段与数据键的命名形态容差。</summary>
    private static string Normalize(string name)
        => name.Replace("_", string.Empty, StringComparison.Ordinal)
               .Replace("-", string.Empty, StringComparison.Ordinal)
               .ToUpperInvariant();
}
