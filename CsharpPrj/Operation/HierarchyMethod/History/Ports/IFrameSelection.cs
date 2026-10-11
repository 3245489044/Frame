using Modules.Node.Spec;

namespace Operation.HierarchyMethod.History.Ports;

/// <summary>
/// 帧级选择端口（框架声明，宿主实现）—— 把域声明 <c>*.json</c>/<c>.ushar</c> 的 <c>selections</c>
/// 编译为可求值谓词。语义：未声明 ∨ 表达式不可识别 ⇒ <c>false</c> + 原因（⊥，不放行，不伪造）。
/// </summary>
public interface IFrameSelection
{
    /// <summary>解析 selectionId 为可求值谓词（失败 ⇒ <c>false</c> + 原因）。</summary>
    /// <param name="selectionId">选择名（可空 ⇒ 缺省选择名）。</param>
    /// <param name="predicate">编译谓词（失败为 null）。</param>
    /// <param name="reason">不可用原因（成功为 null）。</param>
    bool TryResolve(string? selectionId, out ExpressionSelectionPredicate? predicate, out string? reason);
}
