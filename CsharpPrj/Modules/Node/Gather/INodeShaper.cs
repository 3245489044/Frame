using Modules.Node.Projection;

namespace Modules.Node.Gather;

/// <summary>
/// **塑形器**（第三段）：把读项映射为目标形态（视图模型 / 摘要 / 任意 DTO）。
/// <para>
/// 呈现只是塑形的**一种**用途；框架本身不认「渲染」，只认「节点 → 目标形态」。
/// 默认实现 <see cref="DeclaredShaper"/> 读声明；特殊计算按 kind 挂定制塑形器（声明优先、代码兜底）。
/// </para>
/// </summary>
/// <typeparam name="TShape">目标形态（默认 = <see cref="DeclaredNodeShape"/>）。</typeparam>
public interface INodeShaper<TShape>
{
    /// <summary>该塑形器认领的 kind（可为多个）。</summary>
    bool Claims(string kind);

    /// <summary>节点 → 目标形态；返回 <c>null</c> = 该行不产出（如纯账目类）。</summary>
    TShape? Shape(NodeProjectionItem item);
}

/// <summary>
/// 中性节点形态（框架默认塑形结果，零领域类型）：模板只认本记录的字段。
/// <para>
/// 由 <see cref="DeclaredShaper"/> 依 <c>node-gather.json</c> 的 <c>kind-shaping</c>
/// 声明（<c>titleKey</c>/<c>summary[]</c>/<c>badge</c>）从节点展平字段塑形而来。
/// 需要特殊计算的域可另挂定制塑形器作回退。
/// </para>
/// </summary>
/// <param name="Kind">类别（载荷判别值）。</param>
/// <param name="Badge">徽标文本（可空）。</param>
/// <param name="Key">节点键（回显）。</param>
/// <param name="Title">标题（取 <c>titleKey</c> 字段；可空）。</param>
/// <param name="Summary">摘要（按 <c>summary[]</c> 字段拼接；可空）。</param>
/// <param name="Updated">时间（取 <c>timeKey</c> 字段；可空）。</param>
public sealed record DeclaredNodeShape(
    string Kind,
    string? Badge,
    string Key,
    string? Title,
    string? Summary,
    string? Updated);
