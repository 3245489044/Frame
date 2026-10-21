#nullable enable

using System;

namespace UiBackend.Abstractions;

/// <summary>
/// 具名受控态槽（通用件）—— 对 <see cref="UiViewState"/> 的**类型化单槽视图**：
/// 槽名、归一化规则、清空语义一次声明，领域侧只读写 <see cref="Value"/>。
/// <para>
/// 收拢各视图服务手写的「get/set/clear + Trim」包装（同一槽被 4 个成员重复描述）。
/// 线程安全继承自 <see cref="UiViewState"/>（单槽后写胜出）。
/// </para>
/// </summary>
public sealed class UiStateSlot
{
    private readonly UiViewState _state;
    private readonly string _name;
    private readonly Func<string, string>? _normalize;

    /// <param name="state">共享受控态容器。</param>
    /// <param name="name">槽键（全局唯一）。</param>
    /// <param name="normalize">写入归一（如 Trim）；null ⇒ 原样（仅空白判空）。</param>
    public UiStateSlot(UiViewState state, string name, Func<string, string>? normalize = null)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _name = string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("槽键不得为空", nameof(name)) : name;
        _normalize = normalize;
    }

    /// <summary>读取槽值；未设置 / 空白 ⇒ null。</summary>
    public string? Value
    {
        get => _state.Get(_name);
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                _state.Clear(_name);
                return;
            }

            _state.Set(_name, _normalize is null ? value : _normalize(value!));
        }
    }

    /// <summary>是否已置位。</summary>
    public bool Has => _state.Has(_name);

    /// <summary>清空槽值。</summary>
    public void Clear() => _state.Clear(_name);
}
