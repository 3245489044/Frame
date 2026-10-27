#nullable enable

using System;

namespace UiBackend.Abstractions;

/// <summary>
/// 可选能力槽（通用件）—— 声明「服务解析器 ↔ 能力键 ↔ 未装配文案 ↔ 就绪面」，收敛
/// <c>Service&lt;T&gt;() is null</c> 判定 + 未装配文案 + 就绪面归因的重复。
/// <para>
/// 服务解析与就绪面渲染均由宿主注入（本类不引用具体容器与渲染实现），能力键词表归领域。
/// </para>
/// </summary>
public readonly struct UiCapabilitySlot<T> where T : class
{
    private readonly string _capabilityKey;
    private readonly string _notAttachedText;
    private readonly Func<T?> _resolve;
    private readonly Func<string, string> _readiness;

    /// <param name="capabilityKey">能力键（就绪面归因用）。</param>
    /// <param name="notAttachedText">未装配显式文案（空态原因单源）。</param>
    /// <param name="resolve">服务解析器（未装配 ⇒ null）。</param>
    /// <param name="readiness">能力键 → 就绪面 HTML 渲染器。</param>
    public UiCapabilitySlot(
        string capabilityKey,
        string notAttachedText,
        Func<T?> resolve,
        Func<string, string> readiness)
    {
        _capabilityKey = capabilityKey;
        _notAttachedText = notAttachedText;
        _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
        _readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
    }

    /// <summary>已装配服务实例；未装配 ⇒ null。</summary>
    public T? Service => _resolve();

    /// <summary>是否已装配。</summary>
    public bool Attached => Service is not null;

    /// <summary>未装配显式文案（空态原因单源）。</summary>
    public string NotAttachedText => _notAttachedText;

    /// <summary>未装配就绪面（能力键单源）。</summary>
    public string Readiness => _readiness(_capabilityKey);
}
