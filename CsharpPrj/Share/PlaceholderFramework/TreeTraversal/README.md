# TreeTraversal — 通用树遍历框架

## 概述

通用树遍历框架，提供与具体节点类型无关的树遍历算法。任何树形结构只需实现 `ITreeNode<T>` 接口即可使用。

## 核心类型

| 类型 | 说明 |
|------|------|
| `ITreeNode<T>` | 树节点接口（CRTP 模式） |
| `TreeWalker` | 通用树遍历器（静态类） |
| `TraversalMode` | 遍历模式枚举（BFS/DFS） |

## 使用示例

### 1. 定义树节点

```csharp
using Common.PlaceholderFramework.TreeTraversal;

// 任何树形结构只需实现 ITreeNode<T>
public class MyNode : ITreeNode<MyNode>
{
    public string Name { get; }
    public List<MyNode> Children { get; } = new();

    public MyNode(string name) => Name = name;

    public IEnumerable<MyNode> GetChildren() => Children;
}
```

### 2. 遍历树

```csharp
// 创建树
var root = new MyNode("root");
root.Children.Add(new MyNode("child1"));
root.Children.Add(new MyNode("child2"));

// 广度优先遍历
TreeWalker.ForEach(root, node => Console.WriteLine(node.Name));

// 深度优先遍历
TreeWalker.ForEach(root, node => Console.WriteLine(node.Name), TraversalMode.DepthFirst);
```

### 3. 查找节点

```csharp
// 查找第一个匹配的节点
var found = TreeWalker.Find(new[] { root }, node => node.Name == "child1");

// 查找所有匹配的节点
var all = TreeWalker.FindAll(new[] { root }, node => node.Name.Contains("child"));
```

### 4. 收集属性

```csharp
// 收集所有节点的名称
var names = TreeWalker.Collect<MyNode, string>(
    new[] { root },
    node => node.Name);
```

### 5. 统计

```csharp
// 统计节点总数
var count = TreeWalker.Count(root);

// 计算最大深度
var depth = TreeWalker.MaxDepth(root);
```

### 6. 路径查找

```csharp
// 查找从根到匹配节点的路径
var path = TreeWalker.FindPath(new[] { root }, node => node.Name == "child1");
```

## 设计原则

1. **接口分离**：`ITreeNode<T>` 仅要求实现 `GetChildren()`，最小化侵入性
2. **栈替代递归**：所有遍历操作使用显式栈/队列，避免深层嵌套时的栈溢出
3. **泛型安全**：使用 CRTP 模式确保类型安全
4. **无领域依赖**：不绑定任何特定领域模型
