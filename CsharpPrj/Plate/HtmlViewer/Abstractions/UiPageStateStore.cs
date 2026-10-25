#nullable enable

using System;
using System.Collections.Generic;
using TConfig;

namespace UiBackend.Abstractions;

/// <summary>页面回投应用结果：未声明槽（差集报告）+ 需上层处理的展开键。</summary>
public sealed record UiStateApplyResult(IReadOnlyList<string> RejectedSlots, IReadOnlyList<string> Open);

/// <summary>
/// 页面受控态仓库（B2）：复用 <see cref="UiViewState"/>（标量槽）+ <see cref="UiSelectionState"/>（选择槽），
/// 以 <see cref="HtmlPageDeclaration.State"/> 为声明面做默认值与键集合约束。
/// <para>线程语义继承 <see cref="UiViewState"/>（单槽后写胜出）；未声明槽写入 → 明确拒绝并报告（禁隐式状态）。</para>
/// </summary>
public sealed class UiPageStateStore
{
    private readonly UiViewState _values = new();
    private readonly Dictionary<string, string?> _selection = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HtmlPageStateSlot> _declarations = new(StringComparer.Ordinal);

    public UiPageStateStore(IReadOnlyDictionary<string, HtmlPageStateSlot>? declarations = null)
        => Initialize(declarations);

    /// <summary>按声明初始化槽键与默认值（重复调用幂等：已置位槽不被默认值覆盖）。</summary>
    public void Initialize(IReadOnlyDictionary<string, HtmlPageStateSlot>? declarations)
    {
        if (declarations == null) return;

        foreach (var kvp in declarations)
        {
            if (string.IsNullOrWhiteSpace(kvp.Key)) continue;
            var slot = kvp.Key;
            _declarations[slot] = kvp.Value ?? new HtmlPageStateSlot();

            if (_values.Has(slot)) continue;
            var defaultValue = kvp.Value?.Default?.ToString();
            if (!string.IsNullOrWhiteSpace(defaultValue))
                _values.Set(slot, Normalize(kvp.Value, defaultValue));
        }
    }

    /// <summary>读取槽值；未声明/未置位 → null。</summary>
    public string? Get(string slot) => _values.Get(slot);

    /// <summary>读取槽值（未置位时回落声明默认值）；未声明 → null。</summary>
    public string? GetWithDefault(string slot)
    {
        var value = _values.Get(slot);
        if (!string.IsNullOrWhiteSpace(value)) return value;
        return _declarations.TryGetValue(slot, out var declaration)
            ? Normalize(declaration, declaration.Default?.ToString())
            : null;
    }

    /// <summary>写入槽值；未声明槽返回 false（可见化由调用方报告）。</summary>
    public bool TrySet(string slot, string? value, HtmlPageStateSlot? declaration = null)
    {
        if (string.IsNullOrWhiteSpace(slot)) return false;
        if (!_declarations.ContainsKey(slot)) return false;

        var normalized = Normalize(declaration ?? _declarations[slot], value);
        _values.Set(slot, normalized);
        return true;
    }

    /// <summary>当前选择态快照。</summary>
    public UiSelectionState Selection => new(new Dictionary<string, string?>(_selection, StringComparer.Ordinal));

    /// <summary>应用页面回投的选择态（仅接受已声明槽；未声明槽忽略并暴露在返回值）。</summary>
    public IReadOnlyList<string> ApplySelection(UiSelectionState selection)
    {
        var rejected = new List<string>();
        foreach (var kvp in selection.Slots)
        {
            if (!_declarations.ContainsKey(kvp.Key))
            {
                rejected.Add(kvp.Key);
                continue;
            }

            if (kvp.Value == null) _selection.Remove(kvp.Key);
            else _selection[kvp.Key] = kvp.Value;
        }
        return rejected;
    }

    /// <summary>
    /// 应用 <c>uiState</c> 补丁（R3 回投唯一入口）：select → 选择槽；其余标量 → 声明槽；
    /// 未声明槽进差集（不落状态）；open 交回上层（展开态由既有 UiStateBinder 处理）。
    /// </summary>
    public UiStateApplyResult ApplyPatch(UiStatePatch patch)
    {
        var rejected = new List<string>();
        if (patch.Select.Count > 0)
            rejected.AddRange(ApplySelection(UiSelectionState.FromKeys(patch.Select)));

        foreach (var kvp in patch.Scalars)
        {
            if (!TrySet(kvp.Key, kvp.Value))
                rejected.Add(kvp.Key);
        }

        return new UiStateApplyResult(rejected, patch.Open);
    }

    /// <summary>声明槽集合（用于与页面回投 <c>uiState</c> 做差集报告）。</summary>
    public IReadOnlyCollection<string> DeclaredSlots => _declarations.Keys;

    private static string? Normalize(HtmlPageStateSlot? declaration, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        return string.Equals(declaration?.Normalize, "trim", StringComparison.OrdinalIgnoreCase)
            ? value!.Trim()
            : value;
    }
}