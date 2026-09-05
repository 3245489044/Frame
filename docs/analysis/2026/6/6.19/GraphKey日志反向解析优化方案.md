# GraphKey 日志反向解析优化方案

> **核心思想**：将日志从"平面文本"升级为"带 GraphKey 工作流上下文的结构化数据"，让每条日志都知道自己属于哪个工作流图、哪个节点、哪条边，从而实现"从日志反查工作流结构"的能力。
>
> **关键设计原则**：零侵入 — 不修改任何日志调用方，只改两处基础设施。

---

## 1. 现状分析

### 1.1 当前日志流程

```
Placeholder.Key（如 "filedriven:command_executed"）
  ↓
PlaceholderResolverSkeleton.ProcessPlaceholder()  ← 唯一解析入口
  ↓
LogPlaceholderHandlerBase.Handle()  ← 唯一日志 Handler
  ↓
PlaceholderLogHelper.Log()  ← 唯一日志输出入口
  ↓
LogRepository.Write()
  ↓
输出：平面文本日志
```

### 1.2 关键发现：只有两处需要改动

| 位置 | 角色 | 改动 |
|------|------|------|
| **`PlaceholderResolverSkeleton.ProcessPlaceholder()`** | 所有 Placeholder 的唯一解析入口 | 从 `SkeletonContext` 提取工作流上下文，设置到 AsyncLocal |
| **`PlaceholderLogHelper.Log()`** | 所有日志的唯一输出入口 | 从 AsyncLocal 读取工作流上下文，自动嵌入到日志消息 |

**所有日志调用方（Handler、模块代码）完全不需要修改。**

### 1.3 当前存在的问题

| 问题 | 描述 |
|------|------|
| **丢失工作流上下文** | 日志不知道自己在哪个工作流图、哪个节点、哪条边上 |
| **无法反向追溯** | 从一条日志无法反查它来自哪个工作流、哪个步骤 |
| **无法按图结构查询** | 只能按模块/级别查询，不能按工作流图结构查询 |
| **调试困难** | 工作流出错时，需要手动关联日志和工作流定义 |

---

## 2. 优化目标

### 2.1 核心能力

```
正向：Placeholder.Key → 日志消息（已有）
反向：日志消息 → 工作流图 → 节点 → 边 → 占位符解析链（新增）
```

### 2.2 具体目标

1. **每条日志携带工作流上下文**：知道自己在哪个 GraphId、NodeId、Edge 上
2. **支持按图结构查询日志**：按 GraphId、NodeId、Edge 查询
3. **支持占位符解析链追溯**：从日志反查占位符的解析过程
4. **支持工作流执行热力图**：统计每个节点的日志量，快速定位问题节点
5. **支持运行后日志分析重建调用链**：从已产生的日志中重建完整的函数调用链

---

## 3. 设计方案（零侵入）

### 3.1 核心思路：利用 AsyncLocal 实现隐式上下文传递

```csharp
// 不修改任何 Handler 的签名，不修改任何日志调用方
// 利用 AsyncLocal 在同一个异步流中隐式传递工作流上下文

public static class GraphLogContext
{
    // AsyncLocal — 自动沿异步调用链传播
    private static readonly AsyncLocal<WorkflowGraphContext> _current = new();
    
    /// <summary>获取当前工作流上下文</summary>
    public static WorkflowGraphContext? Current => _current.Value;
    
    /// <summary>设置当前工作流上下文（由 PlaceholderResolverSkeleton 调用）</summary>
    public static void Set(WorkflowGraphContext ctx) => _current.Value = ctx;
    
    /// <summary>清除当前工作流上下文</summary>
    public static void Clear() => _current.Value = null;
}
```

**为什么用 AsyncLocal？**

- `PlaceholderResolverSkeleton` 在 `ExecuteCore()` 中设置上下文
- 然后调用 `handler.Handle()` → 内部调用 `PlaceholderLogHelper.Log()`
- 整个调用链在同一个异步流中，`AsyncLocal` 自动传递
- **所有 Handler 和日志调用方完全不需要修改**

### 3.2 新增类型

#### 3.2.1 工作流图上下文

```csharp
/// <summary>
/// 工作流图上下文 — 记录日志所属的工作流图结构位置
/// </summary>
public class WorkflowGraphContext
{
    /// <summary>工作流图 ID（如 "数据处理流程"）</summary>
    public string GraphId { get; set; }
    
    /// <summary>节点 ID（如 "step2"）</summary>
    public string NodeId { get; set; }
    
    /// <summary>节点类型（如 "ProcessData"）</summary>
    public string NodeSymbol { get; set; }
    
    /// <summary>前驱节点 ID（从哪条边来）</summary>
    public string EdgeSource { get; set; }
    
    /// <summary>后继节点 ID（往哪条边去）</summary>
    public string EdgeTarget { get; set; }
    
    /// <summary>执行批次 ID</summary>
    public string BatchId { get; set; }
    
    /// <summary>节点在列表中的索引</summary>
    public int? NodeIndex { get; set; }
    
    /// <summary>占位符解析链</summary>
    public List<PlaceholderResolveRecord> PlaceholderChain { get; set; } = new();
}
```

#### 3.2.2 占位符解析记录

```csharp
/// <summary>
/// 占位符解析记录 — 记录一个 Placeholder 的完整解析过程
/// </summary>
public class PlaceholderResolveRecord
{
    /// <summary>占位符 Key</summary>
    public string Key { get; set; }
    
    /// <summary>占位符类型</summary>
    public string Kind { get; set; }
    
    /// <summary>原始表达式（如 "@node.step1.data"）</summary>
    public string Expression { get; set; }
    
    /// <summary>解析后的值</summary>
    public string ResolvedValue { get; set; }
    
    /// <summary>解析时间</summary>
    public DateTime ResolveTime { get; set; }
    
    /// <summary>处理该占位符的 Handler 名称</summary>
    public string HandlerName { get; set; }
}
```

#### 3.2.3 增强的日志条目

```csharp
/// <summary>
/// 增强的日志条目 — 在原有 LogEntry 基础上附加工作流上下文
/// </summary>
public class EnrichedLogEntry : LogEntry
{
    /// <summary>工作流图上下文</summary>
    public WorkflowGraphContext GraphContext { get; set; }
    
    /// <summary>关联的元数据 ID 列表</summary>
    public List<string> MetadataIds { get; set; } = new();
    
    /// <summary>结构化日志数据（JSON 格式，用于程序化消费）</summary>
    public string StructuredData { get; set; }
}
```

#### 3.2.4 增强的 PlaceholderMetadata

```csharp
/// <summary>
/// 增强的 Placeholder 元数据 — 新增工作流图关联字段
/// </summary>
public class EnrichedPlaceholderMetadata : PlaceholderMetadata
{
    /// <summary>所属工作流图 ID</summary>
    public string GraphId { get; set; }
    
    /// <summary>所属节点 ID</summary>
    public string NodeId { get; set; }
    
    /// <summary>节点类型</summary>
    public string NodeSymbol { get; set; }
    
    /// <summary>前驱节点 ID</summary>
    public string EdgeSource { get; set; }
    
    /// <summary>后继节点 ID</summary>
    public string EdgeTarget { get; set; }
    
    /// <summary>占位符原始表达式</summary>
    public string Expression { get; set; }
}
```

### 3.3 核心改动（仅两处）

#### 3.3.1 改动一：`PlaceholderResolverSkeleton.ProcessPlaceholder()` — 设置 AsyncLocal 上下文

```csharp
// 在 ProcessPlaceholder() 方法中新增

private void ProcessPlaceholder(
    KeyValuePair<string, Graphkey.Core.Placeholder> kvp,
    IResolutionContext resolveCtx,
    ConcurrentDictionary<string, string> resolvedValues,
    ConcurrentBag<PlaceholderMetadata> metadataCache,
    SkeletonContext ctx,
    string batchId,
    string moduleName)
{
    var key = kvp.Key;
    var placeholder = kvp.Value;

    try
    {
        var resolvedValue = PlaceholderResolver.Resolve(placeholder, resolveCtx);

        // === 新增：从 SkeletonContext 提取工作流上下文，设置到 AsyncLocal ===
        var graphCtx = ExtractGraphContext(ctx, placeholder, resolvedValue, batchId, moduleName);
        GraphLogContext.Set(graphCtx);

        // ... 后续代码不变：handler.Handle() → PlaceholderLogHelper.Log() ...
        // AsyncLocal 会自动沿调用链传递，所有 Handler 和 LogHelper 都能获取到

        lock (_metadataLock ?? _syncLock)
        {
            resolvedValues[key] = resolvedValue;
            var processedHandlers = new HashSet<string>();

            foreach (var handler in _handlers)
            {
                if (!handler.CanHandle(placeholder))
                    continue;

                try
                {
                    handler.Handle(placeholder, resolvedValue, ctx, batchId);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[PlaceholderResolver] Handler '{handler.Name}' failed for key '{key}': {ex.Message}");
                }

                if (!processedHandlers.Add(handler.Name))
                    continue;

                try
                {
                    // 保存增强元数据（含工作流上下文）
                    var metadata = CreateEnrichedMetadata(
                        placeholder, resolvedValue, graphCtx, batchId, handler.Name);
                    
                    if (_batchSave && metadataCache != null)
                        metadataCache.Add(metadata);
                    else
                        _metadataStore.Save(metadata);
                }
                catch (Exception ex) { /* ... */ }
            }
        }
    }
    catch (Exception ex) { /* ... */ }
    finally
    {
        // === 新增：清理 AsyncLocal 上下文 ===
        GraphLogContext.Clear();
    }
}

/// <summary>
/// 从 SkeletonContext 中提取工作流图上下文
/// </summary>
private static WorkflowGraphContext ExtractGraphContext(
    SkeletonContext ctx, Placeholder placeholder, 
    string resolvedValue, string batchId, string moduleName)
{
    var graphCtx = new WorkflowGraphContext
    {
        GraphId = ctx.TryGetData<string>(out var gid, "__GraphId") ? gid : null,
        NodeId = ctx.TryGetData<string>(out var nid, "__NodeId") ? nid : null,
        NodeSymbol = ctx.TryGetData<string>(out var nsym, "__NodeSymbol") ? nsym : null,
        EdgeSource = ctx.TryGetData<string>(out var esrc, "__EdgeSource") ? esrc : null,
        EdgeTarget = ctx.TryGetData<string>(out var etgt, "__EdgeTarget") ? etgt : null,
        BatchId = batchId,
        NodeIndex = placeholder.NodeIndex
    };
    
    // 记录当前占位符解析
    graphCtx.PlaceholderChain.Add(new PlaceholderResolveRecord
    {
        Key = placeholder.Key,
        Kind = placeholder.Kind.ToString(),
        Expression = $"{placeholder.Kind}:{placeholder.Key}",
        ResolvedValue = resolvedValue,
        ResolveTime = DateTime.UtcNow,
        HandlerName = moduleName
    });
    
    return graphCtx;
}

/// <summary>
/// 创建增强元数据
/// </summary>
private static PlaceholderMetadata CreateEnrichedMetadata(
    Placeholder ph, string resolvedValue, 
    WorkflowGraphContext graphCtx, string batchId, string handlerName)
{
    var metadata = PlaceholderMetadata.FromPlaceholder(
        ph, resolvedValue,
        module: graphCtx?.SourceModule,
        batchId: batchId,
        handlerName: handlerName);
    
    // 附加工作流上下文
    if (graphCtx != null)
    {
        metadata.GraphId = graphCtx.GraphId;
        metadata.NodeId = graphCtx.NodeId;
        metadata.NodeSymbol = graphCtx.NodeSymbol;
        metadata.EdgeSource = graphCtx.EdgeSource;
        metadata.EdgeTarget = graphCtx.EdgeTarget;
    }
    
    return metadata;
}
```

#### 3.3.2 改动二：`PlaceholderLogHelper.Log()` — 自动嵌入工作流上下文

```csharp
// 在 PlaceholderLogHelper 中新增

/// <summary>
/// 输出日志到 LogRepository（增强版 — 自动嵌入工作流上下文）
/// </summary>
public static void Log(string level, string category, string module, string message)
{
    // === 新增：从 AsyncLocal 获取工作流上下文 ===
    var graphCtx = GraphLogContext.Current;
    
    // 构造增强消息（带工作流位置前缀）
    var enrichedMessage = graphCtx?.GraphId != null
        ? $"[{graphCtx.GraphId}:{graphCtx.NodeId ?? "?"}] {message}"
        : message;
    
    var logEntry = new LogEntry
    {
        Timestamp = DateTime.Now,
        Level = (level?.ToUpperInvariant()) ?? "INFO",
        Category = LogCategoryResolver.Resolve(category),
        Module = module,
        Message = enrichedMessage
    };
    
    LogRepository.Write(logEntry);
    
    // === 新增：同时将工作流上下文写入元数据存储 ===
    if (graphCtx?.GraphId != null)
    {
        var metadata = new PlaceholderMetadata
        {
            Kind = "Log",
            Key = $"{graphCtx.GraphId}:{graphCtx.NodeId ?? "?"}",
            ResolvedValue = enrichedMessage,
            SourceModule = module,
            BatchId = graphCtx.BatchId,
            GraphId = graphCtx.GraphId,
            NodeId = graphCtx.NodeId,
            NodeSymbol = graphCtx.NodeSymbol,
            Timestamp = DateTime.UtcNow,
            MetadataId = Guid.NewGuid().ToString("N")
        };
        LogRepository.WriteMetadata(metadata);
    }
}
```

### 3.4 新增接口：工作流感知的日志查询

```csharp
/// <summary>
/// 工作流感知的日志查询接口
/// </summary>
public interface IGraphAwareLogQuery
{
    /// <summary>查询某个工作流图的所有日志</summary>
    IEnumerable<EnrichedLogEntry> FindByGraphId(string graphId);
    
    /// <summary>查询某个节点的所有日志</summary>
    IEnumerable<EnrichedLogEntry> FindByNodeId(string graphId, string nodeId);
    
    /// <summary>查询某条边的所有日志（两个节点之间的日志）</summary>
    IEnumerable<EnrichedLogEntry> FindByEdge(string graphId, string sourceNode, string targetNode);
    
    /// <summary>按时间范围查询工作流日志</summary>
    IEnumerable<EnrichedLogEntry> FindByTimeRange(string graphId, DateTime start, DateTime end);
    
    /// <summary>按日志级别查询工作流日志</summary>
    IEnumerable<EnrichedLogEntry> FindByLevel(string graphId, string level);
    
    /// <summary>追溯占位符解析链：从日志反查占位符</summary>
    List<PlaceholderResolveRecord> TracePlaceholderChain(string batchId, string metadataId);
    
    /// <summary>获取工作流图的执行热力图（每个节点的日志数量）</summary>
    Dictionary<string, int> GetGraphHeatmap(string graphId);
    
    /// <summary>获取工作流图的错误分布（每个节点的错误数量）</summary>
    Dictionary<string, int> GetGraphErrorDistribution(string graphId);
    
    /// <summary>获取工作流图的执行时间线</summary>
    List<GraphTimelineEntry> GetGraphTimeline(string graphId);
}

/// <summary>
/// 工作流执行时间线条目
/// </summary>
public class GraphTimelineEntry
{
    public string NodeId { get; set; }
    public string NodeSymbol { get; set; }
    public DateTime Timestamp { get; set; }
    public string Level { get; set; }
    public string Message { get; set; }
    public long DurationMs { get; set; }
}
```

### 3.5 SkeletonContext 扩展方法（供调用方设置上下文）

```csharp
/// <summary>
/// SkeletonContext 扩展方法 — 用于设置工作流上下文
/// 调用方在构建 SkeletonContext 时调用，一次设置，全局生效
/// </summary>
public static class GraphContextExtensions
{
    private const string GraphIdKey = "__GraphId";
    private const string NodeIdKey = "__NodeId";
    private const string NodeSymbolKey = "__NodeSymbol";
    private const string EdgeSourceKey = "__EdgeSource";
    private const string EdgeTargetKey = "__EdgeTarget";
    
    /// <summary>设置工作流图 ID</summary>
    public static void SetGraphId(this SkeletonContext ctx, string graphId)
        => ctx.SetData(graphId, GraphIdKey);
    
    /// <summary>设置节点 ID</summary>
    public static void SetNodeId(this SkeletonContext ctx, string nodeId)
        => ctx.SetData(nodeId, NodeIdKey);
    
    /// <summary>设置节点类型</summary>
    public static void SetNodeSymbol(this SkeletonContext ctx, string symbol)
        => ctx.SetData(symbol, NodeSymbolKey);
    
    /// <summary>设置边信息</summary>
    public static void SetEdge(this SkeletonContext ctx, string source, string target)
    {
        ctx.SetData(source, EdgeSourceKey);
        ctx.SetData(target, EdgeTargetKey);
    }
    
    /// <summary>批量设置工作流上下文</summary>
    public static void SetGraphContext(this SkeletonContext ctx, 
        string graphId, string nodeId = null, string nodeSymbol = null,
        string edgeSource = null, string edgeTarget = null)
    {
        if (graphId != null) ctx.SetGraphId(graphId);
        if (nodeId != null) ctx.SetNodeId(nodeId);
        if (nodeSymbol != null) ctx.SetNodeSymbol(nodeSymbol);
        if (edgeSource != null || edgeTarget != null)
            ctx.SetEdge(edgeSource, edgeTarget);
    }
}
```

---

## 4. 使用场景

### 4.1 调用方只需设置一次上下文

```csharp
// 调用方（如 FileDriven 执行引擎）只需在构建 SkeletonContext 时设置一次
var ctx = new SkeletonContext();
ctx.SetBatchId("batch_001");
ctx.SetModuleName("FileDriven");
ctx.SetGraphContext(
    graphId: "数据处理流程",
    nodeId: "step2",
    nodeSymbol: "ProcessData",
    edgeSource: "step1",
    edgeTarget: "step3"
);

// 设置 Placeholder
ctx.SetPlaceholder("log1", Placeholder.Input("filedriven:command_executed"));

// 执行解析 — 所有日志自动携带工作流上下文
resolver.Execute(ctx);
// 日志输出：[数据处理流程:step2] 命令 'ProcessData' 执行完成，耗时 150ms
```

### 4.2 场景1：工作流出错，反向定位

```csharp
// 日志输出：
// [数据处理流程:step2] 命令 'ProcessData' 执行失败：文件未找到

// 反向查询：查看 step2 节点的所有日志
var logs = logQuery.FindByNodeId("数据处理流程", "step2");
foreach (var log in logs.OrderBy(l => l.Timestamp))
{
    Console.WriteLine($"[{log.Timestamp:HH:mm:ss}] {log.Message}");
}

// 输出：
// [10:00:01] [数据处理流程:step2] 开始执行命令 'ProcessData'
// [10:00:02] [数据处理流程:step2] 读取配置文件 'config.json'
// [10:00:02] [数据处理流程:step2] 命令 'ProcessData' 执行失败：文件未找到
```

### 4.3 场景2：追溯数据流

```csharp
// 从一条日志反查它的占位符解析链
var chain = logQuery.TracePlaceholderChain("batch_001", "metadata_abc123");
foreach (var record in chain)
{
    Console.WriteLine($"{record.Key} → {record.ResolvedValue}");
}

// 输出：
// filedriven:command_executed → {"CommandName":"ProcessData","Duration":"150"}
// filedriven:pipeline_started → {"PipelineName":"数据处理流程"}
```

### 4.4 场景3：性能分析热力图

```csharp
// 查看哪个节点日志最多（可能有问题）
var heatmap = logQuery.GetGraphHeatmap("数据处理流程");
foreach (var kv in heatmap.OrderByDescending(kv => kv.Value))
{
    Console.WriteLine($"{kv.Key}: {kv.Value} 条日志");
}

// 输出：
// step2: 150 条日志 ← 最多，可能有问题
// step1: 5 条日志
// step3: 3 条日志
```

### 4.5 场景4：执行时间线

```csharp
// 查看工作流的执行时间线
var timeline = logQuery.GetGraphTimeline("数据处理流程");
foreach (var entry in timeline)
{
    Console.WriteLine($"[+{entry.DurationMs,5}ms] [{entry.Level}] {entry.NodeId}: {entry.Message}");
}

// 输出：
// [    0ms] [INFO] step1: 开始读取文件
// [  120ms] [INFO] step1: 文件读取完成
// [    5ms] [INFO] step2: 开始处理数据
// [  150ms] [ERROR] step2: 处理失败：文件未找到
```

### 4.6 场景5：错误分布

```csharp
// 查看工作流的错误分布
var errors = logQuery.GetGraphErrorDistribution("数据处理流程");
Console.WriteLine("错误分布：");
foreach (var kv in errors.OrderByDescending(kv => kv.Value))
{
    Console.WriteLine($"  {kv.Key}: {kv.Value} 个错误");
}

// 输出：
// 错误分布：
//   step2: 12 个错误
//   step3: 2 个错误
```

---

## 5. 运行后日志分析重建调用链

这是本方案的核心价值之一：**运行完成后，对已产生的日志进行静态分析，重建出完整的函数调用链。**

### 5.1 核心思路

```
日志文件（已包含 GraphId、NodeId、EdgeSource、EdgeTarget、时间戳）
  ↓
离线分析工具读取日志
  ↓
按 GraphId 分组 → 按 NodeId 分组 → 按时间排序
  ↓
从 EdgeSource/EdgeTarget 提取边关系
  ↓
重建完整的调用链图
```

### 5.2 分析工具实现

```csharp
/// <summary>
/// 日志后分析器 — 从已产生的日志中重建函数调用链
/// </summary>
public class LogCallChainAnalyzer
{
    /// <summary>
    /// 从日志重建调用链
    /// </summary>
    public CallChainGraph Analyze(IEnumerable<EnrichedLogEntry> logs)
    {
        var graph = new CallChainGraph();
        
        // 1. 按 GraphId 分组
        var byGraph = logs.GroupBy(l => l.GraphContext?.GraphId);
        
        foreach (var graphGroup in byGraph)
        {
            var graphId = graphGroup.Key ?? "unknown";
            
            // 2. 按 NodeId 分组，每个节点聚合所有日志
            var byNode = graphGroup.GroupBy(l => l.GraphContext?.NodeId);
            
            foreach (var nodeGroup in byNode)
            {
                var nodeId = nodeGroup.Key ?? "unknown";
                var nodeSymbol = nodeGroup.First().GraphContext?.NodeSymbol;
                
                // 3. 按时间排序，得到该节点的执行序列
                var timeline = nodeGroup.OrderBy(l => l.Timestamp).ToList();
                
                // 4. 从元数据中提取边信息
                var edges = timeline
                    .Select(l => l.GraphContext)
                    .Where(ctx => ctx != null)
                    .SelectMany(ctx => ExtractEdges(ctx))
                    .Distinct();
                
                graph.AddNode(nodeId, nodeSymbol, timeline, edges);
            }
            
            // 5. 重建节点间的连接关系
            graph.BuildConnections(graphId);
        }
        
        return graph;
    }
    
    /// <summary>从上下文中提取边</summary>
    private IEnumerable<(string From, string To)> ExtractEdges(WorkflowGraphContext ctx)
    {
        if (ctx.EdgeSource != null && ctx.EdgeTarget != null)
            yield return (ctx.EdgeSource, ctx.EdgeTarget);
    }
}

/// <summary>
/// 调用链图
/// </summary>
public class CallChainGraph
{
    public List<CallChainNode> Nodes { get; set; } = new();
    public List<CallChainEdge> Edges { get; set; } = new();
    
    public void AddNode(string nodeId, string symbol, 
        List<EnrichedLogEntry> timeline, IEnumerable<(string, string)> edges)
    {
        Nodes.Add(new CallChainNode
        {
            NodeId = nodeId,
            Symbol = symbol,
            Timeline = timeline,
            StartTime = timeline.First().Timestamp,
            EndTime = timeline.Last().Timestamp,
            Duration = (timeline.Last().Timestamp - timeline.First().Timestamp).TotalMilliseconds,
            LogCount = timeline.Count,
            ErrorCount = timeline.Count(l => l.Level == "ERROR"),
            Logs = timeline
        });
        
        foreach (var (from, to) in edges)
        {
            Edges.Add(new CallChainEdge
            {
                From = from,
                To = to,
                GraphId = timeline.First().GraphContext?.GraphId
            });
        }
    }
    
    public void BuildConnections(string graphId)
    {
        // 按时间排序节点
        var sortedNodes = Nodes.OrderBy(n => n.StartTime).ToList();
        
        // 如果没有显式边信息，按时间顺序推断
        if (Edges.Count == 0)
        {
            for (int i = 0; i < sortedNodes.Count - 1; i++)
            {
                Edges.Add(new CallChainEdge
                {
                    From = sortedNodes[i].NodeId,
                    To = sortedNodes[i + 1].NodeId,
                    GraphId = graphId,
                    Inferred = true  // 标记为推断的边
                });
            }
        }
    }
}

public class CallChainNode
{
    public string NodeId { get; set; }
    public string Symbol { get; set; }
    public List<EnrichedLogEntry> Timeline { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public double Duration { get; set; }
    public int LogCount { get; set; }
    public int ErrorCount { get; set; }
    public List<EnrichedLogEntry> Logs { get; set; }
}

public class CallChainEdge
{
    public string From { get; set; }
    public string To { get; set; }
    public string GraphId { get; set; }
    public bool Inferred { get; set; }  // true = 按时间推断的，false = 从元数据提取的
}
```

### 5.3 分析结果示例

```csharp
// 使用分析器
var analyzer = new LogCallChainAnalyzer();
var chain = analyzer.Analyze(allLogs);

// 输出调用链
foreach (var node in chain.Nodes.OrderBy(n => n.StartTime))
{
    var status = node.ErrorCount > 0 ? "❌" : "✅";
    Console.WriteLine($"{status} {node.NodeId} ({node.Symbol}) — {node.Duration}ms — {node.LogCount}条日志");
    
    if (node.ErrorCount > 0)
    {
        Console.WriteLine($"  错误：{node.ErrorCount}个");
        foreach (var errLog in node.Logs.Where(l => l.Level == "ERROR"))
        {
            Console.WriteLine($"    → {errLog.Message}");
        }
    }
}

Console.WriteLine("\n调用链：");
foreach (var edge in chain.Edges)
{
    var tag = edge.Inferred ? " (推断)" : "";
    Console.WriteLine($"  {edge.From} → {edge.To}{tag}");
}
```

### 5.4 输出示例

```
✅ step1 (ReadFile) — 120ms — 5条日志
✅ step2 (ProcessData) — 150ms — 3条日志
❌ step3 (WriteResult) — 50ms — 2条日志
  错误：1个
    → 写入文件失败：磁盘空间不足

调用链：
  step1 → step2
  step2 → step3
```

### 5.5 跨批次趋势分析

```csharp
// 分析多个批次的执行趋势
var allBatches = logQuery.GetAllBatchIds();
foreach (var batchId in allBatches)
{
    var logs = logQuery.FindByBatch(batchId);
    var chain = analyzer.Analyze(logs);
    
    Console.WriteLine($"批次 {batchId}:");
    Console.WriteLine($"  总耗时: {chain.Nodes.Sum(n => n.Duration)}ms");
    Console.WriteLine($"  总日志: {chain.Nodes.Sum(n => n.LogCount)}条");
    Console.WriteLine($"  总错误: {chain.Nodes.Sum(n => n.ErrorCount)}个");
    Console.WriteLine($"  调用链: {string.Join(" → ", chain.Nodes.OrderBy(n => n.StartTime).Select(n => n.NodeId))}");
}
```

### 5.6 能力总结

| 能力 | 说明 |
|------|------|
| **重建调用链** | ✅ 从日志中按 GraphId + NodeId 分组 + 时间排序 |
| **推断边关系** | ✅ 有显式边信息用显式，没有则按时间推断 |
| **节点统计** | ✅ 每个节点的耗时、日志量、错误数 |
| **跨批次分析** | ✅ 比较不同批次的执行趋势 |
| **不需要改代码** | ✅ 纯离线分析，只读日志文件 |

---

## 6. 改动清单

### 6.1 新增文件

| 文件 | 内容 |
|------|------|
| `Share/PlaceholderFramework/GraphAware/GraphLogContext.cs` | `AsyncLocal<WorkflowGraphContext>` 隐式上下文传递 |
| `Share/PlaceholderFramework/GraphAware/WorkflowGraphContext.cs` | 工作流图上下文类型 |
| `Share/PlaceholderFramework/GraphAware/PlaceholderResolveRecord.cs` | 占位符解析记录类型 |
| `Share/PlaceholderFramework/GraphAware/EnrichedLogEntry.cs` | 增强日志条目类型 |
| `Share/PlaceholderFramework/GraphAware/IGraphAwareLogQuery.cs` | 工作流感知日志查询接口 |
| `Share/PlaceholderFramework/GraphAware/GraphAwareLogQuery.cs` | 工作流感知日志查询实现 |
| `Share/PlaceholderFramework/GraphAware/GraphContextExtensions.cs` | SkeletonContext 扩展方法 |
| `Share/PlaceholderFramework/GraphAware/LogCallChainAnalyzer.cs` | 日志后分析器（重建调用链） |

### 6.2 修改文件（仅 2 处）

| 文件 | 改动 |
|------|------|
| `PlaceholderResolverSkeleton.cs` | `ProcessPlaceholder()` 中新增 `ExtractGraphContext()` + `GraphLogContext.Set()` |
| `PlaceholderLogHelper.cs` | `Log()` 中新增 `GraphLogContext.Current` 读取 + 嵌入上下文到消息 |

### 6.3 无需改动的文件

| 文件 | 原因 |
|------|------|
| `HandlersService.cs` | 所有 Handler 签名不变，通过 AsyncLocal 隐式获取上下文 |
| `LogPlaceholderHandlerBase.cs` | 不修改 `Handle()` 方法 |
| `LogPlaceholderHandler.cs` | 不修改 |
| `AuditPlaceholderHandler.cs` | 不修改 |
| `MonitorPlaceholderHandler.cs` | 不修改 |
| `PerformancePlaceholderHandler.cs` | 不修改 |
| `GraphKey.cs` | Placeholder 类型定义不变 |
| `LogMessageLoader.cs` | JSON 模板加载逻辑不变 |
| `LogCategoryResolver.cs` | 分类解析逻辑不变 |

---

## 7. 为什么用 AsyncLocal 而不是参数传递？

| 方案 | 优点 | 缺点 |
|------|------|------|
| **参数传递** | 显式、类型安全 | 需要修改所有 Handler 签名 + 所有调用方 |
| **AsyncLocal** | **零侵入**，只改 2 处 | 隐式传递，调试时需注意 |
| **ThreadStatic** | 简单 | 不支持 async/await 调用链 |
| **依赖注入** | 架构清晰 | 需要修改 DI 注册 + 所有 Handler 构造函数 |

**AsyncLocal 是最适合的方案**，因为：

1. `PlaceholderResolverSkeleton` → `Handler` → `PlaceholderLogHelper` 在同一个异步调用链中
2. AsyncLocal 自动沿 async/await 传播，无需任何参数传递
3. 所有现有 Handler 和日志调用方完全不需要修改
4. 在 `finally` 块中清理，不会泄漏到其他调用链

---

## 8. 向后兼容性

### 8.1 兼容策略

| 改动 | 兼容性 |
|------|--------|
| `PlaceholderLogHelper.Log()` 新增上下文嵌入 | 完全向后兼容，无上下文时输出原始消息 |
| `PlaceholderResolverSkeleton` 新增上下文提取 | 完全向后兼容，无上下文时跳过 |
| 新增 `GraphLogContext` 类 | 可选使用，不影响现有代码 |
| 新增 `IGraphAwareLogQuery` 接口 | 可选实现，不影响现有日志系统 |
| 新增 `GraphContextExtensions` | 可选使用，不修改现有 API |

### 8.2 降级行为

当工作流上下文不存在时（旧代码路径），系统自动降级：

```csharp
// PlaceholderLogHelper.Log() 中的自动降级
var graphCtx = GraphLogContext.Current;
var enrichedMessage = graphCtx?.GraphId != null
    ? $"[{graphCtx.GraphId}:{graphCtx.NodeId ?? "?"}] {message}"
    : message;  // 降级：输出原始消息
```

---

## 9. 未来扩展

### 9.1 可视化

- 在日志查看器中显示工作流图结构
- 点击节点显示该节点的所有日志
- 用颜色标记错误节点（红色 = 错误多）
- 调用链图可视化（节点 + 箭头 + 耗时标注）

### 9.2 告警

- 当某个节点的错误率超过阈值时自动告警
- 当工作流执行时间超过预期时自动告警
- 当调用链出现异常分支时自动告警

### 9.3 分析

- 工作流执行耗时分析（每个节点的平均耗时）
- 工作流执行成功率统计
- 工作流执行趋势分析（按天/周/月）
- 调用链变更检测（比较不同版本的调用链差异）

---

## 10. 总结

| 维度 | 优化前 | 优化后 |
|------|--------|--------|
| **日志内容** | 平面文本 | 结构化 + 工作流上下文 |
| **查询方式** | 按模块/级别 | 按工作流图/节点/边 |
| **追溯能力** | 无 | 占位符解析链追溯 |
| **分析能力** | 无 | 热力图/错误分布/时间线/调用链重建 |
| **调试效率** | 手动关联 | 自动关联工作流结构 |
| **改动量** | — | **只改 2 处基础设施** |
| **调用方改动** | — | **零改动** |
