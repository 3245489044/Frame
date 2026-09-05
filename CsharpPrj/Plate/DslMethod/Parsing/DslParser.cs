using Plate.DslMethod.Model;

namespace Plate.DslMethod.Parsing;

/// <summary>
/// DSL 声明解析器。把 clinerules / 契约文件 / 提示词协议 DSL 逐行解析为 <see cref="DslDocument"/>。
/// 规则（对齐 dsl.md §1 + 统一 DSL 解析器与双模型投影方案）：
///  - 注册前缀 `[X]`（见 DslConstants.Prefixes）与特殊行 `%`/`@`/`# TH-`/`$`/`!`；
///  - 名称 = 前缀后至首个 `:`；值 = 首个 `|` 之前；`k=v` 分段为结构化字段；
///  - `[Gate]`/`[Bind]`/`[SM]` 后紧邻的缩进行并入 Continuation（字段/引用合并）；
///  - fenced 模板通道：[SM]/[Template] 头后 ``` 块 → Template + `| slots:` → Slots；
///  - [Gate] 条件通道：缩进行 field=…, op=…, value=[…] → Conditions（不污染 Fields）；
///  - [Schema] 通道：头后缩进块拼接为 SchemaBlock（吸收 FormatSpecParser 规则）；
///  - ``` 其余 fenced 块跳过；##/### 记为章节；# TH- 为推论声明。
/// </summary>
public static class DslParser
{
    /// <summary>注册前缀按长度降序缓存（最长优先：[AX] vs [AX-CORE]，一次性排序避免逐行重排）。</summary>
    private static readonly DslPrefixDef[] OrderedPrefixes =
        DslConstants.Prefixes.OrderByDescending(p => p.Prefix.Length).ToArray();

    public static DslDocument Parse(string text, string sourceFile, string lib)
    {
        var doc = new DslDocument { FilePath = sourceFile, Lib = lib };
        if (string.IsNullOrEmpty(text)) return doc;

        var lines = text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
        var sections = new List<DslSection>();
        int i = 0;

        while (i < lines.Length)
        {
            var line = lines[i];
            var trimmed = line.Trim();
            if (trimmed.Length == 0) { i++; continue; }

            // fenced 模板块：跳过至闭 fence
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                i++;
                while (i < lines.Length && !lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal)) i++;
                i++;
                continue;
            }

            // 章节标题（## / ### / # 非推论）
            if (trimmed.StartsWith("###", StringComparison.Ordinal) ||
                trimmed.StartsWith("##", StringComparison.Ordinal))
            {
                sections.Add(new DslSection { Title = trimmed, LineNumber = i + 1 });
                i++;
                continue;
            }
            if (trimmed.StartsWith("# TH-", StringComparison.Ordinal))
            {
                var decl = MakeSpecial(trimmed, sourceFile, i + 1, DslSymbolKind.Theorem, "# TH-", ":");
                if (decl != null) doc.Declarations.Add(decl);
                i++;
                continue;
            }
            if (trimmed.StartsWith("# ", StringComparison.Ordinal))
            {
                sections.Add(new DslSection { Title = trimmed, LineNumber = i + 1 });
                i++;
                continue;
            }

            var decl2 = TryParseDeclaration(trimmed, sourceFile, i + 1);
            if (decl2 != null)
            {
                // ① fenced 模板通道：[SM]/[Template] 头后 ``` 块 → Template + 紧随 | slots: 行 → Slots
                if (decl2.Kind is DslSymbolKind.Sm or DslSymbolKind.Template)
                {
                    var tmpl = ReadFencedTemplate(lines, i + 1);
                    if (tmpl.Found)
                    {
                        decl2.Template = tmpl.Content;
                        decl2.Slots.AddRange(tmpl.Slots);
                        i = tmpl.Next;
                        doc.Declarations.Add(decl2);
                        RegisterViews(doc, decl2);
                        continue;
                    }
                }
                // ② [Schema] 通道：头后缩进块（跳空行）拼接为 SchemaBlock，并并作字段续行
                else if (decl2.Kind == DslSymbolKind.Schema)
                {
                    var (block, next) = ReadSchemaBlock(lines, i + 1);
                    decl2.SchemaBlock = block;
                    for (var k = i + 1; k < next; k++)
                        if (!string.IsNullOrWhiteSpace(lines[k])) decl2.Continuation.Add(lines[k].Trim());
                    if (decl2.Continuation.Count > 0) ApplyContinuation(decl2, decl2.Continuation);
                    i = next;
                    doc.Declarations.Add(decl2);
                    RegisterViews(doc, decl2);
                    continue;
                }

                // ③ 通用续行：紧随的缩进非空行并入（Gate/Bind 字段续行 / SM 状态机步骤）
                var cont = new List<string>();
                int j = i + 1;
                while (j < lines.Length)
                {
                    var l = lines[j];
                    if (string.IsNullOrWhiteSpace(l)) break;
                    if (l.Length == l.TrimStart().Length) break; // 非缩进
                    cont.Add(l.Trim());
                    j++;
                }
                if (cont.Count > 0)
                {
                    decl2.Continuation.AddRange(cont);
                    ApplyContinuation(decl2, cont);
                    i = j;
                }
                else
                {
                    i++;
                }
                doc.Declarations.Add(decl2);
                RegisterViews(doc, decl2);
                continue;
            }

            i++;
        }

        doc.Sections = sections;
        return doc;
    }

    private static DslDeclaration? TryParseDeclaration(string line, string file, int lineNo)
    {
        // 注册前缀，最长优先（[AX] vs [AX-CORE]），顺序已缓存
        foreach (var pd in OrderedPrefixes)
        {
            if (line.StartsWith(pd.Prefix, StringComparison.Ordinal))
            {
                var rest = line[pd.Prefix.Length..];
                if (rest.Length == 0) continue;
                var idx = rest[0] == ' ' || rest[0] == '\t' ? 1 : 0;
                rest = rest[idx..].TrimStart();
                var colon = rest.IndexOf(':');
                string name, after;
                if (colon >= 0) { name = rest[..colon].Trim(); after = rest[(colon + 1)..]; }
                else { name = rest; after = ""; }
                if (name.Length == 0) continue;
                return Build(name, pd, after, line, file, lineNo);
            }
        }

        // 特殊行前缀
        if (line.StartsWith("% ", StringComparison.Ordinal))
        {
            var eq = line.IndexOf('=');
            if (eq > 0)
            {
                var name = line[2..eq].Trim();
                if (name.Length == 0) return null;
                return Build(name, new DslPrefixDef("%", DslSymbolKind.Param, "Param"), line[(eq + 1)..], line, file, lineNo);
            }
            return null;
        }
        if (line.StartsWith("@ ", StringComparison.Ordinal))
        {
            var colon = line.IndexOf(':');
            var name = colon >= 0 ? line[2..colon].Trim() : line[2..].Trim();
            if (name.Length == 0) return null;
            return Build(name, new DslPrefixDef("@", DslSymbolKind.AntiPattern, "ANTI"),
                colon >= 0 ? line[(colon + 1)..] : "", line, file, lineNo);
        }
        if (line.StartsWith("$ ", StringComparison.Ordinal))
        {
            var eq = line.IndexOf('=');
            var name = eq > 0 ? line[2..eq].Trim() : line[2..].Trim();
            if (name.Length == 0) return null;
            return Build(name, new DslPrefixDef("$", DslSymbolKind.Symbol, "Symbol"),
                eq > 0 ? line[(eq + 1)..] : "", line, file, lineNo);
        }
        if (line.StartsWith("! ", StringComparison.Ordinal))
        {
            var colon = line.IndexOf(':');
            var name = colon >= 0 ? line[2..colon].Trim() : "axiom-block";
            if (name.Length == 0) name = "axiom-block";
            return Build(name, new DslPrefixDef("!", DslSymbolKind.AxiomBlock, "AX-BLOCK"),
                colon >= 0 ? line[(colon + 1)..] : "", line, file, lineNo);
        }
        return null;
    }

    private static DslDeclaration? MakeSpecial(string line, string file, int lineNo, DslSymbolKind kind, string marker, string nameSep)
    {
        // 推论等：marker 后至 nameSep 为名
        var rest = line[marker.Length..].TrimStart();
        var colon = rest.IndexOf(nameSep);
        var name = colon >= 0 ? rest[..colon].Trim() : rest;
        if (name.Length == 0) return null;
        var stub = kind == DslSymbolKind.Theorem ? "TH" : kind.ToString();
        return Build(name, new DslPrefixDef(marker, kind, stub), colon >= 0 ? rest[(colon + 1)..] : "", line, file, lineNo);
    }

    private static DslDeclaration Build(string name, DslPrefixDef pd, string afterColon, string line, string file, int lineNo)
    {
        var decl = new DslDeclaration
        {
            Prefix = pd.Prefix,
            Kind = pd.Kind,
            Stub = pd.Stub,
            Name = name,
            SourceFile = file,
            LineNumber = lineNo,
            RawLine = line,
        };

        var parts = FieldParser.SplitOnPipe(afterColon);
        decl.Fields.AddRange(FieldParser.Parse(parts, out var body));
        decl.Body = body;
        decl.References.AddRange(CrReferenceParser.ParseAll(line, lineNo));
        return decl;
    }

    private static void ApplyContinuation(DslDeclaration decl, List<string> cont)
    {
        foreach (var cl in cont)
        {
            // [Gate] replan 条件通道：field=…, op=…, value=[…] 缩进行 → Conditions（非 k=v 字段，避免污染 Fields）
            if (decl.Kind == DslSymbolKind.Gate && TryParseConditionLine(cl, out var cond) && cond != null)
            {
                decl.Conditions.Add(cond);
                decl.References.AddRange(CrReferenceParser.ParseAll(cl, decl.LineNumber));
                continue;
            }

            foreach (var part in FieldParser.SplitOnPipe(cl))
            {
                var kv = FieldParser.TryParsePart(part);
                if (kv.HasValue &&
                    !decl.Fields.Any(f => string.Equals(f.Key, kv.Value.Key, StringComparison.OrdinalIgnoreCase)))
                {
                    decl.Fields.Add(kv.Value);
                }
            }
            decl.References.AddRange(CrReferenceParser.ParseAll(cl, decl.LineNumber));
        }
    }

    /// <summary>把节点登记到文档便捷视图（Templates/Params/Schemas）。</summary>
    private static void RegisterViews(DslDocument doc, DslDeclaration decl)
    {
        if (decl.Kind == DslSymbolKind.Schema) doc.Schemas.Add(decl);
        else if (decl.Kind == DslSymbolKind.Param) doc.Params.Add(decl);
        else if (decl.Kind is DslSymbolKind.Sm or DslSymbolKind.Template) doc.Templates.Add(decl);
    }

    /// <summary>读取 fenced 模板块（``` ... ```）+ 紧随 `| slots:` 行（吸收协议 DSL ReadFencedBlock 语义）。</summary>
    private static (bool Found, string Content, List<string> Slots, int Next) ReadFencedTemplate(string[] lines, int start)
    {
        const string fence = "```";
        int j = start;
        while (j < lines.Length && string.IsNullOrWhiteSpace(lines[j])) j++;
        if (j >= lines.Length || !lines[j].TrimStart().StartsWith(fence, StringComparison.Ordinal))
            return (false, "", new List<string>(), start);

        var content = new List<string>();
        j++; // 跳过开 fence
        while (j < lines.Length && !lines[j].Trim().StartsWith(fence, StringComparison.Ordinal))
        {
            content.Add(lines[j].TrimEnd('\r'));
            j++;
        }
        j++; // 跳过闭 fence

        var slots = new List<string>();
        int k = j;
        while (k < lines.Length && string.IsNullOrWhiteSpace(lines[k])) k++;
        if (k < lines.Length && lines[k].TrimStart().StartsWith("| slots:", StringComparison.Ordinal))
        {
            const string marker = "| slots:";
            int idx = lines[k].IndexOf(marker, StringComparison.Ordinal);
            if (idx >= 0) slots.AddRange(SplitRespectingBrackets(lines[k].Substring(idx + marker.Length)));
            k++;
        }

        return (true, string.Join("\n", content).TrimEnd('\n'), slots, Math.Max(k, j));
    }

    /// <summary>收集 [Schema] 头后缩进续行（跳空行），拼接为一个逻辑声明串（吸收 FormatSpecParser.ReadSchemaBlock）。</summary>
    private static (string Block, int Next) ReadSchemaBlock(string[] lines, int start)
    {
        var parts = new List<string>();
        int j = start;
        while (j < lines.Length)
        {
            var line = lines[j];
            if (string.IsNullOrWhiteSpace(line)) { j++; continue; } // 块内空行跳过
            if (line.Length > 0 && char.IsWhiteSpace(line[0])) { parts.Add(line.Trim()); j++; continue; }
            break; // 非缩进行结束块
        }
        return (string.Join(" ", parts).Trim(), j);
    }

    /// <summary>解析 [Gate] replan 条件行（field=…, op=…, value=[…]）；非条件行返回 false。</summary>
    private static bool TryParseConditionLine(string line, out Condition? cond)
    {
        cond = null;
        var kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in SplitRespectingBrackets(line))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0) continue;
            kv[part[..eq].Trim()] = part[(eq + 1)..].Trim();
        }
        if (!kv.ContainsKey("value")) return false; // 非条件行（无 value=[…]）

        cond = new Condition
        {
            Field = kv.TryGetValue("field", out var f) && f.Length > 0 ? f : null,
            Op = kv.TryGetValue("op", out var op) && op.Length > 0 ? op : "contains_any",
        };
        var valueText = kv.TryGetValue("value", out var vt) ? vt : "";
        valueText = valueText.Trim('[', ']');
        cond.Value.AddRange(SplitRespectingBrackets(valueText));
        return true;
    }

    /// <summary>按逗号切分，但尊重 [...] 方括号与字符串内容（value=[a,b,c] 内的逗号不作为分隔符）。</summary>
    private static List<string> SplitRespectingBrackets(string input)
    {
        var result = new List<string>();
        var cur = new System.Text.StringBuilder();
        int bracketDepth = 0;
        foreach (char c in input)
        {
            if (c == '[') { bracketDepth++; cur.Append(c); }
            else if (c == ']' && bracketDepth > 0) { bracketDepth--; cur.Append(c); }
            else if (c == ',' && bracketDepth == 0)
            {
                var item = cur.ToString().Trim();
                if (item.Length > 0) result.Add(item);
                cur.Clear();
            }
            else cur.Append(c);
        }
        var last = cur.ToString().Trim();
        if (last.Length > 0) result.Add(last);
        return result;
    }
}

