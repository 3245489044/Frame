# 重构方案：消除 `sideload` 命令路由断裂

## 1. 问题分析

### 1.1 症状

执行命令文件时出现两类错误：

```
错误 1: sideload MathFunctions.json: Unknown non-event command: sideload
错误 2: set valueATextBox 10: No binding found for 'valueATextBox.textchanged'
```

### 1.2 根本原因

**两条管线使用了不同的 `NonEventCommandHandler` 实例：**

| 组件 | 使用的 NonEventCommandHandler | 是否注册了 sideload |
|------|------------------------------|-------------------|
| `ScriptLoadSkeleton` → `FileDriven.Execution.Command.Skeletons.NonEventCommandHandler` | 新版（通过 `FunctionStorageRegistry`） | ✅ 是 |
| `CommandPipeline` → `ParseCommandStage` | 旧版（`Share/CommandFramework.Execution`） | ❌ 否 |

`sideload` 命令被 `CommandParser` 正确识别为非事件命令（`DefaultNonEventCommands` 包含 `"sideload"`），但路由到旧版 `NonEventCommandHandler.HandleAsync()` 时，旧版处理器找不到 `sideload` 的处理器，返回 `"Unknown non-event command: sideload"`。

### 1.3 调用链分析

```
FileDrivenExecutor.InitializeFramework()
  ├── PipelineFactory.RegisterBuiltInCommands()          ← 注册 sideload 到 FunctionStorageRegistry
  └── CommandFrameworkUsage.Reset()                      ← 初始化全局单例
      └── CommandFrameworkHost 内部 new NonEventCommandHandler()  ← 旧版实例 A

FileDrivenExecutor.StartCliMode() / StartGuiMode()
  ├── CommandFrameworkUsage.RegisterServices(
  │     configLoader: pipelineEntry)                     ← 注册 sideload 到实例 A
  │
  └── executionEntry.RunPipeline()
        └── PipelineFactory.CreateFileDrivenPipeline()
              └── PipelineFactory.BuildCommandPipeline()
                    └── PipelineEntry.CreateDefaultPipeline(
                          findBindingFunc: ...,
                          functionExecutor: ...)
                          // ★ 没有传 nonEventHandler 参数！
                          └── new NonEventCommandHandler()  ← 新实例 B！没有 sideload
```

**关键问题：** `BuildCommandPipeline()` 调用 `CreateDefaultPipeline()` 时没有传入 `nonEventHandler` 参数，导致 `CreateDefaultPipeline()` 内部 `new` 了一个全新的 `NonEventCommandHandler` 实例（第 70 行）：

```csharp
// PipelineEntry.cs 第 68-72 行
pipeline.Register(new ParseCommandStage(
    parser ?? new CommandParser(),
    nonEventHandler ?? new NonEventCommandHandler(),  // ← 新实例 B！没有注册 sideload
    engine,
    environmentAdapter))
```

这个新实例 B 只注册了 `echo/sleep/wait` 三个默认命令，**没有经过 `RegisterServiceCommands()` 注册**，所以不认识 `sideload`。

---

## 2. 重构方案（方案C — 外部委托注入）

### 核心思路

`ParseCommandStage` 不再持有旧版 `NonEventCommandHandler`，而是接受一个外部注入的查找委托。适配逻辑全部在 `FileDriven.Execution` 中完成，`Share/CommandFramework` 不新增任何引用。

**委托签名：** `Func<string, CommandContext, Task<bool>>?`

- `string` — 命令动作名（如 `"sideload"`）
- `CommandContext` — 命令上下文，委托内部直接修改其状态
- `Task<bool>` — 返回是否找到并成功执行

### 需要改动的文件（共 5 个）

---

### 2.1 `Share/CommandFramework/Pipeline/Stages/ParseCommandStage.cs`

**改动：**
- 构造函数移除 `NonEventCommandHandler nonEventHandler` 参数
- 增加 `Func<string, CommandContext, Task<bool>>? findNonEventHandler` 参数（可选）
- 移除 `_nonEventHandler` 字段，增加 `_findNonEventHandler` 字段
- `ExecuteCoreAsync()` 中非事件命令路由逻辑改为通过委托查找并调用

```csharp
// 字段
private readonly Func<string, CommandContext, Task<bool>>? _findNonEventHandler;

// 构造函数
public ParseCommandStage(
    CommandParser parser,
    Func<string, CommandContext, Task<bool>>? findNonEventHandler = null,
    CommandEngine? engine = null,
    IEnvironmentAdapter? environmentAdapter = null)
    : base("ParseCommand", "Parse command string and route to appropriate handler")
{
    _parser = parser ?? throw new ArgumentNullException(nameof(parser));
    _findNonEventHandler = findNonEventHandler;
    _engine = engine;
    _environmentAdapter = environmentAdapter;
}

// ExecuteCoreAsync 中非事件命令路由（替换原有第 104-119 行）
// 3.1 非事件命令 → 通过外部委托查找并执行
if (parsed.IsNonEventCommand)
{
    if (_findNonEventHandler == null)
    {
        SetFailure(context, "Non-event command handler is not available");
        return;
    }

    var action = parsed.Action?.ToLowerInvariant() ?? parsed.Verb?.ToLowerInvariant();
    var success = await _findNonEventHandler(action, context);

    if (!success)
    {
        SetFailure(context, context.FunctionResultValue?.ToString()
            ?? $"Unknown non-event command: {action}");
        return;
    }

    context.SetStageResult(Name, true);
    return;
}
```

**注意：** 委托内部已经设置了 `context.FunctionResultValue` 和 `context.FunctionExecutionTime`，`ParseCommandStage` 不再需要处理 `CommandExecutionResult`。

---

### 2.2 `Share/CommandFramework/Pipeline/PipelineEntry.cs`

**改动：**
- `CreateDefaultPipeline()` 方法签名：移除 `NonEventCommandHandler? nonEventHandler` 参数，增加 `Func<string, CommandContext, Task<bool>>? findNonEventHandler` 参数
- `CreateCliPipeline()` 同理
- `PipelineEntryInstance` 类：移除 `_nonEventHandler` 字段和构造函数中的 `NonEventCommandHandler` 参数，增加 `_findNonEventHandler` 字段
- `PipelineEntryInstance.RegisterServices()` 方法：移除（不再需要）
- `PipelineEntryInstance.LoadConfig()` 方法：移除（不再需要）
- `PipelineEntryInstance` 构造函数：移除 `NonEventCommandHandler` 参数

```csharp
// CreateDefaultPipeline 新签名
public static BP.IStagePipeline<CommandContext> CreateDefaultPipeline(
    string name = "DefaultCommandPipeline",
    Action<BP.IStagePipeline<CommandContext>>? configure = null,
    Func<string, string, (string? FunctionName, List<string>? Inputs, string? Output)>? findBindingFunc = null,
    CommandParser? parser = null,
    Func<string, CommandContext, Task<bool>>? findNonEventHandler = null,
    CommandEngine? engine = null,
    IEnvironmentAdapter? environmentAdapter = null,
    IFunctionExecutor? functionExecutor = null)
{
    var pipeline = new BP.StagePipeline<CommandContext>(name, "Default command execution pipeline");

    pipeline
        .Register(new ParseCommandStage(
            parser ?? new CommandParser(),
            findNonEventHandler,
            engine,
            environmentAdapter))
        .Register(new FindBindingStage(findBindingFunc))
        .Register(new ExtractValuesStage())
        .Register(new ExecuteFunctionStage(functionExecutor))
        .Register(new WriteResultStage());

    configure?.Invoke(pipeline);
    return pipeline;
}

// CreateCliPipeline 新签名
public static BP.IStagePipeline<CommandContext> CreateCliPipeline(
    string name = "CliCommandPipeline",
    Func<string, CommandContext, Task<bool>>? findNonEventHandler = null,
    params BP.IPipelineStage<CommandContext>[] additionalStages)
{
    var pipeline = new BP.StagePipeline<CommandContext>(name, "CLI mode command execution pipeline");
    var parser = new CommandParser();

    pipeline
        .Register(new ParseCommandStage(parser, findNonEventHandler));

    foreach (var stage in additionalStages)
        pipeline.Register(stage);

    return pipeline;
}

// PipelineEntryInstance 新构造函数
public PipelineEntryInstance(
    CommandParser parser,
    Func<string, CommandContext, Task<bool>>? findNonEventHandler = null,
    CommandEngine? engine = null,
    IEnvironmentAdapter? environmentAdapter = null)
{
    _parser = parser ?? throw new ArgumentNullException(nameof(parser));
    _findNonEventHandler = findNonEventHandler;
    _engine = engine;
    _environmentAdapter = environmentAdapter;
    _loadedConfigs = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
}
```

**移除的方法：**
- `RegisterServices()` — 不再需要
- `LoadConfig()` — 不再需要
- `FindBinding()` — 不再需要（`FindBindingStage` 直接使用 `findBindingFunc`）

---

### 2.3 `FileDriven/FileDriven.Execution/Command/PipelineFactory.cs`

**改动：**
- `BuildCommandPipeline()` 中构造查找委托，传入 `CreateDefaultPipeline()`
- 委托内部适配 `FunctionStorageRegistry.NonEventRegistry` → 直接修改 `CommandContext`

```csharp
public static BP.IPipeline<FileDrivenPipelineContext, FileDrivenPipelineContext> BuildCommandPipeline(
    IEventBindingResolver? bindingResolver,
    IFunctionExecutor? functionExecutor)
{
    var findBindingFunc = bindingResolver?.ToFindBindingFunc();

    // ★ 构造非事件命令查找委托
    //   将 FunctionStorageRegistry.NonEventRegistry 适配为查找委托
    //   适配逻辑在 FileDriven.Execution 中完成，Share/CommandFramework 不新增引用
    Func<string, CommandContext, Task<bool>>? findNonEventHandler = (action, ctx) =>
    {
        var registry = FunctionStorageRegistry.NonEventRegistry;
        if (!registry.TryGetHandler(action, out var handler))
            return Task.FromResult(false);

        // 适配 CommandContext → IExecutionContext
        var adapter = new FunctionExecutionContextAdapter((FileDrivenPipelineContext)ctx);
        handler(adapter);

        // 将适配器中的结果写回 CommandContext
        ctx.FunctionResultValue = adapter.FunctionResultValue;
        ctx.FunctionExecutionTime = adapter.FunctionExecutionTime;

        return Task.FromResult(adapter.FunctionExecuteSuccess);
    };

    var stagePipeline = CommandFramework.Pipeline.PipelineEntry.CreateDefaultPipeline(
        "CommandPipeline",
        findBindingFunc: findBindingFunc,
        functionExecutor: functionExecutor,
        findNonEventHandler: findNonEventHandler);  // ★ 传入查找委托

    return new BP.DelegatePipeline<FileDrivenPipelineContext, FileDrivenPipelineContext>(
        "CommandPipeline",
        syncWithCtx: (ctx, pipelineCtx) =>
        {
            stagePipeline.ExecuteAsync(ctx, pipelineCtx.CancellationToken)
                .GetAwaiter().GetResult();
            return ctx;
        },
        asyncWithCtx: async (ctx, pipelineCtx) =>
        {
            await stagePipeline.ExecuteAsync(ctx, pipelineCtx.CancellationToken)
                .ConfigureAwait(false);
            return ctx;
        },
        description: "Command execution pipeline (wraps StagePipeline<CommandContext>)");
}
```

---

### 2.4 `FileDriven/FileDriven.Execution/FileDrivenExecutor.cs`

**改动：**
- 移除 GUI 模式中的 `CommandFrameworkUsage.RegisterServices(configLoader: pipelineEntry)` 调用（第 885-886 行）
- 移除 CLI 模式中的 `CommandFrameworkUsage.RegisterServices(configLoader: pipelineEntry)` 调用（第 986-987 行）

---

### 2.5 `Share/CommandFramework/CommandFrameworkHost.cs`

**改动：**
- `RegisterServices()` 方法：移除（不再需要）
- `LoadConfig()` 方法：移除（不再需要）
- 构造函数中不再创建 `NonEventCommandHandler`（第 153 行 `NonEventHandler = new NonEventCommandHandler()` 移除）
- `NonEventHandler` 属性：移除（不再需要）
- `PipelineEntryInstance` 构造函数调用：移除 `NonEventHandler` 参数

---

## 3. 数据流（修复后）

```
命令: sideload MathFunctions.json
  │
  ▼
CommandParser.Parse()
  └── parsed.IsNonEventCommand = true  (DefaultNonEventCommands 包含 "sideload")
  │
  ▼
ParseCommandStage.ExecuteCoreAsync()
  └── 进入非事件命令分支
  └── _findNonEventHandler("sideload", context)
      │
      ▼
  PipelineFactory 中构造的委托:
      └── FunctionStorageRegistry.NonEventRegistry.TryGetHandler("sideload", out handler)
          └── ✅ 找到！
          └── adapter = new FunctionExecutionContextAdapter(ctx)
          └── handler(adapter)
              └── HandleSideload() 执行
                  └── configLoader.LoadConfig(target, ...)
                      └── 配置加载成功 → LoadedConfigs 有数据
          └── ctx.FunctionResultValue = adapter.FunctionResultValue
          └── return true
  │
  ▼
后续命令: set valueATextBox 10
  └── FindBindingStage 查找 "valueATextBox.textchanged"
      └── EventBindingResolver 从 LoadedConfigs 中找到绑定
          └── ✅ 成功！
```

---

## 4. 实施步骤

| # | 操作 | 文件 | 说明 |
|---|------|------|------|
| 1 | 修改构造函数和路由逻辑 | `Share/CommandFramework/Pipeline/Stages/ParseCommandStage.cs` | 移除 `NonEventCommandHandler` 依赖，改用查找委托 |
| 2 | 修改工厂方法签名 + 移除废弃方法 | `Share/CommandFramework/Pipeline/PipelineEntry.cs` | `CreateDefaultPipeline`/`CreateCliPipeline`/`PipelineEntryInstance` 改用查找委托；移除 `RegisterServices()`/`LoadConfig()`/`FindBinding()` |
| 3 | 移除废弃方法 + 移除 NonEventCommandHandler | `Share/CommandFramework/CommandFrameworkHost.cs` | 移除 `RegisterServices()`/`LoadConfig()`/`NonEventHandler` 属性 |
| 4 | 构造查找委托并传入 | `FileDriven/FileDriven.Execution/Command/PipelineFactory.cs` | 适配 `FunctionStorageRegistry.NonEventRegistry` |
| 5 | 移除补丁代码 | `FileDriven/FileDriven.Execution/FileDrivenExecutor.cs` | 移除两处 `CommandFrameworkUsage.RegisterServices(configLoader: pipelineEntry)` |
| 6 | 验证编译 | 项目根目录 | `dotnet build` 确认无编译错误 |
| 7 | 验证功能 | 运行 CLI 模式 | 执行包含 `sideload` 和 `set` 命令的脚本，确认不再报错 |

---

## 5. 注意事项

1. **`Share/CommandFramework` 不新增引用**：委托签名使用 `CommandContext`（已在项目中），不依赖 `FunctionStorage.Abstractions`。

2. **`CommandContext` 缺少 `FunctionExecuteSuccess`/`FunctionErrorMessage` 属性**：这些属性在 `FileDrivenPipelineContext` 中。`ParseCommandStage` 中通过 `SetStageResult()` 标记成功/失败，不直接设置这些属性。委托内部通过 `adapter.FunctionExecuteSuccess` 判断成功/失败。

3. **`CommandFrameworkUsage.RegisterServices()` 和 `PipelineInstance.RegisterServices()` 同时移除**：因为 `PipelineInstance.LoadConfig()` 不再需要，整个 `RegisterServices` 调用链全部移除。

4. **`CommandFrameworkHost.NonEventHandler` 属性移除**：此属性标记为 `[Obsolete]`，方案C彻底消除了对它的依赖，可以安全移除。
