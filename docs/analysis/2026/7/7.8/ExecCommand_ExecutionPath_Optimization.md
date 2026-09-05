# `exec` 命令执行路径集中优化整理

> 基于 `AgentConfigUI_ExecCommand_Analysis.md` 的后续执行路径优化方案
> 日期：2026-07-07

## 一、问题回顾

`AgentConfigUI.json` 的 `initCommands` 中使用 `exec AgentConfigWorkflow _stepSelector=Init`，期望执行已注册的工作流函数。但 `exec` 命令的执行路径存在以下问题：

1. **handler 未注册** — `NonEventCommandCenter` 中未注册 `exec` 的 handler
2. **判断路径不一致** — `ExecuteCommandStage` 通过硬编码字典判断非事件命令，而非查运行时注册中心
3. **委托链冗余** — `IsNonEventCommandAction` 有三层纯转发，无额外逻辑
4. **适配器不完整** — `NonEventCommandLookupAdapter.TryGetHandler` 始终返回 `false`

---

## 二、当前执行路径全景

### 2.1 完整调用链

```
AgentConfigUI.json "exec AgentConfigWorkflow _stepSelector=Init"
  │
  ├─[1] CommandParser 解析
  │   → {Verb="do", Action="exec", Target="AgentConfigWorkflow", Parameters={"_stepSelector"="Init"}}
  │
  ├─[2] ExecuteCommandStage.ExecuteCoreAsync()
  │   │
  │   ├─[2a] 判断非事件命令（★ 问题点）
  │   │   → FileDrivenPipelineContext.IsNonEventCommandAction("exec")
  │   │     → PipelineContextUtility.IsNonEventCommandAction("exec")
  │   │       → NonEventCommands.IsNonEventCommand("exec")
  │   │         → _commandCriticalityMap.ContainsKey("exec")  ← 硬编码字典
  │   │
  │   ├─[2b] HandleNonEventCommand()
  │   │   → _nonEventCommandLookup.TryGetHandler("exec", out handler)
  │   │     → NonEventCommandCenter.Registry.TryGetHandler("exec", ...)
  │   │       → NonEventCommandRegistry._handlers["exec"]  ← 运行时注册中心
  │   │
  │   └─[2c] handler(ctx)
  │       → FunctionStorageRegistry.ExecuteAsync("AgentConfigWorkflow", parameters)
  │
  ├─[3] ExecuteCommandHandler（Container.Core 路径，★ 另一条路径）
  │   │
  │   ├─[3a] 判断非事件命令
  │   │   → runtimeCtx.GetService<INonEventCommandLookup>()
  │   │     → ctx.NonEventCommandLookup  ← NonEventCommandLookupAdapter
  │   │       → nonEventLookup.Contains("exec")
  │   │         → NonEventCommandCenter.Registry.Contains("exec")  ← 运行时注册中心 ✅
  │   │
  │   └─[3b] HandleNonEventCommandAsync()
  │       → nonEventLookup.TryGetHandler("exec", out handler)
  │         → NonEventCommandLookupAdapter.TryGetHandler()  ← 始终返回 false ❌
  │           → 返回 "未找到非事件命令 handler: exec"
```

### 2.2 两条判断路径对比

| 路径 | 位置 | 判断依据 | 数据源 |
|------|------|----------|--------|
| **路径A**（ExecuteCommandStage） | `ExecuteCommandStage.ExecuteCoreAsync()` 第102行 | `IsNonEventCommandAction()` | `NonEventCommands._commandCriticalityMap` **硬编码字典** |
| **路径B**（ExecuteCommandHandler） | `ExecuteCommandHandler.ExecuteCoreAsync()` 第116行 | `INonEventCommandLookup.Contains()` | `NonEventCommandCenter.Registry` **运行时注册中心** |

**问题**：路径A和路径B的判断依据不同。路径A依赖静态字典，路径B依赖运行时注册中心。如果某命令在注册中心注册了但字典没加，路径A会误判为事件命令。

### 2.3 当前代码状态（已修复部分）

| 文件 | 状态 | 说明 |
|------|------|------|
| `NonEventCommandCenter.cs` | ✅ 已修复 | `exec` handler 已改为执行工作流函数 |
| `PipelineFactory.cs` | ✅ 已修复 | 已传入 `NonEventCommandCenter.Registry` |
| `ExecuteCommandStage.cs` | ✅ 已修复 | 已接收并使用 `_nonEventCommandLookup` |

---

## 三、优化方案

### 3.1 核心优化：统一非事件命令判断路径

**目标**：将 `ExecuteCommandStage` 的判断依据从"硬编码字典"改为"运行时注册中心"，与 `ExecuteCommandHandler` 保持一致。

**改动点**：

| 文件 | 行号 | 当前代码 | 改为 |
|------|------|----------|------|
| `ExecuteCommandStage.cs` | 第102行 | `FileDrivenPipelineContext.IsNonEventCommandAction(action)` | `_nonEventCommandLookup?.Contains(action) == true` |

**修改后代码**：

```csharp
// ExecuteCommandStage.cs 第101-107行
// 非事件命令处理
// ★ 2026-07-07 优化：从查硬编码字典改为查运行时注册中心，
//   与 ExecuteCommandHandler（Container.Core）的判断逻辑保持一致。
if (_nonEventCommandLookup?.Contains(action) == true)
{
    HandleNonEventCommand(ctx);
    ctx.SetStageResult(Name, ctx.FunctionExecuteSuccess, ctx.FunctionErrorMessage);
    return;
}
```

**影响分析**：
- `_nonEventCommandLookup` 由 `PipelineFactory` 传入，值为 `NonEventCommandCenter.Registry`
- `NonEventCommandCenter.Registry` 是 `INonEventCommandRegistry`，继承自 `INonEventCommandLookup`
- `INonEventCommandLookup.Contains()` 由 `NonEventCommandRegistry` 实现，查 `_handlers` 字典
- 如果 `_nonEventCommandLookup` 为 null（可选参数），`?.Contains()` 返回 false，走事件命令路径

### 3.2 次要优化：扁平化委托链

**目标**：删除 `IsNonEventCommandAction` 的三层委托转发，直接调用 `NonEventCommands.IsNonEventCommand`。

**改动点**：

| 文件 | 改动 |
|------|------|
| `FileDrivenPipelineContext.cs` | 删除第370-371行的 `IsNonEventCommandAction` 静态方法 |
| `PipelineContextUtility.cs` | 删除第26-29行的 `IsNonEventCommandAction` 方法（仅保留 `ResolveScriptPath`） |

**说明**：由于核心优化后 `ExecuteCommandStage` 不再调用 `IsNonEventCommandAction`，这两个方法已无调用方。如果其他位置仍有引用，需先确认引用方并改为直接调用 `NonEventCommands.IsNonEventCommand`。

### 3.3 可选优化：补全 NonEventCommandLookupAdapter.TryGetHandler

**目标**：使 `NonEventCommandLookupAdapter.TryGetHandler` 能正确适配 handler，为 Container.Core 路径的完整执行做准备。

**改动点**：

| 文件 | 行号 | 当前代码 | 改为 |
|------|------|----------|------|
| `NonEventCommandLookupAdapter.cs` | 第59-67行 | 始终返回 false | 适配 handler 参数类型 |

**修改后代码**：

```csharp
public bool TryGetHandler(string commandName, out Action<Container.Core.CommandExecutionContext>? handler)
{
    if (_inner.TryGetHandler(commandName, out var innerHandler) && innerHandler != null)
    {
        // 将 IExecutionContext handler 包装为 CommandExecutionContext handler
        handler = (containerCtx) =>
        {
            // 创建 IExecutionContext 适配器，将 CommandExecutionContext 映射为 IExecutionContext
            var execCtx = new CommandContextAdapter(containerCtx);
            innerHandler(execCtx);
            // 将执行结果写回 CommandExecutionContext
            containerCtx.Success = execCtx.FunctionExecuteSuccess;
            containerCtx.ResultValue = execCtx.FunctionResultValue?.ToString();
            containerCtx.ErrorMessage = execCtx.FunctionErrorMessage;
        };
        return true;
    }
    handler = null;
    return false;
}
```

**说明**：此优化为可选，因为当前 `ExecuteCommandHandler` 仅使用 `Contains` 方法判断命令类型，`TryGetHandler` 在 Container 路径中暂未被调用。但补全后可为未来扩展做好准备。

---

## 四、优化前后对比

### 4.1 执行路径对比

**优化前**：
```
ExecuteCommandStage
  → IsNonEventCommandAction("exec")      ← 查硬编码字典
  → TryGetHandler("exec", out handler)   ← 查运行时注册中心
  → handler(ctx)
```

**优化后**：
```
ExecuteCommandStage
  → _nonEventCommandLookup.Contains("exec")  ← 统一查运行时注册中心
  → TryGetHandler("exec", out handler)
  → handler(ctx)
```

### 4.2 判断依据统一

| 路径 | 优化前 | 优化后 |
|------|--------|--------|
| ExecuteCommandStage | 硬编码字典 `NonEventCommands._commandCriticalityMap` | 运行时注册中心 `NonEventCommandCenter.Registry.Contains()` |
| ExecuteCommandHandler | 运行时注册中心 `NonEventCommandLookupAdapter.Contains()` | 运行时注册中心 `NonEventCommandCenter.Registry.Contains()` |

### 4.3 文件改动清单

| 文件 | 改动类型 | 风险 |
|------|----------|:----:|
| `ExecuteCommandStage.cs` 第102行 | 修改判断条件 | 低 |
| `FileDrivenPipelineContext.cs` 第370-371行 | 删除静态方法 | 低（需确认无其他引用） |
| `PipelineContextUtility.cs` 第26-29行 | 删除方法 | 低（需确认无其他引用） |
| `NonEventCommandLookupAdapter.cs` 第59-67行 | 补全 TryGetHandler | 低（可选） |

---

## 五、验证方法

### 5.1 编译验证

```bash
dotnet build CsharpPrj/MainProj.sln
dotnet build FileDriven/FileDriven.sln
```

### 5.2 逻辑验证

1. `exec` 命令在 `NonEventCommandCenter.Initialize()` 中注册
2. `ExecuteCommandStage` 通过 `_nonEventCommandLookup.Contains("exec")` 判断为 true
3. `HandleNonEventCommand` 通过 `TryGetHandler("exec", out handler)` 获取 handler
4. handler 调用 `FunctionStorageRegistry.ExecuteAsync("AgentConfigWorkflow", parameters)`
5. 执行结果写回 `ctx.FunctionExecuteSuccess` / `ctx.FunctionResultValue` / `ctx.FunctionErrorMessage`

### 5.3 回归验证

- 其他非事件命令（`echo`、`sleep`、`load` 等）在 `NonEventCommandCenter` 中注册后，应能正常执行
- 事件命令（`click`、`set` 等）不受影响，仍走 `_findBindingFunc` 路径
- `_nonEventCommandLookup` 为 null 时（可选参数未传入），所有命令走事件命令路径

---

## 六、附录：相关文件关系图

```
CommandFramework.Abstractions
  ├── INonEventCommandLookup          ← 轻量级查找接口（Contains / TryGetHandler）
  └── INonEventCommandRegistry        ← 注册表接口（继承 INonEventCommandLookup）
        ↑ 实现
NonEventCommandRegistry               ← FunctionStorage.Core 中的实现类
  ↑ 单例访问
NonEventCommandCenter                 ← 静态入口，Initialize() 中注册 exec handler
  ↑ 传入
PipelineFactory.BuildCommandPipeline  ← 创建 ExecuteCommandStage 时传入
  ↑ 使用
ExecuteCommandStage                   ← 命令执行阶段，判断并分发非事件命令
  ├── [优化前] IsNonEventCommandAction() → NonEventCommands 硬编码字典
  └── [优化后] _nonEventCommandLookup.Contains() → 运行时注册中心
```
