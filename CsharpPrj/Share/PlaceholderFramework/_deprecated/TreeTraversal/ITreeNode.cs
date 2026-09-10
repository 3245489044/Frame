
// ============================================================
// ITreeNode.cs —— 通用树节点接口（CRTP，TreeTraversal 用）
// 状态：已废弃（_deprecated）· 2026-09-10 · M4 树基座收敛
// 原因：全仓 0 消费者（无代码/测试引用，历史文档描述已过时），
//       与 Framework.Tree.TreeWalker 重复。唯一通用树遍历原语应收敛至
//       BasicMethod/Map/GraphFoundation/FrameworkTree。可于 git 找回。
// ============================================================
namespace Common.PlaceholderFramework.TreeTraversal;

public interface ITreeNode<T> where T : ITreeNode<T>
{
    IEnumerable<T> GetChildren();
}
