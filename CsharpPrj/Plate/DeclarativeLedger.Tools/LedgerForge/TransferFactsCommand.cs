using System.Text.Json;
using System.Text.RegularExpressions;

namespace BasicMethod.Declarative.Tools.LedgerForge;

/// <summary>
/// 转移键事实导出（A3/D11）：声明面 <c>[Space]</c> 值集、生成契约常量、Codec 运行期词表/边界，
/// 全部在 C# 内抽取与 Ordinal 比较；PowerShell 只消费 JSON 事实，不再解析声明或复制常量。
/// <para>
/// 三个输入：
/// <list type="bullet">
/// <item><c>--ushar</c> 转移键声明面（[Space] TransferDirection / TransferKeyKind / TransferStatus）</item>
/// <item><c>--consts</c> 生成契约常量（TransferDirections / TransferKinds / TransferStatuses）</item>
/// <item><c>--codec</c> 唯一实现（Prefix / MaxSlotLength / Layers / Kinds）</item>
/// </list>
/// 退出码：0 事实一致；1 存在集合不等价或 Codec 事实缺失；2 输入不可读。
/// </summary>
internal static class TransferFactsCommand
{
    private static readonly JsonSerializerOptions OutputOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly (string Space, string Constants)[] Spaces =
    {
        ("TransferDirection", "TransferDirections"),
        ("TransferKeyKind", "TransferKinds"),
        ("TransferStatus", "TransferStatuses"),
    };

    internal static int Run(string? usharPath, string? constsPath, string? codecPath, string? outPath)
    {
        foreach (var path in new[] { usharPath, constsPath, codecPath })
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                Console.Error.WriteLine($"[ledger:forge] transfer-facts 输入缺失: {path}");
                return 2;
            }
        }

        var usharText = File.ReadAllText(usharPath!);
        var constsText = File.ReadAllText(constsPath!);
        var codecText = File.ReadAllText(codecPath!);

        var realized = new List<SpaceFacts>();
        var mismatches = new List<string>();
        foreach (var (space, constants) in Spaces)
        {
            var declared = ReadUsharpSpaceValues(usharText, space);
            var code = ReadCSharpConstValues(constsText, constants);
            realized.Add(new SpaceFacts(space, constants, declared, code));

            var declaredSet = new HashSet<string>(declared, StringComparer.Ordinal);
            var codeSet = new HashSet<string>(code, StringComparer.Ordinal);
            foreach (var value in declaredSet.Where(value => !codeSet.Contains(value)))
                mismatches.Add($"{space}: 声明独有 '{value}'（C# {constants} 缺失）");
            foreach (var value in codeSet.Where(value => !declaredSet.Contains(value)))
                mismatches.Add($"{space}: C# 独有 '{value}'（声明面缺失）");
        }

        var prefixMatch = Regex.Match(codecText, @"\bPrefix\s*=\s*""([^""]+)""");
        var maxSlotMatch = Regex.Match(codecText, @"MaxSlotLength\s*=\s*(\d+)");
        var layers = ReadStringArray(codecText, "Layers");
        var kinds = ReadStringArray(codecText, "Kinds");

        if (!prefixMatch.Success) mismatches.Add("Codec 缺 Prefix 常量");
        if (!maxSlotMatch.Success) mismatches.Add("Codec 缺 MaxSlotLength 常量");
        if (layers.Count == 0) mismatches.Add("Codec 缺 Layers 词表");
        if (kinds.Count == 0) mismatches.Add("Codec 缺 Kinds 词表");

        var codecFacts = new CodecFacts(
            prefixMatch.Success ? prefixMatch.Groups[1].Value : string.Empty,
            maxSlotMatch.Success ? int.Parse(maxSlotMatch.Groups[1].Value) : 0,
            layers,
            kinds);

        var payload = new TransferFacts(mismatches.Count == 0, realized, codecFacts, mismatches);
        var json = JsonSerializer.Serialize(payload, OutputOptions);
        if (string.IsNullOrWhiteSpace(outPath))
        {
            Console.Out.WriteLine(json);
        }
        else
        {
            File.WriteAllText(outPath, json);
        }

        return mismatches.Count == 0 ? 0 : 1;
    }

    /// <summary>[Space] 块：以块头为起点，遇下一个 [Space] 或 EOF 结束；忽略空行与 # 注释。</summary>
    private static List<string> ReadUsharpSpaceValues(string text, string spaceName)
    {
        var values = new List<string>();
        var inSpace = false;
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (Regex.IsMatch(line, @"^\s*\[Space\]\s+" + Regex.Escape(spaceName) + @"\b"))
            {
                inSpace = true;
                continue;
            }

            if (!inSpace) continue;
            if (Regex.IsMatch(line, @"^\s*\[Space\]")) break;

            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
            values.Add(trimmed);
        }

        return values;
    }

    /// <summary>生成常量：partial class 实现体里的 public const string 值（与运行期载体一致）。</summary>
    private static List<string> ReadCSharpConstValues(string text, string className)
    {
        var classMatch = Regex.Match(
            text,
            @"(?s)class\s+" + Regex.Escape(className) + @"\s*\{(?<body>.*?)\n\s*\}",
            RegexOptions.CultureInvariant);
        if (!classMatch.Success) return new List<string>();

        return Regex.Matches(classMatch.Groups["body"].Value, @"public\s+const\s+string\s+\w+\s*=\s*""([^""]+)""")
            .Select(match => match.Groups[1].Value)
            .ToList();
    }

    /// <summary>Codec 的 <c>private static readonly string[] X = { "a", "b" }</c> 数组。</summary>
    private static List<string> ReadStringArray(string text, string fieldName)
    {
        var match = Regex.Match(
            text,
            @"(?s)" + Regex.Escape(fieldName) + @"\s*=\s*\{[^\}]*\}");
        if (!match.Success) return new List<string>();

        return Regex.Matches(match.Value, @"""([^""]+)""")
            .Select(item => item.Groups[1].Value)
            .ToList();
    }

    private sealed record SpaceFacts(
        string Space,
        string Constants,
        IReadOnlyList<string> Declared,
        IReadOnlyList<string> Code);

    private sealed record CodecFacts(string Prefix, int MaxSlotLength, IReadOnlyList<string> Layers, IReadOnlyList<string> Kinds);

    private sealed record TransferFacts(
        bool Ok,
        IReadOnlyList<SpaceFacts> Spaces,
        CodecFacts Codec,
        IReadOnlyList<string> Mismatches);
}
