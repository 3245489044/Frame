#nullable enable

using System.Windows.Forms;

namespace Share.ControlLookup;

/// <summary>
/// 统一控件树遍历原语（ControlTraverser）——收敛散落在 ControlFinder / TreeWalker / ControlTreeWalker 等处的
/// 「按名 / 按谓词 / 祖先 / 后代」递归逻辑为单点实现。内部统一采用：
///   - 按名深搜：<see cref="Control.ControlCollection.Find(string, bool)"/>（WinForms 原生递归，searchAllChildren=true）；
///   - 谓词遍历：显式栈（Stack&lt;Control&gt;）DFS，避免递归深度栈溢出（深层控件树）。
/// 支持可选的 <c>maxDepth</c> 深度限制（对齐既有 RecursionPolicy.MaxDepth 语义）：maxDepth &lt;= 0 表示不限制（无限深）。
/// </summary>
public static class ControlTraverser
{
    /// <summary>不限制深度的哨兵值。</summary>
    public const int UnlimitedDepth = 0;

    /// <summary>按名称递归查找子控件（统一 WinForms 原生 Controls.Find(name, true) 语义；深度不限）。返回首个命中，未命中返回 null。</summary>
    public static Control? FindByName(Control root, string name)
        => FindByName(root, name, UnlimitedDepth);

    /// <summary>按名称查找子控件；<paramref name="maxDepth"/> 指定最大下探层级（&lt;=0 表示不限，走 Controls.Find(name,true)）。</summary>
    public static Control? FindByName(Control root, string name, int maxDepth)
    {
        if (root == null || string.IsNullOrWhiteSpace(name))
            return null;

        if (maxDepth <= 0)
        {
            var found = root.Controls.Find(name, true);
            return found.Length > 0 ? found[0] : null;
        }

        return FindFirst(child => child.Name == name, root, maxDepth);
    }

    /// <summary>按谓词递归查找首个匹配子孙控件（深度不限；不含根自身）。</summary>
    public static Control? FindFirst(Control root, Func<Control, bool> predicate)
        => FindFirst(predicate, root, UnlimitedDepth);

    /// <summary>按谓词递归查找首个匹配子孙控件（显式栈 DFS，左优先；深度受限；不含根自身）。</summary>
    public static Control? FindFirst(Func<Control, bool> predicate, Control root, int maxDepth)
    {
        if (root == null || predicate == null)
            return null;

        // 显式栈 DFS：元素 = (控件, 深度)。逆序入栈子控件保证正序（自左向右）出栈。
        var stack = new Stack<Frame>();
        PushChildren(root, 1, maxDepth, stack);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (predicate(current.Control))
                return current.Control;

            PushChildren(current.Control, current.Depth + 1, maxDepth, stack);
        }

        return null;
    }

    /// <summary>按谓词收集全部匹配子孙控件（深度不限；不含根自身）。</summary>
    public static List<Control> FindAll(Control root, Func<Control, bool> predicate)
        => FindAll(predicate, root, UnlimitedDepth);

    /// <summary>按谓词收集全部匹配子孙控件（显式栈 DFS；深度受限；不含根自身）。</summary>
    public static List<Control> FindAll(Func<Control, bool> predicate, Control root, int maxDepth)
    {
        var results = new List<Control>();
        if (root == null || predicate == null)
            return results;

        var stack = new Stack<Frame>();
        PushChildren(root, 1, maxDepth, stack);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (predicate(current.Control))
                results.Add(current.Control);

            PushChildren(current.Control, current.Depth + 1, maxDepth, stack);
        }

        return results;
    }

    /// <summary>自下而上查找最近的满足谓词的祖先控件（含自身）；未命中返回 null。</summary>
    public static Control? FindNearestAncestor(Control control, Func<Control, bool> predicate)
    {
        if (control == null || predicate == null)
            return null;

        var node = control;
        while (node != null)
        {
            if (predicate(node))
                return node;
            node = node.Parent;
        }

        return null;
    }

    /// <summary>判断 control 是否为 ancestor 的后代（含直接子级，不含自身）。</summary>
    public static bool IsDescendantOf(Control control, Control ancestor)
    {
        if (control == null || ancestor == null)
            return false;

        var node = control.Parent;
        while (node != null)
        {
            if (ReferenceEquals(node, ancestor))
                return true;
            node = node.Parent;
        }

        return false;
    }

    /// <summary>控件在控件树中的深度（根=0）。</summary>
    public static int GetDepth(Control control)
    {
        if (control == null)
            return -1;

        int depth = 0;
        var node = control.Parent;
        while (node != null)
        {
            depth++;
            node = node.Parent;
        }

        return depth;
    }

    /// <summary>控件所属窗体。</summary>
    public static Form? FindForm(Control control)
        => control?.FindForm();

    /// <summary>仅当目标深度未超出 maxDepth 时，逆序入栈子控件。</summary>
    private static void PushChildren(Control parent, int childDepth, int maxDepth, Stack<Frame> stack)
    {
        if (maxDepth > 0 && childDepth > maxDepth)
            return;

        for (int i = parent.Controls.Count - 1; i >= 0; i--)
        {
            if (parent.Controls[i] is { } c)
                stack.Push(new Frame(c, childDepth));
        }
    }

    private readonly struct Frame(Control control, int depth)
    {
        public Control Control { get; } = control;
        public int Depth { get; } = depth;
    }
}

