# Invoke 目录迁移方案：Share → BackEndDriven

> **目标**：将 `Share/CommandFramework/Invoke/` 的 4 个文件迁移到 `BackEndDriven` 层，消除与 `WorkFlowMethod` 的功能重复，统一命令执行入口。

---

## 1. 现状分析

### 1.1 当前 Invoke 目录（Share 层）

| 文件 | 功能 | 命名空间 | 与 WorkFlowMethod 的关系 |
|------|------|---------|------------------------|
| `FunctionExecution.cs` | `FunctionExecutor` 类 — 函数执行器 | `CommandFramework.Execution` | **完全重复**：WorkFlowMethod 的 `ExecutionFramework.Engine.FunctionExecutor` 做同样的事 |
| `ExecuteCommandStage.cs` | 管线阶段 — 统一处理 set/click/非事件命令 | `CommandFramework.Pipeline.Stages` | **管线核心**：CommandFramework 管线系统的核心阶段，WorkFlowMethod 无对应物 |
| `PipelineEntry.cs` | 管线入口 — 创建标准命令管线 | `CommandFramework.Pipeline` | **管线入口**：CommandFramework 的管线创建入口，WorkFlowMethod 无对应物 |
| `ArgumentBuilder.cs` | 参数构建器 — 统一参数构建 | `CommandFramework.Parameters` | **共享工具**：被 `FunctionStorage.Core.FunctionExecutor` 和 `CommandFramework.Execution.FunctionExecutor` 共同使用 |

### 1.2 三个 FunctionExecutor 的关系

系统中存在 **三个** FunctionExecutor：

| # | 类 | 位置 | 参数处理 | 注册表 |
|---|-----|------|---------|--------|
| 1 | `CommandFramework.Execution.FunctionExecutor` | `Share/CommandFramework/Invoke/FunctionExecution.cs` | `ArgumentBuilder.BuildFromDescriptor` | `Modules.Registry.FunctionRegistry` |
| 2 | `FunctionStorage.Core.FunctionExecutor` | `Operation/FunctionStorage/Framework/Core.cs` | `ArgumentBuilder.BuildFromDelegate` | `TConfig.IFunctionRegistry` |
| 3 | `ExecutionFramework.Engine.FunctionExecutor` | `Modules/ExecutionFramework/Engine/FunctionExecutor.cs` | `ParameterTransformer` | `ITrConfigRegistry` |

**#1 与 #3 功能完全重复**，都实现 `TConfig.IFunctionExecutor` 接口。#2 是 FunctionStorage 专用的执行器。

### 1.3 BackEndDriven 层现状

`BackEndDriven` 已引用 `CommandFramework`（Share 层），且 `Invoke/` 目录下已有：

- `ParameterExecutionWorkflow.cs` — 参数执行工作流实现（框架反转）
- `README.md` — 说明文档

BackEndDriven.csproj 已引用的相关项目：
- `CommandFramework.csproj`（Share 层）
- `Pipeline.csproj`（Modules 层）
- `ExecutionFramework.csproj`（Modules 层）
- `BasicMethod.csproj`
- `FrameworkAbstractions.csproj`
- `EventBindingOrchestrator.csproj`
- `EventTriggerFramework.csproj`
- `Registry.csproj`

---

## 2. 迁移方案

### 2.1 迁移路线

```
Share/CommandFramework/Invoke/                  BackEndDriven/Invoke/
├── FunctionExecution.cs      ──────────────→   ├── FunctionExecution.cs      (命名空间调整)
├── ExecuteCommandStage.cs    ──────────────→   ├── ExecuteCommandStage.cs    (命名空间调整)
├── PipelineEntry.cs          ──────────────→   ├── PipelineEntry.cs          (命名空间调整)
├── ArgumentBuilder.cs        ──────────────→   ├── ArgumentBuilder.cs        (命名空间调整)
                                                ├── ParameterExecutionWorkflow.cs  (已有)
                                                └── README.md                       (已有)
```

### 2.2 命名空间调整

| 文件 | 原命名空间 | 新命名空间 |
|------|-----------|-----------|
| `FunctionExecution.cs` | `CommandFramework.Execution` | `BackEndDriven.Invoke` |
| `ExecuteCommandStage.cs` | `CommandFramework.Pipeline.Stages` | `BackEndDriven.Pipeline` |
| `PipelineEntry.cs` | `CommandFramework.Pipeline` | `BackEndDriven.Pipeline` |
| `ArgumentBuilder.cs` | `CommandFramework.Parameters` | `BackEndDriven.Invoke` |

### 2.3 文件内引用调整

#### FunctionExecution.cs
```csharp
// 原
namespace CommandFramework.Execution;
using Modules.Registry;
using TConfig;
using CommandFramework.Parameters;

// 新
namespace BackEndDriven.Invoke;
using Modules.Registry;
using TConfig;
using BackEndDriven.Invoke;  // ArgumentBuilder 同命名空间，无需 using
```

#### ExecuteCommandStage.cs
```csharp
// 原
namespace CommandFramework.Pipeline.Stages;
using CommandFramework.Abstractions;
using CommandFramework.Models;
using CommandFramework.Parsing;
using ParameterResolver.Abstractions;
using ParameterResolver.Sources;

// 新
namespace BackEndDriven.Pipeline;
using CommandFramework.Abstractions;
using CommandFramework.Models;
using CommandFramework.Parsing;
using ParameterResolver.Abstractions;
using ParameterResolver.Sources;
```

#### PipelineEntry.cs
```csharp
// 原
namespace CommandFramework.Pipeline;
using CommandFramework.Abstractions;
using CommandFramework.Execution;
using CommandFramework.Models;
using CommandFramework.Pipeline.Stages;
using CommandFramework.Parsing;
using BP = Modules.Pipeline;

// 新
namespace BackEndDriven.Pipeline;
using CommandFramework.Abstractions;
using BackEndDriven.Invoke;  // FunctionExecutor 新命名空间
using CommandFramework.Models;
using BackEndDriven.Pipeline;  // ExecuteCommandStage 同命名空间
using CommandFramework.Parsing;
using BP = Modules.Pipeline;
```

#### ArgumentBuilder.cs
```csharp
// 原
namespace CommandFramework.Parameters;
using ParameterResolver.Abstractions;
using ParameterResolver.Builder;
using ParameterResolver.Converter;
using TConfig;
using CommandFramework.Parsing;

// 新
namespace BackEndDriven.Invoke;
using ParameterResolver.Abstractions;
using ParameterResolver.Builder;
using ParameterResolver.Converter;
using TConfig;
using CommandFramework.Parsing;
```

---

## 3. 外部引用方调整

### 3.1 Share 层引用方（需改为引用 BackEndDriven）

以下文件引用了 `CommandFramework.Pipeline` 或 `CommandFramework.Execution` 命名空间中的类型：

| 文件 | 引用内容 | 调整方式 |
|------|---------|---------|
| `Share/CommandFramework/CommandFrameworkHost.cs` | `PipelineEntry.CreateDefaultPipeline` | 改为 `BackEndDriven.Pipeline.PipelineEntry.CreateDefaultPipeline` |
| `Share/CommandFramework/CommandFrameworkUsage.cs` | `PipelineEntry.CreateDefaultPipeline` | 同上 |
| `Share/CommandFramework/Models/CommandExecutionReport.cs` | `ExecuteCommandStage.BindingResult` | 改为 `BackEndDriven.Pipeline.ExecuteCommandStage.BindingResult` |
| `Share/Adapters/CommandFrameworkAdapter.cs` | `CommandFramework.Pipeline` | 改为 `BackEndDriven.Pipeline` |
| `Share/CommandFramework/Registries.cs` | `CommandFramework.Execution` | 移除（FunctionExecutor 已迁移） |
| `Share/CommandFramework/Models/JsonCommandResult.cs` | `CommandFramework.Execution` | 移除 |
| `Share/CommandFramework/Models/CommandContext.cs` | `CommandFramework.Execution` | 移除 |
| `Share/CommandFramework/CommandFrameworkOptions.cs` | `CommandFramework.Execution` | 移除 |
| `Share/CommandFramework/Invoke/PipelineEntry.cs` | 自身引用 | 删除文件 |

### 3.2 Operation 层引用方

| 文件 | 引用内容 | 调整方式 |
|------|---------|---------|
| `Operation/EventTriggerFramework/ControlValueAccessorBase.cs` | `CommandFramework.Execution.FunctionExecutor`（注释） | 更新注释 |
| `Operation/EventTriggerFramework/Pipeline/EventBindingStage.cs` | `CommandFramework.Pipeline` | 改为 `BackEndDriven.Pipeline` |
| `Operation/EventTriggerFramework/FrameworkContext.cs` | `CommandFramework.Pipeline` | 同上 |
| `Operation/EventTriggerFramework/EventTriggerFrameworkInitializer.cs` | `CommandFramework.Pipeline` | 同上 |
| `Operation/FunctionStorage/Framework/FunctionStorageRegistry.cs` | `CommandFramework.Execution.FunctionExecutor` | 改为 `BackEndDriven.Invoke.FunctionExecutor` |
| `Operation/FunctionStorage/Framework/WorkFlowMethodInitializer.cs` | `CommandFramework.Execution.FunctionExecutor` | 同上 |
| `Operation/WdUI/UI/WinFormsCommandEngine.cs` | `CommandFramework.Execution` | 移除 |
| `Operation/WdUI/UI/WdUI.Core.cs` | `CommandFramework.Execution` | 移除 |

### 3.3 Modules 层引用方

| 文件 | 引用内容 | 调整方式 |
|------|---------|---------|
| `Modules/ExecutionFramework/Engine/ParameterProcessor.cs` | `new FunctionExecutor(_registry)` | 改为 `BackEndDriven.Invoke.FunctionExecutor` |
| `Modules/ExecutionFramework/Engine/PipelineExecutor.cs` | `new FunctionExecutor(_registry)` | 同上 |

### 3.4 WorkFlowMethod 引用方

| 文件 | 引用内容 | 调整方式 |
|------|---------|---------|
| `Operation/WorkFlowMethod/WorkflowMethodUseage.cs` | `new FunctionExecutor(_workflowRegistry)` | 改为 `BackEndDriven.Invoke.FunctionExecutor` |
| `Operation/WorkFlowMethod/NodeProcess/Execution/NodeExecutionStage.cs` | `CommandFramework.Parameters` | 改为 `BackEndDriven.Invoke` |

---

## 4. 与 WorkFlowMethod 的对接方案

### 4.1 迁移后的依赖关系

```
WorkFlowMethod ──→ BackEndDriven ──→ CommandFramework (Share)
                    │
                    ├── Invoke/
                    │   ├── FunctionExecutor      ← WorkFlowMethod 通过此执行器执行函数
                    │   ├── ArgumentBuilder       ← 参数构建工具
                    │   └── ParameterExecutionWorkflow  ← 参数执行工作流
                    │
                    └── Pipeline/
                        ├── ExecuteCommandStage   ← 命令管线阶段
                        └── PipelineEntry         ← 管线入口
```

### 4.2 WorkFlowMethod 的调整

WorkFlowMethod 当前使用 `ExecutionFramework.Engine.FunctionExecutor`（#3），迁移后：

- **短期**：保留 #3 作为 WorkFlowMethod 的执行器，#1 迁移到 BackEndDriven 后作为 CommandFramework 管线的执行器
- **长期**：WorkFlowMethod 可通过 BackEndDriven 的 `FunctionExecutor` 执行函数，逐步统一执行路径

---

## 5. 实施步骤

### Step 1：创建目标目录结构

```
BackEndDriven/Invoke/     ← 已有，新增 4 个文件
BackEndDriven/Pipeline/   ← 新建，放 ExecuteCommandStage + PipelineEntry
```

### Step 2：复制并调整 4 个文件

| 文件 | 源路径 | 目标路径 |
|------|--------|---------|
| FunctionExecution.cs | `Share/CommandFramework/Invoke/FunctionExecution.cs` | `BackEndDriven/Invoke/FunctionExecution.cs` |
| ArgumentBuilder.cs | `Share/CommandFramework/Invoke/ArgumentBuilder.cs` | `BackEndDriven/Invoke/ArgumentBuilder.cs` |
| ExecuteCommandStage.cs | `Share/CommandFramework/Invoke/ExecuteCommandStage.cs` | `BackEndDriven/Pipeline/ExecuteCommandStage.cs` |
| PipelineEntry.cs | `Share/CommandFramework/Invoke/PipelineEntry.cs` | `BackEndDriven/Pipeline/PipelineEntry.cs` |

### Step 3：调整命名空间

按第 2.2 节的映射表修改每个文件的命名空间。

### Step 4：更新外部引用方

按第 3 节的映射表更新所有引用方。

### Step 5：删除原文件

删除 `Share/CommandFramework/Invoke/` 下的 4 个文件。

### Step 6：编译验证

```bash
cd CsharpPrj
dotnet build MainProj.sln 2>&1 | findstr /i "error"
```

### Step 7：运行测试

```bash
cd CentralTest/CSharpTest
dotnet test Integration.Tests 2>&1 | findstr /i "failed"
```

---

## 6. 风险与注意事项

### 6.1 分层违规风险

`Share` 层不能引用 `Operation` 层。迁移后：
- `CommandFrameworkHost.cs`（Share 层）引用了 `PipelineEntry`（迁移到 BackEndDriven）
- **解决方案**：在 Share 层保留 `PipelineEntry` 的接口/抽象，或将 `CommandFrameworkHost` 也迁移到 BackEndDriven

### 6.2 循环依赖风险

`BackEndDriven` 已引用 `CommandFramework`，如果 `CommandFramework` 再引用 `BackEndDriven` 会形成循环。

**解决方案**：Share 层不引用 BackEndDriven，所有需要 BackEndDriven 功能的调用方（如 `CommandFrameworkHost`）通过 DI 注入或委托方式获取实现。

### 6.3 测试影响

`FunctionStorageRegistryUsageTests.cs` 中使用了 `FunctionStorageRegistry.ExecuteAsync`，内部使用 `FunctionStorage.Core.FunctionExecutor`（#2），不受此次迁移影响。

---

## 7. 附录：文件内容摘要

### 7.1 FunctionExecution.cs

```csharp
// 函数执行器，实现 IFunctionExecutor 接口
// 通过 FunctionRegistry 查找函数，使用 ArgumentBuilder 构建参数
// 支持：按名称执行、执行委托、执行 IFunctionUnit、执行 ITrConfig
// 迁移后命名空间：BackEndDriven.Invoke
```

### 7.2 ExecuteCommandStage.cs

```csharp
// 统一命令执行阶段，继承 CommandStageBase
// 根据 action 类型处理：set（设置控件值）、click（查找绑定→执行函数→写回结果）、非事件命令
// 支持框架反转：通过 IParameterExecutionWorkflow 委托执行流程
// 迁移后命名空间：BackEndDriven.Pipeline
```

### 7.3 PipelineEntry.cs

```csharp
// 管线入口，提供 CreateDefaultPipeline 静态方法
// 默认阶段顺序：ParseCommand → ExecuteCommand → ResultCollect
// PipelineEntryInstance 类管理共享状态和执行历史
// 迁移后命名空间：BackEndDriven.Pipeline
```

### 7.4 ArgumentBuilder.cs

```csharp
// 统一参数构建器，支持多种输入格式：
//   - string?[] → TypeConverter.ConvertArguments（快速路径）
//   - object?[] → 类型检查 + 转换
//   - IReadOnlyDictionary<string, object?> → 按参数名匹配
//   - 其他 → ParameterBuilder 降级
// 迁移后命名空间：BackEndDriven.Invoke
```
