namespace Share.NodeIo;

/// <summary>
/// 节点文档作用域（**唯一控制反转入口**）：调用方只给域 + loc + 键上下文，
/// 不传位置 id / 载体 id / nodeType / threadId；位置集由声明经 <c>IPlacementResolver</c> 反解。
/// </summary>
/// <param name="Domain">节点域键（声明面 <c>[Domain]</c>）。</param>
/// <param name="Loc">位置轴坐标（声明面 <c>[Matrix] LocToPlacement</c>；非身份）。</param>
/// <param name="KeyContext">键上下文（身份键或单占位符值；禁位置键）。</param>
/// <param name="SelectionId">选择名（可空 = 位置声明的 selection）。</param>
/// <param name="FormOverride">形态限定（可空；给出时须与位置声明一致，否则拒绝）。</param>
public sealed record NodeDocumentScope(
    string Domain,
    string Loc,
    string KeyContext,
    string? SelectionId = null,
    string? FormOverride = null);
