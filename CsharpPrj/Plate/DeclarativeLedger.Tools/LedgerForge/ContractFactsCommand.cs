using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BasicMethod.Declarative.Tools.LedgerForge;

/// <summary>
/// 契约事实导出（A7）：由 C# 一次性抽取「生成常量 / kind 词表 / 槽键 / 分发相位」，PS 只与生成物对表。
/// <para>
/// 真源：<c>Config\**\*.ushar</c> 的 <c>[Const]</c>（经单点编译 → 已提交的 <c>declared-manifest.json</c>）
/// → 生成物 <c>GeneratedContractConstants.g.cs</c>。本命令同时做**新鲜度前置断言**：
/// 生成物成员与 manifest 逐项对账（Ordinal），不一致 ⇒ <c>constantsStale=true</c> + 差异清单，
/// 防止「读生成文件但生成文件已过期」的假绿（与 <c>agent.keyspace.generated.json</c> 同类风险）。
/// </para>
/// <para>退出码：0 已产出（stale 也照产出，由门禁裁决）；2 输入缺失/不可读。</para>
/// </summary>
internal static class ContractFactsCommand
{
    /// <summary>生成常量载体（相对 Agent 根）。</summary>
    internal const string ConstantsRelativePath = @"src\Agent.Contract\Generated\GeneratedContractConstants.g.cs";

    /// <summary>kind 词表 <c>All</c> 数组载体（相对 Agent 根）。</summary>
    internal const string RecognizedKindsRelativePath = @"src\Agent.Contract\NodeDomain\RecognizedKinds.cs";

    /// <summary>已提交声明清单（相对 Agent 根；生成常量器的唯一输入）。</summary>
    internal const string ManifestRelativePath = @"src\Agent.Core\Declared\declared-manifest.json";

    /// <summary>分发相位载体（相对仓库根）。</summary>
    internal const string DispatchTraceRelativePath = @"Frame\CsharpPrj\Modules\KeySpace\Dispatch\DispatchTrace.cs";

    private static readonly JsonSerializerOptions OutputOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly Regex NamespaceRegex = new(@"^namespace\s+([A-Za-z_][A-Za-z0-9_.]*)", RegexOptions.CultureInvariant);
    private static readonly Regex TypeRegex = new(@"public\s+.*partial\s+(?:class|record|struct)\s+([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.CultureInvariant);
    private static readonly Regex ConstRegex = new(@"public\s+const\s+[A-Za-z_][A-Za-z0-9_.<>?\[\]]*\s+([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<value>.+?);", RegexOptions.CultureInvariant);

    internal static int Run(string? repoRoot, string? agentRoot, string? outPath)
    {
        if (string.IsNullOrWhiteSpace(repoRoot) || string.IsNullOrWhiteSpace(agentRoot))
        {
            Console.Error.WriteLine("[ledger:forge] contract-facts 需要 --repo <仓库根> 与 --agent <Agent 根>");
            return 2;
        }

        var constantsPath = Path.Combine(agentRoot, ConstantsRelativePath);
        if (!File.Exists(constantsPath))
        {
            Console.Error.WriteLine($"[ledger:forge] contract-facts 缺生成常量: {constantsPath}");
            return 2;
        }

        string constantsText;
        string? kindsText;
        string? dispatchText;
        try
        {
            constantsText = File.ReadAllText(constantsPath);
            kindsText = ReadIfExists(Path.Combine(agentRoot, RecognizedKindsRelativePath));
            dispatchText = ReadIfExists(Path.Combine(repoRoot, DispatchTraceRelativePath));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ledger:forge] contract-facts 读取失败: {ex.Message}");
            return 2;
        }

        var types = ParseConstants(constantsText);
        var (stale, mismatches) = CompareWithManifest(Path.Combine(agentRoot, ManifestRelativePath), types);

        var recognizedMembers = FindTypeMembers(types, "RecognizedKinds");
        var recognizedKinds = MapAllArray(kindsText, recognizedMembers);
        var slotKeys = FindTypeMembers(types, "AgentRuntimeSlotKeys")
            .ToDictionary(pair => pair.Key, pair => TrimQuotes(pair.Value), StringComparer.Ordinal);
        var phases = ParseStringArray(dispatchText, "Phases");

        var payload = new ContractFactsPayload(
            "ledger:forge/contract-facts",
            Fingerprint(constantsPath, Path.Combine(agentRoot, RecognizedKindsRelativePath), Path.Combine(repoRoot, DispatchTraceRelativePath)),
            types.Count,
            stale,
            mismatches,
            recognizedKinds,
            recognizedMembers.TryGetValue("Unrecognized", out var unrecognized) ? TrimQuotes(unrecognized) : null,
            slotKeys,
            phases);

        if (string.IsNullOrWhiteSpace(outPath))
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(payload, OutputOptions));
        }
        else
        {
            File.WriteAllText(outPath, JsonSerializer.Serialize(payload, OutputOptions));
        }

        if (stale)
        {
            Console.Error.WriteLine($"[ledger:forge] 生成常量已过期（与 {ManifestRelativePath} 不一致，{mismatches.Count} 项）：请重跑 Generate-ContractConstants.ps1");
        }

        return 0;
    }

    private static string? ReadIfExists(string path) => File.Exists(path) ? File.ReadAllText(path) : null;

    /// <summary>逐行状态机解析生成常量：namespace → 类型全名 → <c>public const</c> 成员（值原文）。</summary>
    private static Dictionary<string, Dictionary<string, string>> ParseConstants(string text)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        string? currentNamespace = null;
        string? currentType = null;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var trimmed = line.Trim();

            if (trimmed.Length == 0 || trimmed.StartsWith("//", StringComparison.Ordinal)) continue;

            if (trimmed == "}")
            {
                if (line.Length == trimmed.Length) currentNamespace = null;
                else if (line.StartsWith("    }", StringComparison.Ordinal) && !line.StartsWith("        ", StringComparison.Ordinal)) currentType = null;
                continue;
            }

            var ns = NamespaceRegex.Match(trimmed);
            if (ns.Success)
            {
                currentNamespace = ns.Groups[1].Value;
                continue;
            }

            var type = TypeRegex.Match(trimmed);
            if (type.Success)
            {
                currentType = string.IsNullOrEmpty(currentNamespace)
                    ? type.Groups[1].Value
                    : currentNamespace + "." + type.Groups[1].Value;
                result.TryAdd(currentType, new Dictionary<string, string>(StringComparer.Ordinal));
                continue;
            }

            if (currentType is null) continue;

            var constant = ConstRegex.Match(trimmed);
            if (constant.Success && constant.Groups["value"].Value.Trim().Length > 0)
            {
                result[currentType][constant.Groups[1].Value] = constant.Groups["value"].Value.Trim();
            }
        }

        return result;
    }

    /// <summary>新鲜度前置断言：生成物成员 vs 已提交 manifest 的 <c>constants</c> 段（逐字 Ordinal）。</summary>
    private static (bool Stale, IReadOnlyList<string> Mismatches) CompareWithManifest(
        string manifestPath, Dictionary<string, Dictionary<string, string>> types)
    {
        var mismatches = new List<string>();
        if (!File.Exists(manifestPath))
        {
            return (true, new[] { $"manifest 缺失：{manifestPath}（无法断言生成常量是否过期）" });
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("constants", out var units)
                || units.ValueKind != JsonValueKind.Array)
            {
                return (true, new[] { "manifest 无 constants 段（无法断言生成常量是否过期）" });
            }

            var checkedMembers = 0;
            foreach (var unit in units.EnumerateArray())
            {
                if (!unit.TryGetProperty("target", out var targetElement) || targetElement.ValueKind != JsonValueKind.String) continue;
                var target = targetElement.GetString()!;
                if (!unit.TryGetProperty("members", out var members) || members.ValueKind != JsonValueKind.Array) continue;

                foreach (var member in members.EnumerateArray())
                {
                    if (!member.TryGetProperty("name", out var nameElement) || !member.TryGetProperty("value", out var valueElement)) continue;
                    var name = nameElement.GetString()!;
                    var expected = (valueElement.GetString() ?? string.Empty).Trim();
                    checkedMembers++;

                    if (!types.TryGetValue(target, out var actualMembers))
                    {
                        mismatches.Add($"{target}.{name}: 生成物缺该类型（expected=`{expected}`）");
                        continue;
                    }

                    if (!actualMembers.TryGetValue(name, out var actual))
                    {
                        mismatches.Add($"{target}.{name}: 生成物缺成员（expected=`{expected}`）");
                        continue;
                    }

                    if (!string.Equals(expected, actual, StringComparison.Ordinal))
                    {
                        mismatches.Add($"{target}.{name}: expected=`{expected}` actual=`{actual}`");
                    }
                }
            }

            if (checkedMembers == 0) mismatches.Add("manifest constants 段为空（新鲜度断言无标的）");
        }
        catch (Exception ex)
        {
            mismatches.Add($"manifest 不可解析：{ex.Message}");
        }

        mismatches.Sort(StringComparer.Ordinal);
        return (mismatches.Count > 0, mismatches);
    }

    private static Dictionary<string, string> FindTypeMembers(
        Dictionary<string, Dictionary<string, string>> types, string simpleName)
    {
        foreach (var pair in types)
        {
            var index = pair.Key.LastIndexOf('.');
            var simple = index < 0 ? pair.Key : pair.Key[(index + 1)..];
            if (string.Equals(simple, simpleName, StringComparison.Ordinal)) return pair.Value;
        }

        return new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private static IReadOnlyList<string> MapAllArray(string? kindsText, Dictionary<string, string> members)
    {
        if (string.IsNullOrEmpty(kindsText)) return Array.Empty<string>();

        var block = Regex.Match(kindsText, @"string\[\]\s+All\s*=\s*\{([\s\S]*?)\};", RegexOptions.CultureInvariant);
        if (!block.Success) return Array.Empty<string>();

        var kinds = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match identifier in Regex.Matches(block.Groups[1].Value, @"\b([A-Z][A-Za-z0-9_]*)\b", RegexOptions.CultureInvariant))
        {
            var name = identifier.Groups[1].Value;
            if (!members.TryGetValue(name, out var value)) continue;
            var kind = TrimQuotes(value);
            if (seen.Add(kind)) kinds.Add(kind);
        }

        return kinds;
    }

    private static IReadOnlyList<string> ParseStringArray(string? text, string memberName)
    {
        if (string.IsNullOrEmpty(text)) return Array.Empty<string>();

        var block = Regex.Match(text, @"\b" + Regex.Escape(memberName) + @"\s*=\s*new\[\]\s*\{([\s\S]*?)\};", RegexOptions.CultureInvariant);
        if (!block.Success) return Array.Empty<string>();

        return Regex.Matches(block.Groups[1].Value, @"""([^""]+)""", RegexOptions.CultureInvariant)
            .Select(m => m.Groups[1].Value).ToArray();
    }

    private static string TrimQuotes(string value)
        => value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;

    private static string Fingerprint(params string[] paths)
    {
        var builder = new StringBuilder();
        foreach (var path in paths)
        {
            builder.Append(path).Append(':');
            builder.Append(File.Exists(path) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) : "(missing)");
            builder.Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private sealed record ContractFactsPayload(
        string GeneratedBy,
        string SourcesFingerprint,
        int TypeCount,
        bool ConstantsStale,
        IReadOnlyList<string> ConstantMismatches,
        IReadOnlyList<string> RecognizedKinds,
        string? Unrecognized,
        IReadOnlyDictionary<string, string> AgentRuntimeSlotKeys,
        IReadOnlyList<string> DispatchTracePhases);
}
