using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Usharp.Semantics;

/// <summary>
/// <see cref="IDeclarationEditor"/> 的声明库实现（**逐块文本级**，唯一实现）：
/// 读声明文本（<see cref="IDeclarationStore.GetAsync"/>）→ 按顶层声明头锚点定位块（
/// <c>[Node] &lt;key&gt;:</c> / <c>[Space] &lt;id&gt;:</c> / <c>[Flow] &lt;id&gt;:</c>）→ 块替换/插入/删除 →
/// 写回（<see cref="IDeclarationStore.PutAsync"/>）。
/// <para>不持节点语义、不解析字段：字段合法性由写入后的闸A 编译统一判定（fail-fast，禁本处重判）。</para>
/// <para>换行风格原样保留（检测文档主换行）；禁行号边界推理（对齐 PatchAnchorFlow）。</para>
/// </summary>
public sealed class UsharpDeclarationEditor : IDeclarationEditor
{
    private readonly IDeclarationStore _store;

    public UsharpDeclarationEditor(IDeclarationStore store)
        => _store = store ?? throw new ArgumentNullException(nameof(store));

    /// <inheritdoc />
    public async Task InsertBlockAsync(string logicalName, string blockText, string? afterAnchor = null, CancellationToken ct = default)
    {
        var entry = await RequireAsync(logicalName, ct).ConfigureAwait(false);
        var newline = DetectNewline(entry.Text);
        var lines = SplitLines(entry.Text);
        var block = SplitLines(blockText);

        int insertAt;
        if (string.IsNullOrWhiteSpace(afterAnchor))
        {
            insertAt = lines.Count;
        }
        else
        {
            var anchorIndex = FindAnchorIndex(lines, afterAnchor!);
            if (anchorIndex < 0) throw new InvalidOperationException(
                $"声明 '{logicalName}' 未找到插入锚点 '{afterAnchor}'（插入位置不可定）。");
            insertAt = BlockEnd(lines, anchorIndex);
        }

        var merged = new List<string>(lines.Count + block.Count + 1);
        merged.AddRange(lines.Take(insertAt));
        if (insertAt > 0 && block.Count > 0) merged.Add(string.Empty); // 段间空行（声明块分隔）
        merged.AddRange(block);
        merged.AddRange(lines.Skip(insertAt));

        await WriteAsync(entry, merged, newline, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ReplaceAnchorAsync(string logicalName, string anchor, string newBlockText, CancellationToken ct = default)
    {
        var entry = await RequireAsync(logicalName, ct).ConfigureAwait(false);
        var newline = DetectNewline(entry.Text);
        var lines = SplitLines(entry.Text);

        var anchorIndex = FindAnchorIndex(lines, anchor);
        if (anchorIndex < 0) throw new InvalidOperationException(
            $"声明 '{logicalName}' 未找到锚点 '{anchor}'（替换目标不可定）。");

        var end = BlockEnd(lines, anchorIndex);
        var block = SplitLines(newBlockText);

        var merged = new List<string>(lines.Count + block.Count);
        merged.AddRange(lines.Take(anchorIndex));
        merged.AddRange(block);
        merged.AddRange(lines.Skip(end));

        await WriteAsync(entry, merged, newline, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RemoveAnchorAsync(string logicalName, string anchor, CancellationToken ct = default)
    {
        var entry = await RequireAsync(logicalName, ct).ConfigureAwait(false);
        var newline = DetectNewline(entry.Text);
        var lines = SplitLines(entry.Text);

        var anchorIndex = FindAnchorIndex(lines, anchor);
        if (anchorIndex < 0) throw new InvalidOperationException(
            $"声明 '{logicalName}' 未找到锚点 '{anchor}'（删除目标不可定）。");

        var end = BlockEnd(lines, anchorIndex);
        var merged = new List<string>(lines.Count);
        merged.AddRange(lines.Take(anchorIndex));
        merged.AddRange(lines.Skip(end));

        await WriteAsync(entry, merged, newline, ct).ConfigureAwait(false);
    }

    // ---- 原语 ----

    private async Task<DeclarationEntry> RequireAsync(string logicalName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(logicalName)) throw new ArgumentException("逻辑名不能为空。", nameof(logicalName));
        var entry = await _store.GetAsync(logicalName, ct).ConfigureAwait(false);
        if (entry is null) throw new InvalidOperationException(
            $"声明 '{logicalName}' 不存在（编辑只改存量声明；禁新建空白声明绕闸A）。");
        return entry;
    }

    private Task WriteAsync(DeclarationEntry entry, List<string> lines, string newline, CancellationToken ct)
        => _store.PutAsync(
            new DeclarationEntry(entry.LogicalName, string.Join(newline, lines), entry.Lib, entry.SourcePath), ct);

    /// <summary>检测文档主换行（默认 <c>\r\n</c>，与 Windows 仓库一致）。</summary>
    private static string DetectNewline(string? text)
        => text is not null && text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n"
         : text is not null && text.Contains('\n', StringComparison.Ordinal) ? "\n"
         : "\r\n";

    private static List<string> SplitLines(string? text)
        => (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();

    /// <summary>顶层声明头行判定：去空白后以 <c>[</c> 起始。</summary>
    private static bool IsHeader(string line)
        => line.TrimStart().StartsWith("[", StringComparison.Ordinal);

    /// <summary>头行 → 键（<c>[Node] my_step: fn=…</c> → <c>my_step</c>）；非头行/无键 → false。</summary>
    private static bool TryHeaderKey(string line, out string key)
    {
        key = string.Empty;
        var trimmed = line.TrimStart();
        if (!trimmed.StartsWith("[", StringComparison.Ordinal)) return false;
        var close = trimmed.IndexOf(']');
        if (close < 0) return false;
        var rest = trimmed[(close + 1)..].TrimStart();
        var end = rest.IndexOfAny(new[] { ':', ' ', '\t' });
        key = end < 0 ? rest : rest[..end];
        return key.Length > 0;
    }

    /// <summary>定位锚点块起始行：优先键匹配；兼容以锚点文本直接起始的行。</summary>
    private static int FindAnchorIndex(List<string> lines, string anchor)
    {
        var needle = anchor.Trim();
        for (var i = 0; i < lines.Count; i++)
        {
            if (!IsHeader(lines[i])) continue;
            if (TryHeaderKey(lines[i], out var key) && string.Equals(key, needle, StringComparison.Ordinal))
                return i;
            if (lines[i].TrimStart().StartsWith(needle, StringComparison.Ordinal)) return i;
        }
        return -1;
    }

    /// <summary>块结束行（不含）：自锚点后首条顶层声明头行；无 ⇒ 文末。</summary>
    private static int BlockEnd(List<string> lines, int anchorIndex)
    {
        for (var j = anchorIndex + 1; j < lines.Count; j++)
            if (IsHeader(lines[j])) return j;
        return lines.Count;
    }
}
