using BasicMethod.Declarative.Ledger;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Text.Json;
using DataCenter.Protocol.Standards;
using DataCenter.Protocol.Standards.Descriptors;
using Plate.JsonMethod.Utilities;

namespace DataCenter.Standards;

/// <summary>
/// 结构门词表同步器：schema 内 <c>enum</c> 由**唯一 C# 源**（<see cref="PortImplVocabulary"/>）机械派生。
/// <para>
/// ① <see cref="Verify"/> 只读对账（空 = 全绿）；② <see cref="Sync"/> 幂等写回，**只替换 enum 数组的原始
/// 文本范围**（其余字节与排版不动），仅在显式调用或守卫内运行——**装载期绝不写盘**。
/// </para>
/// </summary>
public static class SchemaEnumSynchronizer
{
    /// <summary>
    /// 「词表 → 结构门 <c>enum</c>」绑定：某处 enum 由某份词表**机械派生**的登记项。
    /// <paramref name="Pointer"/> 指向 enum 节点（<c>/</c> 分隔键路径，形如 <c>$defs/impl/properties/kind/enum</c>）。
    /// </summary>
    public sealed record EnumBinding(string SchemaFile, string Pointer, Func<IReadOnlyList<string>> Vocabulary);

    private static readonly ConcurrentDictionary<string, Func<IReadOnlyList<string>>> VocabularyAccessors = new(StringComparer.Ordinal);

    /// <summary>词表绑定表（**由 <see cref="StandardDescriptors"/> 派生**，取代手抄指针表）。</summary>
    private static readonly EnumBinding[] Bindings = BuildBindings();

    /// <summary>
    /// 由描述符派生绑定：每条 = 某层**带 <c>Vocabulary</c> 成员**的 <c>JsonKey</c> →
    /// 其 <c>Vocabulary</c>（<see cref="PortImplVocabulary"/> 的静态字段/属性）。
    /// <para>指针 = <c>$defs/{Layer}/properties/{JsonKey}/enum</c>（<c>expr</c> 层例外见下）。</para>
    /// <para><c>expr</c> 层**扁平并入**取值来源键（<c>from</c>/<c>convert</c>/… 与来源侧同形），
    /// 故其来源侧 enum（<c>from</c>/<c>convert</c>/<c>onBlankFirstKey</c>/<c>onDuplicate</c>）亦列于 <c>expr</c>——
    /// 与 <c>valueSource</c> 层同词表。</para>
    /// <para>非层绑定（<c>fakes</c> 台账：<c>properties/fakes/additionalProperties/properties/kind</c>）无独立层描述，
    /// 显式登记（唯一手写残留）。</para>
    /// </summary>
    private static EnumBinding[] BuildBindings()
    {
        var bindings = new List<EnumBinding>();
        foreach (var layer in StandardDescriptors.All) AppendLayer(bindings, layer.Layer, layer);

        // expr：扁平并入 valueSource 的取值来源键 → 来源侧 enum 亦列于 expr。
        AppendLayer(bindings, "expr", StandardDescriptors.ValueSource);

        bindings.Add(new(
            "fakes.schema.json",
            "properties/fakes/additionalProperties/properties/kind/enum",
            () => PortImplVocabulary.FakeKindEnum));

        return bindings.ToArray();
    }

    /// <summary>按层追加 enum 绑定（<paramref name="pointerLayer"/> = 指针中的层名；可与 <paramref name="layer"/> 不同，
    /// 用于 <c>expr</c> 并入来源层的键）。</summary>
    private static void AppendLayer(List<EnumBinding> bindings, string pointerLayer, LayerDescriptor layer)
    {
        foreach (var member in layer.Members)
        {
            if (member.Vocabulary is not { } vocabulary || member.JsonKey is not { } key) continue;

            bindings.Add(new(
                "ports.schema.json",
                $"$defs/{pointerLayer}/properties/{key}/enum",
                VocabularyAccessor(vocabulary)));
        }
    }

    /// <summary>
    /// 描述符 <c>Vocabulary</c>（<see cref="PortImplVocabulary"/> 成员名）→ 渲染取数口；
    /// 成员不存在即抛（层↔词表漂移须当场可见，禁静默缺绑定）。
    /// </summary>
    private static Func<IReadOnlyList<string>> VocabularyAccessor(string name)
        => VocabularyAccessors.GetOrAdd(name, static key =>
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;

            if (typeof(PortImplVocabulary).GetField(key, flags) is { } field)
                return () => (IReadOnlyList<string>)field.GetValue(null)!;

            if (typeof(PortImplVocabulary).GetProperty(key, flags) is { } property)
                return () => (IReadOnlyList<string>)property.GetValue(null)!;

            throw new StandardViolationException(
                $"描述符 Vocabulary '{key}' 在 PortImplVocabulary 无对应静态成员（层↔词表漂移）");
        });

    /// <summary>「词表 → 结构门对象键集」绑定：该层 <c>properties</c> 的键集与键序由词表派生。</summary>
    public sealed record PropertiesBinding(string SchemaFile, string Pointer, Func<IReadOnlyList<string>> Keys);

    /// <summary>
    /// enum 绑定表（只读视图；消费方 = 六向完备性矩阵与镜像守卫，**不得另抄一份指针表**）。
    /// </summary>
    public static IReadOnlyList<EnumBinding> EnumBindings => Bindings;

    /// <summary>对象键集绑定表（只读视图）。</summary>
    public static IReadOnlyList<PropertiesBinding> PropertiesKeyBindings => PropertiesBindings;

    /// <summary>
    /// 「对象键集」绑定表：该层 <c>properties</c> 的**键集与键序**由词表派生（写回时保留既有声明原文，
    /// 仅重排/删冗余/补缺键——键集即 <c>additionalProperties:false</c> 的实际闭合面）。
    /// </summary>
    private static readonly PropertiesBinding[] PropertiesBindings =
    [
        new("ports.schema.json", "$defs/expr/properties", () => StandardDescriptors.Expr.Keys),
    ];

    /// <summary>对账（只读）：返回与源不一致的差异明细（空 = 全绿）。</summary>
    public static IReadOnlyList<string> Verify(string schemasDirectory)
    {
        var diffs = new List<string>();
        foreach (var binding in Bindings)
        {
            var path = Path.Combine(schemasDirectory, binding.SchemaFile);
            if (!File.Exists(path))
            {
                diffs.Add($"{binding.SchemaFile}: 文件不存在（{schemasDirectory}）");
                continue;
            }

            var declared = ReadEnum(File.ReadAllText(path), binding.Pointer);
            if (declared is null)
            {
                diffs.Add($"{binding.SchemaFile}#{binding.Pointer}: 未找到 enum 节点");
                continue;
            }

            var expected = binding.Vocabulary();
            if (!SameSet(declared, expected))
                diffs.Add($"{binding.SchemaFile}#{binding.Pointer}: schema=[{string.Join(",", declared)}] 源=[{string.Join(",", expected)}]");
        }

        foreach (var binding in PropertiesBindings)
        {
            var path = Path.Combine(schemasDirectory, binding.SchemaFile);
            if (!File.Exists(path))
            {
                diffs.Add($"{binding.SchemaFile}: 文件不存在（{schemasDirectory}）");
                continue;
            }

            var text = File.ReadAllText(path);
            if (!LedgerTextEditor.TryLocateValueSpan(text, binding.Pointer, out var start, out var length))
            {
                diffs.Add($"{binding.SchemaFile}#{binding.Pointer}: 未找到对象节点");
                continue;
            }

            if (!TryReadPropertyDeclarations(text.Substring(start, length), out var declarations))
            {
                diffs.Add($"{binding.SchemaFile}#{binding.Pointer}: 现有声明无法解析");
                continue;
            }

            var declaredKeys = declarations.Select(declaration => declaration.Name).ToArray();
            var expectedKeys = binding.Keys();
            if (!declaredKeys.SequenceEqual(expectedKeys, StringComparer.Ordinal))
                diffs.Add($"{binding.SchemaFile}#{binding.Pointer}: {DescribeKeySetDiff(declaredKeys, expectedKeys)}");
        }

        return diffs;
    }

    /// <summary>写回（幂等）：逐绑定替换 enum 数组 / properties 键集的原始文本范围；返回实际改动项（空 = 已一致）。</summary>
    public static IReadOnlyList<string> Sync(string schemasDirectory)
    {
        var changes = new List<string>();
        var files = Bindings.Select(binding => binding.SchemaFile)
            .Concat(PropertiesBindings.Select(binding => binding.SchemaFile))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal);

        foreach (var file in files)
        {
            var path = Path.Combine(schemasDirectory, file);
            if (!File.Exists(path))
            {
                changes.Add($"{file}: 文件不存在（{schemasDirectory}）");
                continue;
            }

            var text = File.ReadAllText(path);
            var replacements = new List<LedgerTextReplacement>();

            // 键集先于 enum：键集重排会移动 enum 的文本位置；应用体逐项重新定位，故按此依赖序入列。
            foreach (var binding in PropertiesBindings.Where(binding => binding.SchemaFile == file))
            {
                var keys = binding.Keys();
                if (!LedgerTextEditor.TryLocateValueSpan(text, binding.Pointer, out var start, out var length))
                {
                    changes.Add($"{file}#{binding.Pointer}: 未找到对象节点（跳过）");
                    continue;
                }

                var current = text.Substring(start, length);
                if (!TryReadPropertyDeclarations(current, out var declarations))
                {
                    changes.Add($"{file}#{binding.Pointer}: 现有声明无法解析（跳过）");
                    continue;
                }

                var declared = declarations.Select(declaration => declaration.Name).ToArray();
                var rendered = RenderPropertiesObject(
                    keys, declarations, LineIndent(text, start));

                if (string.Equals(current, rendered, StringComparison.Ordinal))
                    continue;   // 键集/序与声明原文均已一致 → 不动原文（保排版，故可重复运行）

                replacements.Add(new LedgerTextReplacement(binding.Pointer, rendered));
                changes.Add($"{file}#{binding.Pointer}: {DescribeKeySetDiff(declared, keys)}");
            }

            foreach (var binding in Bindings.Where(binding => binding.SchemaFile == file))
            {
                var rendered = "[" + string.Join(", ", binding.Vocabulary().Select(v => $"\"{v}\"")) + "]";
                if (!LedgerTextEditor.TryLocateValueSpan(text, binding.Pointer, out var start, out var length))
                {
                    changes.Add($"{file}#{binding.Pointer}: 未找到 enum 节点（跳过）");
                    continue;
                }

                var current = text.Substring(start, length);
                if (ReadArrayValues(current) is { } declared && SameSet(declared, binding.Vocabulary()))
                    continue;   // 值已一致 → 不动原文（保排版，故可重复运行）

                replacements.Add(new LedgerTextReplacement(binding.Pointer, rendered));
                changes.Add($"{file}#{binding.Pointer}: {current} → {rendered}");
            }

            if (replacements.Count > 0)
                File.WriteAllText(path, LedgerEnumPlan.Apply(text, replacements, out _), new UTF8Encoding(HasBom(path)));
        }

        return changes;
    }

    /// <summary>解析对象文本的属性声明（保序）：返回 (名, 归一化声明原文 <c>"key": value</c>)。</summary>
    private static bool TryReadPropertyDeclarations(
        string objectText, out List<(string Name, string Declaration)> declarations)
    {
        declarations = [];
        try
        {
            var bytes = Encoding.UTF8.GetBytes(objectText);
            var reader = new Utf8JsonReader(bytes);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) return false;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject) return true;
                if (reader.TokenType != JsonTokenType.PropertyName) return false;

                var name = reader.GetString() ?? string.Empty;
                var nameStart = (int)reader.TokenStartIndex;
                if (!reader.Read()) return false;

                if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
                {
                    var nest = 0;
                    while (reader.Read())
                    {
                        if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
                        {
                            nest++;
                        }
                        else if (reader.TokenType is JsonTokenType.EndObject or JsonTokenType.EndArray)
                        {
                            if (nest == 0) break;
                            nest--;
                        }
                    }
                }

                LedgerTextEditor.Utf8ByteRangeToCharRange(bytes, nameStart, (int)reader.BytesConsumed - nameStart, out var start, out var length);
                declarations.Add((name, NormalizeDeclaration(objectText.Substring(start, length))));
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }

    /// <summary>声明原文归一：换行 + 其后的缩进折叠为单空格（JSON 字符串内的裸换行非法，故不伤字面量）。</summary>
    private static string NormalizeDeclaration(string raw)
    {
        var builder = new StringBuilder(raw.Length);
        for (var index = 0; index < raw.Length; index++)
        {
            if (raw[index] is '\r' or '\n')
            {
                while (index + 1 < raw.Length && raw[index + 1] is '\r' or '\n' or ' ' or '\t') index++;

                if (builder.Length > 0 && builder[^1] != ' ') builder.Append(' ');
                continue;
            }

            builder.Append(raw[index]);
        }

        return builder.ToString().Trim();
    }

    /// <summary>位置所在行的前导空白（生成块按此对齐缩进）。</summary>
    private static string LineIndent(string text, int position)
    {
        var lineStart = text.LastIndexOf('\n', Math.Max(position - 1, 0)) + 1;
        var index = lineStart;
        while (index < text.Length && text[index] == ' ') index++;
        return text[lineStart..index];
    }

    /// <summary>
    /// 按派生键集渲染 <c>properties</c> 对象：既有声明的**原文（含键名）逐字保留**；缺声明 → <c>"键": {}</c> 占位。
    /// </summary>
    private static string RenderPropertiesObject(
        IReadOnlyList<string> keys, List<(string Name, string Declaration)> declarations, string indent)
    {
        var byName = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, declaration) in declarations) byName[name] = declaration;

        var entryIndent = indent + "  ";
        var builder = new StringBuilder();
        builder.Append('{').Append('\n');

        for (var index = 0; index < keys.Count; index++)
        {
            builder.Append(entryIndent)
                .Append(byName.TryGetValue(keys[index], out var declaration)
                    ? declaration
                    : $"\"{keys[index]}\": {{}}");

            if (index < keys.Count - 1) builder.Append(',');
            builder.Append('\n');
        }

        builder.Append(indent).Append('}');
        return builder.ToString();
    }

    /// <summary>键集差异要点（紧凑）：增键 / 删键 / 键序；键集一致时只报排版差异。</summary>
    private static string DescribeKeySetDiff(IReadOnlyList<string> declared, IReadOnlyList<string> expected)
    {
        var declaredSet = new HashSet<string>(declared, StringComparer.Ordinal);
        var expectedSet = new HashSet<string>(expected, StringComparer.Ordinal);
        var added = expected.Where(key => !declaredSet.Contains(key)).ToArray();
        var removed = declared.Where(key => !expectedSet.Contains(key)).ToArray();
        var reordered = declared.Count == expected.Count && !declared.SequenceEqual(expected, StringComparer.Ordinal);

        var parts = new List<string>();
        if (added.Length > 0) parts.Add("+[" + string.Join(",", added) + "]");
        if (removed.Length > 0) parts.Add("-[" + string.Join(",", removed) + "]");
        if (reordered) parts.Add("键序不同");
        if (parts.Count == 0) parts.Add($"逐字不同（键集一致，{declared.Count} 键）");

        return string.Join(" ", parts);
    }

    private static bool SameSet(IReadOnlyList<string> left, IReadOnlyList<string> right)
        => left.OrderBy(x => x, StringComparer.Ordinal)
               .SequenceEqual(right.OrderBy(x => x, StringComparer.Ordinal), StringComparer.Ordinal);

    private static bool HasBom(string path)
    {
        Span<byte> head = stackalloc byte[3];
        using var stream = File.OpenRead(path);
        return stream.Read(head) == 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF;
    }

    /// <summary>解析枚举数组文本（非法或非数组 → null = 视为漂移）。</summary>
    private static IReadOnlyList<string>? ReadArrayValues(string arrayText)
    {
        try
        {
            return ReadEnum(arrayText, string.Empty);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IReadOnlyList<string>? ReadEnum(string text, string pointer)
    {
        using var document = JsonDocument.Parse(text, JsonTolerantReader.Tolerant);
        var current = document.RootElement;
        foreach (var segment in pointer.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out var next)) return null;
            current = next;
        }

        return current.ValueKind == JsonValueKind.Array
            ? current.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToArray()
            : null;
    }
}
