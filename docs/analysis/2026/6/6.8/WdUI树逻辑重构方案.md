# WdUI 树逻辑重构方案

> 基于 `FrameworkTree`（`BasicMethod/BasicAlgorithm/FrameworkTree/`）基础设施，对 `Operation/WdUI/` 中的树形结构逻辑进行系统性重构。
>
> **约束：** `FrameworkTree` 中只能新增**通用框架逻辑**（纯抽象，零外部依赖），WdUI 特有的 WinForms 代码必须在 WdUI 侧实现。

---

## 一、FrameworkTree 基础设施总览

| 组件 | 文件 | 能力 | 依赖 |
|------|------|------|------|
| **`ITreeNode`** | `TreeNode.cs` | 树节点接口：`Id`, `Parent`, `Children`, `AddChild`, `RemoveChild` | 无 |
| **`TreeNode`** | `TreeNode.cs` | 树节点基类：组件系统(`INodeComponent`)、行为系统(`IBehavior`)、生命周期(`Create/Destroy/Update`)、节点查找(`FindNode/FindNodes`) | `IContext` |
| **`TreeIndex<TNode, TKey>`** | `TreeIndex.cs` | 树索引：基于 `IMapRegistry`，支持 `Rebuild` 全量重建、`RegisterNode` 增量注册、`TraverseAndIndex` 递归遍历 | `IMapRegistry` |
| **`TreeAdapter<TNode>`** | `TreeIndex.cs` | 外部树适配器：将任意树结构包装为 `ITreeNode`，通过委托适配 | 无 |
| **`TreeNodeSystem`** | `TreeNodeSystem.cs` | ECS 集成：将 TreeNode 绑定到 ECS 实体，每帧更新 | ECS |
| **`ITreeSearch<TNode>`** | `ITreeSearch.cs` | 通用树搜索接口：按名称/类型/谓词查找 | 无 |
| **`TreeSearch<TNode>`** | `TreeSearch.cs` | 深度优先搜索默认实现 | 无 |

---

## 二、WdUI 中存在的树逻辑

### 2.1 控件查找树 — `ControlFinder.cs`（237 行）

**文件：** `Operation/WdUI/Framework/Shared/ControlFinder.cs`

**当前实现：** 两套重复的递归查找逻辑

| 查找路径 | 方法 | 特点 |
|---------|------|------|
| **路径A：基于 `TreeSearch<Control>`** | `FindByName`, `FindByType<T>`, `Find`, `FindAll`, `FindAllByType<T>` | 全局递归，无策略控制，使用反射按 Name 属性查找 |
| **路径B：手写递归 + `RecursionPolicy`** | `FindRecursive`, `FindByType<T>`, `FindByPredicate`, `FindAll` | 带深度限制、SplitContainer 特殊处理，4 个 Internal 方法高度重复 |

**问题：**
1. 两套逻辑功能重叠，维护成本翻倍
2. `TreeSearch` 使用反射（`GetProperty("Name")`），性能差
3. 手写递归的 4 个 Internal 方法结构高度相似，仅谓词不同
4. 每次查找都是 O(n) 遍历，无索引缓存

### 2.2 子容器树 — `ConfigContainer._subContainers`（WdUI.Container.cs）

**文件：** `Operation/WdUI/UI/WdUI.Container.cs`

**当前实现：**
```csharp
private readonly Dictionary<string, ConfigContainer> _subContainers = new(...);
```

**问题：**
1. 需要手动同步 `ControlAdded`/`ControlRemoved` 事件
2. `FindSubContainer` 有快速路径（字典）和回退路径（`ControlFinder.FindRecursive`），两套逻辑
3. 回退路径检测到注册遗漏时自动注册，但日志警告表明这是设计缺陷
4. 事件订阅/取消订阅逻辑分散在 `RegisterSubContainer`、`CreateSubContainer`、`ClearResources` 三处

### 2.3 布局树 — `ApplyLayoutSkeleton.ApplyLayoutRecursive`（ApplyLayoutSkeleton.cs）

**文件：** `Operation/WdUI/Layout/Skeletons/ApplyLayoutSkeleton.cs`

**当前实现：** 手写递归 `ApplyLayoutRecursive`，两遍遍历：
- 第一遍：缓存配置 + 应用比例布局 + 递归子控件
- 第二遍：处理 SplitContainer（SplitterDistance + Panel1/Panel2 子控件 + 布局后属性）

**问题：**
1. 递归逻辑与业务逻辑（比例布局、SplitterDistance）紧密耦合
2. `FindChild` 和 `FindPanel` 委托到 `ControlFinder`，依赖其两套查找逻辑
3. 没有统一的树遍历抽象，难以扩展新的布局策略

### 2.4 创建控件树 — `CreateLayoutPanelSkeleton.CreateControlWithChildren`（CreateLayoutPanelSkeleton.cs）

**文件：** `Operation/WdUI/Layout/Skeletons/CreateLayoutPanelSkeleton.cs`

**当前实现：** 手写递归创建控件树，处理 `SplitContainer.Panel1Controls/Panel2Controls` 和普通子控件

**问题：**
1. 递归创建逻辑与 `ConfigContainer` 注册逻辑耦合
2. `FindNearestConfigContainerAncestor` 是手写的 Parent 链遍历
3. 创建完成后没有统一的树结构表示，后续布局仍需通过 `ControlFinder` 重新查找

---

## 三、FrameworkTree 可新增的通用框架逻辑

以下逻辑是**纯抽象的、不依赖 WinForms 的通用能力**，可以放在 `FrameworkTree` 中。

### 3.1 新增：`TreeWalker<TNode>` — 通用树遍历器

**位置：** `BasicMethod/BasicAlgorithm/FrameworkTree/TreeWalker.cs`

**动机：** `ControlFinder` 和 `ApplyLayoutRecursive` 中都有手写的递归遍历逻辑，且 `TreeSearch` 只支持深度优先搜索。需要一个更通用的树遍历器，支持多种遍历策略。

```csharp
// ====================================================================
// TreeWalker.cs — 通用树遍历器
// 纯抽象，零外部依赖，支持多种遍历策略
// ====================================================================
namespace Framework.Tree
{
    /// <summary>
    /// 树遍历策略
    /// </summary>
    public enum TraversalStrategy
    {
        /// <summary>深度优先搜索（DFS）</summary>
        DepthFirst,
        /// <summary>广度优先搜索（BFS）</summary>
        BreadthFirst,
        /// <summary>先序遍历（Parent → Children）</summary>
        PreOrder,
        /// <summary>后序遍历（Children → Parent）</summary>
        PostOrder,
    }

    /// <summary>
    /// 树遍历配置
    /// </summary>
    public class TraversalOptions
    {
        /// <summary>遍历策略</summary>
        public TraversalStrategy Strategy { get; set; } = TraversalStrategy.DepthFirst;
        
        /// <summary>最大深度（0=不限制）</summary>
        public int MaxDepth { get; set; } = 0;
        
        /// <summary>是否包含根节点</summary>
        public bool IncludeRoot { get; set; } = true;
    }

    /// <summary>
    /// 通用树遍历器 — 支持多种遍历策略和深度限制
    /// <para>适用于任何树形结构：UI 控件树、文件系统、场景图等</para>
    /// </summary>
    /// <typeparam name="TNode">节点类型</typeparam>
    public class TreeWalker<TNode>
    {
        private readonly Func<TNode, IEnumerable<TNode>> _getChildren;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="getChildren">获取子节点集合的委托</param>
        public TreeWalker(Func<TNode, IEnumerable<TNode>> getChildren)
        {
            _getChildren = getChildren ?? throw new ArgumentNullException(nameof(getChildren));
        }

        /// <summary>
        /// 遍历树，对每个节点执行 action
        /// </summary>
        public void Walk(TNode root, Action<TNode, int> action, TraversalOptions? options = null)
        {
            options ??= new TraversalOptions();
            
            switch (options.Strategy)
            {
                case TraversalStrategy.BreadthFirst:
                    WalkBFS(root, action, options);
                    break;
                case TraversalStrategy.PostOrder:
                    WalkPostOrder(root, action, options, 0);
                    break;
                case TraversalStrategy.DepthFirst:
                case TraversalStrategy.PreOrder:
                default:
                    WalkPreOrder(root, action, options, 0);
                    break;
            }
        }

        /// <summary>
        /// 查找第一个匹配的节点
        /// </summary>
        public TNode? FindFirst(TNode root, Func<TNode, bool> predicate, TraversalOptions? options = null)
        {
            TNode? result = default;
            Walk(root, (node, _) =>
            {
                if (result == null && predicate(node))
                    result = node;
            }, options);
            return result;
        }

        /// <summary>
        /// 查找所有匹配的节点
        /// </summary>
        public List<TNode> FindAll(TNode root, Func<TNode, bool> predicate, TraversalOptions? options = null)
        {
            var results = new List<TNode>();
            Walk(root, (node, _) =>
            {
                if (predicate(node))
                    results.Add(node);
            }, options);
            return results;
        }

        /// <summary>
        /// 获取从根到目标节点的路径
        /// </summary>
        public List<TNode> GetPathTo(TNode root, TNode target)
        {
            var path = new List<TNode>();
            if (FindPath(root, target, path))
                return path;
            return new List<TNode>();
        }

        private bool FindPath(TNode current, TNode target, List<TNode> path)
        {
            path.Add(current);
            if (EqualityComparer<TNode>.Default.Equals(current, target))
                return true;

            foreach (var child in _getChildren(current))
            {
                if (FindPath(child, target, path))
                    return true;
            }

            path.RemoveAt(path.Count - 1);
            return false;
        }

        // 先序遍历（DFS）
        private void WalkPreOrder(TNode node, Action<TNode, int> action, TraversalOptions options, int depth)
        {
            if (options.MaxDepth > 0 && depth > options.MaxDepth) return;
            if (depth > 0 || options.IncludeRoot)
                action(node, depth);

            foreach (var child in _getChildren(node))
            {
                if (child != null)
                    WalkPreOrder(child, action, options, depth + 1);
            }
        }

        // 后序遍历
        private void WalkPostOrder(TNode node, Action<TNode, int> action, TraversalOptions options, int depth)
        {
            if (options.MaxDepth > 0 && depth > options.MaxDepth) return;

            foreach (var child in _getChildren(node))
            {
                if (child != null)
                    WalkPostOrder(child, action, options, depth + 1);
            }

            if (depth > 0 || options.IncludeRoot)
                action(node, depth);
        }

        // 广度优先遍历（BFS）
        private void WalkBFS(TNode root, Action<TNode, int> action, TraversalOptions options)
        {
            var queue = new Queue<(TNode Node, int Depth)>();
            if (options.IncludeRoot)
                queue.Enqueue((root, 0));

            while (queue.Count > 0)
            {
                var (node, depth) = queue.Dequeue();
                if (options.MaxDepth > 0 && depth > options.MaxDepth) continue;

                action(node, depth);

                foreach (var child in _getChildren(node))
                {
                    if (child != null)
                        queue.Enqueue((child, depth + 1));
                }
            }
        }
    }
}
```

**预期用途：**
- WdUI 的 `ControlFinder` 可以用 `TreeWalker<Control>` 替代手写递归
- `ApplyLayoutRecursive` 可以用 `TreeWalker<ControlConfig>` 替代手写两遍遍历
- 任何需要树遍历的场景都可以复用

### 3.2 新增：`TreePathFinder` — 树路径查找工具

**位置：** `BasicMethod/BasicAlgorithm/FrameworkTree/TreePathFinder.cs`

**动机：** `CreateLayoutPanelSkeleton.FindNearestConfigContainerAncestor` 是手写的 Parent 链遍历，`ControlFinder` 中也有深度验证逻辑。需要一个通用的祖先查找工具。

```csharp
// ====================================================================
// TreePathFinder.cs — 树路径查找工具
// 纯抽象，零外部依赖，适用于任意树结构
// ====================================================================
namespace Framework.Tree
{
    /// <summary>
    /// 树路径查找工具 — 提供通用的祖先/后代/路径查找能力
    /// <para>适用于任意树结构，通过委托适配</para>
    /// </summary>
    /// <typeparam name="TNode">节点类型</typeparam>
    public class TreePathFinder<TNode>
    {
        private readonly Func<TNode, TNode?> _getParent;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="getParent">获取父节点的委托</param>
        public TreePathFinder(Func<TNode, TNode?> getParent)
        {
            _getParent = getParent ?? throw new ArgumentNullException(nameof(getParent));
        }

        /// <summary>
        /// 查找最近的满足条件的祖先节点
        /// </summary>
        public TNode? FindNearestAncestor(TNode node, Func<TNode, bool> predicate)
        {
            var current = _getParent(node);
            while (current != null)
            {
                if (predicate(current))
                    return current;
                current = _getParent(current);
            }
            return default;
        }

        /// <summary>
        /// 获取从节点到根节点的路径（从近到远）
        /// </summary>
        public List<TNode> GetPathToRoot(TNode node)
        {
            var path = new List<TNode>();
            var current = node;
            while (current != null)
            {
                path.Add(current);
                current = _getParent(current);
            }
            return path;
        }

        /// <summary>
        /// 获取两个节点的深度差（node 相对于 ancestor 的深度）
        /// <para>如果 ancestor 不是 node 的祖先，返回 -1</para>
        /// </summary>
        public int GetDepthDifference(TNode node, TNode ancestor)
        {
            int depth = 0;
            var current = _getParent(node);
            while (current != null)
            {
                if (EqualityComparer<TNode>.Default.Equals(current, ancestor))
                    return depth + 1;
                depth++;
                current = _getParent(current);
            }
            return -1;
        }

        /// <summary>
        /// 判断 target 是否在 root 的指定深度范围内
        /// </summary>
        public bool IsWithinDepth(TNode root, TNode target, int maxDepth)
        {
            int depth = 0;
            var current = _getParent(target);
            while (current != null)
            {
                if (EqualityComparer<TNode>.Default.Equals(current, root))
                    return depth < maxDepth;
                depth++;
                if (depth >= maxDepth) return false;
                current = _getParent(current);
            }
            return false;
        }

        /// <summary>
        /// 判断 target 是否是 root 的后代
        /// </summary>
        public bool IsDescendantOf(TNode node, TNode ancestor)
        {
            var current = _getParent(node);
            while (current != null)
            {
                if (EqualityComparer<TNode>.Default.Equals(current, ancestor))
                    return true;
                current = _getParent(current);
            }
            return false;
        }
    }
}
```

**预期用途：**
- WdUI 的 `FindNearestConfigContainerAncestor` 可以用 `TreePathFinder<Control>.FindNearestAncestor` 替代
- `ControlFinder` 的深度验证可以用 `TreePathFinder<Control>.IsWithinDepth` 替代
- 任何需要 Parent 链遍历的场景都可以复用

### 3.3 新增：`TreeIndex` 增强 — 支持 `GetAll` 批量获取

**位置：** `BasicMethod/BasicAlgorithm/FrameworkTree/TreeIndex.cs`（扩展现有类）

**动机：** `ConfigContainer.GetSubContainers()` 和 `ControlIndex.FindAll` 都需要批量获取所有已注册的节点。当前 `TreeIndex` 继承 `MapRegistryBase`，需要确认 `GetAll` 方法是否已存在。

如果 `MapRegistryBase` 已有 `GetAll` 方法，则无需新增。如果不存在，可以添加：

```csharp
// 在 TreeIndex 中新增（如果 MapRegistryBase 没有 GetAll）
public IReadOnlyDictionary<TKey, TNode> GetAll() 
    => new Dictionary<TKey, TNode>(Store); // Store 是 MapRegistryBase 的内部字典
```

---

## 四、WdUI 侧的重构方案（基于 FrameworkTree 新增能力）

### 🔥 优先级 1：控件查找树重构 — 基于 `TreeIndex<Control, string>` + `TreeWalker<Control>`

**全部在 WdUI 侧实现，不涉及 FrameworkTree。**

#### 方案

```csharp
// ====================================================================
// ControlIndex.cs — 基于 TreeIndex 的控件索引（WdUI 侧）
// ====================================================================
using Framework.Tree;

namespace WdUI.Framework.Shared
{
    /// <summary>
    /// 控件索引 — 基于 TreeIndex 的 O(1) 控件查找
    /// <para>全在 WdUI 侧实现，仅使用 FrameworkTree 的 TreeIndex 泛型能力</para>
    /// </summary>
    public class ControlIndex
    {
        private readonly TreeIndex<Control, string> _index;
        
        public ControlIndex()
        {
            _index = new TreeIndex<Control, string>(StringComparer.OrdinalIgnoreCase);
        }
        
        public void Rebuild(Control root)
        {
            _index.Rebuild(
                root,
                getChildren: c => c.Controls.Cast<Control>(),
                keyExtractor: c => string.IsNullOrEmpty(c.Name) ? null : c.Name);
        }
        
        public void Register(Control control)
        {
            if (!string.IsNullOrEmpty(control.Name))
                _index.RegisterNode(control, control.Name);
        }
        
        public void Unregister(Control control)
        {
            if (!string.IsNullOrEmpty(control.Name))
                _index.UnregisterNode(control.Name);
        }
        
        public Control? FindByName(string name) => _index.Get(name);
        
        // ... 其他方法
    }
}
```

#### 使用 `TreeWalker<Control>` 替代手写递归

```csharp
// ControlFinder 中新增基于 TreeWalker 的方法
private static readonly TreeWalker<Control> _treeWalker = new(c => c.Controls.Cast<Control>());

public static Control? FindRecursive(Control parent, string name, RecursionPolicy? policy = null)
{
    policy ??= RecursionPolicy.Default;
    return _treeWalker.FindFirst(parent, 
        c => c.Name == name,
        new TraversalOptions { MaxDepth = policy.MaxDepth });
}

public static List<Control> FindAll(Control parent, string name, RecursionPolicy? policy = null)
{
    policy ??= RecursionPolicy.Default;
    return _treeWalker.FindAll(parent,
        c => c.Name == name,
        new TraversalOptions { MaxDepth = policy.MaxDepth });
}
```

#### 使用 `TreePathFinder<Control>` 替代 Parent 链遍历

```csharp
// 在 ConfigContainer 或 ControlFinder 中
private static readonly TreePathFinder<Control> _pathFinder = new(c => c.Parent);

// 替代 FindNearestConfigContainerAncestor
private static ConfigContainer? FindNearestConfigContainer(Control control)
    => _pathFinder.FindNearestAncestor(control, c => c is ConfigContainer) as ConfigContainer;

// 替代深度验证
private static bool IsWithinDepth(Control root, Control target, int maxDepth)
    => _pathFinder.IsWithinDepth(root, target, maxDepth);
```

#### 迁移步骤

1. 新建 `ControlIndex` 类（WdUI 侧），封装 `TreeIndex<Control, string>`
2. 在 `RenderPipeline` 中添加 `ControlIndex` 实例
3. 将 `ControlFinder` 中基于 `TreeSearch` 的方法委托到 `ControlIndex`
4. 将 `ControlFinder` 中带 `RecursionPolicy` 的方法改为基于 `TreeWalker<Control>` + `TreePathFinder<Control>`
5. 删除 `ControlFinder` 中不再需要的 4 个 Internal 递归方法

---

### 🔥 优先级 2：子容器树重构 — 基于 `TreeIndex<ConfigContainer, string>`

**全部在 WdUI 侧实现。**

#### 方案

```csharp
// ConfigContainer 中
private readonly TreeIndex<ConfigContainer, string> _subContainerIndex = new(
    StringComparer.OrdinalIgnoreCase);

// 移除 ControlAdded/ControlRemoved 事件处理器
// 简化 FindSubContainer：O(1)，无需回退路径
public ConfigContainer? FindSubContainer(string name)
{
    if (string.IsNullOrEmpty(name)) return null;
    return _subContainerIndex.Get(name);
}
```

#### 迁移步骤

1. 将 `_subContainers` 字段类型从 `Dictionary` 改为 `TreeIndex<ConfigContainer, string>`
2. 移除 `OnControlAddedForSubContainerSync` 和 `OnControlRemovedForSubContainerSync`
3. 简化 `FindSubContainer`：移除回退路径
4. 更新 `RegisterSubContainer`、`CreateSubContainer`、`ClearResources`、`GetSubContainers`

---

### 🟡 优先级 3：布局树重构 — 基于 `TreeWalker<ControlConfig>` + `INodeComponent`

**全部在 WdUI 侧实现。**

#### 方案

使用 `TreeWalker<ControlConfig>` 替代 `ApplyLayoutRecursive` 的手写递归：

```csharp
// ApplyLayoutSkeleton 中
private static readonly TreeWalker<ControlConfig> _configWalker = new(cfg => cfg.Controls ?? new());

private void ApplyLayoutViaWalker(Control parent, List<ControlConfig> configs)
{
    // 第一遍：先序遍历 — 缓存配置 + 应用比例布局
    foreach (var cfg in configs)
    {
        _configWalker.Walk(cfg, (node, depth) =>
        {
            var control = FindChild(parent, node.Name);
            if (control == null) return;
            
            // 缓存配置
            context.State.GetOrCreate(control).Config = node;
            
            // 应用比例布局
            if (node.Layout != null)
                WdUILayout.ApplyProportional(control, ...);
        }, new TraversalOptions { Strategy = TraversalStrategy.PreOrder });
    }
    
    // 第二遍：后序遍历 — 处理 SplitContainer（子控件布局完成后）
    foreach (var cfg in configs)
    {
        _configWalker.Walk(cfg, (node, depth) =>
        {
            var control = FindChild(parent, node.Name);
            if (control is SplitContainer splitter)
            {
                ApplySplitterDistance(splitter, context);
                ApplyPostLayoutProperties(splitter, node, context);
            }
        }, new TraversalOptions { Strategy = TraversalStrategy.PostOrder });
    }
}
```

---

### 🟡 优先级 4：创建控件树重构 — 基于 `TreeAdapter<Control>`

**全部在 WdUI 侧实现。**

#### 方案

```csharp
// CreateLayoutPanelSkeleton 中
private Control ExecuteCreateLayoutPanel(Control input, SkeletonContext ctx)
{
    // ... 现有创建逻辑 ...
    
    // 创建完成后，构建适配树并存入上下文
    var adapter = new TreeAdapter<Control>(
        input,
        getChildren: c => c.Controls.Cast<Control>(),
        idExtractor: c => c.Name ?? c.GetHashCode().ToString());
    
    ctx.SetData("ControlTreeAdapter", adapter);
    return input;
}
```

---

## 五、FrameworkTree 新增内容总结

| 新增组件 | 文件 | 能力 | 用途 |
|---------|------|------|------|
| **`TreeWalker<TNode>`** | `TreeWalker.cs` | 通用树遍历器：DFS/BFS/先序/后序、深度限制、查找、路径 | 替代 ControlFinder 和 ApplyLayoutRecursive 的手写递归 |
| **`TreePathFinder<TNode>`** | `TreePathFinder.cs` | 树路径查找：祖先查找、深度差、后代判断 | 替代 FindNearestConfigContainerAncestor 和深度验证 |
| **`TreeIndex.GetAll()`**（可选） | `TreeIndex.cs` | 批量获取所有已注册节点 | 替代 _subContainers.Values 遍历 |

这些新增组件都是**纯抽象的、零外部依赖**的通用框架逻辑，不包含任何 WinForms 引用。

---

## 六、重构优先级矩阵

| 优先级 | 树逻辑 | 当前问题 | 使用 FrameworkTree 能力 | 实现位置 |
|--------|--------|---------|----------------------|---------|
| **🔥 P1** | **控件查找树** (ControlFinder) | 两套重复递归、O(n) 查找、反射 | `TreeIndex` O(1) 索引 + `TreeWalker` + `TreePathFinder` | WdUI 侧 |
| **🔥 P2** | **子容器树** (_subContainers) | 手动同步、快速+回退两套路径 | `TreeIndex` 自动索引 + 增量注册 | WdUI 侧 |
| **🟡 P3** | **布局树** (ApplyLayoutRecursive) | 手写递归、业务耦合 | `TreeWalker` 遍历策略 + `INodeComponent` | WdUI 侧 |
| **🟡 P4** | **创建控件树** (CreateControlWithChildren) | 创建后无统一树表示 | `TreeAdapter<Control>` 适配 | WdUI 侧 |
| **🟢 P5** | **视图管理树** (ViewManager) | 已较清晰 | 收益有限 | 可选 |
| **🟢 P6** | **布局策略树** (StrategyRegistry) | 注册表模式 | 不适用 | 无需重构 |

---

## 七、实施路线图

### 阶段零：FrameworkTree 新增通用组件

```
Step 1: 新增 TreeWalker<TNode>（TreeWalker.cs）
Step 2: 新增 TreePathFinder<TNode>（TreePathFinder.cs）
Step 3: 确认/新增 TreeIndex.GetAll()
```

### 阶段一：P1 + P2（核心索引化）

```
Step 4: WdUI 侧实现 ControlIndex（基于 TreeIndex<Control, string>）
Step 5: ControlFinder 迁移到 ControlIndex + TreeWalker + TreePathFinder
Step 6: ConfigContainer._subContainers 迁移到 TreeIndex
```

### 阶段二：P3 + P4（布局树解耦）

```
Step 7: ApplyLayoutSkeleton 使用 TreeWalker<ControlConfig> 替代手写递归
Step 8: CreateLayoutPanelSkeleton 集成 TreeAdapter<Control>
```

### 阶段三：集成测试 + 清理

```
Step 9: 集成测试 + 性能对比
Step 10: 删除废弃代码 + 文档更新
```

---

## 八、风险与注意事项

1. **兼容性：** `ControlFinder` 被多处代码引用，重构时需要确保所有调用点同步更新
2. **性能：** `TreeIndex.Rebuild` 全量重建是 O(n)，建议在 `SuspendLayout` 期间执行
3. **线程安全：** `TreeIndex` 继承自 `MapRegistryBase`，需要确认其线程安全性
4. **增量更新：** `ControlAdded`/`ControlRemoved` 事件的订阅时机需要仔细设计
5. **回退路径：** 移除 `FindSubContainer` 的回退路径前，需要确保所有 `ConfigContainer` 都通过 `CreateLayoutPanelSkeleton` 或 `RegisterSubContainer` 注册
