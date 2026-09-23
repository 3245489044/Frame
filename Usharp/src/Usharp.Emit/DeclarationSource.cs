namespace Usharp.Emit;

/// <summary>
/// 声明面装载输入：**逻辑名**（诊断归因，如配置基名 / 声明面文件名）+ **已解析绝对路径**（路径解析由消费方承担，
/// 本层不引入配置面依赖）+ 目标流号（声明面无流概念时传 <c>null</c>）。
/// </summary>
/// <param name="BaseName">逻辑名（日志归因键，如 <c>agent-loop</c> / <c>RuntimeGovernance.ushar</c>）。</param>
/// <param name="Path">已解析的绝对路径（null / 空 = 未解析，装载期判为「未声明」）。</param>
/// <param name="FlowId">目标流号（<c>[Flow]</c>；无流概念的声明面传 null）。</param>
public sealed record DeclarationSource(string BaseName, string? Path, string? FlowId = null)
{
    /// <summary>是否已解析到路径（未解析 ⇒ 装载期判为「未声明」，不做编译）。</summary>
    public bool IsResolvable => !string.IsNullOrWhiteSpace(Path);
}
