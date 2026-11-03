using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Usharp.Semantics;

/// <summary>
/// 声明源指纹的**单一实现**（生成器 / 运行期 / 门禁共用；禁各消费方私拷 SHA256 拼接逻辑）。
/// <para>
/// 口径：按路径序 → <c>{相对路径}|{文本}\n</c> 拼接后取 SHA256（十六进制小写）。
/// <c>root</c> 为空 ⇒ 只取文件名（内存文档/测试口径）。
/// </para>
/// </summary>
public static class DeclarationArtifactFingerprint
{
    /// <summary>全量声明语料指纹。</summary>
    /// <param name="sources">源集。</param>
    /// <param name="root">路径归一基（null/空 = 仅文件名）。</param>
    public static string Of(IEnumerable<UsharpSourceFile> sources, string? root = null)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var buffer = new StringBuilder();
        foreach (var source in sources.OrderBy(s => s.Path, StringComparer.Ordinal))
        {
            buffer.Append(NormalizePath(source.Path, root)).Append('|')
                  .Append(source.Text ?? string.Empty).Append('\n');
        }

        return Hash(buffer.ToString());
    }

    /// <summary>
    /// 键空间语料指纹（**保持既有产物口径**：仅 <c>keyspaces</c> 目录、只取文件名）。
    /// 无该段 ⇒ null（产物不写指纹）。
    /// </summary>
    public static string? OfKeyspaces(IEnumerable<UsharpSourceFile> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var buffer = new StringBuilder();
        foreach (var source in sources
                     .Where(s => NormalizeDirectory(s.Path).Contains("/keyspaces/", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(s => s.Path, StringComparer.Ordinal))
        {
            buffer.Append(Path.GetFileName(source.Path)).Append('|')
                  .Append(source.Text ?? string.Empty).Append('\n');
        }

        return buffer.Length == 0 ? null : Hash(buffer.ToString());
    }

    /// <summary>路径归一（<c>\</c> → <c>/</c>；root 非空 ⇒ 相对路径）。</summary>
    public static string NormalizePath(string path, string? root)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;

        var normalized = string.IsNullOrWhiteSpace(root)
            ? Path.GetFileName(path)
            : Path.GetRelativePath(root!, path);
        return normalized.Replace('\\', '/');
    }

    private static string Hash(string content)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

    private static string NormalizeDirectory(string path)
        => (path ?? string.Empty).Replace('\\', '/');
}
