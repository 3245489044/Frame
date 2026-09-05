# 基于 ECS 运算矩阵的 CLI/GUI 执行路径重构方案

> **编写日期**：2026-07-28  
> **前置依赖**：`docs/Refactoring/ExecutionCenter_ECS运算矩阵重构方案.md`（ExecutionCenter 独立项目 + 矩阵）  
> **替代**：`docs/Refactoring/FileDriven_CLI_GUI_统一执行端口重构方案.md`（旧版，需更新）  
> **状态**：方案设计

---

## 一、全景架构

```
                           ┌──────────────────────────┐
                           │   ExecutionCenter.Matrix  │ ← 独立项目
                           │   (ECS 运算矩阵)          │
                           │                          │
                           │   SchedulerSystem         │
                           │   RouterSystem            │
                           │   RetrySystem             │
                           │   MonitorSystem           │
                           └──────────┬───────────────┘
                                      │ ITaskExecutorPool 接口
                                      │ (SubmitAsync)
             ┌────────────────────────┼────────────────────────┐
             │                        │                        │
             ▼                        ▼                        ▼
   ┌──────────────────┐   ┌──────────────────┐   ┌──────────────────┐
   │ ScriptEngine      │   │ GUI 管线          │   │ 未来其他消费者   │
   │ (CLI 直连)        │   │ (GUI 直连)        │   │ (Sandbox 等)     │
   │                   │   │                   │   │                  │
   │ 不经过消息队列     │   │ 不经过消息队列     │   │ 经过消息队列     │
   │ ScriptLoad        │   │ InitializeGui     │   │ SubmitAsync      │
   │ CommandExecute    │   │ GuiMessageLoop    │   │ → Matrix         │
   └──────────────────┘   └──────────────────┘   └──────────────────┘
```

### 关键原则

1. **FileDriven 脚本执行走直连** — 同一进程内无分布式需求，消息队列增加不必要的异步复杂性
2. **ExecutionCenter 提供基础设施** — 重试/路由/监控等通用能力，但不阻塞主执行路径
3. **GUI 管线扁平化** — 2-Block 架构取代 3-Block
4. **PipelineEntry 单例化** — 已完成（`PipelineEntryPool`）

---

## 二、架构变化对照

### 2.1 之前

```
CLI:  ExecutionPort → ExecutionEntry → SubmitAsync(FileDrivenPipelineMessage) → Handler → FileDrivenPipelineRunner
                                              ↑
GUI:  StartGuiModePipeline(3Blocks) → ExecutionEntry → SubmitAsync(同上) → Handler → FileDrivenPipelineRunner
                                              ↑
                                          ExecutionCenter (旧 TaskExecutorPool)
                                          FileDrivenPipelineMessage (唯一消息)
```

### 2.2 之后

```
CLI:  ExecutionPort → ScriptEngine.Execute() → FileDrivenPipelineRunner（直连）
                                              ↓
GUI:  GuiMessageLoopBlock(2Blocks) → ScriptEngine.Execute() → FileDrivenPipelineRunner（直连）

                          ExecutionCenter.Matrix（独立存在）
                          ├── ExecuteCommandMessage → ExecuteCommandHandler
                          ├── LoadConfigMessage → LoadConfigHandler
                          └── 未来: 外部脚本/插件/沙箱的异步任务
```

---

## 三、详细设计

### 3.1 ScriptEngine 直连接口

```csharp
// FileDriven.Execution/Engine/ScriptEngine.cs（更新版）

public class ScriptEngine
{
    private readonly IScriptExecutionContext _context;

    public ScriptEngine(IScriptExecutionContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>
    /// 直接执行脚本，不经过 ExecutionCenter 消息队列
    /// </summary>
    public CommandPipelineContext Execute(CliOptions options, string commandFile)
    {
        var runner = new FileDrivenPipelineRunner(_context.PipelineEntry, /* executionCell */);
        return runner.ExecutePipeline(options, commandFile, _context.TargetForm);
    }

    /// <summary>
    /// 异步执行（封装 Task.Run）
    /// </summary>
    public Task<CommandPipelineContext> ExecuteAsync(CliOptions options, string commandFile)
    {
        return Task.Run(() => Execute(options, commandFile));
    }

    public IScriptExecutionContext Context => _context;
    public PipelineEntry PipelineEntry => _context.PipelineEntry;
}
```

### 3.2 CLI 执行路径更新

```csharp
// FileDriven.Execution/Bridge/ExecutionPort.cs（更新版）

private ExecutionResult ExecuteCliSync(ExecutionPlan plan, ExecutionResult execResult, Stopwatch stopwatch)
{
    // ★ 不再创建 ExecutionPipelineEntry + SubmitAsync
    //   改为 ScriptEngine 直连执行

    var context = new CliExecutionContext();  // VirtualStore 模式
    var engine = new ScriptEngine(context);

    var pipelineContext = engine.Execute(plan.Options, plan.CommandFile ?? "");

    stopwatch.Stop();

    execResult.CommandResults = BuildResultsFromContext(pipelineContext);
    execResult.Success = !pipelineContext.HasError;
    execResult.Elapsed = stopwatch.Elapsed;

    return execResult;
}
```

**移除的中间层**：
- `ExecutionPipelineEntry.RunPipelineAsync()` 的 `_pool.SubmitAsync()` 调用
- `FrameworkInitializer.RegisterExecutionCenterHandler()` 中的 FileDriven handler 注册
- `FileDrivenPipelineMessage` 类（不再需要）

### 3.3 GUI 执行路径更新

```
之前 (3-Block):
  Block1: CreateExecutionEntryBlock   [创建 PipelineEntry → 独立创建]
  Block2: CreateAndConfigureBlock     [创建 MainForm → guiInit]
  Block3: ApplicationRunBlock         [Application.Run + Shown 事件 → 脚本执行]

之后 (2-Block):
  Block1: InitializeGuiBlock          [PipelineEntryPool.Instance + MainForm]
  Block2: GuiMessageLoopBlock         [Application.Run + Shown 事件 → ScriptEngine.Execute()]
```

```csharp
// InitializeGuiBlock.cs

public class InitializeGuiBlock : BlockBase
{
    public InitializeGuiBlock()
        : base("InitializeGui", "初始化 GUI：PipelineEntry + MainForm")
    {
    }

    public override Task<BlockResult> ExecuteAsync(BlockContext context, CancellationToken ct)
    {
        var guiContext = (StartGuiModeContext)context;

        // 从单例池获取 PipelineEntry
        guiContext.PipelineEntry = PipelineEntryPool.Instance;

        // 创建 MainForm
        var initializer = new FileDrivenGUIFrameworkInitializer();
        initializer.InitializeFramework(guiContext);
        guiContext.MainForm = initializer.MainForm;

        return Task.FromResult(BlockResult.Ok("GUI 初始化完成"));
    }
}
```

```csharp
// GuiMessageLoopBlock.cs

public class GuiMessageLoopBlock : BlockBase
{
    public GuiMessageLoopBlock()
        : base("GuiMessageLoop", "GUI 消息循环 + ScriptEngine 执行")
    {
    }

    public override Task<BlockResult> ExecuteAsync(BlockContext context, CancellationToken ct)
    {
        var guiContext = (StartGuiModeContext)context;

        var tcs = new TaskCompletionSource<BlockResult>();

        // 后台线程启动 Application.Run
        var thread = new Thread(() =>
        {
            guiContext.MainForm.Shown += async (s, e) =>
            {
                try
                {
                    // 在 Shown 事件中执行脚本
                    var execContext = new GuiExecutionContext(guiContext.MainForm);
                    var engine = new ScriptEngine(execContext);

                    var pipelineContext = engine.Execute(
                        guiContext.Options,
                        guiContext.Options.GetEffectiveCommandFile());

                    guiContext.ScriptExecutionResult = ConvertToResult(pipelineContext);
                    tcs.TrySetResult(BlockResult.Ok("脚本执行完成"));
                }
                catch (Exception ex)
                {
                    tcs.TrySetResult(BlockResult.Fail($"脚本执行失败: {ex.Message}"));
                }

                if (guiContext.Options.AutoClose)
                    guiContext.MainForm.Close();
            };

            Application.Run(guiContext.MainForm);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return tcs.Task;
    }
}
```

### 3.4 ExecutionPort 简化

移除 `ITaskExecutorPool` 和 `IExecutionCell` 依赖：

```csharp
// ExecutionPort.cs（简化版）

public class ExecutionPort
{
    private readonly CliOptions _options;
    // ★ 不再需要 _executionPool 和 _executionCell

    public ExecutionPort(CliOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public ExecutionPlan Plan() => new(_options);

    public ExecutionResult Execute(ExecutionPlan plan)
    {
        // CLI 路径直连，GUI 路径走管线
        if (plan.UseGui)
            return ExecuteGuiSync(plan);
        else
            return ExecuteCliSync(plan);
    }
}
```

---

## 四、文件变更清单

### 新建

| 文件 | 说明 |
|------|------|
| `FileDriven.Execution/Gui/Blocks/InitializeGuiBlock.cs` | 合并 Block1+2 取代 `CreateExecutionEntryBlock` + `CreateAndConfigureBlock` |
| `FileDriven.Execution/Gui/Blocks/GuiMessageLoopBlock.cs` | 取代 `ApplicationRunBlock` |

### 修改

| 文件 | 变更 |
|------|------|
| `FileDriven.Execution/Engine/ScriptEngine.cs` | 添加 `Execute()` 直连方法 |
| `FileDriven.Execution/Bridge/ExecutionPort.cs` | CLI 路径直连，移除 `ITaskExecutorPool`/`IExecutionCell` 依赖 |
| `FileDriven.Execution/Gui/Blocks/StartGuiModePipeline.cs` | 改为 2-Block（InitializeGuiBlock + GuiMessageLoopBlock） |
| `FileDriven.Execution/Initial/FrameworkInitializer.cs` | 移除 `RegisterExecutionCenterHandler()` 中的 FileDriven handler |
| `FileDriven.Execution/CommandExecutor.cs` | `ExecuteMode` 闭包中不再传递 ExecutionPool |
| `FileDriven.Execution/Lifecycle/FrameworkShutdown.cs` | 移除 `executionPool.Dispose()` |
| `FileDriven.GlobalUsings/Usings.Other.cs` | 移除 `global using ExecutionCenter.*` |

### 删除

| 文件 | 说明 |
|------|------|
| `FileDriven.Execution/Gui/Blocks/CreateExecutionEntryBlock.cs` | 被 `InitializeGuiBlock` 取代 |
| `FileDriven.Execution/Gui/Blocks/ApplicationRunBlock.cs` | 被 `GuiMessageLoopBlock` 取代 |
| `FileDriven.Execution/Initial/ExecutionPipelineEntry.cs` | `RunPipelineAsync()` 的 SubmitAsync 路径不再使用，直连取代 |
| `FileDriven.Execution/Gui/Sandbox/CommandSandboxPort.cs` | （如不使用消息队列） |

### ExecutionCenter 项目的新建（独立工程）

详见 `docs/Refactoring/ExecutionCenter_ECS运算矩阵重构方案.md`

---

## 五、实施步骤

### 阶段 1：ExecutionCenter 独立（估算 2-3 天）

1. 创建 `CsharpPrj/ExecutionCenter/ExecutionCenter.csproj` 独立项目
2. 迁移现有代码到 `Legacy/` 目录
3. 创建 `Matrix/Components/` 7 个组件
4. 创建 `Matrix/Systems/` 4 个 System 骨架
5. 创建 `Matrix/MatrixEngine.cs` 实现 `ITaskExecutorPool`
6. `dotnet build` 通过，旧路径不受影响

### 阶段 2：FileDriven 直连（估算 1-2 天）

1. `ScriptEngine.Execute()` 直连 `FileDrivenPipelineRunner`
2. `ExecutionPort.ExecuteCliSync()` 移除 SubmitAsync
3. 删除 `ExecutionPipelineEntry.cs`
4. 更新 `FrameworkInitializer.cs` 移除 FileDriven handler 注册
5. 更新 `FrameworkShutdown.cs`
6. 清理 global using
7. `dotnet build` 通过，CLI 测试验证 `totalCommands > 0`

### 阶段 3：GUI 管线扁平化（估算 1-2 天）

1. 创建 `InitializeGuiBlock.cs`（合并 Block1+2）
2. 创建 `GuiMessageLoopBlock.cs`（替代 ApplicationRunBlock）
3. 更新 `StartGuiModePipeline.cs` 为 2-Block
4. 删除旧 Block 文件
5. GUI 模式测试验证

### 阶段 4：激活矩阵（估算 1 天）

1. 实现 `ExecuteCommandHandler.cs` 并注册到矩阵
2. 实现 `LoadConfigHandler.cs` 并注册到矩阵
3. 验证消息队列功能正常

---

## 六、与旧版方案的主要差异

| 维度 | 旧版方案 | 本方案 |
|------|---------|--------|
| ExecutionCenter 角色 | 保留旧 `TaskExecutorPool` | 重构为 ECS 运算矩阵 + 独立项目 |
| FileDriven 与 EC 关系 | FileDriven 经过 EC 消息队列 | FileDriven 直连，EC 独立存在 |
| CLI/GUI 执行 | `ExecutionPipelineEntry` + `SubmitAsync` | `ScriptEngine.Execute()` 直连 |
| 消息类型 | 3 种（1 在用 + 2 闲置） | 后续激活闲置消息到矩阵 |
| 重构范围 | 仅 FileDriven.Execution | ExecutionCenter + FileDriven.Execution |
| 依赖方向 | FileDriven → ExecutionFramework | FileDriven → ExecutionCenter（可选） |

---

> **版本**：v1.0  
> **前置依赖**：`docs/Refactoring/ExecutionCenter_ECS运算矩阵重构方案.md`  
> **旧版方案**：`docs/Refactoring/FileDriven_CLI_GUI_统一执行端口重构方案.md`（需按本文更新）