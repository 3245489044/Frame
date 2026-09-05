# CommandFramework 与 WorkflowExecution：外壳与内核关系

> **文档日期**：2026-07-04  
> **核心定位**：CommandFramework 作为"外壳"，WorkflowExecution 作为"内核"，两者是"调用"关系，而非"合并"关系  
> **关注焦点**：CommandFramework 如何复用 WorkflowExecution 的参数传递和函数调用逻辑

---

## 一、架构定位：外壳与内核

### 1.1 职责边界

```
┌─────────────────────────────────────────────────────────────┐
│                    CommandFramework（外壳）                    │
│                                                             │
│  职责：UI 事件绑定、命令解析、控件交互、命令管线编排            │
│  位置：CsharpPrj/Share/CommandFramework/                     │
│  核心类型：CommandEngine, PipelineEntry, ExecuteCommandStage  │
│  CommandContext, CommandParser, FunctionExecutor             │
│                                                             │
│  ┌───────────────────────────────────────────────────────┐  │
│  │              WorkflowExecution（内核）                  │  │
│  │                                                       │  │
│  │  职责：参数处理、数据转换、条件逻辑、函数调用链编排        │  │
│  │  位置：CsharpPrj/Operation/WorkFlowMethod/WorkflowExec- │  │
│  │         ution/                                         │  │
│  │  核心类型：FuncCallRun, WorkflowEngine, CentralPipeline │  │
│  │  ParameterResolver, NodeExecutor                       │  │
│  │                                                       │  │
│  │  ┌─────────────────────────────────────────────────┐  │  │
│  │  │  复用接口层（外壳→内核的调用桥梁）                │  │  │
│  │  │  IParameterResolver ← 参数合并/存储/检索          │  │  │
│  │  │  IFunctionExecutor  ← 函数注册/执行/参数构建      │  │  │
│  │  │  IWorkflowRegistry ← 工作流函数注册/调用          │  │  │
│  │  │  FuncCallRun       ← 递归函数调用链执行           │  │  │
│  │  └─────────────────────────────────────────────────┘  │  │
│  └───────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────┘
```

### 1.2 核心原则

| 原则 | 说明 |
|------|------|
| **外壳不包含内核逻辑** | CommandFramework 不实现工作流编排、条件判断、循环执行等内核逻辑 |
| **内核不依赖外壳** | WorkflowExecution 不引用 CommandFramework 的 CommandContext、CommandEngine 等类型 |
| **单向调用** | 外壳→内核：CommandFramework 调用 WorkflowExecution 的参数和函数能力 |
| **接口隔离** | 通过接口（IParameterResolver、IFunctionExecutor）定义调用契约 |

---

## 二、参数传递复用

### 2.1 参数解析器（ParameterResolver）

**内核提供**（`CsharpPrj/Share/CommandFramework/Execution/Shared/Parameters/ParameterResolver.cs`）：

```csharp
public class ParameterResolver : IParameterResolver
{
    // 设置/获取执行参数
    public void SetParameter(string executionId, string key, object value);
    public object? GetParameter(string executionId, string key);
    public IDictionary<string, object> GetParameters(string executionId);

    // 合并参数（执行参数 + 节点参数，节点参数优先）
    public IDictionary<string, object> MergeParameters(
        string executionId,
        IDictionary<string, object>? nodeParameters);

    // 存储/获取节点结果
    public void StoreNodeResult(string executionId, string nodeKey, NodeExecutionResult result);
    public object? GetNodeResult(string executionId, string nodeKey);
    public IDictionary<string, object> GetAllNodeResults(string executionId);

    // 清理执行资源
    public void Cleanup(string executionId);
}
```

**外壳复用方式**：

```
CommandFramework（外壳）                    WorkflowExecution（内核）
      │                                           │
      │  ExecuteCommandStage.HandleClick()         │
      │    ↓                                      │
      │  从控件读取原始值 → string?[]              │
      │    ↓                                      │
      │  executor.ExecuteAsync(funcName, params)   │
      │    └──────────────────────────────────────→│  FunctionExecutor
      │                                            │    ↓
      │                                            │  ArgumentBuilder.BuildFromDescriptor()
      │                                            │    ↓
      │                                            │  ParameterResolver.MergeParameters()
      │                                            │    ↓
      │                                            │  DynamicInvoke(args)
      │                                            │    ↓
      │    ←──────────────────────────────────────┘  ExecutionResult
      │    ↓
      │  写回结果到控件/VirtualStore
```

### 2.2 参数构建器（ArgumentBuilder）

**内核提供**（`CsharpPrj/Share/CommandFramework/Parameters/ArgumentBuilder.cs`）：

```csharp
public static class ArgumentBuilder
{
    // 从 IFunctionDescriptor 构建参数（FunctionExecutor 场景）
    public static object?[]? BuildFromDescriptor(
        IFunctionDescriptor descriptor, object? parameters);

    // 从 Delegate 构建参数（FunctionStorage 场景）
    public static object?[] BuildFromDelegate(
        Delegate del, object? parameters);

    // 智能分割参数字符串
    public static string?[] SplitArguments(string rawArgs, int expectedCount = -1);
}
```

**外壳复用方式**：

```
外壳 ExecuteCommandStage：
  1. 从控件读取原始值 → string?[]
  2. 传入 FunctionExecutor.ExecuteAsync(funcName, string?[])
  3. FunctionExecutor 内部调用 ArgumentBuilder.BuildFromDescriptor()
     → 快速路径：string?[] → TypeConverter.ConvertArguments → 类型转换
     → 降级路径：ParameterBuilder 按参数名匹配
```

### 2.3 参数传递流程对比

| 场景 | 外壳（CommandFramework） | 内核（WorkflowExecution） |
|------|------------------------|--------------------------|
| **click 命令** | 查找绑定 → 从控件读值 → 调用函数执行器 | 参数构建 → 类型转换 → DynamicInvoke |
| **set 命令** | 查找控件 → 写入值（不涉及内核） | — |
| **非事件命令** | 委托给 INonEventCommandLookup | — |
| **工作流执行** | — | CentralPipeline 编排 → NodeExecutor 执行节点 |
| **函数管线** | — | FuncCallRun.PipelineRunAsync 递归执行调用链 |

---

## 三、函数调用复用

### 3.1 函数执行器（FunctionExecutor）

**内核提供**（`CsharpPrj/Share/CommandFramework/Execution/Shared/FunctionExecution.cs`）：

```csharp
public class FunctionExecutor : IFunctionExecutor
{
    // 按名称执行函数（外壳最常用的入口）
    public async Task<ExecutionResult> ExecuteAsync(
        string functionName, object?[]? parameters, EXContext? context = null);

    // 执行 Delegate
    public async Task<ExecutionResult> ExecuteAsync(
        Delegate function, object? parameters, EXContext? context = null);

    // 执行 IFunctionUnit
    public async Task<ExecutionResult> ExecuteAsync(
        IFunctionUnit unit, object? input, EXContext? context = null);

    // 执行 ITrConfig（带配置）
    public async Task<ExecutionResult> ExecuteAsync(
        ITrConfig config, object?[]? parameters, EXContext? context = null);

    // 强类型版本
    public async Task<ExecutionResult<TResult>> ExecuteAsync<TResult>(
        string functionName, object?[]? parameters, EXContext? context = null);
}
```

**外壳复用方式**：

```
外壳 ExecuteCommandStage.HandleClick():
  ↓
  1. 查找绑定 → 获取 functionName
  2. 从控件读取参数值 → string?[]
  3. 调用 executor.ExecuteAsync(functionName, string?[])
     └── 内核 FunctionExecutor:
          1. TryGetDescriptor → 获取函数元信息
          2. TryGet → 获取委托
          3. ArgumentBuilder.BuildFromDescriptor → 参数构建
          4. DynamicInvoke → 执行函数
          5. ExtractAsyncResultAsync → 提取异步结果
          6. 返回 ExecutionResult
  ↓
  4. 写回结果到控件
```

### 3.2 函数管线执行器（FuncCallRun）

**内核提供**（`CsharpPrj/Operation/WorkFlowMethod/WorkflowExecution/Execution/FuncCallRun.cs`）：

```csharp
public static class FuncCallRun
{
    // 递归执行函数调用链
    public static async Task<List<object?>> PipelineRunAsync(
        FuncPipelineParameters funcPipeline,
        IWorkflowRegistry functionRegistry,
        FuncExecutionContext context,
        int depth = 0);
}
```

**外壳复用方式**：

```
外壳（CommandFramework）不直接调用 FuncCallRun。
FuncCallRun 是工作流编排的内部机制，用于：
  - 节点执行时的函数调用链递归
  - 条件/循环/并行节点的参数传递
  - 函数管线参数的嵌套解析

外壳通过 FunctionExecutor 执行单个函数，
内核通过 FuncCallRun 编排函数调用链。
```

### 3.3 函数调用层次

```
┌──────────────────────────────────────────────────────────────┐
│                    CommandFramework（外壳）                    │
│                                                              │
│  ExecuteCommandStage                                         │
│    ↓ HandleClick()                                           │
│    ↓ executor.ExecuteAsync(funcName, params)                 │
│    ↓                                                         │
│  ┌──────────────────────────────────────────────────────┐    │
│  │  FunctionExecutor（内核函数执行器）                    │    │
│  │    ↓ BuildArgumentsWithResolver                       │    │
│  │    ↓ ArgumentBuilder.BuildFromDescriptor              │    │
│  │    ↓ DynamicInvoke                                    │    │
│  └──────────────────────────────────────────────────────┘    │
│                                                              │
│  ┌──────────────────────────────────────────────────────┐    │
│  │  FuncCallRun（内核函数管线执行器）                    │    │
│  │    ↓ PipelineRunAsync                                 │    │
│  │    ↓ 递归执行调用链                                    │    │
│  │    ↓ 嵌套参数解析                                      │    │
│  └──────────────────────────────────────────────────────┘    │
└──────────────────────────────────────────────────────────────┘
```

---

## 四、调用关系详解

### 4.1 外壳→内核的调用路径

```
CommandFramework（外壳）
│
├── PipelineEntry.CreateDefaultPipeline()
│   └── 注册 ParseCommandStage → ExecuteCommandStage → ResultCollectStage
│
├── ExecuteCommandStage.HandleClick()
│   ├── 查找绑定（外壳职责：UI 交互）
│   ├── 从控件读取值（外壳职责：控件交互）
│   ├── → 调用 FunctionExecutor.ExecuteAsync()（内核职责：函数执行）
│   │   ├── ArgumentBuilder.BuildFromDescriptor()（内核：参数构建）
│   │   ├── DynamicInvoke()（内核：函数调用）
│   │   └── 返回 ExecutionResult（内核：结果封装）
│   └── 写回结果到控件（外壳职责：UI 交互）
│
├── ExecuteCommandStage.HandleSet()
│   └── 纯外壳职责：查找控件 → 写入值（不调用内核）
│
└── ExecuteCommandStage.HandleNonEvent()
    └── 纯外壳职责：委托给 INonEventCommandLookup（不调用内核）
```

### 4.2 内核内部调用路径（供参考）

```
WorkflowExecution（内核）
│
├── WorkflowEngine.ExecuteAsync()
│   ├── 创建 Session
│   ├── → CentralPipeline.ExecuteAsync()
│   │   ├── StageRunner（初始化阶段）
│   │   ├── 动态阶段（用户注册）
│   │   ├── StageRunner（最终化阶段）
│   │   └── 返回 ExecutionResult
│   └── 更新 Session 状态
│
├── ExecutionOrchestrator.OrchestrateAsync()
│   ├── 遍历节点
│   ├── → NodeExecutor.ExecuteAsync()
│   │   ├── ParameterResolver.MergeParameters()
│   │   ├── FuncCallRun.PipelineRunAsync()（函数调用链）
│   │   └── 返回 NodeExecutionResult
│   └── 条件/循环/并行控制
│
└── FuncCallRun.PipelineRunAsync()
    ├── 递归遍历 FuncPipelineParameters.Calls
    ├── 嵌套参数解析（子 PipelineGroup）
    ├── → IWorkflowRegistry.Call()（函数调用）
    └── 返回 List<object?>
```

### 4.3 调用契约接口

```csharp
// 参数解析契约（内核提供，外壳调用）
public interface IParameterResolver
{
    void SetParameter(string executionId, string key, object value);
    object? GetParameter(string executionId, string key);
    IDictionary<string, object> GetParameters(string executionId);
    IDictionary<string, object> MergeParameters(string executionId, IDictionary<string, object>? nodeParameters);
    void StoreNodeResult(string executionId, string nodeKey, NodeExecutionResult result);
    object? GetNodeResult(string executionId, string nodeKey);
    IDictionary<string, object> GetAllNodeResults(string executionId);
    void Cleanup(string executionId);
}

// 函数执行契约（内核提供，外壳调用）
public interface IFunctionExecutor
{
    Task<ExecutionResult> ExecuteAsync(string functionName, object?[]? parameters, EXContext? context = null);
    Task<ExecutionResult> ExecuteAsync(Delegate function, object? parameters, EXContext? context = null);
    Task<ExecutionResult> ExecuteAsync(IFunctionUnit unit, object? input, EXContext? context = null);
    Task<ExecutionResult> ExecuteAsync(ITrConfig config, object?[]? parameters, EXContext? context = null);
    bool HasFunction(string functionName);
    IEnumerable<string> GetFunctionNames();
}

// 工作流函数注册契约（内核内部使用）
public interface IWorkflowRegistry
{
    bool HasFunction(string funcName);
    object? Call(string funcName, FuncExecutionContext context);
}
```

---

## 五、文件归属清单

### 5.1 外壳（CommandFramework）— 保留在 Share 层

| 文件 | 职责 | 与内核关系 |
|------|------|-----------|
| `Pipeline/PipelineEntry.cs` | 命令管线入口 | 调用 FunctionExecutor |
| `Pipeline/Stages/ExecuteCommandStage.cs` | 统一命令执行阶段 | HandleClick 调用 FunctionExecutor |
| `Pipeline/Stages/ParseCommandStage.cs` | 命令解析阶段 | 不依赖内核 |
| `Pipeline/Stages/ResultCollectStage.cs` | 结果收集阶段 | 不依赖内核 |
| `Pipeline/PipelineStageBase.cs` | 阶段基类 | 不依赖内核 |
| `Execution/CommandEngine.cs` | 命令引擎 | 不依赖内核 |
| `Execution/CommandPipeline/` | 命令管线组件 | 不依赖内核 |
| `Models/CommandContext.cs` | 命令上下文 | 不依赖内核 |
| `Models/CommandResults.cs` | 命令执行结果 | 不依赖内核 |
| `Parsing/` | 命令解析 | 不依赖内核 |
| `Parameters/ArgumentBuilder.cs` | 参数构建器 | 内核提供，外壳复用 |
| `Abstractions/` | 抽象接口 | 定义调用契约 |
| `CommandFrameworkHost.cs` | 统一入口 | 组装外壳+内核 |

### 5.2 内核（WorkflowExecution）— 在 Operation 层

| 文件 | 职责 | 与外壳关系 |
|------|------|-----------|
| `Execution/FuncCallRun.cs` | 函数管线执行器 | 内核内部使用 |
| `Engine/WorkflowEngine.cs` | 工作流引擎 | 内核核心 |
| `Engine/ExecutionOrchestrator.cs` | 执行编排器 | 内核核心 |
| `Engine/NodeExecutor.cs` | 节点执行器 | 内核核心 |
| `Pipeline/CentralPipeline.cs` | 中央管线 | 内核核心 |
| `Pipeline/PipelineBase.cs` | 管线基类 | 内核核心 |
| `Pipeline/PipelineStages.cs` | 管线阶段工厂 | 内核核心 |
| `Pipeline/WorkflowContext.cs` | 工作流上下文 | 内核核心 |
| `Models/NodeDefinition.cs` | 节点定义 | 内核模型 |
| `Models/WorkflowDefinition.cs` | 工作流定义 | 内核模型 |
| `Runtime/RuntimeContext.cs` | 运行时上下文 | 内核核心 |
| `Runtime/RuntimeContextManager.cs` | 运行时上下文管理 | 内核核心 |
| `Skeletons/NodeProcessSkeleton.cs` | 节点处理骨架 | 内核核心 |
| `Skeletons/NodeChainSkeleton.cs` | 节点链骨架 | 内核核心 |
| `Abstractions/` | 工作流抽象接口 | 内核内部契约 |

### 5.3 共享组件（被外壳和内核共同引用）

| 文件 | 职责 | 所在位置 |
|------|------|---------|
| `Execution/Shared/Parameters/ParameterResolver.cs` | 参数解析器 | CommandFramework |
| `Execution/Shared/FunctionExecution.cs` | 函数执行器 | CommandFramework |
| `Execution/Shared/PipelineBase.cs` | 通用管线基类 | CommandFramework |
| `Execution/Shared/CancellationTokenSourceManager.cs` | 取消令牌管理 | CommandFramework |
| `Execution/Shared/StateManagement/` | 状态管理 | CommandFramework |

---

## 六、典型调用示例

### 6.1 click 命令的完整调用链

```
用户操作：点击按钮 "Save"
  ↓
CommandFramework 外壳层：
  1. ParseCommandStage: 解析 "click Save" → ParsedCommand { Action="click", Target="Save" }
  2. ExecuteCommandStage.HandleClick():
     a. 查找绑定: FindBinding("Save", "click") → BindingResult { FunctionName="SaveData", Parameter="InputText", Target="ResultLabel" }
     b. 从控件读值: source.ReadValue("InputText") → "user input data"
     c. 调用内核: executor.ExecuteAsync("SaveData", ["user input data"])
        └── WorkflowExecution 内核层：
            1. FunctionExecutor.ExecuteAsync("SaveData", ["user input data"])
            2. ArgumentBuilder.BuildFromDescriptor → 类型转换
            3. DynamicInvoke → 执行 SaveData 函数
            4. 返回 ExecutionResult { IsSuccess=true, Value="saved" }
     d. 写回结果: WriteResult(context, "ResultLabel", "saved")
  3. ResultCollectStage: 收集执行结果
```

### 6.2 工作流执行的完整调用链

```
用户触发：执行工作流 "OrderProcess"
  ↓
WorkflowExecution 内核层：
  1. WorkflowEngine.ExecuteAsync(workflow, context):
     a. 创建 Session
     b. CentralPipeline.ExecuteAsync(workflow, context):
        - StageRunner(Initialization)
        - 动态阶段（用户注册的节点）
        - StageRunner(Finalization)
     c. 更新 Session 状态
  2. ExecutionOrchestrator.OrchestrateAsync():
     a. 遍历节点
     b. NodeExecutor.ExecuteAsync():
        - ParameterResolver.MergeParameters()
        - FuncCallRun.PipelineRunAsync()（函数调用链）
        - 返回 NodeExecutionResult
```

---

## 七、设计原则总结

### 7.1 为什么是"调用"而非"合并"

| 维度 | 合并方案 | 调用方案（当前） |
|------|---------|----------------|
| **耦合度** | 高耦合，一改俱改 | 低耦合，接口隔离 |
| **职责清晰度** | 模糊，外壳包含内核逻辑 | 清晰，外壳=UI交互，内核=业务编排 |
| **可测试性** | 差，需要同时 mock 两层 | 好，可分别 mock 接口测试 |
| **复用性** | 差，内核逻辑被外壳绑定 | 好，内核可被其他外壳复用 |
| **演进灵活性** | 差，修改内核影响外壳 | 好，内核可独立演进 |

### 7.2 复用边界

```
CommandFramework 可以复用 WorkflowExecution 的：
  ✅ IParameterResolver — 参数合并/存储/检索
  ✅ IFunctionExecutor — 函数注册/执行/参数构建
  ✅ ArgumentBuilder — 参数构建/类型转换
  ✅ ExecutionResult — 执行结果封装

CommandFramework 不应复用 WorkflowExecution 的：
  ❌ WorkflowEngine — 工作流编排逻辑
  ❌ CentralPipeline — 工作流级管线
  ❌ ExecutionOrchestrator — 节点编排
  ❌ FuncCallRun — 函数调用链递归（内核内部使用）
  ❌ NodeExecutor — 节点执行器
  ❌ WorkflowContext — 工作流上下文
```

### 7.3 未来演进方向

```
1. 接口稳定化
   IParameterResolver / IFunctionExecutor 接口稳定后，
   可提取到独立的共享抽象程序集。

2. 内核独立演进
   WorkflowExecution 可独立增加新的编排能力（如 DAG 执行、事件驱动），
   不影响 CommandFramework 的外壳职责。

3. 外壳扩展
   CommandFramework 可增加新的命令类型（如 gesture、voice），
   复用内核的参数和函数能力。

4. 多外壳复用内核
   同一个 WorkflowExecution 内核可被多个外壳复用：
   - CommandFramework（当前：命令驱动）
   - FileDriven（未来：文件驱动）
   - EventTriggerFramework（未来：事件驱动）
```
