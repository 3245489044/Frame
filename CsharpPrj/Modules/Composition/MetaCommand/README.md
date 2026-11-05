# MetaCommand Framework v3.0

工业级通用元指令框架 - 领域无关的命令协议

## 核心改进（v3.0）

| 特性 | 描述 |
|------|------|
| **模块化重构** | 将单文件 MetaCommand.cs 拆分为 13 个职责单一的文件 |
| **接口抽象** | 所有核心功能通过接口定义，便于测试和扩展 |
| **线程安全** | 使用 ConcurrentDictionary 和 ReaderWriterLockSlim 保护并发访问 |
| **命令解析** | 支持带空格参数、引号转义、丰富类型系统（bool/int/long/double/decimal/DateTime/Guid） |
| **中间件管道** | 支持日志、性能监控、权限校验等中间件 |
| **错误码体系** | 完整的错误码分类，便于程序化错误处理 |
| **权限控制** | 命令级权限校验、适配器访问控制 |
| **可观测性** | 执行时长统计、成功率追踪、慢命令告警 |
| **DI 集成** | 提供 `AddMetaCommand()` 扩展方法，无缝集成 .NET DI 容器 |
| **JSON 序列化** | 支持命令和结果的 JSON 序列化/反序列化 |

## 领域无关的元命令集

| 元命令 | 语义 | 参数 | 说明 |
|--------|------|------|------|
| **do** | 执行一个动作 | `<action> [target] [params]` | 对环境施加影响 |
| **get** | 读取实体的状态/值 | `<entity>` | 从环境中提取数据 |
| **look** | 获取当前可见的世界模型 | `[scope]` | 返回环境全貌或局部视图 |
| **wait** | 等待条件满足 | `<condition>` | 同步操作 |
| **assert** | 验证断言 | `<predicate>` | 用于测试和确认 |

## 架构设计

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                      AI Agent / Client                                     │
│  ┌─────────────┐                                                          │
│  │  思考决策层  │──命令──▶                                                 │
│  └─────────────┘                                                          │
└───────────────────────────────│─────────────────────────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                         CommandRouter                                      │
│  ┌──────────────────────────────────────────────────────────────────────┐   │
│  │  命令解析 → 中间件管道 → 路由分发 → 调用适配器 → 返回结果             │   │
│  │  [日志] → [性能] → [权限] → [执行]                                 │   │
│  └──────────────────────────────────────────────────────────────────────┘   │
└───────────────────────────────│─────────────────────────────────────────────┘
                                │
          ┌─────────────────────┼─────────────────────┐
          ▼                     ▼                     ▼
┌──────────────────┐  ┌──────────────────┐  ┌──────────────────┐
│   UI适配器       │  │  终端适配器      │  │   API适配器      │
└──────────────────┘  └──────────────────┘  └──────────────────┘
```

## 快速开始

### 基础用法

```csharp
// 创建命令路由器
var router = new CommandRouter(
    logger: new ConsoleLogger(),
    statistics: new CommandStatistics()
);

// 注册适配器
router.RegisterAdapter("console", new ConsoleAdapter());
router.SetActiveAdapter("console");

// 执行命令
var result = await router.ExecuteCommandAsync(@"do write message=""Hello World""");

// 获取执行指标
var metrics = router.GetMetrics();
Console.WriteLine($"成功率: {metrics.SuccessRate:P2}");
Console.WriteLine($"平均耗时: {metrics.AverageDurationMs:F2}ms");
```

### DI 集成

```csharp
// Program.cs
services.AddMetaCommand(options =>
{
    options.EnableMetrics = true;
    options.EnableAuthorization = true;
    options.EnableMiddlewarePipeline = true;
});

// 注册适配器
services.AddSingleton<IEnvironmentAdapter>(sp => new ConsoleAdapter());
```

### 带权限控制的配置

```csharp
var permissionStore = new DefaultPermissionStore();
var permissionService = new PermissionService(permissionStore);

var router = new CommandRouter(
    logger: new ConsoleLogger(),
    statistics: new CommandStatistics(),
    permissionService: permissionService
);
```

### 使用中间件管道

```csharp
var pipeline = new MiddlewarePipeline()
    .Use(new LoggingMiddleware(logger))
    .Use(new PerformanceMiddleware(logger, statistics))
    .Use(new AuthorizationMiddleware(permissionService));

var router = new CommandRouter(
    logger: logger,
    statistics: statistics,
    middlewarePipeline: pipeline
);
```

## 命令示例

```text
// 操作控制台
do write message="Hello World"
get stateValue
look

// 操作UI
do click btnSubmit timeout=5000
get txtUsername
look mainWindow

// 操作终端
do execute "ls -la" directory="/var/log"
get fileSize "/var/log/syslog"

// 操作HTTP API
do request method="POST" url="https://api.example.com/data" body="{\"key\":\"value\"}"
get responseHeader "X-Request-Id"
```

## 扩展适配器

实现 `IEnvironmentAdapter` 接口即可接入新的领域：

```csharp
public class MyAdapter : IEnvironmentAdapter
{
    public async Task<OperationResult<object>> DoAsync(string action, string? target, 
                                                      Dictionary<string, object>? parameters, 
                                                      CancellationToken cancellationToken)
    {
        // 实现自定义逻辑
        return OperationResult<object>.SuccessResult(result, "Operation completed");
    }
    
    // 实现其他方法...
}
```

或继承 `BaseAdapter` 抽象类：

```csharp
public class MyAdapter : BaseAdapter
{
    public MyAdapter() : base("my-adapter", "My Adapter", "1.0") { }
    
    public override Task<OperationResult<object>> DoAsync(...) { ... }
    // ...
}
```

## 错误码体系

| 错误码范围 | 类别 | 说明 |
|-----------|------|------|
| 1000-1099 | 通用错误 | 未知错误、参数错误、超时等 |
| 1100-1199 | 命令解析错误 | 格式错误、未知动词等 |
| 1200-1299 | 适配器错误 | 适配器不存在、未激活等 |
| 1300-1399 | 权限错误 | 访问拒绝、权限不足等 |
| 1400-1499 | 执行错误 | 执行失败、动作不存在等 |
| 1500-1599 | 存储错误 | 数据访问失败等 |

## 项目结构

```
MetaCommand/
├── Abstractions.cs              # 所有接口定义（ICommandExecutor、ICommandRouter、ICommandMiddleware 等）
├── Models.cs                    # 核心数据模型（MetaCommand、OperationResult<T>、ErrorCode 等）
├── Parsing.cs                   # 命令分词器、参数解析器、命令规范化器、命令解析器
├── Execution.cs                 # 中间件管线、内置中间件、命令路由器实现
├── Observability.cs             # 可观测性（命令统计和动词统计）
├── Security.cs                  # 权限控制（IPermissionService、IPermissionStore 实现）
├── Adapters.cs                  # 命令参数解析器、执行器基类、空执行器
├── Extensions.cs                # DI 集成和扩展方法
├── ControlAbstractions.cs       # 控件服务接口族（IControlValueAccessor、IControlFinder、IControlEventBinder、IControlInvoker、IControlService）
├── ControlServiceBase.cs        # 控件服务抽象基类（适配器注册表 + 继承链查找 + 递归树遍历 + 反射事件绑定）
├── ControlExecutorBase.cs       # 控件执行器基类（将 Do/Get/Look/Wait/Assert 映射到控件 5 原语）
├── ControlServiceExtensions.cs  # 控件服务扩展方法（SetItems、GetPreviewValue、批量读写）
├── MetaCommand.csproj           # 项目文件
└── README.md                    # 本文件
```

## WinForms 控件服务集成

MetaCommand 框架通过 `ControlServiceBase` / `ControlExecutorBase` 提供统一的 WinForms 控件服务能力。

### 核心实现

| 组件 | 位置 | 说明 |
|------|------|------|
| `WinFormsControlService` | `BasicMethod/Common/WinForms/` | 控件服务具体实现（值读写、控件查找、事件绑定、UI线程调度） |
| `WinFormsExecutor` | `BasicMethod/Common/WinForms/` | 控件执行器（Do/Get/Look/Wait/Assert → 控件 5 原语） |

### 废弃状态

以下旧类已标记 `[Obsolete]`，请迁移到 `WinFormsControlService.Instance`：

| 废弃类 | 位置 | 替代方案 |
|--------|------|---------|
| `ControlValueHelper` | `BasicMethod/Common/WinForms/` | `WinFormsControlService.Instance` |
| `ControlExtensions` | `BasicMethod/Common/WinForms/` | `WinFormsControlService.Instance` |
| `WinFormsCommandEngine` | `BasicMethod/Common/WinForms/` | `WinFormsExecutor` + `CommandRouter` |
| `WinFormsEventBinder` | `FileDriven/FileDriven.Business/UI/` | `WinFormsControlService.Instance` |
| `WdUIControlValueWriterAdapter` | `Operation/WdUI/Adapters/` | `WinFormsControlService.Instance` |
| `WdUIControlFinderAdapter` | `Operation/WdUI/Adapters/` | `WinFormsControlService.Instance` |
| `WdUIEventTrigger` | `Operation/WdUI/Adapters/` | `WinFormsControlService.Instance` |
| `WinFormsEventHelper` | `Operation/WdUI/UI/` | `WinFormsControlService.Instance` |


## 运行测试

```bash
cd MetaCommand.Tests
dotnet test
```

## 许可证

MIT License
