# AgentConfigUI TreeView 工作流加载 — 重构步骤

> **基于** `AgentConfigUI_TreeView工作流加载分析_重构方案.md` 的规划
> **目标**：消除单函数旧执行路径，所有函数统一走工作流执行路径
> **状态**：已完成 11 项 / 共 12 项

---

## 已完成的工作（11 项）

### 步骤 1：增强 TConfig.WorkflowDefinitionEntry（添加 [JsonPropertyName]）

**文件**：`CsharpPrj/Plate/TConfig/TrConfig/Models/WorkflowDefinitionEntry.cs`

**变更**：
- 为 `WorkflowDefinitionEntry` 和 `WorkflowNodeEntry` 的所有属性添加 `[JsonPropertyName]` 特性
- 保留 `ToExecutionPlan()` / `ToStepDefinition()` 方法

### 步骤 2：删除 ConfigurationModels 中的重复类型

**文件**：`CsharpPrj/Share/UniversalMiddleware/ConfigurationModels/Models/WorkflowDefinitionEntry.cs`

**变更**：删除整个文件（两个类定义）

### 步骤 3：更新 EventConfig.cs 类型引用

**文件**：`CsharpPrj/Share/UniversalMiddleware/ConfigurationModels/Models/EventConfig.cs`

**变更**：
- `Workflows` 属性类型从 `Dictionary<string, WorkflowDefinitionEntry>?` 改为 `Dictionary<string, TConfig.WorkflowDefinitionEntry>?`
- 添加 `using TConfig;`

### 步骤 4：更新 EventBindingContext.cs 类型引用

**文件**：`CsharpPrj/Share/EventBindingOrchestrator/Contexts/EventBindingContext.cs`

**变更**：
- 移除 `using Modules.ConfigurationModels;`
- `WorkflowDefinitions` 属性类型改为 `Dictionary<string, TConfig.WorkflowDefinitionEntry>?`
- 添加 `using TConfig;`

### 步骤 5：重构 BindingMetadataTransformer.cs（删除 ConvertToBindingWorkflow）

**文件**：`CsharpPrj/Share/EventBindingOrchestrator/Transformers/BindingMetadataTransformer.cs`

**变更**：
- 移除 `using Modules.ConfigurationModels;`
- 添加 `using TConfig;`
- 删除 `ConvertToBindingWorkflow` 方法（不再需要类型转换）
- `ExtractFromConfig` 直接使用 `bindings.Workflows`（TConfig 版本）
- `EnsureWorkflowDefinitions` 使用 `TConfig.WorkflowDefinitionEntry` 和 `TConfig.WorkflowNodeEntry`

### 步骤 6：清理 EventBindingOrchestrator.cs 的 using

**文件**：`CsharpPrj/Share/EventBindingOrchestrator/EventBindingOrchestrator.cs`

**变更**：移除 `using Modules.ConfigurationModels;`

### 步骤 7：构建验证

- 确认 `TConfig.WorkflowDefinitionEntry` 添加 `[JsonPropertyName]` 后 JSON 反序列化正常
- 确认 `Modules.ConfigurationModels.WorkflowDefinitionEntry` 删除后无编译错误
- 确认 `EventConfig.Workflows` 类型变更后反序列化正常
- 确认 `BindingMetadataTransformer` 不再需要 `ConvertToBindingWorkflow`

### 阶段 3：事件处理器简化

**文件**：`CsharpPrj/Share/EventBindingOrchestrator/Transformers/BindingMetadataTransformer.cs`

**变更**：`CreateEventHandler` 已统一走 `IEventExecutionService`，删除旧的事件处理器创建逻辑

### 阶段 4：initCommands 迁移

**文件**：`FileDriven/FileDriven.UI/MainForm.cs`

**变更**：`initCommands` 统一执行路径，通过 `IEventExecutionService` 执行

### 阶段 5：测试验证

- 修复预先存在的编译错误
- 功能验证通过

### 修复预先存在的错误

- `AppServiceProvider` 找不到 → 修复引用
- `WorkFlowMethod.Usage` 找不到 → 修复引用
- nullable 警告 → 修复

---

## 后续需要做的步骤（1 项）

### 步骤 A：FunctionRegistrationLoader 增强 — 注册委托

**问题**：`FunctionRegistrationLoader.RegisterWorkflow()` 目前只注册 `ITrConfig`（`Delegate = null`），不注册委托。这意味着通过 `registration.json` 注册的 workflow 类型函数，`WorkflowExecutionLayer.ExecuteStepsCore()` 中通过 `FunctionStorageRegistry.Registry.TryGetDelegate()` 找不到委托。

**方案**：在 `FunctionRegistrationLoader.RegisterWorkflow()` 中，当 `workflowRef` 不为空时，从 `WorkflowExecutionLayer` 加载工作流定义并注册为委托。

**文件**：`FileDriven/FileDriven.Business/Registrations/FunctionRegistrationLoader.cs`

**变更**：
```csharp
private void RegisterWorkflow(RegistrationEntry reg)
{
    // 1. 注册 ITrConfig（已有逻辑）
    var trConfig = new TrConfig
    {
        Name = reg.Name,
        Description = reg.Description,
        InputType = typeof(object),
        OutputType = typeof(object),
        IsAsync = false,
        Parameters = Array.Empty<IParamConfig>(),
        Delegate = null
    };

    if (_registry is ITrConfigRegistry trConfigRegistry)
    {
        trConfigRegistry.Register(reg.Name, trConfig);
        _logger?.LogInformation($"Registered workflow config: {reg.Name}");
    }

    // 2. ★ 新增：如果 workflowRef 不为空，从 WorkflowExecutionLayer 加载并注册为委托
    //    使 WorkflowExecutionLayer.ExecuteStepsCore() 也能通过 TryGetDelegate 找到
    if (!string.IsNullOrWhiteSpace(reg.WorkflowRef))
    {
        try
        {
            var workflowDir = FileDriven.Abstractions.PathMananger.GetWorkFlowDir();
            var workflowPath = System.IO.Path.Combine(workflowDir, reg.WorkflowRef);
            var def = WorkflowExecutionLayer.LoadWorkflowDefinition(workflowPath);
            if (def != null)
            {
                // 注册为委托，使 WorkflowExecutionLayer 可以调用
                _registry.Register(reg.Name,
                    (Func<string, string>)(input =>
                        WorkflowExecutionLayer.ExecuteWorkflow(def, input)),
                    reg.Description);
                _logger?.LogInformation($"Registered workflow delegate: {reg.Name} → {reg.WorkflowRef}");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError($"Failed to load workflow '{reg.WorkflowRef}' for '{reg.Name}': {ex.Message}");
        }
    }
}
```

**影响**：
- 通过 `registration.json` 注册的 workflow 类型函数，既有 `ITrConfig` 又有委托
- `WorkflowExecutionLayer.ExecuteStepsCore()` 可以通过 `TryGetDelegate` 找到并执行
- `WorkflowMethodExecutionService` 可以通过 `TryGetConfig` 找到配置（已有逻辑，无需变更）

---

## 影响范围总览

| 文件 | 变更类型 | 风险 | 状态 |
|------|---------|------|------|
| `Plate/TConfig/TrConfig/Models/WorkflowDefinitionEntry.cs` | 修改（添加 JsonPropertyName） | 低 | ✅ 已完成 |
| `Share/UniversalMiddleware/ConfigurationModels/Models/WorkflowDefinitionEntry.cs` | **删除** | 中 | ✅ 已完成 |
| `Share/UniversalMiddleware/ConfigurationModels/Models/EventConfig.cs` | 修改（类型引用） | 低 | ✅ 已完成 |
| `Share/EventBindingOrchestrator/Contexts/EventBindingContext.cs` | 修改（类型引用） | 低 | ✅ 已完成 |
| `Share/EventBindingOrchestrator/Transformers/BindingMetadataTransformer.cs` | 重构 | 中 | ✅ 已完成 |
| `Operation/WorkFlowMethod/WorkflowMethodExecutionService.cs` | 修改 | 低 | ✅ 已完成 |
| `Share/EventBindingOrchestrator/EventBindingOrchestrator.cs` | 清理 using | 低 | ✅ 已完成 |
| `FileDriven/FileDriven.Business/Registrations/FunctionRegistrationLoader.cs` | **增强** | 低 | ⏳ 待完成 |

---

## 验证清单

- [x] `TConfig.WorkflowDefinitionEntry` 添加 `[JsonPropertyName]` 后 JSON 反序列化正常
- [x] `Modules.ConfigurationModels.WorkflowDefinitionEntry` 删除后无编译错误
- [x] `EventConfig.Workflows` 类型变更后反序列化正常
- [x] `BindingMetadataTransformer` 不再需要 `ConvertToBindingWorkflow`
- [x] `EventBindingContext.WorkflowDefinitions` 类型统一
- [x] `WorkflowMethodExecutionService.ExecuteFunctionAsync` 只走工作流路径
- [x] 工作流定义不存在时抛 `InvalidOperationException`
- [x] 单函数自动包装为单节点工作流后执行正常
- [x] 多步骤工作流执行正常
- [ ] `FunctionRegistrationLoader` 注册的 workflow 类型函数可通过委托执行

---

## 架构全景图

### 两个工作流体系

系统中存在**两个独立的工作流体系**，它们共享同一个函数注册中心 `TrConfigRegistry`，但工作流定义格式和执行引擎完全不同：

| 维度 | AgentConfigUI 工作流 | FileDriven 工作流 |
|------|---------------------|-------------------|
| **模型** | `TConfig.WorkflowDefinitionEntry` / `WorkflowNodeEntry` | `FunctionStorage.Models.WorkflowDefinition` / `WorkflowStep` |
| **JSON 位置** | `AgentConfigUI.json` → `events.bindings.workflows` | `FileDriven/DrivenConfig/WorkFlow/*.json` |
| **JSON 格式** | `{ nodes: [{ id, function, parameters, outputKey }] }` | `{ steps: [{ stepName, funcName, inputKeys, outputKey }] }` |
| **执行引擎** | `WorkflowMethodExecutionService` → `NodeExecutor` | `WorkflowExecutionLayer` → `ExecuteStepsCore` |
| **函数查找** | `_registry.TryGetConfig(node.FunctionName)` | `FunctionStorageRegistry.Registry.TryGetDelegate(name)` |
| **注册方式** | `EventBindingOrchestrator` → `RegisterWorkflows()` → `_workflows` 字典 | `WorkflowLibrary.RegisterAll()` → `_registry.Register(name, delegate)` |
| **用途** | UI 控件事件绑定（afterselect、afterexpand） | 多步骤函数编排（如 MathFunctionBrowserWorkflow 的 6 个步骤） |

### 共享的注册中心

```
TrConfigRegistry（单一注册中心，两个体系共享）
  │
  ├── _configs (ITrConfig)
  │     ← WorkflowLibrary.RegisterAll() 自动包装（FileDriven 工作流步骤函数）
  │     ← FunctionRegistrationLoader.RegisterWorkflow() 手动创建（registration.json）
  │     ← WorkflowMethodExecutionService 通过 TryGetConfig 查找
  │
  └── _delegates (Delegate)
        ← WorkflowLibrary.RegisterAll() 注册（FileDriven 工作流整体注册为委托）
        ← ★ FunctionRegistrationLoader 增强后注册（registration.json 中的 workflow 类型）
        ← WorkflowExecutionLayer 通过 TryGetDelegate 查找
```

### 工作流注册链路

```
链路 1：AgentConfigUI.json → EventBindingOrchestrator（UI 事件工作流）
  AgentConfigUI.json
    → EventConfigConverter 反序列化
    → EventConfig.Bindings.Workflows (Dictionary<string, TConfig.WorkflowDefinitionEntry>)
    → BindingMetadataTransformer.ExtractFromConfig()
      → 直接使用 bindings.Workflows（TConfig 版本）
      → EnsureWorkflowDefinitions()：将 EventsDictionary 中未在 Workflows 定义的
        单函数名自动包装为单节点工作流
    → EventBindingContext.WorkflowDefinitions
    → EventBindingOrchestrator → wfService.RegisterWorkflows()
    → WorkflowMethodExecutionService._workflows 字典

链路 2：WorkFlow/*.json → WorkflowLibrary.RegisterAll()（FileDriven 工作流）
  WorkFlow/*.json（如 MathFunctionBrowserWorkflow.json）
    → WorkflowExecutionLayer.EnsureCachePopulated()
    → WorkflowLibrary.RegisterAll()
    → _registry.Register(name, delegate)  ← 注册为委托
    → TrConfigRegistry 自动包装为 ITrConfig → _configs[name]
    → 同时存入 _delegates[name]

链路 3：FunctionRegistrations/*.registration.json → FunctionRegistrationLoader
  *.registration.json (type: "workflow")
    → FunctionRegistrationLoader.RegisterWorkflow()
    → trConfigRegistry.Register(name, trConfig)  ← 注册 ITrConfig（已有逻辑）
    → ★ 新增：_registry.Register(name, delegate)  ← 注册委托
```

### 工作流执行链路

```
执行链路 A：UI 事件触发 → WorkflowMethodExecutionService
  UI 事件触发（afterselect / afterexpand / selectedIndexChanged）
    → EventBindingOrchestrator.TriggerEventAsync()
    → IEventExecutionService.ExecuteFunctionAsync(functionName)
    → WorkflowMethodExecutionService.ExecuteFunctionAsync()
      → 查 _workflows 字典（链路 1 注册的 AgentConfigUI 工作流定义）
      → 找不到 → 查 _registry.TryGetConfig（链路 2/3 注册的函数配置）
        → 找到 → 自动包装为单节点工作流 → 注册到 _workflows → 执行
      → 都找不到 → 抛 InvalidOperationException
    → ExecuteWorkflowAsync()
      → 遍历 Nodes
      → 每个节点 _registry.TryGetConfig(node.FunctionName)
        → 链路 2/3 注册的函数都能找到（都在 _configs 字典中）
      → _nodeExecutor.ExecuteNodeAsync()
        → _executor.ExecuteAsync(functionName, ...)
        → WorkflowMethodExecutionService (IFunctionExecutor 实现)
          → ExecuteFunctionAsync(functionName, parameters)
          → 查 _workflows → 找不到 → 查 _registry.TryGetConfig
          → 找到 → 包装为单节点工作流 → 执行

执行链路 B：FileDriven 内部调用 → WorkflowExecutionLayer
  FileDriven 内部调用
    → WorkflowExecutionLayer.ExecuteStepsCore()
    → FunctionStorageRegistry.Registry.TryGetDelegate(name)
    → 链路 2 注册的 → 找到委托 → 直接执行
    → 链路 3 注册的 → ★ 增强后也能找到委托 → 执行
```

### 两个体系的关系

```
┌─────────────────────────────────────────────────────────────────────────┐
│  两个工作流体系共存，共享 TrConfigRegistry                                │
│                                                                         │
│  ┌─────────────────────────────────────┐  ┌───────────────────────────┐ │
│  │ AgentConfigUI 工作流体系              │  │ FileDriven 工作流体系       │ │
│  │                                     │  │                           │ │
│  │ AgentConfigUI.json                  │  │ WorkFlow/*.json           │ │
│  │   bindings.workflows:               │  │   steps: [{               │ │
│  │     { nodes: [{                     │  │     stepName, funcName,   │ │
│  │         id, function,               │  │     inputKeys, outputKey  │ │
│  │         parameters, outputKey       │  │   }]                      │ │
│  │     }]}                             │  │                           │ │
│  │         │                           │  │         │                 │ │
│  │         ▼                           │  │         ▼                 │ │
│  │  WorkflowMethodExecutionService     │  │  WorkflowExecutionLayer   │ │
│  │  → _workflows 字典                  │  │  → TryGetDelegate         │ │
│  │  → TryGetConfig                     │  │         │                 │ │
│  │         │                           │  │         │                 │ │
│  └─────────┼───────────────────────────┘  └─────────┼─────────────────┘ │
│            │                                         │                  │
│            └──────────┬──────────────────────────────┘                  │
│                       ▼                                                 │
│            ┌──────────────────────┐                                     │
│            │   TrConfigRegistry   │                                     │
│            │  ┌────────────────┐  │                                     │
│            │  │ _configs       │  │  ← 链路 2 自动包装 + 链路 3 手动创建 │
│            │  │ (ITrConfig)    │  │                                     │
│            │  ├────────────────┤  │                                     │
│            │  │ _delegates     │  │  ← 链路 2 注册 + 链路 3 增强后注册   │
│            │  │ (Delegate)     │  │                                     │
│            │  └────────────────┘  │                                     │
│            └──────────────────────┘                                     │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## 回退方案

如果重构后出现问题，可以：

1. **类型回退**：
   - 恢复 `Modules.ConfigurationModels.WorkflowDefinitionEntry` 文件
   - 恢复 `EventConfig.cs` 和 `EventBindingContext.cs` 中的类型引用
   - 恢复 `BindingMetadataTransformer.ConvertToBindingWorkflow` 方法

2. **执行路径回退**：
   - 恢复 `WorkflowMethodExecutionService` 中的旧回退路径

3. **注册回退**：
   - 恢复 `FunctionRegistrationLoader.RegisterWorkflow()` 的旧实现
   - 移除委托注册逻辑
