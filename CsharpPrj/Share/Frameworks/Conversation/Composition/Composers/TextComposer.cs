using System.Collections.Generic;
using System.Text;

namespace Share.Composition;

/// <summary>
/// 文本拼接内核 —— 空段过滤 + 空行（<c>\n\n</c>）分隔。
/// 本方法是提示词/文本/快照 system 段拼接语义的<b>唯一权威</b>：
/// <c>PromptText.Assemble</c> / <c>PromptSnapshot.System</c> / <c>PromptNodeProjector.Join</c> 一律委托至此，
/// 消除既有三份平行实现（SingleImplementation）。
/// 纯函数、零依赖、可单测。
/// </summary>
public static class TextComposer
{
    /// <summary>空行拼接（params 形式；与 <c>PromptText.Assemble</c> 既有签名等价）。</summary>
    public static string Join(params string?[] parts) => JoinNonEmpty(parts);

    /// <summary>空行拼接：按序连接非空片段，片段间以空行分隔（空/null 段跳过）。</summary>
    public static string JoinNonEmpty(IEnumerable<string?>? parts)
    {
        if (parts is null) return string.Empty;

        var sb = new StringBuilder();
        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(part)) continue;
            if (sb.Length > 0) { sb.Append('\n'); sb.Append('\n'); }
            sb.Append(part);
        }
        return sb.ToString();
    }

    /// <summary>拼接并追加命名小节（如 L2 生成的「## 焦点」）；节体为空 → 原样返回。</summary>
    public static string AppendSection(string body, string? heading, string? sectionBody)
        => string.IsNullOrWhiteSpace(sectionBody)
            ? body
            : $"{body}\n\n{heading}\n{sectionBody}";

    /// <summary>
    /// 过滤空白片段（<see cref="string.IsNullOrWhiteSpace"/> 判据），保持原序 —— 片段<b>筛选</b>原语。
    /// 与 <see cref="JoinNonEmpty"/> 的判据不同（后者为 <see cref="string.IsNullOrEmpty"/>），两者勿混用。
    /// </summary>
    /// <param name="parts">片段序列（null → 空列表）。</param>
    public static IReadOnlyList<string> NonBlank(IEnumerable<string?>? parts)
    {
        var list = new List<string>();
        if (parts is null) return list;

        foreach (var part in parts)
            if (!string.IsNullOrWhiteSpace(part)) list.Add(part!);
        return list;
    }
}
