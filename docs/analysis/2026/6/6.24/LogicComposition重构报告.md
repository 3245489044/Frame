# LogicComposition 重构报告

## 概述

本文档记录 WorkFlowMethod 合并逻辑基于 LogicComposition 通用框架的重构过程。

### 重构目标

1. **消除重复逻辑**：将 WorkFlowMethod 中分散的合并/去重/组合逻辑统一到 LogicComposition
2. **直接使用默认实现**：不引入适配器层，直接使用 `DefaultChainComposer<T>`、`DefaultMergeStrategy<T>`、`DefaultCompositionValidator`
3. **领域隔离**：NodeConfig 特有的引用处理保留在 `ConfigRelationHandler` 领域层
4. **向后兼容**：旧接口标记为 `[Obsolete]`，保留实现但不推荐使用

---

## 架构对比

### 重构前

```
WorkFlowMethod 合并逻辑（分散、重复）
├── ConfigDeduplicationProcessor（手动去重）
│   ├── BuildIdentifierMap（手动标识符收集）
│   ├── ResolveDuplicates（手动重命名）
│   └── ApplyRenames（手动引用更新）
├── WorkflowConnectorMergeStrategy（链合并）
│   └── Merge（手动节点合并）
├── ChainLinkProcessor（链连接）
│   └── InsertNode（手动插入）
└── WorkflowMediator（工作流中介）
    └── ConnectWorkflows（手动连接）
```

### 重构后

```
LogicComposition 默认实现（直接使用）
├── DefaultChainComposer<NodeConfig> ← 直接注入 keyExtractor
│   ├── Compose（序列组合 + 去重）
│   ├── Insert（节点插入）
│   └── Connect（序列连接）
├── DefaultMergeStrategy<NodeConfig> ← 直接使用 ObjectMerger
│   └── Merge（属性级合并）
└── DefaultCompositionValidator ← 直接使用
    └── Validate（基础验证）

领域层（保留）
├── ConfigRelationHandler（NodeConfig 引用字段处理）
├── ChainLinkProcessor（NodeConfig 链连接）
└── ConfigDeduplicationProcessor（注入 LogicComposition 默认实现）
```

---

## 改造文件清单

| 文件 | 路径 | 改造内容 |
|------|------|----------|
| `ConfigDeduplicationProcessor.cs` | `GraphUsing/Deduplication/` | 注入 `DefaultChainComposer<NodeConfig>` + `DefaultMergeStrategy<NodeConfig>` |
| `ConfigRelationHandler.cs` | `GraphUsing/Deduplication/` | 保留领域引用处理，不依赖 LogicComposition |
| `WorkflowMediator.cs` | `Execution/Chain/` | 注入 `DefaultChainComposer<NodeConfig>` 替代旧逻辑 |
| `ChainMergeStrategy.cs` | `Execution/Chain/` | 旧接口标记 `[Obsolete]`，委托给 `DefaultChainComposer` |
| `ChainLinkProcessor.cs` | `Execution/Chain/` | 保留领域链连接，不依赖 LogicComposition |
| `WorkflowRegistrar.cs` | `Execution/Chain/` | 注入 `DefaultChainComposer<NodeConfig>` 实现注册时去重 |
| `IWorkflowMediator.cs` | `Infrastructure/Abstractions/` | 添加重构说明注释 |
| `IDeduplicationProcessor.cs` | `Infrastructure/Abstractions/` | 添加重构说明注释 |

---

## 使用示例

### ConfigDeduplicationProcessor（注入方式）

```csharp
var processor = new ConfigDeduplicationProcessor(
    nodeHandler, config, nameGenerator, nodeRepository);
var result = processor.Deduplicate(chains);
// 内部自动使用 DefaultChainComposer<NodeConfig> 进行去重组合
```

### WorkflowMediator（直接使用）

```csharp
var mediator = new WorkflowMediator();
var merged = mediator.ConnectWorkflows(sourceChain, targetChain, request);
// 内部使用 DefaultChainComposer<NodeConfig>.Compose()
```

### 直接使用 LogicComposition（不经过领域类）

```csharp
var composer = new DefaultChainComposer<NodeConfig>(
    keyExtractor: n => n.basicConfig?.Callkey);

var result = composer.Compose(
    new[] { chain1.AllNodes, chain2.AllNodes },
    new CompositionOptions { DuplicateHandling = DuplicateHandling.Skip });
```

---

## 向后兼容

所有旧接口标记为 `[Obsolete]`，保留实现：

```csharp
[Obsolete("请使用 DefaultChainComposer<NodeConfig> 替代")]
public interface IChainMergeStrategy { ... }
```

旧代码无需修改即可编译运行，但会收到编译警告。

---

## 关键设计决策

1. **不引入适配器层**：LogicComposition 的 `DefaultChainComposer<T>` 已经支持 `keyExtractor` 和 `mergeStrategy` 注入，NodeConfig 直接作为泛型参数即可使用，不需要适配器包装。

2. **领域逻辑保留**：`ConfigRelationHandler`（引用字段更新）和 `ChainLinkProcessor`（链连接）是 NodeConfig 领域特有的，不抽象到 LogicComposition。

3. **ConfigDeduplicationProcessor 保留**：虽然去重组合委托给了 `DefaultChainComposer`，但标识符收集、重命名生成、引用更新仍然是 WorkFlowMethod 特有的流程，保留此处理器作为编排入口。
