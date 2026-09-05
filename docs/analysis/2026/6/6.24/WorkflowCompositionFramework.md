# WorkflowCompositionFramework — 工作流逻辑组合框架设计文档

> **版本**：v1.0  
> **目标位置**：`Share/WorkflowExecution/Composition/`  
> **状态**：设计阶段  
> **关联项目**：WorkFlowPrj

---

## 1. 背景与动机

### 1.1 现状问题

在 `Operation/WorkFlowMethod` 中，存在多套分散的"逻辑合并/组合"代码，它们职责重叠但互不关联：

| 组件 | 位置 | 职责 | 操作类型 |
|------|------|------|---------|
| `NodeConfigFactory.CompleteConfig` | `Orchestration/State/` | 配置模板合并 | 属性级合并 |
| `WorkflowConnectorMergeStrategy` | `Execution/Chain/` | 链合并策略 | 集合级合并 |
| `WorkflowMediator` | `Execution/Chain/` | 工作流中介器 | 编排级组合 |
| `ChainLinkProcessor` | `Execution/Chain/` | 节点链接 | 关系级链接 |
| `ObjectMerger` | `Share/FrameworkAbstractions/` | 对象属性合并 | 反射级合并 |
| `TemplateConfigProvider` | `Share/FrameworkAbstractions/` | 模板配置提供 | 模板级管理 |

### 1.2 核心痛点

1. **无统一抽象**：`IChainMergeStrategy` 只能合并 `ChainInfo`，`ObjectMerger` 只能合并同类型对象，没有泛型合并接口
2. **无结果反馈**：合并操作没有返回值（`void`），调用方无法感知合并过程中是否发生冲突
3. **职责边界模糊**：`WorkflowMediator` 同时承担了合并、链接、构建三种职责
4. **重复定义**：`ValidationResult` 在 `WorkflowBuilder` 和 `JsonElementExtensions` 中各有一份

### 1.3 目标

在 `Share/WorkflowExecution` 中构建一个**与类型无关的统一逻辑组合框架**，将合并、插入、连接、验证等操作抽象为通用接口，让 Operation 层的具体实现通过实现这些接口来统一。

---

## 2. 架构概览

### 2.1 目录结构

```
Share/WorkflowExecution/
├── Composition/                          # [新增] 逻辑组合框架
│   ├── Abstractions/                     # 抽象接口层
│   │   ├── IMergeStrategy.cs             # 泛型合并策略接口
│   │   ├── IChainComposer.cs             # 链组合器接口
│   │   └── ICompositionValidator.cs      # 组合验证器接口
│   ├── Models/                           # 数据模型层
│   │   ├── MergeResult.cs                # 合并结果模型
│   │   ├── CompositionOptions.cs         # 组合选项
│   │   ├── InsertPosition.cs             # 插入位置定义
│   │   ├── ConnectionPoint.cs            # 连接点定义
│   │   └── ValidationResult.cs           # 通用验证结果模型
│   ├── DefaultImplementations/           # 默认实现层
│   │   ├── DefaultMergeStrategy.cs       # 默认合并策略（基于 ObjectMerger）
│   │   ├── DefaultChainComposer.cs       # 默认链组合器
│   │   └── DefaultCompositionValidator.cs# 默认组合验证器
│   └── README.md                         # 使用说明
├── Models/                               # 已有
├── Engine/                               # 已有
├── Skeletons/                            # 已有
└── Extensions/                           # 已有
```

### 2.2 分层依赖

```
┌─────────────────────────────────────────────────┐
│  Operation/WorkFlowMethod                        │
│  (具体实现层：NodeConfigFactory, WorkflowMediator) │
├─────────────────────────────────────────────────┤
│  Share/WorkflowExecution/Composition/            │
│  (抽象框架层：IMergeStrategy, IChainComposer)      │
├─────────────────────────────────────────────────┤
│  Share/FrameworkAbstractions/                    │
│  (基础工具层：ObjectMerger, TemplateConfigProvider)│
└─────────────────────────────────────────────────┘
```

---

## 3. 核心接口定义

### 3.1 `IMergeStrategy<T>` — 泛型合并策略

```csharp
/// <summary>
/// 泛型合并策略接口。
/// 定义两个同类型对象的合并契约，支持选项配置和结果反馈。
/// </summary>
/// <typeparam name="T">要合并的对象类型</typeparam>
public interface IMergeStrategy<T>
{
    /// <summary>
    /// 将 source 合并到 target，返回合并结果
    /// </summary>
    /// <param name="source">源对象（提供覆盖值）</param>
    /// <param name="target">目标对象（被覆盖方）</param>
    /// <param name="options">合并选项</param>
    /// <returns>合并结果，包含合并后的对象和冲突信息</returns>
    MergeResult<T> Merge(T source, T target, MergeOptions? options = null);
}
```

**设计要点**：
- 泛型 `T` 使接口适用于任何类型（`NodeConfig`、`ChainInfo`、`WorkflowDefinition` 等）
- 返回 `MergeResult<T>` 而非 `void`，让调用方感知合并结果
- `MergeOptions` 支持策略配置（覆盖/跳过/插入等）

### 3.2 `IChainComposer<TNode>` — 链组合器

```csharp
/// <summary>
/// 链组合器接口。
/// 定义链式结构的组合操作：合并、插入、连接。
/// </summary>
/// <typeparam name="TNode">节点类型</typeparam>
public interface IChainComposer<TNode>
{
    /// <summary>
    /// 将多个链组合为一个链
    /// </summary>
    /// <param name="chains">多个链的枚举</param>
    /// <param name="options">组合选项</param>
    /// <returns>组合结果</returns>
    MergeResult<IEnumerable<TNode>> Compose(
        IEnumerable<IEnumerable<TNode>> chains,
        CompositionOptions? options = null);

    /// <summary>
    /// 在链的指定位置插入一个节点
    /// </summary>
    /// <param name="chain">目标链</param>
    /// <param name="node">要插入的节点</param>
    /// <param name="position">插入位置</param>
    /// <returns>插入后的链</returns>
    MergeResult<IEnumerable<TNode>> Insert(
        IEnumerable<TNode> chain,
        TNode node,
        InsertPosition position);

    /// <summary>
    /// 连接两个链（源链的出口连接到目标链的入口）
    /// </summary>
    /// <param name="sourceChain">源链</param>
    /// <param name="targetChain">目标链</param>
    /// <param name="sourcePoint">源链的连接点</param>
    /// <param name="targetPoint">目标链的连接点</param>
    /// <returns>连接后的链</returns>
    MergeResult<IEnumerable<TNode>> Connect(
        IEnumerable<TNode> sourceChain,
        IEnumerable<TNode> targetChain,
        ConnectionPoint sourcePoint,
        ConnectionPoint targetPoint);
}
```

**设计要点**：
- 泛型 `TNode` 使接口适用于任何节点类型
- 三种操作（Compose/Insert/Connect）覆盖了所有链组合场景
- 返回 `MergeResult` 提供结果反馈

### 3.3 `ICompositionValidator` — 组合验证器

```csharp
/// <summary>
/// 组合验证器接口。
/// 在合并/组合操作前验证数据的合法性。
/// </summary>
public interface ICompositionValidator
{
    /// <summary>
    /// 验证组合操作是否合法
    /// </summary>
    /// <param name="context">验证上下文</param>
    /// <returns>验证结果</returns>
    ValidationResult Validate(CompositionValidationContext context);
}

/// <summary>
/// 组合验证上下文
/// </summary>
public class CompositionValidationContext
{
    /// <summary>操作类型（Merge/Insert/Connect）</summary>
    public string OperationType { get; set; } = string.Empty;

    /// <summary>源对象描述</summary>
    public string SourceDescription { get; set; } = string.Empty;

    /// <summary>目标对象描述</summary>
    public string TargetDescription { get; set; } = string.Empty;

    /// <summary>自定义验证规则</summary>
    public Dictionary<string, object> CustomRules { get; set; } = new();
}
```

---

## 4. 数据模型定义

### 4.1 `MergeResult<T>` — 合并结果

```csharp
/// <summary>
/// 合并结果模型。
/// 提供合并操作的结果反馈，包括合并后的对象、冲突信息和错误信息。
/// </summary>
/// <typeparam name="T">合并的对象类型</typeparam>
public class MergeResult<T>
{
    /// <summary>合并后的对象</summary>
    public T? Result { get; }

    /// <summary>是否成功</summary>
    public bool IsSuccess { get; }

    /// <summary>合并过程中检测到的冲突列表</summary>
    public IReadOnlyList<MergeConflict> Conflicts { get; }

    /// <summary>错误信息（失败时）</summary>
    public string? ErrorMessage { get; }

    /// <summary>合并统计信息</summary>
    public MergeStatistics Statistics { get; }

    private MergeResult(T? result, bool isSuccess,
        IReadOnlyList<MergeConflict>? conflicts,
        string? errorMessage, MergeStatistics? statistics)
    {
        Result = result;
        IsSuccess = isSuccess;
        Conflicts = conflicts ?? Array.Empty<MergeConflict>();
        ErrorMessage = errorMessage;
        Statistics = statistics ?? new MergeStatistics();
    }

    public static MergeResult<T> Success(T result,
        IReadOnlyList<MergeConflict>? conflicts = null,
        MergeStatistics? statistics = null) =>
        new(result, true, conflicts, null, statistics);

    public static MergeResult<T> Failure(string error,
        T? partialResult = default) =>
        new(partialResult, false, null, error, null);
}

/// <summary>合并冲突信息</summary>
public class MergeConflict
{
    /// <summary>冲突的键/属性名</summary>
    public string Key { get; }

    /// <summary>源对象的值</summary>
    public object? SourceValue { get; }

    /// <summary>目标对象的值</summary>
    public object? TargetValue { get; }

    /// <summary>解决方式（Overwritten/Skipped/Merged）</summary>
    public string Resolution { get; }

    public MergeConflict(string key, object? sourceValue,
        object? targetValue, string resolution)
    {
        Key = key;
        SourceValue = sourceValue;
        TargetValue = targetValue;
        Resolution = resolution;
    }
}

/// <summary>合并统计信息</summary>
public class MergeStatistics
{
    /// <summary>总处理项数</summary>
    public int TotalItems { get; set; }

    /// <summary>实际覆盖项数</summary>
    public int OverwrittenItems { get; set; }

    /// <summary>跳过的项数</summary>
    public int SkippedItems { get; set; }

    /// <summary>新增的项数</summary>
    public int AddedItems { get; set; }

    /// <summary>冲突项数</summary>
    public int ConflictCount => Conflicts.Count;

    /// <summary>冲突详情</summary>
    public List<MergeConflict> Conflicts { get; set; } = new();
}
```

### 4.2 `CompositionOptions` — 组合选项

```csharp
/// <summary>
/// 组合选项。
/// 控制合并/组合操作的行为。
/// </summary>
public class CompositionOptions
{
    /// <summary>重复节点处理策略</summary>
    public DuplicateHandling DuplicateHandling { get; set; } = DuplicateHandling.Skip;

    /// <summary>是否在合并前验证</summary>
    public bool ValidateBeforeMerge { get; set; } = true;

    /// <summary>是否记录冲突详情</summary>
    public bool TrackConflicts { get; set; } = true;

    /// <summary>自定义键提取器（用于判断节点是否重复）</summary>
    public Func<object, string?>? KeyExtractor { get; set; }

    /// <summary>默认组合选项</summary>
    public static readonly CompositionOptions Default = new();

    /// <summary>覆盖模式（重复时覆盖）</summary>
    public static readonly CompositionOptions Overwrite = new()
    {
        DuplicateHandling = DuplicateHandling.Overwrite
    };

    /// <summary>跳过模式（重复时跳过）</summary>
    public static readonly CompositionOptions Skip = new()
    {
        DuplicateHandling = DuplicateHandling.Skip
    };
}

/// <summary>重复节点处理策略</summary>
public enum DuplicateHandling
{
    /// <summary>跳过重复节点</summary>
    Skip,

    /// <summary>用新节点覆盖旧节点</summary>
    Overwrite,

    /// <summary>保留两个节点（可能造成重复）</summary>
    KeepBoth,

    /// <summary>合并两个节点的属性</summary>
    Merge
}
```

### 4.3 `InsertPosition` — 插入位置

```csharp
/// <summary>
/// 插入位置定义。
/// 描述在链中插入节点的位置。
/// </summary>
public class InsertPosition
{
    /// <summary>插入模式</summary>
    public InsertMode Mode { get; }

    /// <summary>目标节点标识（After/Before 模式使用）</summary>
    public string? TargetKey { get; }

    /// <summary>目标节点索引（AtIndex 模式使用）</summary>
    public int? Index { get; }

    /// <summary>插入到链尾</summary>
    public static InsertPosition End() =>
        new(InsertMode.End, null, null);

    /// <summary>插入到链首</summary>
    public static InsertPosition Start() =>
        new(InsertMode.Start, null, null);

    /// <summary>在指定节点之后插入</summary>
    public static InsertPosition After(string nodeKey) =>
        new(InsertMode.After, nodeKey, null);

    /// <summary>在指定节点之前插入</summary>
    public static InsertPosition Before(string nodeKey) =>
        new(InsertMode.Before, nodeKey, null);

    /// <summary>在指定索引处插入</summary>
    public static InsertPosition AtIndex(int index) =>
        new(InsertMode.AtIndex, null, index);

    private InsertPosition(InsertMode mode, string? targetKey, int? index)
    {
        Mode = mode;
        TargetKey = targetKey;
        Index = index;
    }
}

/// <summary>插入模式</summary>
public enum InsertMode
{
    /// <summary>插入到链尾</summary>
    End,

    /// <summary>插入到链首</summary>
    Start,

    /// <summary>在指定节点之后</summary>
    After,

    /// <summary>在指定节点之前</summary>
    Before,

    /// <summary>在指定索引处</summary>
    AtIndex
}
```

### 4.4 `ConnectionPoint` — 连接点

```csharp
/// <summary>
/// 连接点定义。
/// 描述链与链之间的连接位置。
/// </summary>
public class ConnectionPoint
{
    /// <summary>连接点类型</summary>
    public ConnectionPointType Type { get; }

    /// <summary>节点标识（ByNodeKey 模式使用）</summary>
    public string? NodeKey { get; }

    /// <summary>连接点类型</summary>
    public enum ConnectionPointType
    {
        /// <summary>链的起始节点</summary>
        ChainStart,

        /// <summary>链的结束节点</summary>
        ChainEnd,

        /// <summary>指定节点</summary>
        ByNodeKey
    }

    public static ConnectionPoint Start() =>
        new(ConnectionPointType.ChainStart, null);

    public static ConnectionPoint End() =>
        new(ConnectionPointType.ChainEnd, null);

    public static ConnectionPoint ByKey(string nodeKey) =>
        new(ConnectionPointType.ByNodeKey, nodeKey);

    private ConnectionPoint(ConnectionPointType type, string? nodeKey)
    {
        Type = type;
        NodeKey = nodeKey;
    }
}
```

### 4.5 `ValidationResult` — 通用验证结果

```csharp
/// <summary>
/// 通用验证结果模型。
/// 统一项目中所有验证操作的返回类型。
/// </summary>
public class ValidationResult
{
    private readonly List<string> _errors = new();
    private readonly List<string> _warnings = new();

    /// <summary>是否通过验证</summary>
    public bool IsValid => _errors.Count == 0;

    /// <summary>错误列表</summary>
    public IReadOnlyList<string> Errors => _errors;

    /// <summary>警告列表</summary>
    public IReadOnlyList<string> Warnings => _warnings;

    /// <summary>添加错误</summary>
    public void AddError(string error) => _errors.Add(error);

    /// <summary>添加警告</summary>
    public void AddWarning(string warning) => _warnings.Add(warning);

    /// <summary>合并另一个验证结果</summary>
    public void Merge(ValidationResult other)
    {
        if (other == null) return;
        _errors.AddRange(other._errors);
        _warnings.AddRange(other._warnings);
    }

    /// <summary>获取错误摘要</summary>
    public string GetErrorString() => string.Join(Environment.NewLine, _errors);

    /// <summary>获取完整摘要</summary>
    public string GetSummary()
    {
        var parts = new List<string>();
        if (_errors.Count > 0)
            parts.Add($"Errors ({_errors.Count}):\n{GetErrorString()}");
        if (_warnings.Count > 0)
            parts.Add($"Warnings ({_warnings.Count}):\n{string.Join(Environment.NewLine, _warnings)}");
        return parts.Count > 0 ? string.Join("\n\n", parts) : "Validation passed";
    }

    /// <summary>创建成功的验证结果</summary>
    public static ValidationResult Success() => new();

    /// <summary>从错误列表创建失败的验证结果</summary>
    public static ValidationResult Failure(params string[] errors)
    {
        var result = new ValidationResult();
        foreach (var error in errors)
            result.AddError(error);
        return result;
    }
}
```

---

## 5. 默认实现

### 5.1 `DefaultMergeStrategy<T>` — 基于 ObjectMerger 的默认实现

```csharp
/// <summary>
/// 默认合并策略。
/// 委托给 Share.FrameworkAbstractions 的 ObjectMerger 实现属性级合并。
/// </summary>
public class DefaultMergeStrategy<T> : IMergeStrategy<T>
    where T : class
{
    public MergeResult<T> Merge(T source, T target, MergeOptions? options = null)
    {
        try
        {
            var conflicts = new List<MergeConflict>();
            var stats = new MergeStatistics();

            // 委托给 ObjectMerger
            ObjectMerger.Merge(target, source, options?.ToObjectMergerOptions());

            return MergeResult<T>.Success(target, conflicts, stats);
        }
        catch (Exception ex)
        {
            return MergeResult<T>.Failure($"合并失败: {ex.Message}", target);
        }
    }
}
```

### 5.2 `DefaultChainComposer<TNode>` — 默认链组合器

```csharp
/// <summary>
/// 默认链组合器。
/// 实现 Compose/Insert/Connect 三种操作，支持去重和位置控制。
/// </summary>
public class DefaultChainComposer<TNode> : IChainComposer<TNode>
{
    private readonly ICompositionValidator? _validator;
    private readonly Func<TNode, string?>? _keyExtractor;

    public DefaultChainComposer(
        ICompositionValidator? validator = null,
        Func<TNode, string?>? keyExtractor = null)
    {
        _validator = validator;
        _keyExtractor = keyExtractor;
    }

    public MergeResult<IEnumerable<TNode>> Compose(
        IEnumerable<IEnumerable<TNode>> chains,
        CompositionOptions? options = null)
    {
        options ??= CompositionOptions.Default;
        var result = new List<TNode>();
        var seenKeys = new HashSet<string>();
        var conflicts = new List<MergeConflict>();
        var stats = new MergeStatistics();

        foreach (var chain in chains)
        {
            foreach (var node in chain)
            {
                var key = _keyExtractor?.Invoke(node);
                stats.TotalItems++;

                if (key != null && !seenKeys.Add(key))
                {
                    // 重复节点
                    switch (options.DuplicateHandling)
                    {
                        case DuplicateHandling.Skip:
                            stats.SkippedItems++;
                            continue;
                        case DuplicateHandling.Overwrite:
                            var idx = result.FindIndex(n =>
                                _keyExtractor?.Invoke(n) == key);
                            if (idx >= 0)
                            {
                                conflicts.Add(new MergeConflict(
                                    key, node, result[idx], "Overwritten"));
                                result[idx] = node;
                                stats.OverwrittenItems++;
                            }
                            continue;
                        case DuplicateHandling.KeepBoth:
                            result.Add(node);
                            stats.AddedItems++;
                            continue;
                        case DuplicateHandling.Merge:
                            // 合并逻辑委托给 IMergeStrategy
                            continue;
                    }
                }

                result.Add(node);
                stats.AddedItems++;
            }
        }

        return MergeResult<IEnumerable<TNode>>.Success(
            result, conflicts, stats);
    }

    public MergeResult<IEnumerable<TNode>> Insert(
        IEnumerable<TNode> chain,
        TNode node,
        InsertPosition position)
    {
        var list = chain.ToList();
        var conflicts = new List<MergeConflict>();

        switch (position.Mode)
        {
            case InsertMode.End:
                list.Add(node);
                break;
            case InsertMode.Start:
                list.Insert(0, node);
                break;
            case InsertMode.After:
                if (position.TargetKey != null)
                {
                    var idx = list.FindIndex(n =>
                        _keyExtractor?.Invoke(n) == position.TargetKey);
                    if (idx >= 0)
                        list.Insert(idx + 1, node);
                    else
                        list.Add(node);
                }
                break;
            case InsertMode.Before:
                if (position.TargetKey != null)
                {
                    var idx = list.FindIndex(n =>
                        _keyExtractor?.Invoke(n) == position.TargetKey);
                    if (idx >= 0)
                        list.Insert(idx, node);
                    else
                        list.Insert(0, node);
                }
                break;
            case InsertMode.AtIndex:
                if (position.Index.HasValue &&
                    position.Index.Value >= 0 &&
                    position.Index.Value <= list.Count)
                    list.Insert(position.Index.Value, node);
                else
                    list.Add(node);
                break;
        }

        return MergeResult<IEnumerable<TNode>>.Success(list, conflicts);
    }

    public MergeResult<IEnumerable<TNode>> Connect(
        IEnumerable<TNode> sourceChain,
        IEnumerable<TNode> targetChain,
        ConnectionPoint sourcePoint,
        ConnectionPoint targetPoint)
    {
        var sourceList = sourceChain.ToList();
        var targetList = targetChain.ToList();
        var result = new List<TNode>(sourceList);

        // 根据连接点确定插入位置
        int insertIndex = sourcePoint.Type switch
        {
            ConnectionPoint.ConnectionPointType.ChainEnd => sourceList.Count,
            ConnectionPoint.ConnectionPointType.ByNodeKey
                when sourcePoint.NodeKey != null =>
                sourceList.FindIndex(n =>
                    _keyExtractor?.Invoke(n) == sourcePoint.NodeKey) + 1,
            _ => sourceList.Count
        };

        // 根据目标连接点确定要插入的节点范围
        var nodesToInsert = targetPoint.Type switch
        {
            ConnectionPoint.ConnectionPointType.ChainStart => targetList,
            ConnectionPoint.ConnectionPointType.ChainEnd => targetList,
            ConnectionPoint.ConnectionPointType.ByNodeKey
                when targetPoint.NodeKey != null =>
                targetList.SkipWhile(n =>
                    _keyExtractor?.Invoke(n) != targetPoint.NodeKey).ToList(),
            _ => targetList
        };

        result.InsertRange(insertIndex, nodesToInsert);

        return MergeResult<IEnumerable<TNode>>.Success(result);
    }
}
```

---

## 6. 迁移映射

### 6.1 Operation 层 → Share 层映射

| Operation 层现有代码 | Share 层目标 | 迁移方式 |
|---------------------|-------------|---------|
| `IChainMergeStrategy` | `IMergeStrategy<ChainInfo>` | 实现新接口，标记旧接口 `[Obsolete]` |
| `WorkflowConnectorMergeStrategy` | `DefaultChainComposer<TNode>` + 自定义 `IMergeStrategy<ChainInfo>` | 重构为组合模式 |
| `WorkflowMediator` | 使用 `IChainComposer` 替代直接调用 | 注入替换 |
| `ChainLinkProcessor` | 保留在 Operation 层（领域特有） | 不迁移 |
| `NodeConfigFactory.CompleteConfig` | 使用 `IMergeStrategy<NodeConfig>` | 注入替换 |
| `ObjectMerger` | 已在 Share 层 | 作为 `DefaultMergeStrategy` 的基础 |
| `TemplateConfigProvider` | 已在 Share 层 | 作为组合选项的一部分 |
| `ValidationResult`（WorkflowBuilder） | `Composition.ValidationResult` | 统一到 Share 层 |

### 6.2 迁移步骤

```
Step 1: 创建 Composition/ 目录和所有接口/模型
        └── 新增文件，无破坏性

Step 2: DefaultChainComposer 实现 IChainComposer
        └── 新增文件，无破坏性

Step 3: WorkflowConnectorMergeStrategy 实现 IMergeStrategy<ChainInfo>
        └── 新增接口实现，旧接口标记 [Obsolete]

Step 4: WorkflowMediator 使用 IChainComposer
        └── 注入替换，行为不变

Step 5: NodeConfigFactory 使用 IMergeStrategy<NodeConfig>
        └── 注入替换，行为不变

Step 6: 统一 ValidationResult
        └── 删除重复定义，统一引用 Share 层
```

---

## 7. 使用示例

### 7.1 链组合

```csharp
// 创建链组合器
var composer = new DefaultChainComposer<NodeConfig>(
    keyExtractor: n => n.basicConfig?.Callkey);

// 组合多个链
var result = composer.Compose(
    new[] { chain1.AllNodes, chain2.AllNodes, chain3.AllNodes },
    CompositionOptions.Overwrite);

if (result.IsSuccess)
{
    Console.WriteLine($"组合成功: {result.Statistics.AddedItems} added, " +
                      $"{result.Statistics.OverwrittenItems} overwritten");
}
else
{
    Console.WriteLine($"组合失败: {result.ErrorMessage}");
}
```

### 7.2 节点插入

```csharp
// 在指定节点之后插入
var result = composer.Insert(
    chain.AllNodes,
    newNode,
    InsertPosition.After("call_3"));

// 在链首插入
var result2 = composer.Insert(
    chain.AllNodes,
    newNode,
    InsertPosition.Start());
```

### 7.3 链连接

```csharp
// 连接两个链（源链的结束 → 目标链的开始）
var result = composer.Connect(
    sourceChain.AllNodes,
    targetChain.AllNodes,
    ConnectionPoint.End(),
    ConnectionPoint.Start());
```

### 7.4 配置合并

```csharp
// 创建配置合并策略
var mergeStrategy = new DefaultMergeStrategy<NodeConfig>();

// 合并配置
var result = mergeStrategy.Merge(
    source: templateConfig,
    target: userConfig,
    options: MergeOptions.SkipNullWithEmptyString);

if (result.IsSuccess)
{
    // 使用合并后的配置
    var mergedConfig = result.Result;
}
```

---

## 8. 设计原则

### 8.1 单一职责

- `IMergeStrategy<T>` — 只做"合并"一件事
- `IChainComposer<TNode>` — 只做"链的组合操作"
- `ICompositionValidator` — 只做"验证"

### 8.2 接口隔离

- 每个接口只包含最少的必要方法
- 调用方只需依赖自己需要的接口

### 8.3 依赖倒置

- Operation 层依赖 Share 层的抽象接口
- Share 层不依赖 Operation 层的具体类型

### 8.4 开闭原则

- 新增合并策略只需实现 `IMergeStrategy<T>`
- 新增组合操作只需扩展 `IChainComposer<TNode>`
- 不修改现有代码

### 8.5 结果可观测

- 所有操作返回 `MergeResult<T>`，包含冲突和统计信息
- 调用方可以根据结果做决策（回滚、重试、记录日志）

---

## 9. 与现有框架的关系

```
UniversalMiddleware (JSON 工具)
       │
       ▼
WorkflowExecution/Composition (逻辑组合框架)
       │
       ├──► WorkflowExecution/Engine (执行引擎)
       │        使用 Composition 构建好的链执行
       │
       ├──► WorkflowExecution/Skeletons (骨架)
       │        使用 Composition 的验证结果
       │
       └──► Operation/WorkFlowMethod (具体实现)
                实现 Composition 的接口
```

---

## 10. 后续规划

### Phase 1 — 基础设施（当前）
- [ ] 创建 `Composition/` 目录结构
- [ ] 定义所有接口和模型
- [ ] 实现默认实现

### Phase 2 — Operation 层适配
- [ ] `WorkflowConnectorMergeStrategy` 适配 `IMergeStrategy<ChainInfo>`
- [ ] `WorkflowMediator` 适配 `IChainComposer<NodeConfig>`
- [ ] `NodeConfigFactory` 适配 `IMergeStrategy<NodeConfig>`
- [ ] 统一 `ValidationResult`

### Phase 3 — 测试与文档
- [ ] 单元测试覆盖所有接口和默认实现
- [ ] 集成测试验证 Operation 层适配
- [ ] 完善使用文档和示例

### Phase 4 — 推广
- [ ] 评估其他模块（FileDriven、EventTriggerFramework）是否可以使用
- [ ] 根据反馈优化接口设计
