namespace Usharp.Semantics;

/// <summary>
/// 声明编辑原语（T9）—— 对 USharp 声明文档做**锚点替换 / 块插入 / 锚点移除**的薄封装：
/// 只做文本锚点定位 + 块替换，**不持节点语义**（语义归声明面编译器）。
/// <para>唯一写入口：一切增改删节点/流/函数挂载经本端口 → 写入声明库
/// （<see cref="IDeclarationStore.PutAsync"/>）→ 闸A 编译 → 热装载；禁绕过本端口直写
/// <c>Config/**/*.ushar</c>（守卫 <c>Check-DeclarationEditSingleSource</c>）。</para>
/// <para>锚点 = 顶层声明头行的键（<c>[Node] &lt;key&gt;:</c> / <c>[Space] &lt;id&gt;:</c> /
/// <c>[Flow] &lt;id&gt;:</c> 的 key）；编辑 = **锚点替换**（对齐 PatchAnchorFlow：禁行号边界推理）。</para>
/// </summary>
public interface IDeclarationEditor
{
    /// <summary>在 <paramref name="afterAnchor"/> 声明的块之后插入一个声明块；<c>afterAnchor</c> 空 ⇒ 追加到文末。</summary>
    Task InsertBlockAsync(string logicalName, string blockText, string? afterAnchor = null, CancellationToken ct = default);

    /// <summary>锚点替换：把 <paramref name="anchor"/> 标识的声明块整体替换为 <paramref name="newBlockText"/>。</summary>
    Task ReplaceAnchorAsync(string logicalName, string anchor, string newBlockText, CancellationToken ct = default);

    /// <summary>锚点移除：删除 <paramref name="anchor"/> 标识的声明块。</summary>
    Task RemoveAnchorAsync(string logicalName, string anchor, CancellationToken ct = default);
}
