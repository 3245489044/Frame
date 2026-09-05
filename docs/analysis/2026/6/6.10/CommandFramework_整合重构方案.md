# CommandFramework 整合重构方案

> 版本：v1.0
> 日期：2026-06-10
> 状态：方案设计

---

## 一、现状分析

### 1.1 核心问题

CommandFramework 存在**两条并行的命令执行路径**，导致代码混乱：

```
命令字符串 "do click addButton"
    │
    ├─ 路径 A：CommandEngine.ExecuteParsedCommandAsync()
    │   自己解析 → 自己分类 → 自己路由 → 自己执行 → 自己管理撤销/重做
    │
    └─ 路径 B：Pipeline.ExecuteAsync()
        自己解析 → 自己找绑定 → 自己提取值 → 自己执行函数 → 自己写回结果
```

**两条路径都能处理大部分命令，但各自有对方没有的能力：**

| 能力 | 引擎路径 | 管线路径 |
|------|:-------:|:--------:|
| 命令解析 | ✅ | ✅ |
| 非事件命令处理 | ✅ | ✅ |
| 环境适配器路由 | ✅ | ❌ |
| 撤销/重做 | ✅ | ❌ |
| 事件绑定查找 | ❌ | ✅ |
| 控件值提取 | ❌ | ✅ |
| 函数执行 | ❌ | ✅ |
| 结果写回 | ❌ | ✅ |

### 1.2 其他问题

| 问题 | 说明 |
|------|------|
| ICommand.cs 过度膨胀 | 751 行包含 12 个不相关的接口 |
| 两个上下文重叠 | CommandContext 和 CommandExecutionContext 字段高度重叠 |
| 非事件命令三层嵌套 | 分类在 Parser、分流在 ParseCommandStage、执行在 NonEventCommandHandler |
| 横切关注点混入 | ValidationStage/LoggingStage/RetryStage 与业务阶段在同一目录 |
| FunctionExecution.cs 过度合并 | 669 行包含 13 个类 |
| EventTriggerFramework 残留 | 迁移不彻底，存在重复代码 |

---

## 二、整合目标

### 2.1 核心目标

**将引擎和管线合并为一个统一的命令执行入口**，让调用方只需要知道一个类。

### 2.2 具体目标

1. **统一入口**：`CommandPipeline` 作为唯一的命令执行入口
2. **统一上下文**：只有一个 `CommandContext`
3. **统一接口文件**：按职责拆分 ICommand.cs
4. **统一阶段组织**：业务阶段和横切阶段分离
5. **消除重复**：清理 EventTriggerFramework 残留

---

## 三、新架构设计

### 3.1 架构总览

```
┌──────────────────────────────────────────────────────────────────┐
│                     CommandPipeline（统一入口）                     │
│                                                                    │
│  ExecuteAsync(string command) → CommandContext                     │
│  ExecuteFileAsync(string path) → (int success, int fail)          │
│  UndoAsync() / RedoAsync()                                        │
│                                                                    │
│  内部使用 Pipeline 执行命令，自动记录历史支持撤销/重做               │
└──────────────────────────┬───────────────────────────────────────┘
                           │
                           ▼
┌──────────────────────────────────────────────────────────────────┐
│              StagePipeline<CommandContext>（管线引擎）              │
│                                                                    │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐            │
│  │ ParseCommand │→│ FindBinding  │→│ ExtractValues│            │
│  │    Stage     │  │    Stage     │  │    Stage     │            │
│  └──────────────┘  └──────────────┘  └──────────────┘            │
│         │                                                         │
│         ▼                                                         │
│  ┌──────────────┐  ┌──────────────┐                              │
│  │ExecuteFunction│→│ WriteResult  │                              │
│  │    Stage     │  │    Stage     │                              │
│  └──────────────┘  └──────────────┘                              │
│                                                                    │
│  横切阶段（可选）：ValidationStage → LoggingStage → RetryStage     │
└──────────────────────────────────────────────────────────────────┘

工具类（被管线阶段使用）：
    CommandParser          — 命令解析
    NonEventCommandHandler — 非事件命令处理
    FunctionRegistry       — 函数注册表
    FunctionExecutor       — 函数执行器
    ControlValueAccessor   — 控件值读写
    InMemoryStateStore     — 状态存储
    EnvironmentAdapter     — 环境适配器
```

### 3.2 核心类设计

```csharp
/// <summary>
/// 命令管线 — 统一的命令执行入口
/// 合并了原 CommandEngine 的调度能力和 Pipeline 的处理能力
/// </summary>
public class CommandPipeline : IDisposable
{
    // ================================================================
    // 管线引擎（内部使用 BasicMethod.Pipeline 的 StagePipeline）
    // ================================================================
    private readonly BP.IStagePipeline<CommandContext> _pipeline;

    // ================================================================
    // 撤销/重做历史栈（来自原 CommandEngine）
    // ================================================================
    private readonly LinkedList<CommandContext> _history = new();
    private readonly LinkedList<CommandContext> _redoStack = new();
    private readonly object _lock = new();

    // ================================================================
    // 工具类（由管线阶段使用）
    // ================================================================
    public CommandParser Parser { get; }
    public NonEventCommandHandler NonEventHandler { get; }
    public FunctionRegistry Functions { get; }
    public FunctionExecutor Executor { get; }
    public IEnvironmentAdapter? EnvironmentAdapter { get; set; }

    // ================================================================
    // 构造函数
    // ================================================================
    public CommandPipeline(Action<BP.IStagePipeline<CommandContext>>? configure = null,
                           Func<string, string, (string?, List<string>?, string?)>? findBindingFunc = null)
    {
        Parser = new CommandParser();
        NonEventHandler = new NonEventCommandHandler();
        Functions = new FunctionRegistry();
        Executor = new FunctionExecutor(Functions);

        _pipeline = CreatePipeline(configure, findBindingFunc);
    }

    // ================================================================
    // 统一执行入口
    // ================================================================

    /// <summary>执行单条命令</summary>
    public async Task<CommandContext> ExecuteAsync(string command,
        Dictionary<string, string?>? virtualStore = null,
        object? targetForm = null)
    {
        var context = new CommandContext
        {
            CurrentCommand = command,
            CurrentCommandLineNumber = 1,
            VirtualStore = virtualStore,
            Mode = targetForm != null ? "GUI" : "CLI"
        };

        if (targetForm != null)
            context.SharedData["TargetForm"] = targetForm;

        await _pipeline.ExecuteAsync(context);

        // 记录历史（支持撤销的事件命令）
        if (context.ParsedCommand?.IsNonEventCommand == false && !context.HasError)
        {
            lock (_lock)
            {
                _history.AddLast(context);
                _redoStack.Clear(); // 新命令执行后清空重做栈
            }
        }

        return context;
    }

    /// <summary>批量执行命令</summary>
    public async Task<List<CommandContext>> ExecuteCommandsAsync(
        IEnumerable<string> commands,
        Dictionary<string, string?>? virtualStore = null)
    {
        var results = new List<CommandContext>();
        int lineNumber = 1;
        foreach (var command in commands)
        {
            var result = await ExecuteAsync(command, virtualStore);
            result.CurrentCommandLineNumber = lineNumber++;
            results.Add(result);
        }
        return results;
    }

    /// <summary>从文件执行命令</summary>
    public async Task<(int SuccessCount, int FailCount)> ExecuteFileAsync(string filePath)
    {
        var lines = await File.ReadAllLinesAsync(filePath);
        var commands = lines
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#") && !l.StartsWith("//"))
            .ToList();

        int success = 0, fail = 0;
        foreach (var cmd in commands)
        {
            var result = await ExecuteAsync(cmd);
            if (!result.HasError) success++; else fail++;
        }
        return (success, fail);
    }

    // ================================================================
    // 撤销/重做
    // ================================================================

    public bool CanUndo => _history.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    public async Task<bool> UndoAsync()
    {
        CommandContext? context;
        lock (_lock)
        {
            if (_history.Count == 0) return false;
            context = _history.Last!.Value;
            _history.RemoveLast();
        }
        // 撤销逻辑：反向执行（具体由业务层实现）
        _redoStack.AddLast(context);
        return true;
    }

    public async Task<bool> RedoAsync()
    {
        CommandContext? context;
        lock (_lock)
        {
            if (_redoStack.Count == 0) return false;
            context = _redoStack.Last!.Value;
            _redoStack.RemoveLast();
        }
        await ExecuteAsync(context.CurrentCommand!);
        return true;
    }

    public void ClearHistory()
    {
        lock (_lock)
        {
            _history.Clear();
            _redoStack.Clear();
        }
    }

    // ================================================================
    // 管线构建
    // ================================================================

    private BP.IStagePipeline<CommandContext> CreatePipeline(
        Action<BP.IStagePipeline<CommandContext>>? configure,
        Func<string, string, (string?, List<string>?, string?)>? findBindingFunc)
    {
        var pipeline = new BP.StagePipeline<CommandContext>(
            "CommandPipeline", "Unified command execution pipeline");

        pipeline
            .Register(new ParseCommandStage(Parser, NonEventHandler))
            .Register(new FindBindingStage(findBindingFunc))
            .Register(new ExtractValuesStage())
            .Register(new ExecuteFunctionStage(Executor))
            .Register(new WriteResultStage());

        configure?.Invoke(pipeline);
        return pipeline;
    }

    public void Dispose()
    {
        ClearHistory();
    }
}
```

### 3.3 调用方式

```csharp
// === 基本用法 ===
var pipeline = new CommandPipeline();

// 非事件命令
var result = await pipeline.ExecuteAsync("echo Hello World");
Console.WriteLine(result.FunctionResultValue); // "Hello World"

// 事件命令（CLI 模式）
var store = new Dictionary<string, string?>();
await pipeline.ExecuteAsync("do set userName John", store);

// 事件命令（GUI 模式）
await pipeline.ExecuteAsync("do click addButton", targetForm: myForm);

// 文件批量执行
var (success, fail) = await pipeline.ExecuteFileAsync("commands.txt");

// 撤销/重做
await pipeline.UndoAsync();
await pipeline.RedoAsync();


// === 高级用法：注册业务函数 ===
pipeline.Functions.Register("Calculate", (int a, int b) => a + b);
pipeline.Functions.Register("SaveData", async (string data) => {
    await File.WriteAllTextAsync("data.txt", data);
});

// 注册非事件命令处理器
pipeline.NonEventHandler.RegisterHandler("customCmd", async (parsed) => {
    return NonEventCommandResult.Success($"Executed: {parsed.Target}");
});


// === 高级用法：自定义管线 ===
var pipeline = new CommandPipeline(pipeline => {
    pipeline
        .Register(new ValidationStage<CommandContext>())
        .Register(new LoggingStage<CommandContext>());
});
```

---

## 四、文件结构重构

### 4.1 重构后的目录结构

```
Share/CommandFramework/
├── CommandFramework.csproj
│
├── Abstractions/                    ← 接口定义（每个接口独立文件）
│   ├── ICommand.cs                  ← 命令接口
│   ├── ICommandPipeline.cs          ← 管线接口族
│   ├── ICommandParser.cs            ← 命令解析器接口
│   ├── INonEventCommandHandler.cs   ← 非事件命令处理器接口
│   ├── IEnvironmentAdapter.cs       ← 环境适配器接口
│   ├── IStateStore.cs               ← 状态存储接口
│   ├── IValueExtractor.cs           ← 值提取器接口
│   ├── IFunctionRegistry.cs         ← 函数注册表接口族
│   ├── IFunctionExecutor.cs         ← 函数执行器接口
│   ├── IControlValueAccessor.cs     ← 控件值读写器接口
│   └── ICommandExecutionResult.cs   ← 执行结果接口
│
├── Models/                          ← 数据模型
│   ├── CommandContext.cs            ← 统一命令上下文（合并原两个上下文）
│   ├── ParsedCommand.cs             ← 解析后的命令
│   ├── NonEventCommandResult.cs     ← 非事件命令执行结果
│   └── JsonCommandResult.cs         ← JSON 命令执行结果
│
├── Parsing/                         ← 命令解析
│   ├── CommandParser.cs             ← 命令解析器实现
│   └── JsonCommandExtensions.cs     ← JSON 命令解析扩展
│
├── Execution/                       ← 命令执行
│   ├── CommandPipeline.cs           ← ★ 统一入口（新增，合并原 Engine + PipelineEntry）
│   ├── NonEventCommandHandler.cs    ← 非事件命令处理器
│   ├── CommandExecutionResult.cs    ← 命令执行结果实现
│   ├── EnvironmentAdapterBase.cs    ← 环境适配器基类
│   ├── InMemoryStateStore.cs        ← 内存状态存储
│   ├── StateStoreExtensions.cs      ← 状态存储扩展
│   ├── ControlValueAccessorBase.cs  ← 控件值读写器基类
│   ├── FunctionRegistry.cs          ← 函数注册表（从 FunctionExecution.cs 拆分）
│   ├── FunctionExecutor.cs          ← 函数执行器（从 FunctionExecution.cs 拆分）
│   ├── ArgumentMatchers.cs          ← 参数匹配器策略链（从 FunctionExecution.cs 拆分）
│   └── TypeConverter.cs             ← 类型转换器（从 FunctionExecution.cs 拆分）
│
├── Pipeline/                        ← 管线阶段
│   ├── PipelineStageBase.cs         ← 阶段基类 + CommandStageBase
│   ├── Stages/                      ← 业务阶段
│   │   ├── ParseCommandStage.cs
│   │   ├── FindBindingStage.cs
│   │   ├── ExtractValuesStage.cs
│   │   ├── ExecuteFunctionStage.cs
│   │   └── WriteResultStage.cs
│   └── CrossCutting/                ← 横切关注点
│       ├── ValidationStage.cs
│       ├── LoggingStage.cs
│       └── RetryStage.cs
│
├── Registration/                    ← 命令注册
│   └── CommandRegistry.cs           ← 命令注册表
│
├── CommandFrameworkOptions.cs       ← 框架配置选项
├── CommandFrameworkHost.cs          ← 框架宿主
├── CommandFrameworkUsage.cs         ← 使用示例
└── doc/
    └── CommandFramework_设计文档.md  ← 设计文档
```

### 4.2 文件变更清单

| 操作 | 文件 | 说明 |
|------|------|------|
| **新增** | `Execution/CommandPipeline.cs` | 统一入口，合并 Engine + PipelineEntry |
| **删除** | `Execution/CommandEngine.cs` | 功能合并到 CommandPipeline |
| **删除** | `Pipeline/PipelineEntry.cs` | 功能合并到 CommandPipeline |
| **删除** | `Models/CommandExecutionContext.cs` | 字段合并到 CommandContext |
| **拆分** | `ICommand.cs` → 10 个独立接口文件 | 按职责拆分 |
| **拆分** | `Execution/FunctionExecution.cs` → 4 个文件 | 按职责拆分 |
| **移动** | `ValidationStage.cs` → `CrossCutting/` | 横切关注点分离 |
| **移动** | `LoggingStage.cs` → `CrossCutting/` | 横切关注点分离 |
| **移动** | `RetryStage.cs` → `CrossCutting/` | 横切关注点分离 |
| **保留** | 其他文件 | 仅调整命名空间引用 |

---

## 五、阶段职责说明

### 5.1 业务阶段（5 个，按顺序执行）

| 阶段 | 输入 | 输出 | 职责 |
|------|------|------|------|
| **ParseCommandStage** | `CurrentCommand` | `ParsedCommand`, `FunctionResultValue` | 解析命令字符串，非事件命令直接处理 |
| **FindBindingStage** | `ParsedCommand`, `FindBindingFunc` | `BoundFunctionName`, `BoundInputs`, `BoundOutputTarget` | 查找控件的事件绑定 |
| **ExtractValuesStage** | `BoundInputs`, `VirtualStore/TargetForm` | `ExtractedValues` | 从控件读取输入值 |
| **ExecuteFunctionStage** | `BoundFunctionName`, `ExtractedValues` | `FunctionResultValue` | 执行业务函数 |
| **WriteResultStage** | `FunctionResultValue`, `BoundOutputTarget` | 控件更新/VirtualStore 更新 | 将结果写回目标控件 |

### 5.2 横切阶段（3 个，可选）

| 阶段 | 职责 | 可插入位置 |
|------|------|-----------|
| **ValidationStage** | 验证命令合法性 | 任意位置 |
| **LoggingStage** | 记录执行日志 | 任意位置 |
| **RetryStage** | 失败自动重试 | 包装任意阶段 |

---

## 六、执行流程

### 6.1 事件命令执行流程（GUI 模式）

```
pipeline.ExecuteAsync("do click addButton", targetForm: form)
    │
    ▼
ParseCommandStage
    ├─ Parser.Normalize("do click addButton") → "do click addButton"
    ├─ Parser.Parse → ParsedCommand { Verb="do", Action="click", Target="addButton" }
    ├─ IsNonEventCommand = false → 标记为事件命令
    └─ 继续管线执行
    │
    ▼
FindBindingStage
    ├─ InferEventType("click", "addButton") → "click"
    ├─ findBindingFunc("addButton", "click") → { FunctionName="AddButton_Click", Inputs=["addButton"], Output="resultLabel" }
    └─ 填充 BoundFunctionName/BoundInputs/BoundOutputTarget
    │
    ▼
ExtractValuesStage
    ├─ GUI 模式：从 TargetForm 查找 addButton 控件，读取 Text 值
    └─ 填充 ExtractedValues = { "addButton": "当前文本" }
    │
    ▼
ExecuteFunctionStage
    ├─ Executor.ExecuteAsync("AddButton_Click", ExtractedValues)
    └─ 填充 FunctionResultValue = 函数返回值
    │
    ▼
WriteResultStage
    ├─ GUI 模式：将 FunctionResultValue 写回 resultLabel 控件
    └─ 完成
    │
    ▼
CommandPipeline 自动记录历史 → _history.AddLast(context)
```

### 6.2 事件命令执行流程（CLI 模式）

```
pipeline.ExecuteAsync("do set userName John", virtualStore: store)
    │
    ▼
ParseCommandStage
    ├─ Parser.Parse → ParsedCommand { Verb="do", Action="set", Target="userName", Parameters={value="John"} }
    └─ IsNonEventCommand = false
    │
    ▼
FindBindingStage
    ├─ InferEventType("set", "userName") → "textchanged"
    ├─ findBindingFunc("userName", "textchanged") → { FunctionName="UserName_TextChanged", Inputs=["userName"], Output=null }
    └─ 填充绑定信息
    │
    ▼
ExtractValuesStage
    ├─ CLI 模式：从 VirtualStore 读取 userName 的值
    ├─ set 命令特定值覆盖：value = "John"
    └─ ExtractedValues = { "userName": "John" }
    │
    ▼
ExecuteFunctionStage
    ├─ Executor.ExecuteAsync("UserName_TextChanged", ExtractedValues)
    └─ FunctionResultValue = 函数返回值
    │
    ▼
WriteResultStage
    ├─ BoundOutputTarget 为空 → 不需要写回
    └─ 完成
```

### 6.3 非事件命令执行流程

```
pipeline.ExecuteAsync("echo Hello World")
    │
    ▼
ParseCommandStage
    ├─ Parser.Parse → ParsedCommand { Verb="echo", IsNonEventCommand=true }
    ├─ NonEventHandler.HandleAsync(parsed) → { IsSuccess=true, ResultValue="Hello World" }
    ├─ FunctionResultValue = "Hello World"
    └─ 标记成功，跳过后续阶段
```

---

## 七、迁移步骤

| 阶段 | 任务 | 涉及文件 | 预期耗时 |
|------|------|---------|---------|
| **Phase 1** | 拆分 ICommand.cs | 1 个文件 → 10 个文件 | 2h |
| **Phase 2** | 拆分 FunctionExecution.cs | 1 个文件 → 4 个文件 | 1h |
| **Phase 3** | 合并 CommandContext | 2 个文件 → 1 个文件 | 1h |
| **Phase 4** | 创建 CommandPipeline | 新增 1 个文件 | 3h |
| **Phase 5** | 删除 CommandEngine + PipelineEntry | 删除 2 个文件 | 0.5h |
| **Phase 6** | 分离横切关注点 | 移动 3 个文件 | 0.5h |
| **Phase 7** | 清理 EventTriggerFramework 残留 | 检查重复代码 | 1h |
| **Phase 8** | 适配所有引用方 | 全局搜索引用 | 2h |
| **Phase 9** | 编译测试 | dotnet build + test | 1h |

**总计预计：12h**

---

## 八、风险与注意事项

### 8.1 兼容性风险

- `CommandEngine` 被 `EventTriggerFramework` 引用，删除后需要适配
- `PipelineEntry` 被 `FileDriven` 引用，删除后需要适配
- `CommandExecutionContext` 被其他项目引用，合并后需要适配

### 8.2 建议的迁移策略

**不要一次性删除旧类**，采用"先新增、后废弃、再删除"的策略：

```
Step 1: 新增 CommandPipeline（旧类保留）
Step 2: 将引用方逐步改为使用 CommandPipeline
Step 3: 将旧类标记为 [Obsolete]
Step 4: 确认无引用后删除旧类
```

### 8.3 保留的扩展点

| 扩展点 | 说明 |
|--------|------|
| `Functions.Register()` | 注册业务函数 |
| `NonEventHandler.RegisterHandler()` | 注册自定义非事件命令 |
| `Pipeline 自定义阶段` | 通过构造函数注入自定义阶段 |
| `IEnvironmentAdapter` | 环境适配器扩展 |
| `IControlValueAccessor` | 控件值读写器扩展 |
