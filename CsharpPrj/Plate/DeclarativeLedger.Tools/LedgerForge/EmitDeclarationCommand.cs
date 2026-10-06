using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace BasicMethod.Declarative.Tools.LedgerForge;

/// <summary>单键落点事实（台账 / golden 两侧）。</summary>
internal sealed record DeclarationKeyFacts(
    string Key,
    bool InLedger,
    string? Kind,
    string? Factory,
    bool HasScript,
    bool InGolden,
    IReadOnlyList<string> Missing);

/// <summary>族文件扫描事实：逐键落点 + 未登记标注 + 阻碍构造计数（工作量估算）。</summary>
internal sealed record FamilyFacts(
    string Path,
    string Family,
    int Lines,
    int Annotations,
    int Methods,
    IReadOnlyList<DeclarationKeyFacts> Keys,
    IReadOnlyList<string> Unregistered,
    IReadOnlyList<(string Label, int Count)> Obstructions);

/// <summary>
/// <c>emit-declaration</c>：从**夹具实现体 + 台账现状 + golden 快照**生成「声明化骨架草稿 + 缺失落点清单」，
/// 供 F3 逐键声明化使用（方案 §5.4 F3-1）。
/// <para>
/// 纪律（对齐既有 <c>Convert-CSharpToSpec.ps1 -Mode scaffold</c>）：**非严格模式** —— 可机推字段填好
/// （<c>in</c> 声明 → <c>bind</c> 骨架、<c>out.fields</c> → <c>productFields</c>、既有 <c>deliver</c> 沿用），
/// 不可机推处一律写**显式哨兵</b> <c>__BIND__</c>；草稿**禁止直接落台账**，须人工审定。
/// </para>
/// <para>缺省只打印（stdout）；<c>--out &lt;dir&gt;</c> 才落草稿与报告（产物为草稿，非台账）。</para>
/// </summary>
internal static class EmitDeclarationCommand
{
    /// <summary>不可机推的声明片段哨兵（草稿须人工审定）。</summary>
    private const string Sentinel = "__BIND__";

    private static readonly Regex Annotation = new(
        @"\[PortImplementation\(""(?<key>[^""]+)""\)\]", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex FamilyName = new(
        @"\[PortFamily\(""(?<name>[^""]+)""\)\]", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex MethodCount = new(
        @"(?m)^\s*private static ", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>阻碍构造计数（口径 = 方案附录 A3）。</summary>
    private static readonly (string Label, string Pattern)[] ObstructionPatterns =
    [
        ("GetAwaiter().GetResult()", @"GetAwaiter\(\)\.GetResult"),
        ("Func<", @"Func<"),
        ("ReferenceEquals", @"ReferenceEquals"),
        (" is ", @"\s+is\s+"),
        (" as ", @"\s+as\s+"),
        ("switch", @"\bswitch\b"),
        ("new SpecRunException", @"new\s+SpecRunException"),
    ];

    internal static int Run(LedgerView view, string familyPath, string? outDirectory)
    {
        var facts = Scan(view, familyPath);
        var report = BuildReport(view, facts);

        Console.WriteLine(report);
        if (outDirectory is not { Length: > 0 })
        {
            Console.WriteLine(view.Message("emitWriteHint"));
            return 0;
        }

        Directory.CreateDirectory(outDirectory);
        var stem = Path.GetFileNameWithoutExtension(familyPath);
        LedgerBytes.Write(Path.Combine(outDirectory, stem + ".declaration.draft.json"), BuildDraft(view, facts));
        LedgerBytes.Write(Path.Combine(outDirectory, stem + ".report.md"), report);
        Console.WriteLine(view.Message("emitWritten", outDirectory));
        return 0;
    }

    /// <summary>扫描夹具文件 + 台账现状 + golden 快照 → 逐键落点与阻碍构造事实。</summary>
    private static FamilyFacts Scan(LedgerView view, string familyPath)
    {
        if (!File.Exists(familyPath))
            throw new FileNotFoundException(string.Format(view.Message("forgeFileMissing"), familyPath));

        var text = File.ReadAllText(familyPath);
        var keys = Annotation.Matches(text).Select(match => match.Groups["key"].Value).ToArray();
        var golden = ReadGolden();
        var ledger = LedgerFacts(view, keys);
        var facts = new List<DeclarationKeyFacts>();

        foreach (var key in keys)
        {
            var found = ledger.TryGetValue(key, out var entry);
            var inGolden = golden.Contains(key, StringComparison.Ordinal);
            var missing = new List<string>();

            if (!found) missing.Add(view.Message("emitGapLedger"));
            else
            {
                if (entry!.Factory is { Length: > 0 } factory) missing.Add(view.Message("emitGapFactory", factory));
                if (!entry.HasScript) missing.Add(view.Message("emitGapScript"));
            }

            if (!inGolden) missing.Add(view.Message("emitGapGolden"));

            facts.Add(new DeclarationKeyFacts(
                key, found, entry?.Kind, entry?.Factory, entry?.HasScript ?? false, inGolden, missing));
        }

        var obstructions = ObstructionPatterns
            .Select(pattern => (pattern.Label, Count: new Regex(pattern.Pattern).Matches(text).Count))
            .ToArray();

        return new FamilyFacts(
            Path.GetFullPath(familyPath),
            FamilyName.Match(text) is { Success: true } family ? family.Groups["name"].Value : "<未标注>",
            text.Split('\n').Length,
            keys.Length,
            MethodCount.Matches(text).Count,
            facts,
            [.. keys.Where(key => !ledger.ContainsKey(key))],
            obstructions);
    }

    /// <summary>台账现状（仅取被标注的键；缺键不入表 ⇒ 由落点判据记为「台账键缺失」）。</summary>
    private static Dictionary<string, DeclarationKeyFacts> LedgerFacts(LedgerView view, IReadOnlyList<string> keys)
    {
        var map = new Dictionary<string, DeclarationKeyFacts>(StringComparer.Ordinal);
        if (!File.Exists(view.LedgerPath)) return map;

        using var ledger = JsonDocument.Parse(File.ReadAllText(view.LedgerPath));
        if (!ledger.RootElement.TryGetProperty(view.PortsProperty, out var ports)
            || ports.ValueKind != JsonValueKind.Object) return map;

        foreach (var key in keys)
        {
            if (!ports.TryGetProperty(key, out var port)) continue;

            var impl = port.TryGetProperty("impl", out var node) && node.ValueKind == JsonValueKind.Object ? node : default;

            map[key] = new DeclarationKeyFacts(
                key,
                true,
                impl.ValueKind == JsonValueKind.Object && impl.TryGetProperty("kind", out var kind) ? kind.GetString() : null,
                Factory(impl),
                impl.ValueKind == JsonValueKind.Object && impl.TryGetProperty("script", out _),
                false,
                []);
        }

        return map;
    }

    private static string? Factory(JsonElement impl)
    {
        if (impl.ValueKind != JsonValueKind.Object
            || !impl.TryGetProperty("deps", out var deps) || deps.ValueKind != JsonValueKind.Array) return null;

        foreach (var dep in deps.EnumerateArray())
            if (dep.ValueKind == JsonValueKind.Object
                && dep.TryGetProperty("factory", out var factory)
                && factory.GetString() is { Length: > 0 } name) return name;

        return null;
    }


    /// <summary>报告文本（纯 stdout 可读；<c>--out</c> 时同文落 <c>&lt;族&gt;.report.md</c>）。</summary>
    private static string BuildReport(LedgerView view, FamilyFacts facts)
    {
        var report = new StringBuilder();
        report.AppendLine(view.Message("emitTitle"));
        report.AppendLine(view.Message(
            "emitFamilyLine", facts.Path, facts.Family, facts.Annotations, facts.Methods, facts.Lines));

        report.AppendLine(view.Message("emitFactsHeader"));
        foreach (var key in facts.Keys)
            report.AppendLine(view.Message("emitFactsRow",
                key.Key, Flag(view, key.InLedger), key.Kind ?? "-", key.Factory ?? "-",
                Flag(view, key.HasScript), Flag(view, key.InGolden)));

        report.AppendLine(view.Message("emitMissingHeader"));
        var missing = facts.Keys.Where(key => key.Missing.Count != 0).ToArray();
        if (missing.Length == 0) report.AppendLine(view.Message("emitMissingNone"));
        else
            foreach (var key in missing)
                report.AppendLine(view.Message("emitMissingRow", key.Key, string.Join("；", key.Missing)));

        if (facts.Unregistered.Count != 0)
        {
            report.AppendLine(view.Message("emitUnregisteredHeader", facts.Unregistered.Count));
            foreach (var key in facts.Unregistered) report.AppendLine("  " + key);
        }

        report.AppendLine(view.Message("emitObstructionHeader"));
        foreach (var (label, count) in facts.Obstructions)
            report.AppendLine(view.Message("emitObstructionRow", label, count));

        report.AppendLine(view.Message("emitSkeletonHeader"));
        report.AppendLine(BuildDraft(view, facts));
        return report.ToString();
    }

    /// <summary>
    /// 声明骨架草稿：机推面填好（<c>in</c> → <c>bind</c>、既有 <c>productFields</c> / <c>deliver</c> 沿用），
    /// <c>script</c> 一律写哨兵 <c>__BIND__</c>（须人工设计）。
    /// </summary>
    private static string BuildDraft(LedgerView view, FamilyFacts facts)
    {
        var root = new JsonObject();
        if (!File.Exists(view.LedgerPath)) return root.ToJsonString(Indented);

        using var ledger = JsonDocument.Parse(File.ReadAllText(view.LedgerPath));
        if (!ledger.RootElement.TryGetProperty(view.PortsProperty, out var ports)
            || ports.ValueKind != JsonValueKind.Object) return root.ToJsonString(Indented);

        foreach (var fact in facts.Keys)
        {
            if (!ports.TryGetProperty(fact.Key, out var port) || port.ValueKind != JsonValueKind.Object) continue;

            var draft = new JsonObject { ["kind"] = fact.Kind ?? "scripted", ["id"] = fact.Key };

            if (port.TryGetProperty("in", out var inNode) && inNode.ValueKind == JsonValueKind.Object)
            {
                var bind = new JsonObject();
                foreach (var slot in inNode.EnumerateObject())
                    bind[slot.Name] = new JsonObject { ["from"] = "with", ["key"] = slot.Name, ["optional"] = true };

                if (bind.Count != 0) draft["bind"] = bind;
            }

            if (port.TryGetProperty("impl", out var impl) && impl.ValueKind == JsonValueKind.Object)
            {
                if (impl.TryGetProperty("productFields", out var fields)) draft["productFields"] = fields.GetString();
                if (impl.TryGetProperty("deliver", out var deliver)) draft["deliver"] = JsonNode.Parse(deliver.GetRawText());
            }

            draft["script"] = new JsonArray(Sentinel);
            root[fact.Key] = new JsonObject { ["impl"] = draft };
        }

        return root.ToJsonString(Indented);
    }

    /// <summary>golden 快照（找不到 → 空串 ⇒ 自证落点一律记为缺失，不静默通过）。</summary>
    private static string ReadGolden()
    {
        const string Relative = @"Environment\CentralTest\AgentTest\Agent.Spec.Guard.Tests\golden\ledger.declarations.json";
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, Relative);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            directory = directory.Parent;
        }

        return string.Empty;
    }

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private static string Flag(LedgerView view, bool value) => view.Message(value ? "emitYes" : "emitNo");
}
