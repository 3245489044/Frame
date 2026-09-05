# Placeholder 通用数据引用框架

## 1. 概述

### 1.1 设计目标

构建一个**通用的数据引用追踪框架**，核心思想是：

> **业务代码只声明"我引用了什么数据"，框架负责"怎么解析、怎么处理、怎么追溯"**

### 1.2 核心抽象

```
Placeholder 机制 = 声明式数据引用 + 延迟解析 + 元数据持久化
```

- **声明式数据引用**：业务代码通过 Placeholder 声明数据来源，不关心后续处理
- **延迟解析**：Placeholder 在 Pipeline 下游统一解析，解析时机与业务执行分离
- **元数据持久化**：所有引用关系持久化存储，支持事后追溯和分析

### 1.3 与传统架构的对比

| 维度 | 传统架构 | Placeholder 框架 |
|------|---------|-----------------|
| 业务代码职责 | 处理数据 + 记录日志 + 触发告警 + ... | 只处理数据，声明引用 |
| 横切关注点 | 分散在各处，难以统一治理 | 集中在下游 Handler 处理 |
| 新增处理场景 | 修改业务代码 | 新增 Handler，不改业务 |
| 数据可追溯性 | 无（或手动记录） | 自动记录所有引用关系 |
| 关注点分离 | ❌ 紧耦合 | ✅ 完全解耦 |

---

## 2. 核心概念

### 2.1 Placeholder — 数据引用指针

Placeholder 来自 `GraphKey.Core` 命名空间（`Graphkey.Core` 包），是一个 sealed record：

```csharp
/// <summary>
/// 数据引用指针：声明"我引用了什么数据"
/// </summary>
public sealed record Placeholder : IPlaceholder
{
    /// <summary>引用类型</summary>
    public PlaceholderKind Kind { get; init; }
    
    /// <summary>引用的键名（语义化描述）</summary>
    public string Key { get; init; }
    
    /// <summary>引用的索引</summary>
    public int? Index { get; init; }
    
    /// <summary>引用的节点ID</summary>
    public string NodeId { get; init; }
    
    /// <summary>引用的节点索引</summary>
    public int? NodeIndex { get; init; }
}
```

#### 引用类型

| Kind | 语义 | 创建方式 |
|------|------|---------|
| `Input` | 数据来自外部输入 | `Placeholder.Input("orderId")` |
| `Prev` | 数据来自上一个处理步骤 | `Placeholder.Prev()` / `Placeholder.PrevByKey("total")` / `Placeholder.PrevByIndex(0)` |
| `NodeReference` | 数据来自指定节点 | `Placeholder.From("calcNode", "result")` / `Placeholder.From("calcNode", 0)` |
| `NodeIndex` | 数据来自流程中第N个节点 | `Placeholder.Node(0)` |

### 2.2 SkeletonContext — 引用上下文

本框架**不定义独立的 PlaceholderContext**，而是通过扩展方法在 `SkeletonContext`（来自 SkeletonFlow）上提供 Placeholder 操作语义。

```csharp
// 命名空间：Common.PlaceholderFramework.Core
public static class PlaceholderContextExtensions
{
    // ===== BatchId 管理 =====
    public static string GetBatchId(this SkeletonContext ctx);
    public static void SetBatchId(this SkeletonContext ctx, string batchId);

    // ===== ModuleName 管理 =====
    public static string GetModuleName(this SkeletonContext ctx);  // 默认 "Unknown"
    public static void SetModuleName(this SkeletonContext ctx, string moduleName);

    // ===== Placeholder 批量操作 =====
    public static void SetPlaceholder(this SkeletonContext ctx, string key, Placeholder placeholder);
    public static void SetPlaceholders(this SkeletonContext ctx, IDictionary<string, Placeholder> placeholders);
    public static IEnumerable<KeyValuePair<string, Placeholder>> GetAllPlaceholders(this SkeletonContext ctx);
    public static int GetPlaceholderCount(this SkeletonContext ctx);
    public static bool TryGetPlaceholder(this SkeletonContext ctx, string key, out Placeholder placeholder);
    public static bool RemovePlaceholder(this SkeletonContext ctx, string key);
    public static void ClearPlaceholders(this SkeletonContext ctx);

    // ===== 便捷设置方法 =====
    public static void SetInputPlaceholder(this SkeletonContext ctx, string key, string inputKey);
    public static void SetPrevPlaceholder(this SkeletonContext ctx, string key, string prevKey = null, int? index = null);
    public static void SetNodeRefPlaceholder(this SkeletonContext ctx, string key, string nodeId, string outputKey = null, int? index = null);
    public static void SetNodeIndexPlaceholder(this SkeletonContext ctx, string key, int nodeIndex);
}
```

### 2.3 PlaceholderMetadata — 持久化元数据

```csharp
// 命名空间：Common.PlaceholderFramework.Core
public class PlaceholderMetadata
{
    public string Kind { get; set; }           // 引用类型（Input、Prev、NodeReference、NodeIndex）
    public string Key { get; set; }            // 引用的键名（语义化描述）
    public int? Index { get; set; }            // 引用的索引
    public string NodeId { get; set; }         // 引用的节点ID
    public int? NodeIndex { get; set; }        // 引用的节点索引
    public string ResolvedValue { get; set; }  // 解析后的值
    public string SourceModule { get; set; }   // 来源模块
    public string SourceMember { get; set; }   // 来源成员（方法名）
    public DateTime Timestamp { get; set; }    // 时间戳
    public string BatchId { get; set; }        // 批次ID
    public string MetadataId { get; set; }     // 元数据ID（唯一标识）
    public string HandlerName { get; set; }    // 处理 Handler 名称
    public string ExtraData { get; set; }      // 额外数据（扩展字段，JSON 格式）

    public static PlaceholderMetadata FromPlaceholder(
        Placeholder ph, string resolvedValue,
        string module = null, string member = null,
        string batchId = null, string handlerName = null,
        string extraData = null);
}
```

---

## 3. 架构设计

### 3.1 整体架构

```
┌─────────────────────────────────────────────────────────────────────┐
│                        Pipeline 执行流程                             │
│                                                                     │
│  ┌──────────────┐   ┌──────────────┐   ┌──────────────────────┐    │
│  │ Init         │   │ 业务Skeleton  │   │ Placeholder          │    │
│  │ Skeleton     │ → │ (设置         │ → │ ResolverSkeleton     │    │
│  │              │   │  Placeholder) │   │ (统一解析 + 分发)     │    │
│  └──────────────┘   └──────────────┘   └──────────┬───────────┘    │
│                                                    │                │
│                          ┌─────────────────────────┼─────────┐     │
│                          │                         │         │     │
│                    ┌─────▼─────┐           ┌───────▼───────┐ │     │
│                    │ 日志处理   │           │ 监控告警处理   │ │     │
│                    │ Handler   │           │ Handler       │ │     │
│                    └───────────┘           └───────────────┘ │     │
│                          │                         │         │     │
│                    ┌─────▼─────┐           ┌───────▼───────┐ │     │
│                    │ 审计追踪   │           │ 性能分析      │ │     │
│                    │ Handler   │           │ Handler       │ │     │
│                    └───────────┘           └───────────────┘ │     │
│                          │                         │         │     │
│                          └─────────────────────────┼─────────┘     │
│                                                    │                │
│                                                    ▼                │
│  ┌──────────────┐   ┌──────────────┐   ┌──────────────────────┐    │
│  │ Flush        │ ← │ Summary      │ ← │ 更多处理 Handler...  │    │
│  │ Skeleton     │   │ Skeleton     │   │                      │    │
│  └──────────────┘   └──────────────┘   └──────────────────────┘    │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────────┐
│                        持久化存储                                    │
│  ┌──────────────────────────────────────────────────────────────┐  │
│  │ IMetadataStore (接口)                                         │  │
│  │ MemoryMetadataStore (默认内存实现)                             │  │
│  │ { Kind, Key, ResolvedValue, SourceModule, Timestamp, ... }    │  │
│  └──────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────────┐
│                        追溯查询层                                    │
│  ┌──────────────────────┐  ┌──────────────────────────────────┐    │
│  │ 正向查询              │  │ 反向追溯                        │    │
│  │ PlaceholderQuery     │  │ PlaceholderReverseResolver      │    │
│  │ - 批次统计           │  │ - 重建调用链                    │    │
│  │ - 结果格式化         │  │ - 重建数据流图                  │    │
│  └──────────────────────┘  │ - 追踪数据来源/去向              │    │
│                             └──────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────────────┘
```

### 3.2 核心组件

| 组件 | 命名空间 | 职责 | 可扩展点 |
|------|---------|------|---------|
| `PlaceholderContextExtensions` | `Core` | 在 SkeletonContext 上提供 Placeholder 操作 | 无（基础设施） |
| `PlaceholderMetadata` | `Core` | 持久化引用元数据 | 可扩展 ExtraData 字段 |
| `PlaceholderResolverSkeleton` | `Core` | 统一调度：遍历 → 解析 → 分发 → 持久化 | 注册多个 Handler |
| `IPlaceholderHandler` | `Handlers` | 处理单个 Placeholder 解析结果 | 新增 Handler = 新增场景 |
| `IMetadataStore` | `Storage` | 元数据存储接口 | 可替换存储后端 |
| `MemoryMetadataStore` | `Storage` | 默认内存存储实现 | 无（默认实现） |
| `PlaceholderQuery` | `Query` | 正向查询 + 批次统计 | 无 |
| `PlaceholderReverseResolver` | `Query` | 反向追溯（调用链、数据流图） | 无 |

### 3.3 处理 Handler 接口

```csharp
// 命名空间：Common.PlaceholderFramework.Handlers
public interface IPlaceholderHandler
{
    /// <summary>Handler 名称（用于元数据记录）</summary>
    string Name { get; }
    
    /// <summary>是否处理该 Placeholder</summary>
    bool CanHandle(Placeholder placeholder);
    
    /// <summary>处理解析后的 Placeholder</summary>
    void Handle(Placeholder placeholder, string resolvedValue,
                SkeletonContext context, string batchId);
}
```

---

## 4. 使用示例

### 4.1 业务 Skeleton — 只声明引用

```csharp
public class OrderProcessSkeleton : SkeletonBase<OrderContext, OrderContext>
{
    protected override OrderContext ExecuteCore(OrderContext input, SkeletonContext ctx)
    {
        // ★ 业务代码只声明"我引用了什么数据"
        // 不关心这些数据会被用来做什么（日志？监控？审计？）
        
        ctx.SetPlaceholder("order:start",
            Placeholder.Input("开始处理订单"));
        
        var total = CalculateTotal(input);
        
        ctx.SetPlaceholder("order:total",
            Placeholder.PrevByKey("totalAmount"));
        
        ctx.SetPlaceholder("order:end",
            Placeholder.Input("处理完成"));
        
        return input;
    }
}
```

### 4.2 Pipeline 组装 — 按需添加处理场景

```csharp
// 基础管线
var pipeline = Flow.Start<OrderContext>()
    .Then(new InitSkeleton())
    .Then(new OrderProcessSkeleton())           // 业务：只设 Placeholder
    .Then(new PlaceholderResolverSkeleton(       // 统一解析 + 分发
        handlers: new IPlaceholderHandler[]
        {
            new LogPlaceholderHandler(),         // 场景1：日志
            new MonitorPlaceholderHandler(),     // 场景2：监控告警
            new AuditPlaceholderHandler(),       // 场景3：审计追踪
            new PerformancePlaceholderHandler(), // 场景4：性能分析
        }))
    .Then(new FlushSkeleton());

// 执行
var result = pipeline.Execute(input);
```

### 4.3 日志 Handler

```csharp
// 命名空间：Common.PlaceholderFramework.Handlers
public class LogPlaceholderHandler : IPlaceholderHandler
{
    public string Name => "Log";
    
    public bool CanHandle(Placeholder placeholder)
        => placeholder.Key?.StartsWith("log:") == true
           || placeholder.Key?.StartsWith("order:") == true
           || placeholder.Key?.StartsWith("debug:") == true;
    
    public void Handle(Placeholder placeholder, string resolvedValue,
                       SkeletonContext context, string batchId)
    {
        var moduleName = context.GetModuleName();
        var level = ParseLevel(placeholder.Key);
        var message = FormatMessage(placeholder, resolvedValue);
        var category = ResolveLogCategory(placeholder.Key);

        // 使用 Diagnostics 的 LogEntryCollector 记录日志
        LogEntryCollector.Log(context, level, category, moduleName, message);
    }
}
```

### 4.4 监控告警 Handler

```csharp
// 命名空间：Common.PlaceholderFramework.Handlers
public class MonitorPlaceholderHandler : IPlaceholderHandler
{
    public string Name => "Monitor";
    
    public bool CanHandle(Placeholder placeholder)
        => placeholder.Key?.StartsWith("monitor:") == true
           || placeholder.Key?.StartsWith("metric:") == true
           || placeholder.Key?.StartsWith("alert:") == true;
    
    public void Handle(Placeholder placeholder, string resolvedValue,
                       SkeletonContext context, string batchId)
    {
        // 记录监控指标到 Context
        var metrics = context.GetData<Dictionary<string, object>>("__MonitorMetrics")
                      ?? new Dictionary<string, object>();
        metrics[placeholder.Key] = resolvedValue;
        context.SetData(metrics, "__MonitorMetrics");

        // 评估告警规则
        if (double.TryParse(resolvedValue, out var numericValue))
        {
            EvaluateAlertRules(placeholder.Key, numericValue, context, batchId);
        }
    }
}
```

### 4.5 审计追踪 Handler

```csharp
// 命名空间：Common.PlaceholderFramework.Handlers
public class AuditPlaceholderHandler : IPlaceholderHandler
{
    public string Name => "Audit";
    
    public bool CanHandle(Placeholder placeholder)
        => placeholder.Key?.StartsWith("audit:") == true
           || placeholder.Key?.StartsWith("trace:") == true;
    
    public void Handle(Placeholder placeholder, string resolvedValue,
                       SkeletonContext context, string batchId)
    {
        var auditEntry = new AuditEntry
        {
            Action = placeholder.Key,
            Data = resolvedValue,
            Source = placeholder.Kind.ToString(),
            SourceModule = context.GetModuleName(),
            NodeId = placeholder.NodeId,
            NodeIndex = placeholder.NodeIndex,
            Timestamp = DateTime.UtcNow,
            BatchId = batchId
        };

        // 存储到 Context 中，供后续 Flush Skeleton 持久化
        var auditTrail = context.GetData<List<AuditEntry>>("__AuditTrail")
                         ?? new List<AuditEntry>();
        auditTrail.Add(auditEntry);
        context.SetData(auditTrail, "__AuditTrail");
    }
}
```

### 4.6 性能分析 Handler

```csharp
// 命名空间：Common.PlaceholderFramework.Handlers
public class PerformancePlaceholderHandler : IPlaceholderHandler
{
    public string Name => "Performance";
    
    public bool CanHandle(Placeholder placeholder)
        => placeholder.Key?.StartsWith("perf:") == true
           || placeholder.Key?.StartsWith("timing:") == true
           || placeholder.Key?.StartsWith("duration:") == true;
    
    public void Handle(Placeholder placeholder, string resolvedValue,
                       SkeletonContext context, string batchId)
    {
        if (double.TryParse(resolvedValue, out var duration))
        {
            // 记录性能数据
            var perfData = context.GetData<List<PerformanceRecord>>("__PerformanceData")
                           ?? new List<PerformanceRecord>();
            perfData.Add(new PerformanceRecord
            {
                Key = placeholder.Key,
                Duration = duration,
                Module = context.GetModuleName(),
                Kind = placeholder.Kind.ToString(),
                Timestamp = DateTime.UtcNow,
                BatchId = batchId
            });
            context.SetData(perfData, "__PerformanceData");

            // 更新统计信息
            var stats = context.GetData<PerformanceStats>("__PerformanceStats")
                        ?? new PerformanceStats();
            stats.Record(placeholder.Key, duration);
            context.SetData(stats, "__PerformanceStats");
        }
    }
}
```

---

## 5. 场景扩展表

| 场景 | Key 前缀 | Handler 职责 | 新增成本 |
|------|---------|-------------|---------|
| 日志 | `log:` / `order:` / `debug:` | 输出到 Logger + 持久化元数据 | 1 个 Handler 类 |
| 监控告警 | `monitor:` / `metric:` / `alert:` | 评估告警规则 + 记录指标 | 1 个 Handler 类 |
| 审计追踪 | `audit:` / `trace:` | 记录审计条目 | 1 个 Handler 类 |
| 性能分析 | `perf:` / `timing:` / `duration:` | 记录耗时 + 统计 | 1 个 Handler 类 |
| 数据校验 | `validate:` | 执行校验规则 | 1 个 Handler 类 |
| A/B 测试 | `experiment:` | 动态路由到实验分支 | 1 个 Handler 类 |
| 事件驱动 | `event:` | 派发事件到消息队列 | 1 个 Handler 类 |
| 调试分析 | `debug:` | 记录调试信息到独立存储 | 1 个 Handler 类 |

**关键优势**：新增一个处理场景 = 新增一个 `IPlaceholderHandler` 实现 + 在 Pipeline 中注册，**无需修改任何业务代码**。

---

## 6. 存储层

### 6.1 存储接口

```csharp
// 命名空间：Common.PlaceholderFramework.Storage
public interface IMetadataStore
{
    void Save(PlaceholderMetadata metadata);
    void SaveBatch(IEnumerable<PlaceholderMetadata> metadataList);

    // 查询
    IEnumerable<PlaceholderMetadata> FindByBatch(string batchId);
    IEnumerable<PlaceholderMetadata> FindByModule(string batchId, string module);
    IEnumerable<PlaceholderMetadata> FindByKind(string batchId, string kind);
    IEnumerable<PlaceholderMetadata> FindByTimeRange(string batchId, DateTime start, DateTime end);
    IEnumerable<PlaceholderMetadata> FindByKey(string batchId, string key);
    IEnumerable<PlaceholderMetadata> FindByNodeId(string batchId, string nodeId);

    // 管理
    IEnumerable<string> GetAllBatchIds();
    int GetCount(string batchId);
    void ClearBatch(string batchId);
    void ClearAll();
}
```

### 6.2 默认实现：MemoryMetadataStore

使用 `ConcurrentDictionary<string, List<PlaceholderMetadata>>` 按 BatchId 分组存储，线程安全。

---

## 7. 追溯查询

### 7.1 正向查询 — PlaceholderQuery

```csharp
// 命名空间：Common.PlaceholderFramework.Query
public class PlaceholderQuery
{
    public PlaceholderQuery(IMetadataStore store);

    /// <summary>获取批次统计信息</summary>
    BatchStatistics GetBatchStatistics(string batchId);

    /// <summary>格式化查询结果为文本</summary>
    string FormatResults(IEnumerable<PlaceholderMetadata> results);
}

public class BatchStatistics
{
    public string BatchId { get; set; }
    public int TotalCount { get; set; }
    public Dictionary<string, int> KindDistribution { get; set; }
    public Dictionary<string, int> ModuleDistribution { get; set; }
    public (DateTime Min, DateTime Max) TimeRange { get; set; }
}
```

### 7.2 反向追溯 — PlaceholderReverseResolver

```csharp
// 命名空间：Common.PlaceholderFramework.Query
public class PlaceholderReverseResolver
{
    public PlaceholderReverseResolver(IMetadataStore store);

    /// <summary>重建调用链（按时间排序）</summary>
    CallChain RebuildCallChain(string batchId);

    /// <summary>重建数据流图</summary>
    DataFlowGraph RebuildDataFlow(string batchId);

    /// <summary>追踪数据来源</summary>
    DataSourceTrace TraceDataSource(string batchId, string metadataId);

    /// <summary>追踪数据去向</summary>
    IEnumerable<DataSink> TraceDataSink(string batchId, string metadataId);
}
```

#### 返回类型

| 类型 | 说明 | 关键字段 |
|------|------|---------|
| `CallChain` | 调用链 | `BatchId`, `Steps` (List of `CallChainStep`) |
| `CallChainStep` | 调用步骤 | `MetadataId`, `Kind`, `Key`, `ResolvedValue`, `Timestamp` |
| `DataFlowGraph` | 数据流图 | `Nodes`, `Edges`, `Inputs` |
| `DataFlowNode` | 数据流节点 | `Id`, `Key`, `Kind`, `Value`, `Module`, `Handler` |
| `DataFlowEdge` | 数据流边 | `From`, `To`, `Label` |
| `DataSourceTrace` | 数据来源追踪 | `SourceType`, `SourceMetadataId`, `SourceDescription` |
| `DataSink` | 数据去向 | `SinkKey`, `SinkKind`, `SinkValue`, `Relation` |

---

## 8. 与 UniversalReference 的区别

### 8.1 UniversalReference 是什么

`Share/UniversalReference` 是一个**通用工作流框架**，核心能力是：

- **资源管理**：以图数据库方式管理资源（Module、Version、Artifact、Process）
- **引用关系管理**：在资源之间建立/查询/删除引用关系（DependsOn、Includes、Extends）
- **持久化存储**：支持内存、JSON 文件、LiteDB、SQLite 多种存储后端
- **事件驱动**：资源/关系变化时发布事件通知
- **分支管理**：管理工作流分支的生命周期
- **操作映射**：UI 操作到 AI 操作的映射

### 8.2 核心区别

| 维度 | UniversalReference | Placeholder Framework |
|------|-------------------|----------------------|
| **关注点** | 资源之间的**静态关系**（谁依赖谁） | 运行时数据的**动态引用**（数据从哪里来） |
| **数据粒度** | 资源级别（Module、Artifact） | 数据字段级别（orderId、totalAmount） |
| **时间维度** | 持久化存储，跨会话 | 运行时追踪，按批次 |
| **关系类型** | DependsOn、Includes、Extends | Input、Prev、NodeReference、NodeIndex |
| **查询方向** | 正向：资源 → 依赖/被依赖 | 双向：正向（按批次查）+ 反向（追溯数据来源） |
| **使用时机** | 设计时/部署时（配置资源关系） | 运行时（追踪数据流） |
| **存储内容** | 资源元数据 + 引用关系 | Placeholder 元数据 + 解析值 |
| **典型场景** | 模块依赖分析、版本管理 | 日志追溯、监控告警、审计追踪 |

### 8.3 类比理解

```
UniversalReference = 城市地图（静态道路网络）
Placeholder Framework = 实时交通追踪（车辆行驶轨迹）

城市地图告诉你：A 路连接 B 路，B 路连接 C 路（静态关系）
实时追踪告诉你：这辆车从 A 出发，经过 B，到达 C（动态数据流）
```

### 8.4 互补关系

两者不是竞争关系，而是**互补**的：

```
UniversalReference 管理"谁和谁有关系"
Placeholder Framework 管理"数据在运行时如何流动"

结合使用：
1. UniversalReference 定义系统的静态拓扑结构
2. Placeholder Framework 追踪数据在拓扑上的运行时流动
3. 追溯时：Placeholder 的 NodeId 可以关联到 UniversalReference 的资源
```

### 8.5 与现有框架的关系

```
                    ┌─────────────────────┐
                    │   GraphKey          │
                    │   Placeholder 体系   │ ← 提供占位符机制
                    └─────────┬───────────┘
                              │
                    ┌─────────▼───────────┐
                    │   SkeletonFlow      │
                    │   Pipeline 框架      │ ← 提供执行流程
                    └─────────┬───────────┘
                              │
                    ┌─────────▼───────────┐
                    │   Placeholder       │
                    │   Framework         │ ← 本框架（运行时数据流追踪）
                    └─────────┬───────────┘
                              │
              ┌───────────────┼───────────────┐
              │               │               │
    ┌─────────▼─────┐ ┌───────▼───────┐ ┌─────▼─────────┐
    │ 日志处理       │ │ 监控告警      │ │ 审计追踪       │
    │ (Log Handler) │ │ (Monitor     │ │ (Audit        │
    │               │ │  Handler)    │ │  Handler)     │
    └───────────────┘ └───────────────┘ └───────────────┘
                              │
                              ▼
                    ┌─────────────────────┐
                    │   UniversalReference │ ← 静态资源关系（互补）
                    │   (资源依赖管理)     │
                    └─────────────────────┘
```

---

## 9. 项目结构

```
Share/PlaceholderFramework/
├── PlaceholderFramework.csproj          # 项目文件（RootNamespace: Common.PlaceholderFramework）
├── README.md                           # 使用说明
├── Core/
│   ├── PlaceholderMetadata.cs          # 元数据模型
│   ├── PlaceholderContextExtensions.cs  # SkeletonContext 扩展方法
│   └── PlaceholderResolverSkeleton.cs  # 解析器骨架（核心编排器）
├── Handlers/
│   ├── IPlaceholderHandler.cs          # Handler 接口
│   ├── LogPlaceholderHandler.cs        # 日志处理
│   ├── MonitorPlaceholderHandler.cs    # 监控告警
│   ├── AuditPlaceholderHandler.cs      # 审计追踪
│   └── PerformancePlaceholderHandler.cs # 性能分析
├── Storage/
│   ├── IMetadataStore.cs               # 存储接口
│   └── MetadataStore.cs                # 内存实现（MemoryMetadataStore）
└── Query/
    ├── PlaceholderQuery.cs             # 正向查询 + 统计
    └── PlaceholderReverseResolver.cs   # 反向追溯
```

### 依赖关系

```
PlaceholderFramework.csproj
├── BasicMethod.csproj      → SkeletonFlow、GraphKey.Core
└── Share.csproj            → Common.Diagnostics（LogEntryCollector、LogCategory）
```

---

## 10. 设计原则

### 10.1 业务代码原则

1. **只声明引用，不关心处理** — 业务 Skeleton 只调用 `ctx.SetPlaceholder()`
2. **Key 要有语义** — Key 应该能让人理解这条引用在记录什么
3. **优先使用结构化引用** — 能用 `PrevByKey` 就不用 `Input`

### 10.2 Handler 原则

1. **一个 Handler 只负责一个场景** — 日志、监控、审计各一个 Handler
2. **Handler 之间无依赖** — 每个 Handler 独立处理 Placeholder
3. **Handler 可插拔** — 新增/移除 Handler 不影响业务代码

### 10.3 框架原则

1. **延迟解析** — Placeholder 在 Pipeline 下游统一解析
2. **元数据持久化** — 所有引用关系自动持久化
3. **可追溯** — 支持正向查询和反向追溯

---

## 11. 总结

Placeholder 通用数据引用框架的核心价值：

1. **声明式数据引用** — 业务代码只声明"我用了什么数据"，不关心后续处理
2. **关注点分离** — 数据采集（业务）与数据处理（框架）完全解耦
3. **统一治理** — 所有数据引用集中管理，便于审计、监控、分析
4. **可插拔** — 新增一种处理场景，只需新增一个 Handler，无需修改业务代码
5. **可追溯** — 所有引用关系自动持久化，支持正向查询和反向追溯

```
业务代码：ctx.SetPlaceholder("key", placeholder)
                │
                ▼
PlaceholderResolverSkeleton（统一解析 + 分发 + 持久化）
                │
        ┌───────┼───────┐
        │       │       │
        ▼       ▼       ▼
    Log     Monitor  Audit  ...（可插拔 Handler）
    Handler Handler  Handler
        │       │       │
        └───────┼───────┘
                ▼
          IMetadataStore（持久化）
                │
                ▼
    追溯查询（正向 PlaceholderQuery + 反向 PlaceholderReverseResolver）
```
