# FileDriven CLI/GUI 统一执行端口重构方案

> **目标**：消除 CLI 与 GUI 两条独立执行路径的重复逻辑与错逻辑，引入 FrontEndDriven 三端口设计模式，实现 CLI 作为 GUI 上层封装的新架构。

---

## 目录

- [FileDriven CLI/GUI 统一执行端口重构方案](#filedriven-cligui-统一执行端口重构方案)
  - [目录](#目录)
  - [1. 现状问题分析](#1-现状问题分析)
    - [1.1 当前调用路径全景](#11-当前调用路径全景)
      - [CLI 路径](#cli-路径)
      - [GUI 路径](#gui-路径)
      - [两条路径的关系](#两条路径的关系)
    - [1.2 错误逻辑](#12-错误逻辑)
    - [1.3 重复逻辑](#13-重复逻辑)
    - [1.4 冗余逻辑](#14-冗余逻辑)
    - [1.5 根因总结](#15-根因总结)
  - [2. 架构设计目标与原则](#2-架构设计目标与原则)
    - [目标](#目标)
    - [原则](#原则)
  - [3. FrontEndDriven 模式分析](#3-frontenddriven-模式分析)
  - [4. 新架构设计](#4-新架构设计)
    - [4.1 架构全景](#41-架构全景)
    - [4.2 统一执行端口 ScriptExecutionPort](#42-统一执行端口-scriptexecutionport)
    - [4.3 脚本引擎 ScriptEngine](#43-脚本引擎-scriptengine)
    - [4.4 CLI = GUI + VirtualStore](#44-cli--gui--virtualstore)
    - [4.5 PipelineEntry 单例化](#45-pipelineentry-单例化)
    - [4.6 GUI 管线扁平化](#46-gui-管线扁平化)
  - [5. 详细类型设计](#5-详细类型设计)
    - [5.1 ScriptExecutionPort](#51-scriptexecutionport)
    - [5.2 ScriptEngine](#52-scriptengine)
    - [5.3 IScriptExecutionContext](#53-iscriptexecutioncontext)
    - [5.4 PipelineEntryPool](#54-pipelineentrypool)
  - [6. 文件变更清单](#6-文件变更清单)
    - [阶段 1 — ScriptEngine + 直连接口（修复 CLI 路径 B2）](#阶段-1--scriptengine--直连接口修复-cli-路径-b2)
    - [阶段 2 — GUI 管线重构（修复 GUI 路径 B3）](#阶段-2--gui-管线重构修复-gui-路径-b3)
    - [阶段 3 — 清理（删除旧文件）](#阶段-3--清理删除旧文件)
  - [7. 迁移计划](#7-迁移计划)
    - [阶段 1：修复 CLI 路径（估算 1-2 天）](#阶段-1修复-cli-路径估算-1-2-天)
    - [阶段 2：修复 GUI 路径（估算 1-2 天）](#阶段-2修复-gui-路径估算-1-2-天)
    - [阶段 3：清理（估算 0.5 天）](#阶段-3清理估算-05-天)
  - [8. 风险与缓解](#8-风险与缓解)

---

## 1. 现状问题分析

### 1.1 当前调用路径全景

#### CLI 路径

```
FileDrivenExecutor.Run(args)
  → base.Run(args) [FrameworkExecutor Template Method]
    → ExecuteMode = ExecutionPort.Plan → Execute → Converge
      → ExecutionPort.ExecuteCliSync()
        → PipelineEntryFactory.CreateWithExecutionEntry()    [创建 PipelineEntry A]
        → executionEntry.RunPipeline()
          → _pool.SubmitAsync(FileDrivenPipelineMessage)    [提交到 ExecutionCenter]
            → Handler (FrameworkInitializer中注册):
                pipelineEntry = SharedData["PipelineEntry"]  [取出 PipelineEntry A]
                runner = new FileDrivenPipelineRunner(pipelineEntry, executionCell)
                context = runner.ExecutePipeline(options, commandFile, ...)
                  → CreateContext()
                  → **原 BUG：直接 return，未执行管线**
                  → **修复后：** 
                    CommandInputPort(entityManager)
                    PipelineFactory.CreateFileDrivenPipeline()
                    pipeline.ExecuteAsync(context, ...)
                → FileDrivenResultBuilder.BuildResults(context)
                → TaskResponse(result: ExecutionResult)
            ← ExecutionPipelineEntry: taskResponse.Result 类型匹配失败 [B2]
```

#### GUI 路径

```
FileDrivenExecutor.RunGui()
  → base.Run(guiArgs)
    → ExecuteMode = StartGuiMode()
      → StartGuiModePipeline (FailFast=true)
        → Block1: CreateExecutionEntryBlock   [创建 PipelineEntry B]
        → Block2: CreateAndConfigureBlock     [创建 guiInitializer + MainForm]
        → Block3: ApplicationRunBlock
            → 后台线程: Application.Run(mainForm)
            → Shown 事件:
                → initTask.WaitAsync()          [等待 UI 初始化]
                → guiInit.ExecuteScriptAsync()  [失败 → AutoClose → 56ms 结束] [B3]
                  → FileDrivenScriptExecutor
                    → ExecutionEntry.RunPipelineAsync()
                      → _pool.SubmitAsync(同CLI) 
```

#### 两条路径的关系

```
CLI:  ExecutionPort → ExecutionEntry → ExecutionCenter → Handler → FileDrivenPipelineRunner
GUI:  StartGuiModePipeline(3Blocks) → ExecutionEntry → ExecutionCenter → Handler → FileDrivenPipelineRunner
         ↑                              ↑
    PipelineEntry B               PipelineEntry A
    (独立创建)                     (独立创建)
```

**核心问题**：两条路径直到 ExecutionCenter handler 才汇合，但此前 PipelineEntry、ExecutionContext 各自独立创建，状态不共享。

### 1.2 错误逻辑

| 编号 | 严重 | 位置 | 问题 |
|------|------|------|------|
| **B1** | ★高 | `CommandPipelineRunner.cs:122-127` | `ExecutePipeline()` 创建 `PipelineContext` 后直接 `return context`，从未调用管线执行。**已修复**。 |
| **B2** | ★高 | `ExecutionPipelineEntry.cs:88-93` | CLI 路径类型匹配失败：`taskResponse.Result` 运行时类型非 `BackEndDriven.Models.ExecutionResult`，抛出 `"意外的响应类型: TaskResponse"`。 |
| **B3** | ★高 | `StartGuiModePipeline` → `ApplicationRunBlock` | GUI 模式 56ms 内完成，UI 初始化静默失败。`FailFast=true` 可能导致首个 Block 失败后跳过后续，且异常被最外层 catch 吞掉。 |
| **B4** | 中 | `FrameworkInitializer.cs:464-470` | Handler 中 `new CliOptions` 只设置 4 个属性，`Mode`/`CommandFile` 缺失，`ShouldRunGuiMode()` 可能判断错误。 |

### 1.3 重复逻辑

| 编号 | 严重 | 位置 | 问题 |
|------|------|------|------|
| **R1** | ★高 | 多文件 | CLI、GUI 各自独立创建 `PipelineEntry`，`_loadedConfigs`（ConcurrentDictionary）状态分裂。sideload 的配置只在创建者的字典中可见。 |
| **R2** | 中 | `CommandExecutor.cs:63-72` vs `:396-403` | `ExecuteMode` 闭包与 `StartCliMode()` 方法的三端口调用完全相同，代码重复。 |
| **R3** | 低 | `ApplicationRunBlock.cs` | `ExecutionLog.Log` 与 `Console.Error.WriteLine` 双重输出同一错误信息。 |

### 1.4 冗余逻辑

| 编号 | 严重 | 位置 | 问题 |
|------|------|------|------|
| **O1** | 中 | `ExecutionPort.cs` | Plan 只封装几个布尔标志（UseGui/IsPipeMode/AutoClose），三端口模式在当前复杂度下过度设计。 |
| **O2** | 低 | `StartGuiModePipeline.cs` | 3个 Block 职责过细：Block1 和 Block2 可合并（Block2 依赖 Block1 的输出）。 |
| **O3** | 低 | `CommandResultBuilder.cs:47-49` | `FromCommandResults` 传 `totalCommands: 0` 但注释说"自动计算"。 |
| **O4** | 低 | `CommandPipelineRunner.cs:28` | `IExecutionCell` 构造函数参数在修复后的管线执行中未使用。 |

### 1.5 根因总结

```
┌─────────────────────────────────────────────────────────────────┐
│ 根因 1: ExecutionCenter 中间层引入不必要的类型转换             │
│ → FileDrivenPipelineMessage → TaskResponse → 类型匹配脆弱       │
│ → ExecutePipeline 空执行 bug 暴露此层的不可靠性                  │
├─────────────────────────────────────────────────────────────────┤
│ 根因 2: CLI/GUI 执行路径分裂                                     │
│ → PipelineEntry 独立创建, _loadedConfigs 不共享                  │
│ → CLI 路径不经过 MainForm 初始化, GUI 路径必须 MainForm          │
│ → 两条路径各自维护相同的管线工厂逻辑                              │
├─────────────────────────────────────────────────────────────────┤
│ 根因 3: StartGuiModePipeline 异常处理脆弱                        │
│ → FailFast=true 且 3 个 Block 耦合, 任一 Block 失败无声跳过      │
│ → 诊断日志在管道模式下被 Console.Error 吞噬                      │
├─────────────────────────────────────────────────────────────────┤
│ 根因 4: CLI ≠ GUI 上层封装, 而是独立的实现                       │
│ → CLI 有自己的三端口实现(ExecutionPort)                           │
│ → GUI 有自己的三 Block 实现(StartGuiModePipeline)                │
│ → 两者只在底层 ExecutionCenter handler 汇合                      │
└─────────────────────────────────────────────────────────────────┘
```

---

## 2. 架构设计目标与原则

### 目标

1. **CLI = GUI 上层封装** — CLI 模式复用 GUI 的脚本引擎，区别仅在于是否有窗口
2. **消除 ExecutionCenter 中间层** — 脚本执行不经消息队列，直接调用引擎
3. **PipelineEntry 单例化** — CLI/GUI 共享同一 `_loadedConfigs`
4. **三端口聚拢** — 借鉴 `FrontEndDriven` 的 `EcsInputPort` / `CsgEntryPoint` / `WinFormsOutputPort` 模式

### 原则

- **关注点分离**：输入端口（脚本加载）→ 执行引擎（命令管线）→ 输出端口（结果归并）
- **接口隔离**：`IScriptExecutionContext` 统一 CLI 和 GUI 的执行上下文差异
- **渐近迁移**：分 3 阶段重构，每个阶段保持构建通过、测试可运行

---

## 3. FrontEndDriven 模式分析

FrontEndDriven 的核心设计是三端口 + ECS World：

```
EcsInputPort [写入] → ECS World → CsgEntryPoint [读取] → WinFormsOutputPort [应用]
     ↓                     ↓                    ↓                    ↓
  配置加载             Entity树           CanonicalForm          WinForms控件
  事件回传             组件状态         WinFormSnapshot          属性应用
  数据变更             事件绑定                                   树构建
```

设计特征：

| 特征 | FrontEndDriven | FileDriven.Execution（当前） | 目标 |
|------|---------------|---------------------------|------|
| 输入端口 | `EcsInputPort`（单一入口） | 2 条入口（CLI/GUI） | `ScriptInputPort`（单一入口） |
| 数据模型 | `ECS World`（单一真实来源） | `_loadedConfigs` 分裂 | `PipelineEntryPool`（单例） |
| 读取端口 | `CsgEntryPoint`（唯一转换） | 无统一读取 | `ScriptExecutionPort`（统一） |
| 输出端口 | `WinFormsOutputPort`（渲染） | 分散在多个文件中 | `ScriptResultCollector`（归并） |
| 适配模式 | `port.Plan → Execute → Converge` | 已在 `ExecutionPort` 中使用 | 保留并统一 |

---

## 4. 新架构设计

### 4.1 架构全景

```
                    ┌─────────────────────────────────────────┐
                    │         ScriptExecutionPort             │ ← 统一执行端口
                    │   (取代 ExecutionPort)                   │
                    │                                         │
                    │  Plan(scriptPath, mode) → PlanResult    │
                    │  Execute(plan) → ExecutionResult         │
                    │  Converge(result) → SummaryResult        │
                    └─────────────┬───────────────────────────┘
                                  │
          ┌───────────────────────┼───────────────────────┐
          ▼                       ▼                       ▼
┌─────────────────────┐ ┌─────────────────────┐ ┌─────────────────────┐
│  ScriptInputPort    │ │  ScriptInputPort    │ │  ScriptInputPort    │
│  (CLI)              │ │  (GUI)              │ │  (Analyze)          │
│                     │ │                     │ │                     │
│  VirtualStore=true  │ │  TargetForm!=null   │ │  OutputCollector    │
└──────────┬──────────┘ └──────────┬──────────┘ └──────────┬──────────┘
           │                       │                       │
           └───────────────────────┼───────────────────────┘
                                   ▼
                    ┌─────────────────────────────────────────┐
                    │           ScriptEngine                  │ ← 统一脚本引擎
                    │                                         │
                    │  ① ScriptLoadSkeleton                    │
                    │     (JSON → CommandLines)               │
                    │  ② CommandExecutionSkeleton              │
                    │     (ForEach 执行命令)                   │
                    │                                         │
                    │  依赖: PipelineEntryPool (单例)          │
                    │        ExecutionCenter (可选,默认直连)   │
                    └─────────────────────────────────────────┘
                                   │
                    ┌──────────────┴──────────────┐
                    ▼                             ▼
           ┌──────────────────┐      ┌──────────────────────┐
           │  CLI 输出         │      │  GUI 输出             │
           │  VirtualStore    │      │  MainForm             │
           │  Console         │      │  WinForms窗口          │
           └──────────────────┘      └──────────────────────┘
```

### 4.2 统一执行端口 ScriptExecutionPort

借鉴 FrontEndDriven 的 `Port.cs` 模式，将 `ExecutionPort` 合并为统一入口：

```csharp
// 取代 ExecutionPort（Port A/B/C 三端口模式保留但简化）
public class ScriptExecutionPort
{
    public ScriptExecutionPlan Plan(CliOptions options);
    public ScriptExecutionResult Execute(ScriptExecutionPlan plan);
    public BackEndDriven.Models.ExecutionResult Converge(ScriptExecutionResult result);
}
```

**关键变化**：
- `Execute()` 内部根据 `plan.UseGui` 创建不同的 `IScriptExecutionContext`
- 但无论 CLI/GUI，都调用同一 `ScriptEngine.Execute(context)`
- 不再经过 `ExecutionCenter`（不创建 `FileDrivenPipelineMessage`）

### 4.3 脚本引擎 ScriptEngine

统一执行管线，取代 `FileDrivenPipelineRunner`：

```csharp
public class ScriptEngine
{
    // 不经过 ExecutionCenter，直接执行
    public CommandPipelineContext Execute(
        CliOptions options,
        string commandFile,
        IScriptExecutionContext executionContext);
}

public interface IScriptExecutionContext
{
    Form? TargetForm { get; }        // GUI: mainForm, CLI: null
    bool UseVirtualStore { get; }    // CLI: true, GUI: false
    Dictionary<string, string?>? VirtualStore { get; }
    
    // PipelineEntry 从单例池获取
    PipelineEntry PipelineEntry { get; }
}
```

**CLI 路径**：
```
ScriptExecutionPort → ScriptEngine.Execute(CliContext) 
  → ScriptLoadSkeleton(JSON → CommandLines)
  → CommandExecutionSkeleton(ForEach + VirtualStore)
  → ScriptResultCollector
```

**GUI 路径**：
```
ScriptExecutionPort → GuiMessageLoopBlock 
  → Application.Run(mainForm) [后台线程]
  → Shown 事件: ScriptEngine.Execute(GuiContext)
    → ScriptLoadSkeleton(JSON → CommandLines)
    → CommandExecutionSkeleton(ForEach + real controls on MainForm)
  → AutoClose after complete
```

### 4.4 CLI = GUI + VirtualStore

CLI 模式下，`ScriptEngine` 创建 `VirtualControlStore` 模拟控件行为：

```csharp
public class VirtualControlStore
{
    private readonly Dictionary<string, Dictionary<string, string?>> _controls;
    
    // set textBox1 "Hello"
    public void SetValue(string controlName, string property, string? value);
    
    // get textBox1 → "Hello"
    public string? GetValue(string controlName, string property);
    
    // exec Math_Execute "ReLU|-1.0" → result
    public string? ExecuteFunction(string funcName, string paramStr);
}
```

GUI 模式下，所有命令通过 `EventBindingResolver` 找到绑定的函数并在 MainForm 上执行。

### 4.5 PipelineEntry 单例化

引入 `PipelineEntryPool` 管理全局唯一 PipelineEntry 实例：

```csharp
public static class PipelineEntryPool
{
    private static readonly Lazy<PipelineEntry> _instance = new(() =>
        PipelineEntryFactory.CreateDefault());
    
    public static PipelineEntry Instance => _instance.Value;
}
```

CLI 和 GUI 路径都从 `PipelineEntryPool.Instance` 获取 PipelineEntry，确保 `_loadedConfigs` 共享。

### 4.6 GUI 管线扁平化

将 `StartGuiModePipeline` 从 3 个 Block 合并为 2 个：

```
Before:    Block1(CreateExecEntry) → Block2(CreateAndConfigure) → Block3(ApplicationRun)
After:     Block1(InitializeGui) → Block2(GuiMessageLoop)
                    ↓                          ↓
           PipelineEntry + MainForm       Application.Run + ScriptEngine.Execute
           (新建或从池获取)                  (Shown 事件中执行)
```

---

## 5. 详细类型设计

### 5.1 ScriptExecutionPort

```csharp
// FileDriven.Execution/Ports/ScriptExecutionPort.cs

/// <summary>统一执行端口（三端口模式：Plan / Execute / Converge）</summary>
public class ScriptExecutionPort
{
    private readonly ITaskExecutorPool _executionPool;
    private readonly IExecutionCell _executionCell;

    // ── 端口 A：执行计划 ──
    public ScriptExecutionPlan Plan(CliOptions options)
    {
        return new ScriptExecutionPlan(options);
    }

    // ── 端口 B：执行脚本 ──
    public ScriptExecutionResult Execute(ScriptExecutionPlan plan)
    {
        var stopwatch = Stopwatch.StartNew();
        
        // 根据 mode 创建执行上下文
        IScriptExecutionContext execContext;
        if (plan.UseGui)
        {
            execContext = CreateGuiContext(plan);
            // 进入 GUI 消息循环，在 Shown 事件中执行脚本
            return ExecuteGui(plan, execContext, stopwatch);
        }
        else
        {
            execContext = CreateCliContext(plan);
            // 直接执行脚本
            return ExecuteCli(plan, execContext, stopwatch);
        }
    }

    // ── 端口 C：结果归并 ──
    public ExecutionResult Converge(ScriptExecutionResult result)
    {
        return FileDrivenResultBuilder.FromCommandResults(
            result.CommandResults, result.Elapsed,
            result.Mode, result.BatchId);
    }
    
    // ── CLI 执行（核心） ──
    private ScriptExecutionResult ExecuteCli(ScriptExecutionPlan plan, 
        IScriptExecutionContext ctx, Stopwatch stopwatch)
    {
        var engine = new ScriptEngine(ctx, _executionCell);
        var pipelineCtx = engine.Execute(plan.Options, plan.CommandFile);
        // ... 构建结果
    }

    // ── GUI 执行（封装 Application.Run） ──
    private ScriptExecutionResult ExecuteGui(ScriptExecutionPlan plan,
        IScriptExecutionContext ctx, Stopwatch stopwatch)
    {
        // 创建 GuiMessageLoopBlock
        // 后台线程启动 Application.Run
        // 前台线程等待 Form 关闭
        // Shown 事件中调用 engine.Execute()
    }
}
```

### 5.2 ScriptEngine

```csharp
// FileDriven.Execution/Engine/ScriptEngine.cs

/// <summary>统一脚本执行引擎（CLI/GUI 共用）</summary>
public class ScriptEngine
{
    private readonly IScriptExecutionContext _context;
    private readonly IExecutionCell _executionCell;

    /// <summary>执行脚本（不经过 ExecutionCenter）</summary>
    public CommandPipelineContext Execute(
        CliOptions options,
        string commandFile)
    {
        // 1. 创建 CommandPipelineContext
        var context = CreateContext(options, commandFile);
        
        // 2. 创建管线（ScriptLoad + CommandExecution）
        var entityManager = AppServiceProvider.Resolve<EntityManager>();
        var commandInputPort = new CommandInputPort(entityManager);
        var pipeline = PipelineFactory.CreateFileDrivenPipeline(options, commandInputPort);
        
        // 3. 直接执行（不经过 ExecutionCenter）
        pipeline.ExecuteAsync(context, new PipelineContext()).GetAwaiter().GetResult();
        
        return context;
    }
}
```

### 5.3 IScriptExecutionContext

```csharp
// FileDriven.Execution/Abstractions/IScriptExecutionContext.cs

public interface IScriptExecutionContext
{
    /// <summary>GUI 模式的目标 Form（CLI 模式为 null）</summary>
    Form? TargetForm { get; }
    
    /// <summary>是否使用虚拟控件存储（CLI 模式）</summary>
    bool UseVirtualStore { get; }
    
    /// <summary>虚拟控件存储（CLI 模式使用）</summary>
    Dictionary<string, string?>? VirtualStore { get; }
    
    /// <summary>PipelineEntry（从单例池获取）</summary>
    PipelineEntry PipelineEntry { get; }
}

public class CliExecutionContext : IScriptExecutionContext
{
    public Form? TargetForm => null;
    public bool UseVirtualStore => true;
    public Dictionary<string, string?>? VirtualStore { get; } = new();
    public PipelineEntry PipelineEntry => PipelineEntryPool.Instance;
}

public class GuiExecutionContext : IScriptExecutionContext
{
    public Form? TargetForm { get; }
    public bool UseVirtualStore => false;
    public Dictionary<string, string?>? VirtualStore => null;
    public PipelineEntry PipelineEntry => PipelineEntryPool.Instance;
    
    public GuiExecutionContext(MainForm mainForm)
    {
        TargetForm = mainForm;
    }
}
```

### 5.4 PipelineEntryPool

```csharp
// FileDriven.Pipeline/PipelineEntryPool.cs

public static class PipelineEntryPool
{
    private static readonly Lazy<PipelineEntry> _instance = new(() =>
    {
        var entry = PipelineEntryFactory.CreateDefault();
        PipelineEntryFactory.EnsureCommandFrameworkUsage("GUI", entry);
        return entry;
    }, LazyThreadSafetyMode.ExecutionAndPublication);

    public static PipelineEntry Instance => _instance.Value;
    
    /// <summary>重置（仅测试/全量重载时调用）</summary>
    public static void Reset()
    {
        if (_instance.IsValueCreated)
            _instance.Value.Dispose();
        // Lazy 无法重置，需通过反射或重新赋值（测试专用）
    }
}
```

---

## 6. 文件变更清单

### 阶段 1 — ScriptEngine + 直连接口（修复 CLI 路径 B2）

| 文件 | 操作 | 说明 |
|------|------|------|
| `FileDriven.Execution/Engine/ScriptEngine.cs` | **新建** | 统一脚本引擎，不经过 ExecutionCenter |
| `FileDriven.Execution/Abstractions/IScriptExecutionContext.cs` | **新建** | 执行上下文接口 |
| `FileDriven.Execution/Context/CliExecutionContext.cs` | **新建** | CLI 上下文实现（VirtualStore） |
| `FileDriven.Execution/Context/GuiExecutionContext.cs` | **新建** | GUI 上下文实现（MainForm） |
| `FileDriven.Pipeline/PipelineEntryPool.cs` | **新建** | PipelineEntry 单例池 |
| `FileDriven.Execution/CommandPipelineRunner.cs` | **修改** | ExecutePipeline 改用 ScriptEngine |
| `FileDriven.Execution/Initial/FrameworkInitializer.cs` | **修改** | 注册 PipelineEntryPool |
| `FileDriven.Execution/Initial/ExecutionPipelineEntry.cs` | **修改** | 新增 `ExecuteDirectAsync()` 不经过 ExecutionCenter |

### 阶段 2 — GUI 管线重构（修复 GUI 路径 B3）

| 文件 | 操作 | 说明 |
|------|------|------|
| `FileDriven.Execution/Ports/ScriptExecutionPort.cs` | **新建** | 统一执行端口，取代 ExecutionPort |
| `FileDriven.Execution/Gui/Blocks/InitializeGuiBlock.cs` | **新建** | 合并 Block1+2 |
| `FileDriven.Execution/Gui/Blocks/GuiMessageLoopBlock.cs` | **新建** | 替代原 ApplicationRunBlock |
| `FileDriven.Execution/Gui/Blocks/StartGuiModePipeline.cs` | **修改** | 2 Block 版本 |
| `FileDriven.Execution/Gui/Blocks/ApplicationRunBlock.cs` | **删除** | 被 GuiMessageLoopBlock 取代 |
| `FileDriven.Execution/Gui/Blocks/CreateExecutionEntryBlock.cs` | **删除** | 合并到 InitializeGuiBlock |
| `FileDriven.Execution/CommandExecutor.cs` | **修改** | ExecuteMode 统一使用 ScriptExecutionPort |

### 阶段 3 — 清理（删除旧文件）

| 文件 | 操作 | 说明 |
|------|------|------|
| `FileDriven.Execution/Bridge/ExecutionPort.cs` | **删除** | 被 ScriptExecutionPort 取代 |
| `FileDriven.Execution/Pipeline/CommandResultBuilder.cs` | **清理** | 删除 O3 冗余参数 |
| `FileDriven.Execution/Gui/FileDrivenScriptExecutor.cs` | **删除** | GUI 脚本执行由 ScriptEngine 统一处理 |

---

## 7. 迁移计划

### 阶段 1：修复 CLI 路径（估算 1-2 天）

**目标**：CLI 模式能正确执行 JSON 脚本，`totalCommands > 0`

步骤：
1. 创建 `ScriptEngine` 类（核心执行逻辑）
2. 创建 `IScriptExecutionContext` 接口 + `CliExecutionContext`
3. 创建 `PipelineEntryPool` 单例
4. 修改 `ExecutionPipelineEntry`，新增 `ExecuteDirectAsync` 方法
5. 修改 `FileDrivenPipelineRunner.ExecutePipeline` 改为调用 `ScriptEngine`
6. `dotnet build` 通过，`echo 5 | 交互启动.bat` 验证

**验证标准**：CLI 模式测试5输出显示 `totalCommands=21, successCount=21`

### 阶段 2：修复 GUI 路径（估算 1-2 天）

**目标**：GUI 模式 MainForm 能正常初始化，脚本在 Shown 事件中执行

步骤：
1. 创建 `GuiExecutionContext`（封装 MainForm 引用）
2. 创建 `ScriptExecutionPort`（统一三端口）
3. 重构 `StartGuiModePipeline` 为 2 个 Block
4. `ScriptEngine` 支持 `TargetForm != null` 模式
5. GUI 模式下 MainForm.Shown 事件中执行 `ScriptEngine.Execute()`

**验证标准**：GUI 模式测试5 WinForms 窗口正常显示，命令执行完毕自动关闭

### 阶段 3：清理（估算 0.5 天）

**目标**：删除所有被取代的旧文件，消除冗余参数

步骤：
1. 删除 `ExecutionPort.cs`
2. 删除 `FileDrivenScriptExecutor.cs`
3. 清理 `CommandResultBuilder.cs` 冗余参数
4. 全面回归测试（测试1-12）

---

## 8. 风险与缓解

| 风险 | 影响 | 概率 | 缓解 |
|------|------|------|------|
| ExecutionCenter 消息队列被其他模块依赖 | 其他模块的文件驱动执行中断 | 中 | 保留 TaskExecutorPool，仅 FileDriven 路径不经过它。其他模块保持 `SubmitAsync`。 |
| PipelineEntryPool 单例状态的线程安全 | 并发侧面调度时 `_loadedConfigs` 竞争 | 低 | `ConcurrentDictionary` 已是线程安全；`Lazy<T>` 保障初始化线程安全。 |
| CLI 模式 VirtualStore 与事件绑定不兼容 | `set/get` 命令在虚拟存储中找不到控件 | 中 | VirtualStore 实现 `FindBinding` 接口，从 PipelineEntry._loadedConfigs 反向查找控件定义。 |
| GUI 管线扁平化导致初始化顺序变化 | MainForm 加载完成前 Shown 事件触发 | 低 | 保留 `InitializationTask`（TaskCompletionSource），Shown 事件中 `await initTask` 后再执行脚本。 |
| 旧文件删除后其他模块的编译错误 | 跨项目引用被破坏 | 中 | 阶段 3 前做全局 `dotnet build`，确认零引用后再删除。 |

---

> **编写日期**：2026-07-28  
> **版本**：v1.0  
> **相关文档**：`docs/Refactoring/FileDriven_FrontEndDriven_聚拢点重构方案.md` — 前置分析文档