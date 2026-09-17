using System.Text;
using System.Text.Json;
using DataCenter.Protocol.Standards;

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
    private sealed record EnumBinding(string SchemaFile, string Pointer, Func<IReadOnlyList<string>> Vocabulary);

    /// <summary>词表绑定表（键路径按 <c>/</c> 分隔；表即「哪处 enum 由哪份词表派生」的唯一登记）。</summary>
    private static readonly EnumBinding[] Bindings =
    [
        new("ports.schema.json", "$defs/impl/properties/kind/enum", () => PortImplVocabulary.KindEnum),
        new("ports.schema.json", "$defs/arg/properties/convert/enum", () => PortImplVocabulary.ConversionEnum),
        new("ports.schema.json", "$defs/valueSource/properties/convert/enum", () => PortImplVocabulary.ConversionEnum),
        new("ports.schema.json", "$defs/valueSource/properties/from/enum", () => PortImplVocabulary.FromEnum),
        new("ports.schema.json", "$defs/valueSource/properties/onBlankFirstKey/enum", () => PortImplVocabulary.OnBlankFirstKeyEnum),
        new("ports.schema.json", "$defs/valueSource/properties/onDuplicate/enum", () => PortImplVocabulary.OnDuplicateEnum),
        new("ports.schema.json", "$defs/dep/properties/fake/enum", () => PortImplVocabulary.FakeKindEnum),
        new("ports.schema.json", "$defs/composedArg/properties/convert/enum", () => PortImplVocabulary.ConversionEnum),
        new("ports.schema.json", "$defs/composedField/properties/select/enum", () => PortImplVocabulary.SelectEnum),
        new("ports.schema.json", "$defs/scope/properties/kind/enum", () => PortImplVocabulary.ScopeKindEnum),
        new("ports.schema.json", "$defs/deliver/properties/kind/enum", () => PortImplVocabulary.DeliverKindEnum),
        new("ports.schema.json", "$defs/expr/properties/from/enum", () => PortImplVocabulary.FromEnum),
        new("ports.schema.json", "$defs/expr/properties/convert/enum", () => PortImplVocabulary.ConversionEnum),
        new("ports.schema.json", "$defs/expr/properties/onBlankFirstKey/enum", () => PortImplVocabulary.OnBlankFirstKeyEnum),
        new("ports.schema.json", "$defs/expr/properties/onDuplicate/enum", () => PortImplVocabulary.OnDuplicateEnum),
        new("fakes.schema.json", "properties/fakes/additionalProperties/properties/kind/enum", () => PortImplVocabulary.FakeKindEnum),
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

        return diffs;
    }

    /// <summary>写回（幂等）：逐绑定替换 enum 数组的原始文本范围；返回实际改动项（空 = 已一致）。</summary>
    public static IReadOnlyList<string> Sync(string schemasDirectory)
    {
        var changes = new List<string>();
        foreach (var group in Bindings.GroupBy(b => b.SchemaFile, StringComparer.Ordinal))
        {
            var path = Path.Combine(schemasDirectory, group.Key);
            if (!File.Exists(path))
            {
                changes.Add($"{group.Key}: 文件不存在（{schemasDirectory}）");
                continue;
            }

            var text = File.ReadAllText(path);
            var fileChanges = 0;
            foreach (var binding in group)
            {
                var rendered = "[" + string.Join(", ", binding.Vocabulary().Select(v => $"\"{v}\"")) + "]";
                if (!TryFindValueSpan(text, binding.Pointer, out var start, out var length))
                {
                    changes.Add($"{group.Key}#{binding.Pointer}: 未找到 enum 节点（跳过）");
                    continue;
                }

                var current = text.Substring(start, length);
                if (ReadArrayValues(current) is { } declared && SameSet(declared, binding.Vocabulary()))
                    continue;   // 值已一致 → 不动原文（保排版，故可重复运行）

                text = string.Concat(text.AsSpan(0, start), rendered, text.AsSpan(start + length));
                changes.Add($"{group.Key}#{binding.Pointer}: {current} → {rendered}");
                fileChanges++;
            }

            if (fileChanges > 0) File.WriteAllText(path, text, new UTF8Encoding(HasBom(path)));
        }

        return changes;
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
        using var document = JsonDocument.Parse(text, JsonDocumentOptions);
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

    private static readonly JsonDocumentOptions JsonDocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>定位 <paramref name="pointer"/> 所指值的原始文本范围（字节偏移）。</summary>
    private static bool TryFindValueSpan(string text, string pointer, out int start, out int length)
    {
        start = 0;
        length = 0;

        var segments = pointer.Split('/');
        var containers = new Stack<string>();
        var pending = string.Empty;

        var bytes = Encoding.UTF8.GetBytes(text);
        var reader = new Utf8JsonReader(bytes);
        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject:
                case JsonTokenType.StartArray:
                    containers.Push(pending);      // 该容器的键名（根容器为 ""）
                    pending = string.Empty;
                    break;

                case JsonTokenType.EndObject:
                case JsonTokenType.EndArray:
                    if (containers.Count > 0) containers.Pop();
                    break;

                case JsonTokenType.PropertyName:
                    pending = reader.GetString() ?? string.Empty;
                    if (!IsTargetPath(containers, pending, segments) || !reader.Read()) break;

                    var byteStart = (int)reader.TokenStartIndex;
                    if (reader.TokenType is not (JsonTokenType.StartArray or JsonTokenType.StartObject))
                    {
                        SetSpan(bytes, byteStart, (int)reader.ValueSpan.Length, out start, out length);
                        return true;
                    }

                    // 容器值：按嵌套计数找到配对的收尾 token，范围 = [起始, 收尾末字符)
                    var nest = 0;
                    while (reader.Read())
                    {
                        if (reader.TokenType is JsonTokenType.StartArray or JsonTokenType.StartObject)
                        {
                            nest++;
                        }
                        else if (reader.TokenType is JsonTokenType.EndArray or JsonTokenType.EndObject)
                        {
                            if (nest == 0)
                            {
                                SetSpan(bytes, byteStart, (int)reader.BytesConsumed - byteStart, out start, out length);
                                return true;
                            }

                            nest--;
                        }
                    }

                    return false;
            }
        }

        return false;
    }

    /// <summary>字节范围 → 字符范围（非 ASCII 文本前置时两者不等，须显式换算）。</summary>
    private static void SetSpan(byte[] bytes, int byteStart, int byteLength, out int start, out int length)
    {
        start = Encoding.UTF8.GetCharCount(bytes, 0, byteStart);
        length = Encoding.UTF8.GetCharCount(bytes, 0, byteStart + byteLength) - start;
    }

    /// <summary>当前容器路径（去根哨兵，自内向外）+ 待定键 == 目标键路径？（不依赖读者深度口径）</summary>
    private static bool IsTargetPath(Stack<string> containers, string name, string[] segments)
    {
        if (containers.Count - 1 != segments.Length - 1) return false;
        if (!string.Equals(name, segments[^1], StringComparison.Ordinal)) return false;

        var index = segments.Length - 2;
        foreach (var container in containers)
        {
            if (index < 0) break;
            if (!string.Equals(container, segments[index], StringComparison.Ordinal)) return false;
            index--;
        }

        return index == -1;
    }
}
