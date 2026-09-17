namespace BasicMethod.Declarative.StructuralWalk;

/// <summary>
/// 结构遍历选项：<see cref="MaxDepth"/> ≤ 0 = 不限深度；<see cref="VisitRoot"/> false = 只访问子节点；
/// <see cref="SkipRepeatedReferences"/> true = 同一引用只下钻一次（防共享子树重复访问与自引用死循环）。
/// </summary>
public sealed record StructuralWalkOptions(
    int MaxDepth = 64,
    bool VisitRoot = true,
    bool SkipRepeatedReferences = true)
{
    /// <summary>缺省选项（深度 64 / 含根 / 引用去重）。</summary>
    public static readonly StructuralWalkOptions Default = new();
}

/// <summary>遍历统计：<see cref="Visited"/> = 实际访问次数；<see cref="Truncated"/> = 是否因深度上限丢弃了子树。</summary>
public readonly record struct StructuralWalkStats(int Visited, bool Truncated);

/// <summary>
/// 通用结构遍历骨架（零依赖）：前序下钻 + 显式栈 + 深度上限 + 引用环去重 + 同级保序。
/// <para>消费方以 <c>children</c> 函数式描述子节点（语义契约同 <c>Framework.Tree.TreeWalker&lt;TNode&gt;</c>），
/// 从而「遍历序 / 截断 / 去重 / 栈安全」四处口径只此一份，各领域不再各写递归。</para>
/// </summary>
public static class StructuralWalk
{
    /// <summary>前序访问全部节点；返回访问次数与截断标志（截断 = 深度上限丢子树，须由消费方可见化）。</summary>
    public static StructuralWalkStats Visit<TNode>(
        TNode root,
        Func<TNode, IEnumerable<TNode>> children,
        Action<TNode> visit,
        StructuralWalkOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(children);
        ArgumentNullException.ThrowIfNull(visit);

        var cursor = new Cursor<TNode>(options ?? StructuralWalkOptions.Default);
        var visited = 0;
        foreach (var node in cursor.Run(root, children))
        {
            visit(node);
            visited++;
        }

        return new StructuralWalkStats(visited, cursor.Truncated);
    }

    /// <summary>按前序收集全部节点（声明序）。</summary>
    public static IReadOnlyList<TNode> Collect<TNode>(
        TNode root,
        Func<TNode, IEnumerable<TNode>> children,
        StructuralWalkOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(children);

        var cursor = new Cursor<TNode>(options ?? StructuralWalkOptions.Default);
        var nodes = new List<TNode>();
        foreach (var node in cursor.Run(root, children)) nodes.Add(node);
        return nodes;
    }

    /// <summary>按前序查找首个命中节点（未命中 → default）。</summary>
    public static TNode? FindFirst<TNode>(
        TNode root,
        Func<TNode, IEnumerable<TNode>> children,
        Func<TNode, bool> predicate,
        StructuralWalkOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(children);
        ArgumentNullException.ThrowIfNull(predicate);

        var cursor = new Cursor<TNode>(options ?? StructuralWalkOptions.Default);
        foreach (var node in cursor.Run(root, children))
            if (predicate(node)) return node;

        return default;
    }

    /// <summary>遍历游标：承载「显式栈 + 深度 + 去重 + 截断」四项口径的唯一点。</summary>
    private sealed class Cursor<TNode>(StructuralWalkOptions options)
    {
        private readonly StructuralWalkOptions _options = options;
        private readonly HashSet<object>? _seen = options.SkipRepeatedReferences && !typeof(TNode).IsValueType
            ? new HashSet<object>(ReferenceEqualityComparer.Instance)
            : null;

        /// <summary>是否因深度上限丢弃过子树（供消费方把「截断」可见化，禁静默）。</summary>
        public bool Truncated { get; private set; }

        public IEnumerable<TNode> Run(TNode root, Func<TNode, IEnumerable<TNode>> children)
        {
            var stack = new Stack<(TNode Node, int Depth)>();
            stack.Push((root, 0));

            while (stack.Count > 0)
            {
                var (node, depth) = stack.Pop();

                if (node is null) continue;
                if (_seen is not null && !_seen.Add(node)) continue;

                if (depth > 0 || _options.VisitRoot) yield return node;

                var next = new List<TNode>();
                foreach (var child in children(node))
                    if (child is not null) next.Add(child);

                if (next.Count == 0) continue;

                if (_options.MaxDepth > 0 && depth + 1 > _options.MaxDepth)
                {
                    Truncated = true;
                    continue;
                }

                for (var i = next.Count - 1; i >= 0; i--) stack.Push((next[i], depth + 1));
            }
        }
    }
}
