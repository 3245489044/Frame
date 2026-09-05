# EventTrigger / WorkFlowMethod 调用链整合方案

> **目标**：解决参数传递路径过长、横跨多个项目、参数名信息丢失的问题
> **日期**：2026-07-04
> **状态**：方案设计

---

## 1. 当前调用链全景

### 1.1 两条路径

```
路径A：UI 事件触发（EventTrigger → IEventExecutionService）
EventTriggerFramework (UI层)
  → IEventExecutionService.ExecuteFunctionAsync(Dictionary)     ← 接口定义在 Share.EventBindingOrchestrator
    → WorkflowMethodExecutionService (WorkFlowMethod项目)        ← 实现 IEventExecutionService
      → 将 Dictionary 转成 object?[]（按位置索引排序）
        → WorkFlowMethodUsage.Executor.ExecuteAsync(object?[])   ← WorkFlowMethod项目
          → FunctionExecutor.ExecuteAsync                        ← Modules.ExecutionFramework
            → ArgumentBuilder.BuildFromDescriptor(object?[])     ← BackEndDriven项目
              → 快速路径2：按位置匹配

路径B：BackEndDriven 参数执行工作流
BackEndDrivenBuilder (BackEndDriven项目)
  → WorkflowMethodExecutionWorkflow.ExecuteAsync                ← BackEndDriven项目
    → BuildWorkflowContextAsync → WfContext.Parameters(Dictionary)
      → 从 Dictionary 提取值转成 object?[]（按位置索引排序）
        → WorkFlowMethodUsage.Executor.ExecuteAsync(object?[])   ← WorkFlowMethod项目
          → FunctionExecutor.ExecuteAsync                        ← Modules.ExecutionFramework
            → ArgumentBuilder.BuildFromDescriptor(object?[])     ← BackEndDriven项目
              → 快速路径2：按位置匹配
```

### 1.2 涉及的项目

| 项目 | 角色 | 文件 |
|------|------|------|
| `EventTriggerFramework` | UI 事件绑定层 | `EventBindingTransformers.cs` |
| `Share.EventBindingOrchestrator` | 接口定义层 | `IEventExecutionService.cs` |
| `WorkFlowMethod` | 工作流执行入口 | `WorkflowMethodExecutionService.cs`, `WorkflowMethodUseage.cs` |
| `Modules.ExecutionFramework` | 函数执行引擎 | `FunctionExecutor.cs` |
| `BackEndDriven` | 参数构建 + 工作流执行 | `ArgumentBuilder.cs`, `WorkflowMethodExecutionWorkflow.cs` |

### 1.3 核心问题

| # | 问题 | 影响 |
|---|------|------|
| 1 | **调用链太长**：UI 事件 → 最终执行横跨 5 个项目 | 调试困难，修改一处需要理解整个链路 |
| 2 | **参数格式反复转换**：Dictionary → object?[] → 按位置匹配 | 丢失参数名信息，位置索引排序可能出错 |
| 3 | **两条路径功能重叠**：`WorkflowMethodExecutionService` 和 `WorkflowMethodExecutionWorkflow` 做同样的事 | 代码重复，维护成本翻倍 |
| 4 | **Dictionary 同时写入控件名和位置索引**：`"valueATextBox"` 和 `"0"` 共存 | 排序后位置索引顺序可能与函数签名参数顺序不一致 |

---

## 2. 整合方案

### 2.1 核心思路

**让 `IEventExecutionService` 成为唯一执行入口**，`WorkflowMethodExecutionWorkflow` 不再直接调用 `Executor.ExecuteAsync`，而是委托给 `IEventExecutionService`。参数以 `Dictionary<string, object>` 形式直达 `ArgumentBuilder` 的快速路径3（按参数名匹配），不再转换为 `object?[]`。

### 2.2 整合后的调用链

```
唯一路径：
CreateEventHandler / WorkflowMethodExecutionWorkflow
  → IEventExecutionService.ExecuteFunctionAsync(functionName, Dictionary)
    → WorkFlowMethodUsage.Executor.ExecuteAsync(functionName, Dictionary)  ← 新增 Dictionary 重载
      → FunctionExecutor.ExecuteAsync(functionName, Dictionary)
        → ArgumentBuilder.BuildFromDescriptor(Dictionary)
          → 快速路径3：按参数名匹配 ✅ 保留参数名信息
```

### 2.3 架构变化

```
修改前：                         修改后：
                                  ┌──────────────────────┐
                                  │  IEventExecutionService │
                                  │  (唯一入口)            │
                                  └──────────┬───────────┘
                                             │
                    ┌────────────────────────┼────────────────────────┐
                    │                        │                        │
                    ▼                        ▼                        ▼
          EventTriggerFramework    WorkflowMethodExecutionWorkflow   未来扩展
          (UI 事件)                (BackEndDriven 工作流)
                    │                        │
                    └────────────┬───────────┘
                                 │ Dictionary<string, object>
                                 ▼
                    WorkFlowMethodUsage.Executor
                                 │ Dictionary<string, object>
                                 ▼
                    FunctionExecutor
                                 │ Dictionary<string, object>
                                 ▼
                    ArgumentBuilder.BuildFromDescriptor
                                 │ 快速路径3：按参数名匹配
                                 ▼
                    目标函数（参数名精确匹配）
```

### 2.4 具体修改点

#### 修改 1：`WorkflowMethodExecutionService.cs` — Dictionary 版本直接传 Dictionary

```csharp
// 修改前：Dictionary → object?[] → 按位置匹配
public async Task<ExecutionResult> ExecuteFunctionAsync(string functionName, Dictionary<string, object> namedParameters)
{
    // ... 将 Dictionary 转成 object?[] ...
    var result = await WorkFlowMethodUsage.Executor.ExecuteAsync(functionName, parameters, execContext);
    // ...
}

// 修改后：Dictionary → Dictionary → 按参数名匹配
public async Task<ExecutionResult> ExecuteFunctionAsync(string functionName, Dictionary<string, object> namedParameters)
{
    // ... 直接传 Dictionary ...
    var result = await WorkFlowMethodUsage.Executor.ExecuteAsync(functionName, namedParameters, execContext);
    // ...
}
```

#### 修改 2：`WorkFlowMethodUsage.cs` — 新增 Dictionary 重载

```csharp
// 新增：接受 Dictionary<string, object> 的 ExecuteAsync 重载
public static async Task<BmExecutionResult> ExecuteAsync(
    string functionName,
    Dictionary<string, object> namedParameters,
    BmExecutionContext? context = null)
{
    EnsureInitialized();
    return await _functionExecutor.ExecuteAsync(functionName, namedParameters, context);
}
```

#### 修改 3：`FunctionExecutor.cs` — 新增 Dictionary 重载

```csharp
// 新增：接受 Dictionary<string, object> 的 ExecuteAsync 重载
public async Task<BmExecutionResult> ExecuteAsync(
    string functionName,
    Dictionary<string, object> namedParameters,
    BmExecutionContext? context = null)
{
    // ... 查找函数描述符 ...
    var args = ArgumentBuilder.BuildFromDescriptor(descriptor, namedParameters);
    // ... 执行 ...
}
```

#### 修改 4：`WorkflowMethodExecutionWorkflow.cs` — 委托给 IEventExecutionService

```csharp
// 修改前：直接调用 Executor.ExecuteAsync
var execResult = await WorkFlowMethodUsage.Executor.ExecuteAsync(
    request.FunctionName, parameters, execContext);

// 修改后：委托给 IEventExecutionService
var executionService = AppServiceProvider.Resolve<IEventExecutionService>();
var execResult = await executionService.ExecuteFunctionAsync(
    request.FunctionName, wfContext.Parameters);
```

#### 修改 5：`EventBindingTransformers.cs` — 无需修改

`BuildParametersFromBindings` 已返回 `Dictionary<string, object>`，且同时包含控件名键和位置索引键。整合后 Dictionary 直达 ArgumentBuilder 快速路径3，按参数名匹配，控件名键被正确使用，位置索引键作为回退。

### 2.5 无需修改的文件

| 文件 | 原因 |
|------|------|
| `ArgumentBuilder.cs` | 已有快速路径3（Dictionary 按参数名匹配），无需修改 |
| `IEventExecutionService.cs` | 接口定义不变，Dictionary 重载已存在 |
| `EventBindingTransformers.cs` | `BuildParametersFromBindings` 已返回 Dictionary |
| `BackEndDrivenBuilder.cs` | 注册逻辑不变 |

---

## 3. 优势

| # | 优势 | 说明 |
|---|------|------|
| 1 | **参数名信息不再丢失** | Dictionary 直达 ArgumentBuilder，按参数名精确匹配 |
| 2 | **调用链缩短** | 从 5 层减少到 3 层（EventTrigger → IEventExecutionService → Executor → ArgumentBuilder） |
| 3 | **单一入口** | 所有参数传递都经过 `IEventExecutionService` |
| 4 | **向后兼容** | `object?[]` 版本保留，不影响现有调用方 |
| 5 | **消除代码重复** | `WorkflowMethodExecutionWorkflow` 不再重复执行逻辑 |

---

## 4. 实施步骤

| 步骤 | 文件 | 修改内容 | 风险 |
|------|------|---------|------|
| 1 | `WorkFlowMethodUsage.cs` | 新增 `ExecuteAsync(functionName, Dictionary, context)` 重载 | 低 |
| 2 | `FunctionExecutor.cs` | 新增 `ExecuteAsync(functionName, Dictionary, context)` 重载 | 中（需确认 FunctionExecutor 内部逻辑） |
| 3 | `WorkflowMethodExecutionService.cs` | Dictionary 版本直接传 Dictionary，不再转 object?[] | 低 |
| 4 | `WorkflowMethodExecutionWorkflow.cs` | 改为委托 IEventExecutionService | 中（需注入 IEventExecutionService） |
| 5 | 编译验证 | `dotnet build MainProj.sln` | - |
| 6 | 运行时验证 | 触发 UI 事件，确认参数正确传递 | - |

---

## 5. 回退方案

如果整合后出现问题，可以：

1. **保留两条路径**：`WorkflowMethodExecutionWorkflow` 继续直接调用 `Executor.ExecuteAsync`，不委托给 `IEventExecutionService`
2. **保留 object?[] 转换**：`WorkflowMethodExecutionService` 继续将 Dictionary 转成 object?[]，走快速路径2
3. **逐步迁移**：先合并参数传递（Dictionary 直达），再合并执行路径

---

## 附录：当前文件清单

| 文件路径 | 行数 | 角色 |
|---------|------|------|
| `CsharpPrj/Share/EventBindingOrchestrator/Abstractions/IEventExecutionService.cs` | 89 | 接口定义 |
| `CsharpPrj/Operation/EventTriggerFramework/EventBinding/EventBindingTransformers.cs` | 626 | UI 事件绑定 + 参数构建 |
| `CsharpPrj/Operation/WorkFlowMethod/WorkflowMethodExecutionService.cs` | 124 | IEventExecutionService 实现 |
| `CsharpPrj/Operation/WorkFlowMethod/WorkflowMethodUseage.cs` | 911 | 工作流执行入口 |
| `CsharpPrj/Operation/BackEndDriven/Invoke/WorkflowMethodExecutionWorkflow.cs` | 316 | 参数执行工作流 |
| `CsharpPrj/Operation/BackEndDriven/Invoke/ArgumentBuilder.cs` | 346 | 参数构建器 |
