#nullable enable

namespace UiBackend.Abstractions;

/// <summary>
/// 渲染产物（L1 通用契约，P0「领域只产产物」）——业务/渲染侧函数调用的**类型化返回**：
/// <see cref="Text"/> 为叙述/文本结果（经宿主写回或 DOM set），<see cref="Plan"/> 为写回计划
/// （由写协调器 <see cref="IUiRenderExecutor"/> 单点执行）。二者互斥；均为空 ⇒ <see cref="Skip"/>（不写）。
/// <para>取代「领域内直呼通道写回」：领域只产本对象，DOM 落地由框架协调器负责（去字符串哨兵）。</para>
/// </summary>
public sealed record RenderOutput(string? Text = null, UiWritePlan? Plan = null)
{
    /// <summary>不写（显式空产物；等价「无输出」，禁哨兵串）。</summary>
    public static RenderOutput Skip { get; } = new();

    /// <summary>叙述结果（文本）。</summary>
    public static RenderOutput Narrative(string? text) => new(text);

    /// <summary>写回计划产物（由协调器执行）。</summary>
    public static RenderOutput Write(UiWritePlan plan) => new(null, plan);

    /// <summary>是否无产物（不写）。</summary>
    public bool IsSkip => string.IsNullOrEmpty(Text) && Plan is null;

    /// <summary>是否携带写回计划。</summary>
    public bool HasPlan => Plan is not null;

    /// <summary>文本 ↔ 产物隐式转换（兼容既有返回纯文本的注册函数；null/空 ⇒ Skip）。</summary>
    public static implicit operator RenderOutput(string? text)
        => string.IsNullOrEmpty(text) ? Skip : new(text);

    /// <summary>回退为文本（文本为空 ⇒ 空串）。</summary>
    public override string ToString() => Text ?? string.Empty;
}
