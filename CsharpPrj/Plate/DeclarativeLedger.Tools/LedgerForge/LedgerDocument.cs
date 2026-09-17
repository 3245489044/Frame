using System.Text.Json;
using BasicMethod.Declarative.Ledger;

namespace BasicMethod.Declarative.Tools.LedgerForge;

/// <summary>
/// 台账文档（**有序成员模型**）：元数据段（非 <c>ports</c> 的顶层成员）+ 端口段（<c>ports</c> 成员）；
/// 两段成员序一律 = 来源文件序（不排序、不重排）。两种来源共用同一渲染口径：
/// <see cref="FromParts"/>（<c>_meta.json</c> + 各组分片按组序）与 <see cref="FromLedger"/>（台账产物）。
/// </summary>
internal sealed class LedgerDocument : IDisposable
{
    private readonly List<JsonDocument> _owners = [];
    private readonly string _sectionName;

    private LedgerDocument(string sectionName, List<Member> meta, List<Member> ports)
    {
        _sectionName = sectionName;
        Meta = meta;
        Ports = ports;
    }

    /// <summary>单成员（成员序即文件序；值为原文随行的元素）。</summary>
    internal readonly record struct Member(string Name, JsonElement Value);

    /// <summary>元数据段成员（非 <c>ports</c> 顶层成员，源序）。</summary>
    internal IReadOnlyList<Member> Meta { get; }

    /// <summary>端口段成员（源序 = 分片按组序 / 产物的 <c>ports</c> 段序）。</summary>
    internal IReadOnlyList<Member> Ports { get; }

    /// <summary>台账产物文本（PowerShell <c>ConvertTo-Json</c> 逐字节口径）。</summary>
    internal string Render() => PsJsonText.WriteWithSection(Pairs(Meta), _sectionName, Pairs(Ports));

    /// <summary>元数据分片文本（<c>_meta.json</c>）。</summary>
    internal string RenderMeta() => PsJsonText.WriteMembers(Pairs(Meta));

    public void Dispose()
    {
        foreach (var owner in _owners) owner.Dispose();
        _owners.Clear();
    }

    /// <summary>分片 → 文档（<c>_meta.json</c> 全量成员 + 各组分片按组序；分片内以维护提示开头的成员不参与重组）。</summary>
    internal static LedgerDocument FromParts(LedgerView view)
    {
        if (!File.Exists(view.MetaPath)) throw new FileNotFoundException($"元数据分片缺失：{view.MetaPath}");

        var document = new LedgerDocument(view.PortsProperty, [], []);
        var meta = document.ParseFile(view.MetaPath);
        var ports = new List<Member>();
        var owner = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var group in view.Groups)
        {
            var shardPath = Path.Combine(view.PartsDirectory, group.File);
            if (!File.Exists(shardPath))
                throw new InvalidOperationException(string.Format(view.Message("missingShard"), shardPath, group.Name));

            foreach (var property in document.ParseFile(shardPath).RootElement.EnumerateObject())
            {
                if (property.Name.StartsWith(view.ShardCommentKey, StringComparison.Ordinal)) continue;

                if (owner.TryGetValue(property.Name, out var previous))
                    throw new InvalidOperationException(string.Format(
                        view.Message("duplicateInParts"), property.Name, previous));

                owner[property.Name] = group.Name;
                ports.Add(new Member(property.Name, property.Value));
            }
        }

        return new LedgerDocument(view.PortsProperty, [.. meta.RootElement.EnumerateObject().Select(p => new Member(p.Name, p.Value))], ports);
    }

    /// <summary>台账产物 → 文档（<c>ports</c> 段 → 端口段；其余顶层成员 → 元数据段）。</summary>
    internal static LedgerDocument FromLedger(LedgerView view)
    {
        if (!File.Exists(view.LedgerPath)) throw new FileNotFoundException($"台账缺失：{view.LedgerPath}");

        var document = new LedgerDocument(view.PortsProperty, [], []);
        var ledger = document.ParseFile(view.LedgerPath);
        var root = ledger.RootElement;

        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty(view.PortsProperty, out var section)
            || section.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException(string.Format(
                view.Message("missingPorts"), view.LedgerPath, view.PortsProperty));

        var meta = new List<Member>();
        foreach (var property in root.EnumerateObject())
            if (!string.Equals(property.Name, view.PortsProperty, StringComparison.Ordinal))
                meta.Add(new Member(property.Name, property.Value));

        var ports = section.EnumerateObject().Select(property => new Member(property.Name, property.Value)).ToList();
        return new LedgerDocument(view.PortsProperty, meta, ports);
    }

    private JsonDocument ParseFile(string path)
    {
        // 注释处理保持默认（Disallow）：台账是机器产物，出现 JSON 注释即口径漂移，须当场可见（不静默丢弃）。
        var parsed = JsonDocument.Parse(File.ReadAllText(path));
        _owners.Add(parsed);
        return parsed;
    }

    private static (string Name, JsonElement Value)[] Pairs(IReadOnlyList<Member> members)
        => [.. members.Select(member => (member.Name, member.Value))];
}
