namespace Share.ContextGeometry;

/// <summary>工具面规范输入（声明顺序即语义；禁运行期重排）。</summary>
public readonly record struct ToolFaceInput(string Name, string Description, string Schema);

/// <summary>单工具冻结摘要（可归因：描述/顺序变化定位到具体工具）。</summary>
public sealed record ToolFaceDigest(string Name, string DescriptionDigest, string Digest);

/// <summary>工具面规范字节产物（聚合摘要 + 逐工具摘要，顺序即语义）。</summary>
public sealed record ToolFaceCanonical(string Digest, IReadOnlyList<ToolFaceDigest> Tools);

/// <summary>
/// 工具面规范序列化（纯函数）：<c>name|description|schema\n</c> 按声明顺序逐工具拼接。
/// <para>K3：跨进程/跨委托字节相等；顺序对调 ⇒ 摘要变化；单工具描述改动 ⇒ 逐工具摘要可定位。</para>
/// </summary>
public static class ToolFaceCanonicalizer
{
    /// <summary>规范字节（唯一拼接口径；禁空白归一/禁排序）。</summary>
    public static string CanonicalText(IEnumerable<ToolFaceInput>? tools)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var tool in tools ?? Enumerable.Empty<ToolFaceInput>())
        {
            sb.Append(tool.Name ?? string.Empty).Append('|')
              .Append(tool.Description ?? string.Empty).Append('|')
              .Append(tool.Schema ?? string.Empty).Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>规范序列化（逐工具 + 聚合；空工具面 ⇒ 空聚合摘要）。</summary>
    public static ToolFaceCanonical Of(IEnumerable<ToolFaceInput>? tools)
    {
        var inputs = (tools ?? Enumerable.Empty<ToolFaceInput>()).ToArray();
        var digests = new List<ToolFaceDigest>(inputs.Length);
        var lines = new List<string>(inputs.Length);
        foreach (var tool in inputs)
        {
            var description = ContentHash.Sha256Hex(tool.Description ?? string.Empty);
            var digest = ContentHash.Sha256Hex(
                (tool.Name ?? string.Empty) + "|" + (tool.Description ?? string.Empty) + "|" + (tool.Schema ?? string.Empty));
            digests.Add(new ToolFaceDigest(tool.Name ?? string.Empty, description, digest));
            lines.Add($"{tool.Name}|{description}|{digest}");
        }

        return new ToolFaceCanonical(ContentHash.Sha256Hex(string.Join("\n", lines)), digests);
    }
}