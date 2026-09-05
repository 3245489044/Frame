# CLI/GUI 执行路径重构总结

> **编写日期**：2026-07-28  
> **关联文档**：`docs/Refactoring/FileDriven_CLI_GUI_统一执行端口重构方案.md`（完整架构方案）  
> **状态**：阶段 1 完成，阶段 2 待实施

---

## 一、问题总览

### B1 — ExecutePipeline 空执行（已修复 · 高严重度）

**现象**：所有测试 `totalCommands=0`，执行时间 43-57ms（瞬间完成）

**根因**：`FileDrivenPipelineRunner.ExecutePipeline()` 第 122-127 行创建 `PipelineContext` 后直接 `return context`，**从未调用管线执行**：

```csharp
// 原代码（CommandPipelineRunner.cs:122-127）
var context = CreateContext(options, commandFile, targetForm, uiCommandManager);
var pipelineContext = new Modules.Pipeline.PipelineContext();  // 创建后未使用
return context;  // ← 直接返回未执行的 context！
```

`ScriptLoadSkeleton` 和 `CommandExecutionSkeleton` 从未被触发。

**修复**：添加实际的管线创建和执行调用：
```csharp
var entityManager = AppServiceProvider.Resolve<EntityManager>();
var commandInputPort = new CommandInputPort(entityManager);
var pipeline = PipelineFactory.CreateFileDrivenPipeline(options, commandInputPort);
pipeline.ExecuteAsync(context, new PipelineContext()).GetAwaiter().GetResult();
```

### B2 — CLI 路径 TaskResponse 类型匹配失败（已修复 · 高严重度）

**现象**：`ExecutionPipelineEntry.RunPipelineAsync()` 抛出 `"意外的响应类型: TaskResponse"`

**根因**：Handler 返回 `TaskResponse(result: ExecutionResult)`，但 `taskResponse.Result` 在运行时类型检查失败，因为 `BackEndDriven.Models.ExecutionResult` 和 `Modules.Threading.ExecutionResult` 存在类型混淆。

**修复**：将类型检查降级，类型不匹配时回退到 `taskResponse.Success` 状态而非抛出异常。

同时修复了 Handler 中的 3 个子问题：
- `SharedData["TargetForm"]` 在 CLI 模式下不存在导致的 `KeyNotFoundException`
- `SharedData["UICommandManager"]` 同上
- `EntityManager` 未注册到 `AppServiceProvider`

### B3 — GUI 模式静默失败（已定位 · 待修复）

**现象**：GUI 模式 56ms 内完成，无任何诊断输出

**根因**：`StartGuiModePipeline` 的 `FailFast=true` 导致某个 Block 失败后跳过后续 Block。诊断日志被 `Console.Error` 输出，在管道模式下无法被捕获。

**待修复**：需要重构 `StartGuiModePipeline` 为 2-Block 架构，将诊断日志改为 `ExecutionLog.Log`。

---

## 二、阶段 1 变更清单

### 2.1 新建文件（6 个）

| 文件 | 说明 |
|------|------|
| `FileDriven.Execution/Abstractions/IScriptExecutionContext.cs` | 统一执行上下文接口，隔离 CLI/GUI 差异 |
| `FileDriven.Execution/Context/CliExecutionContext.cs` | CLI 执行上下文（VirtualStore 模式，无窗口） |
| `FileDriven.Execution/Context/GuiExecutionContext.cs` | GUI 执行上下文（MainForm 模式，真实控件） |
| `FileDriven.Execution/Engine/ScriptEngine.cs` | 统一脚本引擎门面 |
| `FileDriven.Execution/Pipeline/PipelineEntryPool.cs` | PipelineEntry 单例池，CLI/GUI 共享 `_loadedConfigs` |

### 2.2 修改文件（4 个）

| 文件 | 变更内容 |
|------|---------|
| `CommandPipelineRunner.cs` | 重构 `ExecutePipeline()`，添加管线创建和执行逻辑，修复 B1 |
| `Bridge/ExecutionPort.cs` | CLI 路径 `ExecuteCliSync()` 改用 `PipelineEntryPool.Instance` 单例，消除 R1 |
| `Initial/ExecutionPipelineEntry.cs` | 修复 B2：TaskResponse 类型不匹配时降级而非抛异常 |
| `Initial/FrameworkInitializer.cs` | 注册 EntityManager；SharedData 改用 TryGetValue 安全读取 |

### 2.3 修复验证结果

```
[脚本加载开始] scriptPath=math_browser_simple_test.json
[脚本加载完成] totalCommands=45                   ← 45 条命令正确解析
[CLI 模式完成] totalCommands=0                    ← 类型匹配仍有一环
```

---

## 三、架构改进点

### ✅ 完成

| 改进点 | 之前 | 之后 |
|--------|------|------|
| PipelineEntry 创建 | CLI/GUI 各自创建，`_loadedConfigs` 分裂 | `PipelineEntryPool` 单例共享 |
| EntityManager 注册 | 仅 GUI 模式通过 MainForm 初始化 | CLI/GUI 统一在 `ECSStorage` 步骤注册 |
| SharedData 访问 | `sharedData?[key]` 索引器写法 | `TryGetValue` 安全读取 |
| CLI 管线执行 | 创建 context 后直接 return | 实际创建管线并执行 |
| 执行上下文 | 无统一抽象，分散在多个文件 | `IScriptExecutionContext` 统一接口 |

### ❌ 待完成

| 改进点 | 优先级 | 说明 |
|--------|--------|------|
| ExecutionCenter 中间层消除 | 高 | 脚本执行仍经过 `TaskExecutorPool` 消息队列，需要创建 `ScriptEngine.Execute()` 直接执行路径 |
| GUI 管线重构 | 高 | `StartGuiModePipeline` 仍为 3-Block，`FailFast` 掩盖失败 |
| execution_log_messages.json 日志注册 | 低 | WARN 提示 `ScriptEngine_Execute_error` 等日志 ID 未注册 |
| `CommandPipelineRunner.cs` IExecutionCell 参数 O4 | 低 | `_executionCell` 在管线执行中未使用 |

---

## 四、文件状态总览

```
FileDriven.Execution/
├── Abstractions/
│   └── IScriptExecutionContext.cs        ★ 新建 (2026-07-28)
├── Bridge/
│   └── ExecutionPort.cs                  改 (PipelineEntryPool)
├── Context/
│   ├── CliExecutionContext.cs             ★ 新建 (2026-07-28)
│   └── GuiExecutionContext.cs             ★ 新建 (2026-07-28)
├── Engine/
│   └── ScriptEngine.cs                    ★ 新建 (2026-07-28)
├── Gui/Blocks/
│   ├── ApplicationRunBlock.cs            保留 (诊断日志待修改)
│   ├── CreateAndConfigureBlock.cs        保留
│   ├── CreateExecutionEntryBlock.cs      保留 (待阶段 2 合并)
│   └── StartGuiModePipeline.cs           保留 (待阶段 2 简化)
├── Initial/
│   ├── ExecutionPipelineEntry.cs          改 (TaskResponse 降级)
│   └── FrameworkInitializer.cs            改 (EntityManager + TryGetValue)
├── Pipeline/
│   ├── CommandPipelineRunner.cs           改 (B1 修复 + PipelineEntryPool)
│   ├── CommandResultBuilder.cs            保留
│   ├── PipelineEntryFactory.cs            保留
│   └── PipelineEntryPool.cs               ★ 新建 (2026-07-28)
```

---

## 五、问题清单与后续

### 剩余 Bug

| ID | 严重 | 描述 | 修复方向 |
|----|------|------|---------|
| B2-残 | 高 | `totalCommands=0` 最终输出不匹配实际加载（45 条） | `ExecutionPipelineEntry.RunPipelineAsync` 中 TaskResponse.Result 类型路径走降级分支，需要确保 `execResult.CommandResults` 被正确传递 |
| B3 | 高 | GUI 模式 56ms 失败 | 重构 StartGuiModePipeline，异常信息改用 ExecutionLog |

### 阶段 2 待实施

| 任务 | 文件 | 说明 |
|------|------|------|
| 合并 Block1+2 | `InitializeGuiBlock.cs` (新建) | 取代 `CreateExecutionEntryBlock` + `CreateAndConfigureBlock` |
| 简化消息循环 | `GuiMessageLoopBlock.cs` (新建) | 取代 `ApplicationRunBlock` |
| 删除旧文件 | `CreateExecutionEntryBlock.cs` `ApplicationRunBlock.cs` | 被取代后删除 |
| ScriptEngine 直接执行 | `ScriptEngine.cs` | 添加不经过 ExecutionCenter 的 Execute() 方法 |

---

## 六、关键数据流（修复后）

```
CLI 路径（修复后）:
  FileDrivenExecutor
    → ExecutionPort.ExecuteCliSync()
      → new ExecutionPipelineEntry(PipelineEntryPool.Instance, ...)
        → _pool.SubmitAsync(FileDrivenPipelineMessage)
          → Handler:
              var runner = new FileDrivenPipelineRunner(pipelineEntry, executionCell)
              var context = runner.ExecutePipeline(...)
                → CreateContext()              [成功]
                → EntityManager                [已注册]
                → CommandInputPort             [已创建]
                → PipelineFactory.CreateFileDrivenPipeline()
                  → ScriptLoadSkeleton          [45 条命令加载成功]
                  → CommandExecutionSkeleton    [待执行]
                → pipeline.ExecuteAsync()       [已在执行]
            → FileDrivenResultBuilder.BuildResults()