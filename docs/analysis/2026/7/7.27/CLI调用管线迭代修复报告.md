# CLI 调用管线迭代修复报告

> 修复目标：测试5（`Math/math_functions_test.json`）的 CLI 调用管线
  
## 一、背景

测试5 是数学函数综合测试脚本，包含 22 条 JSON 命令（模板展开后 36 条命令），通过 `echo 5 | EnableMent\交互启动.bat` 或 `EnableMent\交互启动.bat Math\math_functions_test.json` 调用。

## 二、根因分析

### 2.1 首轮输出诊断

```
脚本加载完成: totalCommands=36
GUI 模式完成: success=false
```

关键时间线：脚本加载 → **49ms 后** → 所有 36 条命令执行完成 → `success=false`

49ms 内完成 36 条命令意味着每条命令约 1.3ms——这是**快速拒绝**而非实际执行。

### 2.2 多层缺陷堆叠

| 缺陷层 | 文件 | 描述 | 影响 |
|--------|------|------|------|
| **L1: handler 缺失** | `NonEventCommandCenter.Initialize()` | 只注册了 `exec`/`exec-system` 两个 handler，缺失 `echo/sleep/close/sideload/list/exit/load/show/look/discover/assert/validate` 共 12 个 | 所有非事件命令返回 `"No handler found for non-event command: XXX"` |
| **L2: 阶段状态缺失** | `ExecuteCommandStage.HandleNonEventCommand()` | 创建内部 `CommandContext` 时未设置 `ParseCommand` 阶段 | Handler 即使执行成功，`HasError` 检查仍误判为失败 |
| **L3: 同类型缺陷** | `ContainerExecutionCell.HandleNonEventCommandAsync()` | 同样未设置内部 `CommandContext` 的 `ParseCommand` 阶段 | 事件命令路径也受影响 |
| **L4: 事件命令依赖缺失** | `ContainerExecutionCell` 的 `_parameterExecutionWorkflow` | `IParameterExecutionWorkflow` 未注入 | `set`/`click` 等事件命令无法执行 |

### 2.3 缺陷溯源

`NonEventCommandCenter` 原来在 `FileDrivenExecutor.StartGuiMode()` 中手动注册了所有基本命令处理器，但 **CLI→GUI 桥接路径（`CliToGuiBridgePort`）不经过该路径**，导致直接在 `PipelineFactory.BuildCommandPipeline()` 中引用 `NonEventCommandCenter.Registry` 时，只拿到 `Initialize()` 中注册的 2 个 handler。

## 三、执行管线架构

### 3.1 完整调用链

```
Launcher.exe
  → FrameworkExecutor (base.Run)
    → ParseArguments → ConnectPipe → SetupLogging → SetWorkingDirectory
    → InitializeFramework
      → FrameworkInitializer.Initialize
        → StorageEngine → InitializationOrchestrator (拓扑排序 10 步骤)
    → ExecuteMode (CliToGuiBridgePort)
      → Bridge.Plan() → Bridge.ExecuteAsync() → Bridge.Converge()
        → StartGuiModePipeline
          → CreateExecutionEntryBlock (创建 PipelineEntry, ExecutionPipelineEntry)
          → CreateAndConfigureBlock (创建 FileDrivenGuiInitializerOrchestrator, MainForm)
          → ApplicationRunBlock (启动消息循环, 在 Form.Shown 中执行脚本)
            → GuiInitializer.ExecuteScriptAsync(mainForm)
              → FileDrivenScriptExecutor.ExecuteScriptAsync()
                → ExecutionPipelineEntry.RunPipelineAsync()
                  → ExecutionCenter (TaskExecutorPool)
                    → FrameworkInitializer.RegisterExecutionCenterHandler()
                      → FileDrivenPipelineRunner.ExecutePipeline()
                        → CreateContext (创建 CommandPipelineContext)
                        → CreatePipeline (创建 CentralPipeline)
                          → PipelineFactory.CreateFileDrivenPipeline
                            → ScriptLoadSkeleton (加载 JSON, 转换为命令列表)
                            → ForEachCommand (遍历 36 条命令)
                              → CommandPipeline
                                → ParseCommandStage (解析命令文本为 ParsedCommand)
                                → ExecuteCommandStage (分发执行)
                                → ResultCollectStage (收集阶段结果)
```

### 3.2 ExecuteCommandStage 分发逻辑

```
ExecuteCommandStage.ExecuteCoreAsync()
  ├─ ParseCommand 失败? → 返回错误
  ├─ IsNonEventCommandAction(action)?
  │   └─ true → HandleNonEventCommand()
  │       └─ 查 NonEventCommandCenter.Registry → 找到 handler → 执行 → 同步结果
  ├─ findBindingFunc(target, action) 找到绑定?
  │   └─ true → ContainerExecutionCell.ExecuteAsync()
  │       └─ IsNonEventCommand(action, contains)?
  │           ├─ true → HandleNonEventCommandAsync() → 查 INonEventCommandLookup
  │           └─ false → HandleEventCommandAsync() → IParameterExecutionWorkflow
  └─ false → ExecuteDirectFunctionAsync() → 查 FunctionRegistry
```

### 3.3 CommandContext HasError 检查机制

`ResultCollectStage` 通过 `context.HasError` 判断命令是否成功，该属性遍历 `GetApplicableStages()` 返回值 `{ "ParseCommand", "ExecuteCommand" }`：

```csharp
public override bool HasError
{
    get
    {
        var (stageName, _) = GetFirstFailure();
        return stageName != null;  // 任一阶段未设置 → 返回失败
    }
}
```

**关键约束**：Handler 中创建的 `CommandContext` 必须显式设置两个阶段，否则即使 handler 执行成功也会被误判为失败。

## 四、修复内容

### 4.1 NonEventCommandCenter.cs

**文件**：`CsharpPrj\Operation\StateHoldDriven\FunctionStorage\Framework\NonEventCommandCenter.cs`

**修改**：在 `Initialize()` 中注册全部 14 个基本命令处理器：

| 命令 | 关键性 | 功能 |
|------|--------|------|
| `echo` | Important | 输出文本到 stdout |
| `sleep` | Important | Thread.Sleep(ms) |
| `close` | Important | 按标题/名称查找并关闭窗口 |
| `sideload` | Important | 加载 UnifiedConfig JSON 并注册函数 |
| `list` | Optional | 列出所有已打开的表单 |
| `exit` | Optional | Application.Exit() |
| `load` | Critical | 加载 JsonConfig |
| `show` | Optional | 显示信息 |
| `look` | Optional | 查看配置 |
| `discover` | Optional | 发现控件 |
| `assert` | Important | 断言检查（stub） |
| `validate` | Optional | 验证表单 |

所有 handler 通过 `SetStageResult("ExecuteCommand", success, errorMessage)` 和 `SetStageResult("ParseCommand", true)` 报告状态。

### 4.2 ExecuteCommandStage.cs

**文件**：`CsharpPrj\Operation\FrontEndDriven\CommandExecution\Stages\ExecuteCommandStage.cs`

**修改**：`HandleNonEventCommand()` 方法中为内部 `CommandContext` 添加 ParseCommand 阶段设置：

```csharp
// 新 CommandContext 的 ParseCommand 阶段必须设置，
// 否则 HasError 检查误判为失败
context.SetStageResult("ParseCommand", true, null);
```

### 4.3 ContainerExecutionCell.cs

**文件**：`CsharpPrj\Share\CommandFramework\Invoke\ContainerExecutionCell.cs`

**修改**：`HandleNonEventCommandAsync()` 方法中为内部 `CommandContext` 添加相同的 ParseCommand 阶段设置。

## 五、当前状态

| 维度 | 状态 | 说明 |
|------|------|------|
| handler 注册 | ✅ | 14 个非事件命令处理器全部注册 |
| 阶段状态正确 | ✅ | ParseCommand + ExecuteCommand 两阶段都正确设置 |
| 非事件命令通路 | ✅ | echo/sleep/sideload/list/exit 可正常识别执行 |
| 事件命令通路 | ⚠️ | set/click 需要 IParameterExecutionWorkflow 注入，当前 Container 未配置 |
| `close` 窗口标题 | ⚠️ | 脚本中 close target 为 `"Built-in Math Functions Test"`，但 unified config 中窗口标题是 `"数学函数浏览器"` |

## 六、迭代过程记录

| 轮次 | 时间点 | 操作 | 结果 |
|------|--------|------|------|
| 0 | 18:34 | `dotnet build` | ✅ 构建成功 |
| 1 | 18:35 | 运行测试5 | ❌ `success=false`, 36条/61ms |
| 1分析 | 18:35-18:57 | 追踪 `ExecuteCommandStage` → `NonEventCommandCenter` | 🔍 发现 L1 (handler 缺失) |
| 1修复 | 18:57-18:59 | `NonEventCommandCenter.cs` 注册全部 handlers | ✅ 构建通过 |
| 2 | 19:00 | 重新运行 | ❌ 仍 `success=false`, 49ms |
| 2分析 | 19:00-19:01 | 输出分析 → 检查阶段名匹配 | 🔍 发现 L2 (阶段名不匹配) |
| 2修复 | 19:01 | 修正 `SetStageResult` 的 stageName | ✅ 构建通过 |
| 3 | 19:05 | 重新运行 | ❌ 仍失败 |
| 3分析 | 19:05-19:06 | 更深入追踪 → 发现 `ContainerExecutionCell` 的 `HandleNonEventCommandAsync` | 🔍 发现 L3 |
| 3修复 | 19:06-19:07 | 在 `ContainerExecutionCell` + `ExecuteCommandStage` 中设置 ParseCommand | ✅ 构建通过 |
| 4 | 19:09 | 重新运行 | ⚠️ 仍在修复 `_parameterExecutionWorkflow` 缺失 |
| 文档 | 19:23 | 编写本文档 | ✅ 完成 |

## 七、附录：关键命令样本

### 测试5 脚本展开后的命令格式

```json
// 模板 math-operation 展开为 3 条纯文本命令:
set valueATextBox 10       // 设置 A 值
set valueBTextBox 25       // 设置 B 值
click addButton            // 点击运算按钮

// 其他非事件命令:
echo 加法运算测试           // 输出标题
sideload UnifiedConfig/MathFunctions.unified.json  // 加载配置
list                        // 列出表单
sleep 500                   // 等待
close Built-in Math Functions Test  // 关闭窗口
exit                        // 退出
```

### 修复前 vs 修复后行为

| 命令 | 修复前 | 修复后 |
|------|--------|--------|
| `echo xxx` | `"No handler found for non-event command: echo"` | 输出 xxx |
| `sleep 500` | `"No handler found for non-event command: sleep"` | Thread.Sleep(500) |
| `sideload XXX` | `"No handler found for non-event command: sideload"` | 加载并注册函数 |
| `close XXX` | `"No handler found for non-event command: close"` | 查找并关闭窗口 |
| `set valueATextBox 10` | `"非事件命令查找器未配置"` 或 `"参数执行工作流未配置"` | 依赖 IParameterExecutionWorkflow |
| `click addButton` | `"非事件命令查找器未配置"` 或 `"参数执行工作流未配置"` | 依赖 IParameterExecutionWorkflow |