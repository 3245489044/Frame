# Placeholder 通用数据引用框架

## 概述

构建一个**通用的数据引用追踪框架**，核心思想是：

> **业务代码只声明"我引用了什么数据"，框架负责"怎么解析、怎么处理、怎么追溯"**

## 核心抽象

```
Placeholder 机制 = 声明式数据引用 + 延迟解析 + 元数据持久化
```

- **声明式数据引用**：业务代码通过 Placeholder 声明数据来源，不关心后续处理
- **延迟解析**：Placeholder 在 Pipeline 下游统一解析，解析时机与业务执行分离
- **元数据持久化**：所有引用关系持久化存储，支持事后追溯和分析

## 项目结构

```
Share/PlaceholderFramework/
├── Core/                              # 核心类型
│   ├── Placeholder.cs                   # 声明式数据引用类型（Input/Prev/NodeReference/NodeIndex）
│   ├── IResolutionContext.cs            # 解析上下文接口
│   ├── PlaceholderContextExtensions.cs  # 引用上下文扩展
│   ├── PlaceholderMetadata.cs           # 持久化元数据
│   ├── PlaceholderResolverSkeleton.cs   # 统一解析 + 分发 Skeleton
│   ├── PlaceholderTemplateFormatter.cs  # 模板格式化工具类
│   ├── PlaceholderLogHelper.cs          # 日志输出辅助类
│   ├── LogCategoryResolver.cs           # 统一日志分类解析器
│   ├── LogMessageLoader.cs              # 日志消息加载器
│   └── ConfigInjector.cs                # ★ 配置注入器（通过特性标注自动注入配置）
├── Handlers/                          # Handler 接口与实现
│   ├── IPlaceholderHandler.cs           # Handler 接口
│   ├── LogPlaceholderHandler.cs         # 日志 Handler
│   ├── MonitorPlaceholderHandler.cs     # 监控告警 Handler
│   ├── AuditPlaceholderHandler.cs       # 审计追踪 Handler
│   ├── PerformancePlaceholderHandler.cs # 性能分析 Handler
│   ├── FileDrivenLogPlaceholderHandler.cs  # FileDriven 日志 Handler
│   ├── FsLogPlaceholderHandler.cs         # FunctionStorage 日志 Handler
│   ├── WdUILogPlaceholderHandler.cs       # WdUI 日志 Handler
│   └── ConfigPlaceholderHandler.cs        # ★ 配置 Handler（从 JSON 读取配置值）
├── Storage/                           # 持久化存储
│   ├── IMetadataStore.cs                # 存储接口
│   └── MetadataStore.cs                 # 内存实现
├── Query/                             # 追溯查询
│   ├── PlaceholderQuery.cs              # 正向查询
│   └── PlaceholderReverseResolver.cs    # 反向追溯
└── README.md
```

## 快速开始

### 1. 在业务 Skeleton 中声明 Placeholder

```csharp
public class OrderProcessSkeleton : SkeletonBase<OrderContext, OrderContext>
{
    protected override OrderContext ExecuteCore(OrderContext input, SkeletonContext ctx)
    {
        // 业务代码只声明"我引用了什么数据"
        ctx.SetPlaceholder("order:start", Placeholder.Input("开始处理订单"));
        ctx.SetPlaceholder("order:total", Placeholder.PrevByKey("totalAmount"));
        ctx.SetPlaceholder("order:end", Placeholder.Input("处理完成"));
        return input;
    }
}
```

### 2. 组装 Pipeline

```csharp
using Common.PlaceholderFramework.Core;
using Common.PlaceholderFramework.Handlers;
using Common.PlaceholderFramework.Storage;

// 创建元数据存储
var metadataStore = new MemoryMetadataStore();

// 创建 Pipeline
var pipeline = Flow.Start<OrderContext>()
    .Then(new InitSkeleton())
    .Then(new OrderProcessSkeleton())
    .Then(new PlaceholderResolverSkeleton(
        handlers: new IPlaceholderHandler[]
        {
            new LogPlaceholderHandler(),
            new MonitorPlaceholderHandler(),
            new AuditPlaceholderHandler(),
            new PerformancePlaceholderHandler(),
        },
        metadataStore: metadataStore))
    .Then(new FlushSkeleton());

// 执行
var result = pipeline.Execute(input);
```

### 3. 追溯查询

```csharp
using Common.PlaceholderFramework.Query;

var query = new PlaceholderQuery(metadataStore);

// 正向查询
var batchResults = query.FindByBatch(batchId);
var moduleResults = query.FindByModule(batchId, "OrderProcess");
var kindResults = query.FindByKind(batchId, "Input");

// 反向追溯
var resolver = new PlaceholderReverseResolver(metadataStore);
var callChain = resolver.RebuildCallChain(batchId);
var dataFlow = resolver.RebuildDataFlow(batchId);
var source = resolver.TraceDataSource(batchId, metadataId);
var sinks = resolver.TraceDataSink(batchId, metadataId);
```

## 配置注入（新增功能）

### 方式一：通过 ConfigPlaceholderHandler 在 Pipeline 中使用

```csharp
// 在 Pipeline 中注册 ConfigPlaceholderHandler
var pipeline = Flow.Start<MyContext>()
    .Then(new MySkeleton())
    .Then(new PlaceholderResolverSkeleton(
        handlers: new IPlaceholderHandler[]
        {
            new ConfigPlaceholderHandler("appsettings.json"),
            // ... 其他 Handler
        }));

// 在 Skeleton 中声明配置引用
ctx.SetPlaceholder("config:CodeRL.Pipeline.Name", Placeholder.Input("pipelineName"));
ctx.SetPlaceholder("config:CodeRL.Training.MaxEpisodes", Placeholder.Input("maxEpisodes"));
```

### 方式二：通过 ConfigInjector 直接注入强类型对象

```csharp
using Common.PlaceholderFramework.Core;

// 定义配置类
public class MyConfig
{
    [ConfigProperty("CodeRL.Pipeline.Name")]
    public string PipelineName { get; set; }

    [ConfigProperty("CodeRL.Training.MaxEpisodes", DefaultValue = 100)]
    public int MaxEpisodes { get; set; }

    [ConfigProperty("CodeRL.Weights.Temperature", DefaultValue = 1.0)]
    public double Temperature { get; set; }

    [ConfigProperty("CodeRL.Pipeline.EnableLLMNavigation", DefaultValue = true)]
    public bool EnableLLMNavigation { get; set; }
}

// 从 JSON 字符串注入
var config = ConfigInjector.Inject<MyConfig>(jsonString);

// 从已有对象注入
var config = new MyConfig();
ConfigInjector.InjectInto(config, jsonString);
```

### 方式三：直接使用 ConfigPlaceholderHandler 的查询方法

```csharp
var configHandler = new ConfigPlaceholderHandler("test_parameters.json");

// 直接读取配置值
string name = configHandler.GetString("CodeRL.Pipeline.Name");
int maxEpisodes = configHandler.GetInt("CodeRL.Training.MaxEpisodes", defaultValue: 100);
double threshold = configHandler.GetDouble("CodeRL.Training.ConvergenceThreshold");
bool enabled = configHandler.GetBool("CodeRL.Pipeline.EnableLLMNavigation");
double[] rewards = configHandler.GetDoubleArray("CodeRL.Convergence.StableRewards");
```

## 场景扩展表

| 场景 | Key 前缀 | Handler 职责 | 新增成本 |
|------|---------|-------------|---------|
| 日志 | `log:` / `order:` | 输出到 Logger + 持久化元数据 | 1 个 Handler 类 |
| 监控告警 | `monitor:` | 评估告警规则 + 记录指标 | 1 个 Handler 类 |
| 审计追踪 | `audit:` | 记录审计条目 | 1 个 Handler 类 |
| 性能分析 | `perf:` | 记录耗时 + 统计 | 1 个 Handler 类 |
| 数据校验 | `validate:` | 执行校验规则 | 1 个 Handler 类 |
| A/B 测试 | `experiment:` | 动态路由到实验分支 | 1 个 Handler 类 |
| 事件驱动 | `event:` | 派发事件到消息队列 | 1 个 Handler 类 |
| 调试分析 | `debug:` | 记录调试信息到独立存储 | 1 个 Handler 类 |
| **配置管理** | **`config:`** | **从 JSON 读取配置值** | **1 个 Handler 类** |

**关键优势**：新增一个处理场景 = 新增一个 `IPlaceholderHandler` 实现 + 在 Pipeline 中注册，**无需修改任何业务代码**。

## 与 UniversalReference 的区别

| 维度 | UniversalReference | Placeholder Framework |
|------|-------------------|----------------------|
| **关注点** | 资源之间的**静态关系**（谁依赖谁） | 运行时数据的**动态引用**（数据从哪里来） |
| **数据粒度** | 资源级别（Module、Artifact） | 数据字段级别（orderId、totalAmount） |
| **时间维度** | 持久化存储，跨会话 | 运行时追踪，按批次 |
| **关系类型** | DependsOn、Includes、Extends | Input、Prev、NodeReference、NodeIndex |
| **查询方向** | 正向：资源 → 依赖/被依赖 | 双向：正向（按批次查）+ 反向（追溯数据来源） |
| **使用时机** | 设计时/部署时（配置资源关系） | 运行时（追踪数据流） |
| **典型场景** | 模块依赖分析、版本管理 | 日志追溯、监控告警、审计追踪、**配置注入** |

两者是**互补**关系：
- UniversalReference 管理"谁和谁有关系"（静态拓扑）
- Placeholder Framework 管理"数据在运行时如何流动"（动态数据流）
