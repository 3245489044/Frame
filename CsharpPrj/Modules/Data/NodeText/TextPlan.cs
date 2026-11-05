#nullable enable

using System.Collections.Generic;
using System.Linq;

namespace Share.Composition.Text;

/// <summary>
/// 计划 —— 一次装配的段集合（已按序）+ 契约 + 指纹。只描述「有哪些段、各自什么状态」，不负责拼接。
/// </summary>
/// <param name="Blocks">段值（按 (Order, 声明序) 稳定排序）。</param>
/// <param name="Contract">由同一批声明派生的端口契约。</param>
/// <param name="ProfileKey">档案键（计划来源标识，参与诊断）。</param>
/// <param name="Fingerprint">计划指纹（有序 键|状态|文本哈希|组|序 的摘要；用于对拍/缓存）。</param>
public sealed record TextPlan(
    IReadOnlyList<TextBlock> Blocks,
    TextContract Contract,
    string ProfileKey,
    string Fingerprint)
{
    /// <summary>空计划。</summary>
    public static TextPlan Empty { get; } = new(
        System.Array.Empty<TextBlock>(),
        TextContract.Empty,
        string.Empty,
        TextDigest.Hash(string.Empty));

    /// <summary>按键取段值（不存在 → null）。</summary>
    public TextBlock? BlockOf(TextKey key)
        => Blocks.FirstOrDefault(b => b.Key == key);

    /// <summary>按键取段状态（计划未产出该键 ⇒ <see cref="TextState.Unknown"/>，即「应有但未产」）。</summary>
    public TextState StateOf(TextKey key) => BlockOf(key)?.State ?? TextState.Unknown;

    /// <summary>
    /// 轻量计划 —— 只按契约从事实面取状态，<b>不跑任何生成/取值侧效应</b>。
    /// 供执行体启动前的必要性判定复用同一判据（不为了预检触发昂贵解析）。
    /// </summary>
    /// <param name="contract">端口契约。</param>
    /// <param name="context">事实上下文。</param>
    /// <param name="blankAs">空白串口径（前置门域传 <see cref="BlankSemantics.Absent"/>）。</param>
    /// <param name="profileKey">档案键（诊断标识）。</param>
    public static TextPlan FromPorts(
        TextContract contract,
        ISegmentContext context,
        BlankSemantics blankAs = BlankSemantics.Value,
        string profileKey = "")
    {
        System.ArgumentNullException.ThrowIfNull(contract);
        System.ArgumentNullException.ThrowIfNull(context);

        var blocks = new System.Collections.Generic.List<TextBlock>(contract.Ports.Count);
        var lines = new System.Collections.Generic.List<string>(contract.Ports.Count);
        foreach (var port in contract.Ports)
        {
            var block = context.BlockOf(port.Key, blankAs);
            blocks.Add(block);
            lines.Add($"{port.Key.Value}|{block.State}|{TextDigest.Hash(block.Text)}||0");
        }

        return new TextPlan(blocks, contract, profileKey, TextDigest.HashLines(lines));
    }
}
