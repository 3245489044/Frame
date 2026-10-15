#nullable enable

using System;
using System.Collections.Generic;

namespace UiBackend.Abstractions;

/// <summary>
/// UI 选择态（L1 通用契约，态外置）——「当前选中了什么」是一等**模型状态**，非 DOM 副产物。
/// 槽键（Slot）区分不同选择维度（如 view/history/task），值 = 该槽当前选中的键；零领域语义，框架只搬运。
/// <para>线格式（页面回投 <c>uiState.select</c>）：字符串数组 <c>"slot=key"</c>（与 <c>uiState.open</c> 同形），
/// 复用既有数组读取通道，避免为对象键新增读取器。</para>
/// <para>修根因：回投不再靠 DOM 选择器扫 <c>.active</c> 反查领域属性——选中态由声明式槽回投，
/// 消除「类名漂移 / 容器形态不同 / 领域字段硬编码」三类脆弱点。</para>
/// </summary>
public sealed record UiSelectionState(IReadOnlyDictionary<string, string?> Slots)
{
    /// <summary>空选择态（无任何选中）。</summary>
    public static UiSelectionState Empty { get; } =
        new(new Dictionary<string, string?>(StringComparer.Ordinal));

    /// <summary>是否无任何选择。</summary>
    public bool IsEmpty => Slots.Count == 0;

    /// <summary>取某槽的选中键（未声明 / 无选中 → null）。</summary>
    public string? Get(string slot) => Slots.TryGetValue(slot, out var v) ? v : null;

    /// <summary>是否显式声明了某槽（区别于「声明了但为空」）。</summary>
    public bool Has(string slot) => Slots.ContainsKey(slot);

    /// <summary>
    /// 从线格式字符串数组（<c>"slot=key"</c>）解析；空 / null → <see cref="Empty"/>。
    /// key 段允许为空（显式「该槽无选中」）；非法段（无 '='）跳过。
    /// </summary>
    public static UiSelectionState FromKeys(IReadOnlyList<string>? keys)
    {
        if (keys is null || keys.Count == 0) return Empty;
        var map = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var raw in keys)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var eq = raw.IndexOf('=');
            if (eq <= 0) continue;
            var slot = raw.Substring(0, eq).Trim();
            if (slot.Length == 0) continue;
            var key = raw.Substring(eq + 1);
            map[slot] = string.IsNullOrWhiteSpace(key) ? null : key;
        }
        return map.Count == 0 ? Empty : new UiSelectionState(map);
    }
}
