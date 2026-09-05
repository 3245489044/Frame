using System.Text;
using Plate.DslMethod.Abstractions;
using Plate.DslMethod.Model;
using Plate.DslMethod.Parsing;

namespace Plate.DslMethod.CodeGen;

/// <summary>反转生成选项。</summary>
public sealed record AxiomCodeGeneratorOptions
{
    /// <summary>仅生成白名单内公理（§6 与 Agent.Core 直接相关的子集）；为空 = 全量。</summary>
    public IReadOnlySet<string>? IncludeNames { get; init; }

    /// <summary>产出映射报告（prompt文件+行 → 公理 → 层级）。</summary>
    public bool EmitMappingReport { get; init; } = true;

    /// <summary>显式层级覆盖（符号名 → 目标层级）。同一符号在多个契约文件/层级出现时，以覆盖值为准；
    /// 未覆盖时取各候选层级的**最小**值（L0&lt;L0.5&lt;L1&lt;L2&lt;L3，最基础/引擎层优先）。</summary>
    public IReadOnlyDictionary<string, AxiomLevel>? LevelOverrides { get; init; }

    /// <summary>显式轴覆盖（符号名 → 目标轴）。优先级低于声明内 `@axis` 标注，高于名字启发式。</summary>
    public IReadOnlyDictionary<string, string>? AxisOverrides { get; init; }
}

/// <summary>一条已生成公理的产物记录。</summary>
public sealed record GeneratedAxiom(string Name, AxiomLevel Level, string SourceFile, string OutputPath);

/// <summary>
/// 公理代码生成器：读取 prompts/*.md（权威契约），按 §7 层级映射规则发射
/// <c>Axioms/{Level}/{Name}.cs</c> 公理类（IAxiom + [Axiom]）。反转生成、静态产物提交入库。
/// </summary>
public sealed class AxiomCodeGenerator
{
    public List<GeneratedAxiom> Generate(IReadOnlyList<string> contractSourceFiles, string outDir, AxiomCodeGeneratorOptions? options = null)
    {
        var opts = options ?? new AxiomCodeGeneratorOptions();
        var result = new List<GeneratedAxiom>();
        var mapping = new List<string>();

        // 收集全部候选声明，按符号名分组（同一符号可能声明于多契约文件/层级）
        var candidates = new Dictionary<string, List<(DslDeclaration Decl, AxiomLevel Level)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in contractSourceFiles)
        {
            var doc = DslParser.Parse(StripSkillFrontMatter(File.ReadAllText(file)), file, Path.GetFileNameWithoutExtension(file));
            foreach (var decl in doc.Declarations)
            {
                if (!IsAxiomSymbol(decl.Kind) || string.IsNullOrWhiteSpace(decl.Name)) continue;
                var level = LevelMappingRules.Resolve(doc.FilePath, SectionTitleOf(doc, decl.LineNumber), decl.Prefix);
                if (!candidates.TryGetValue(decl.Name, out var list))
                {
                    list = new List<(DslDeclaration, AxiomLevel)>();
                    candidates[decl.Name] = list;
                }
                list.Add((decl, level));
            }
        }

        // 每符号只生成一个公理类（去重）：canonical 层级 = 显式@level > 覆盖值 > 候选最小值
        foreach (var (name, list) in candidates.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (opts.IncludeNames is not null && !opts.IncludeNames.Contains(name)) continue;

            // 显式 @level:@L2 标注优先（自描述元数据，替代节标题/文件名启发式）
            var explicitDecl = list.Select(c => c.Decl).FirstOrDefault(d => TryExplicitLevel(d, out _));
            var canonical = explicitDecl is not null && TryExplicitLevel(explicitDecl, out var explLevel)
                ? explLevel
                : CanonicalLevel(name, list, opts);
            // canonical 必为某候选值（Min）或覆盖值；覆盖值（如 LevelOverrides 指定 L1）可能无同层候选 →
            // 回退到最小值候选（CanonicalLevel 缺省语义），避免 First 落空抛 "Sequence contains no matching element"。
            // 注：候选为值元组（FirstOrDefault 空时 Decl 为 null），故对可空 Decl 判空回退。
            var chosen = explicitDecl is not null
                ? explicitDecl
                : (list.FirstOrDefault(c => c.Level == canonical).Decl ?? list.MinBy(c => c.Level).Decl);

            var axis = AxisOf(chosen, name, opts);
            var content = Render(chosen, canonical, axis);

            var dir = Path.Combine(outDir, canonical.ToFolder());
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, StringValueParser.SanitizeIdentifier(name) + ".cs");
            File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            result.Add(new GeneratedAxiom(name, canonical, chosen.SourceFile, path));
            mapping.Add($"{chosen.SourceFile}:{chosen.LineNumber}\t{name}\t{canonical.ToDisplay()}\t{axis}");
        }

        if (opts.EmitMappingReport)
        {
            var reportPath = Path.Combine(outDir, "mapping-report.txt");
            // 按符号名合并去重：--include 定向生成时仅新增/更新目标行，既有条目原样保留（幂等）。
            var merged = new SortedDictionary<string, string>(StringComparer.Ordinal);
            if (File.Exists(reportPath))
                foreach (var line in File.ReadAllLines(reportPath))
                {
                    var parts = line.Split('\t');
                    if (parts.Length >= 2 && !string.IsNullOrWhiteSpace(parts[1]))
                        merged[parts[1].Trim()] = line; // key = 符号名（tab 第 2 段）
                }
            foreach (var m in mapping) // 新生成条目按名覆盖旧条目（含层级/轴更新）
            {
                var parts = m.Split('\t');
                if (parts.Length >= 2 && !string.IsNullOrWhiteSpace(parts[1]))
                    merged[parts[1].Trim()] = m;
            }
            if (merged.Count > 0)
                File.WriteAllLines(reportPath, merged.Values, Encoding.UTF8);
        }

        return result;
    }

    /// <summary>剥离 SKILL.md 前导 YAML front-matter（首个 `---` 至第二个 `---` 之间）；非 front-matter 文本原样返回。
    /// 契约源统一为 skill 正文（dsl: 承载），构建期按此取 DSL 原文，与运行期 PromptDslSource 同源。</summary>
    private static string StripSkillFrontMatter(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        if (lines.Length == 0 || !string.Equals(lines[0].Trim(), "---", StringComparison.Ordinal))
            return text;
        int end = -1;
        for (int i = 1; i < lines.Length; i++)
        {
            if (string.Equals(lines[i].Trim(), "---", StringComparison.Ordinal)) { end = i; break; }
        }
        return end < 0 ? text : string.Join("\n", lines.Skip(end + 1));
    }

    /// <summary>canonical 层级：覆盖值优先；否则取候选最小值（L0&lt;L0.5&lt;L1&lt;L2&lt;L3，最基础/引擎层优先）。</summary>
    private static AxiomLevel CanonicalLevel(string name, List<(DslDeclaration, AxiomLevel)> list, AxiomCodeGeneratorOptions opts)
    {
        if (opts.LevelOverrides is not null && opts.LevelOverrides.TryGetValue(name, out var overrideLevel))
            return overrideLevel;
        return list.Min(c => c.Item2);
    }

    /// <summary>要生成公理类的符号种类：公理 / 策略 / 门控 / 状态机 / 算子。</summary>
    private static bool IsAxiomSymbol(DslSymbolKind kind) => kind switch
    {
        DslSymbolKind.Axiom => true,
        DslSymbolKind.Strategy => true,
        DslSymbolKind.Gate => true,
        DslSymbolKind.Sm => true,
        DslSymbolKind.Lo => true,
        _ => false,
    };

    private static string Render(DslDeclaration decl, AxiomLevel level, string axis)
    {
        var ns = "Plate.Axioms." + level.ToFolder();
        var typeName = StringValueParser.SanitizeIdentifier(decl.Name);
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("// 反转生成：prompts/*.md → 公理代码库（AxiomCodeGenerator）。禁止手工编辑；变更须作用于契约源后重生成。");
        sb.AppendLine("// 契约源文件映射集中登记于本目录 mapping-report.txt（源契约 → 公理 → 层级 → 轴）。");
        sb.AppendLine();
        sb.AppendLine("using Plate.DslMethod.Abstractions;");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine($"/// <summary>公理 {decl.Name}（层级 {level.ToDisplay()}，轴 {axis}）。源契约映射见 mapping-report.txt</summary>");
        sb.AppendLine($"[Axiom(AxiomLevel.{level}, \"{decl.Name}\", Axis = \"{axis}\")]");
        sb.AppendLine($"public sealed class {typeName} : IAxiom");
        sb.AppendLine("{");
        sb.AppendLine($"    public string AxiomName => \"{decl.Name}\";");
        sb.AppendLine("}");
        return sb.ToString();
    }

    /// <summary>离当前声明行最近的已出现节标题。</summary>
    private static string SectionTitleOf(DslDocument doc, int lineNo)
    {
        DslSection? cur = null;
        foreach (var s in doc.Sections)
        {
            if (s.LineNumber > lineNo) break;
            cur = s;
        }
        return cur?.Title ?? "";
    }

    /// <summary>公理名 → 轴。优先级：声明内显式 @axis > AxisOverrides > 名字启发式。</summary>
    private static string AxisOf(DslDeclaration decl, string name, AxiomCodeGeneratorOptions opts)
    {
        var explicitAxis = TryExplicitAxis(decl);
        if (explicitAxis is not null) return explicitAxis;
        if (opts.AxisOverrides?.TryGetValue(name, out var a) == true && !string.IsNullOrWhiteSpace(a)) return a;
        return AxisOf(name);
    }

    /// <summary>解析声明行内显式 `@axis:xxx` 标注（自描述元数据，向后兼容）；未标注返回 null。</summary>
    private static string? TryExplicitAxis(DslDeclaration decl)
    {
        var m = System.Text.RegularExpressions.Regex.Match(decl.RawLine, @"@axis\s*:\s*([\w-]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return m.Success && m.Groups[1].Value.Length > 0 ? m.Groups[1].Value : null;
    }

    /// <summary>解析声明行内显式 `@level:xxx` 标注；成功且可解析返回 true。</summary>
    private static bool TryExplicitLevel(DslDeclaration decl, out AxiomLevel level)
    {
        var m = System.Text.RegularExpressions.Regex.Match(decl.RawLine, @"@level\s*:\s*([\w.]*)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (m.Success && AxiomLevelExtensions.TryParse(m.Groups[1].Value, out level)) return true;
        level = default;
        return false;
    }

    /// <summary>公理名 → 轴（启发式，供 §6 四簇归轴）。语义关键字优先于字面子串，避免 "information" 误中 "format"。</summary>
    private static string AxisOf(string name)
    {
        if (name.Contains("Boundary", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Spatial", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Pressure", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Scope", StringComparison.OrdinalIgnoreCase)) return "boundary";
        if (name.Contains("Termination", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Round", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Decision", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Evaluation", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Classify", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Entry", StringComparison.OrdinalIgnoreCase)) return "round";
        if (name.Contains("Information", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Implementation", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Reflection", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Redundancy", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Strategy", StringComparison.OrdinalIgnoreCase)) return "infra";
        if (name.Contains("Output", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Format", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Transient", StringComparison.OrdinalIgnoreCase)
            || name.Contains("FirstCall", StringComparison.OrdinalIgnoreCase)) return "output";
        return "infra";
    }

}
