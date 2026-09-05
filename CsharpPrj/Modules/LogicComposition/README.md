# LogicComposition — 通用逻辑组合框架

## 概述

**LogicComposition** 是一个与领域无关的通用逻辑组合框架，提供合并、组合、插入、连接、验证等操作的抽象接口和默认实现。

## 核心抽象

| 接口 | 职责 | 方法 |
|------|------|------|
| `IMergeStrategy<T>` | 两个同类型对象的合并 | `Merge(T source, T target, MergeOptions?)` |
| `IChainComposer<TElement>` | 序列（链）的组合操作 | `Compose` / `Insert` / `Connect` |
| `ICompositionValidator` | 组合操作的合法性验证 | `Validate(CompositionValidationContext)` |

## 数据模型

| 模型 | 说明 |
|------|------|
| `MergeResult<T>` | 合并结果（含合并后对象、冲突列表、统计信息） |
| `MergeConflict` | 合并冲突详情 |
| `MergeStatistics` | 合并统计信息 |
| `MergeOptions` | 合并选项（策略、字段/属性开关、跳过列表） |
| `CompositionOptions` | 组合选项（重复处理策略、验证开关） |
| `InsertPosition` | 插入位置（End/Start/After/Before/AtIndex） |
| `ConnectionPoint` | 连接点（ChainStart/ChainEnd/ByElementKey） |
| `ValidationResult` | 通用验证结果（错误/警告收集、合并、摘要） |

## 使用示例

### 配置合并

```csharp
// 创建合并策略
var mergeStrategy = new DefaultMergeStrategy<NodeConfig>();

// 合并配置
var result = mergeStrategy.Merge(
    source: templateConfig,
    target: userConfig,
    options: MergeOptions.SkipNullWithEmptyString);

if (result.IsSuccess)
{
    var mergedConfig = result.Result;
    Console.WriteLine($"合并成功，共处理 {result.Statistics.TotalItems} 项");
}
```

### 序列组合

```csharp
// 创建链组合器
var composer = new DefaultChainComposer<NodeConfig>(
    keyExtractor: n => n.basicConfig?.Callkey);

// 组合多个序列
var result = composer.Compose(
    new[] { chain1, chain2, chain3 },
    CompositionOptions.Overwrite);

if (result.IsSuccess)
{
    Console.WriteLine($"组合成功: {result.Statistics.AddedItems} added, " +
                      $"{result.Statistics.OverwrittenItems} overwritten");
}
```

### 元素插入

```csharp
// 在指定元素之后插入
var result = composer.Insert(
    chain,
    newElement,
    InsertPosition.After("element_key_3"));

// 在序列开头插入
var result2 = composer.Insert(
    chain,
    newElement,
    InsertPosition.Start());
```

### 序列连接

```csharp
// 连接两个序列（源序列的结束 → 目标序列的开始）
var result = composer.Connect(
    sourceChain,
    targetChain,
    ConnectionPoint.End(),
    ConnectionPoint.Start());
```

## 依赖关系

```
LogicComposition（独立项目，无外部依赖）
  内部包含：
    ├── ObjectMerger（通用对象属性合并工具）
    ├── TemplateConfigProvider<T>（配置模板提供器）
    ├── DefaultMergeStrategy<T>（默认合并策略实现）
    ├── DefaultChainComposer<TElement>（默认链组合器）
    └── DefaultCompositionValidator（默认组合验证器）
```

## 设计原则

- **单一职责**：每个接口只做一件事
- **接口隔离**：调用方只需依赖自己需要的接口
- **依赖倒置**：上层依赖抽象接口，而非具体实现
- **开闭原则**：新增策略只需实现接口，不修改现有代码
- **结果可观测**：所有操作返回 `MergeResult<T>`，包含冲突和统计信息
