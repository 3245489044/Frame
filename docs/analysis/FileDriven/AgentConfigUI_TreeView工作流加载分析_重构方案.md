# 单函数执行统一为工作流执行路径 — 重构方案

> **基于** `AgentConfigUI_TreeView工作流加载分析.md` 的规划
> **目标**：消除单函数旧执行路径，所有函数统一走工作流执行路径
> **核心原则**：冲突类型统一迁移到 `TConfig`，消除 `Modules.ConfigurationModels` 中的重复定义

---

## 1. 现状分析

### 1.1 当前架构

```
JSON 反序列化
  │
  ▼
Modules.ConfigurationModels.WorkflowDefinitionEntry  ← 有 [JsonPropertyName] 特性
  │
  ▼  BindingMetadataTransformer.ConvertToBindingWorkflow()
  │
Share.EventBindingOrchestrator.Parameters.Binding.WorkflowDefinitionEntry  ← 旧业务类型（已废弃）
  │
  ▼  EventBindingContext.WorkflowDefinitions
  │
Modules.ConfigurationModels.WorkflowDefinitionEntry  ← 实际存储类型
  │
  ▼  EventBindingOrchestrator → wfService.RegisterWorkflows()
  │
TConfig.WorkflowDefinitionEntry  ← WorkflowMethodExecutionService 期望的类型
  │
  ▼  ExecuteWorkflowAsync()
  │
NodeExecutor.ExecuteNodeAsync()
```

### 1.2 类型冲突

存在 **两个** `WorkflowDefinitionEntry` / `WorkflowNodeEntry` 类型：

| 类型 | 所在项目 | 命名空间 | 用途 | 特性 |
|------|---------|---------|------|------|
| `WorkflowDefinitionEntry` | `Plate/TConfig` | `TConfig` | 业务执行模型 | 有 `ToExecutionPlan()` 方法 |
| `WorkflowDefinitionEntry` | `Share/UniversalMiddleware` | `Modules.ConfigurationModels` | JSON 反序列化模型 | 有 `[JsonPropertyName]` 特性 |

**问题**：`EventBindingContext.WorkflowDefinitions` 存储的是 `Modules.ConfigurationModels` 版本，
但 `WorkflowMethodExecutionService.RegisterWorkflows()` 期望的是 `TConfig` 版本，
两者类型不兼容，当前代码存在编译错误。

### 1.3 旧回退路径

`WorkflowMethodExecutionService.ExecuteFunctionAsync()` 旧实现中：
1. 先查 `_workflows` 字典（工作流路径）
2. 找不到时回退到手动构建 `StepDefinition` 的旧路径（单函数路径）
3. 旧路径通过 `_registry.TryGetConfig` 查找函数配置

---

## 2. 重构目标

1. **统一类型**：将 `Modules.ConfigurationModels.WorkflowDefinitionEntry` 合并到 `TConfig`，
   消除重复定义
2. **统一执行路径**：`ExecuteFunctionAsync` 只走工作流路径，找不到定义时直接抛错
3. **集中工作流逻辑**：工作流注册、查找、执行全部集中在 `WorkFlowMethod` 项目

---

## 3. 详细重构步骤

### 步骤 1：增强 TConfig.WorkflowDefinitionEntry（合并配置模型特性）

**文件**：`CsharpPrj/Plate/TConfig/TrConfig/Models/WorkflowDefinitionEntry.cs`

**变更**：
- 添加 `[JsonPropertyName]` 特性（从 `Modules.ConfigurationModels` 版本合并）
- 保留 `ToExecutionPlan()` / `ToStepDefinition()` 方法

```csharp
namespace TConfig;

public class WorkflowDefinitionEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("nodes")]
    public List<WorkflowNodeEntry> Nodes { get; set; } = new();

    public ExecutionPlan ToExecutionPlan() { ... }
}

public class WorkflowNodeEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("function")]
    public string? FunctionName { get; set; }

    [JsonPropertyName("parameters")]
    public object? Parameters { get; set; }

    [JsonPropertyName("outputKey")]
    public string? OutputKey { get; set; }

    [JsonPropertyName("outputTarget")]
    public string? OutputTarget { get; set; }

    public StepDefinition ToStepDefinition() { ... }
}
```

### 步骤 2：删除 Modules.ConfigurationModels 中的重复类型

**文件**：`CsharpPrj/Share/UniversalMiddleware/ConfigurationModels/Models/WorkflowDefinitionEntry.cs`

**变更**：删除整个文件（两个类定义）

**影响分析**：
- `EventConfig.cs` 中 `EventBinding.Workflows` 属性的类型改为 `Dictionary<string, TConfig.WorkflowDefinitionEntry>?`
- `EventBindingContext.cs` 中 `WorkflowDefinitions` 属性的类型改为 `Dictionary<string, TConfig.WorkflowDefinitionEntry>?`
- `BindingMetadataTransformer.cs` 中移除 `ConvertToBindingWorkflow` 方法（不再需要类型转换）

### 步骤 3：更新 EventConfig.cs

**文件**：`CsharpPrj/Share/UniversalMiddleware/ConfigurationModels/Models/EventConfig.cs`

**变更**：
- 第 240 行：`Workflows` 属性类型从 `Dictionary<string, WorkflowDefinitionEntry>?` 改为 `Dictionary<string, TConfig.WorkflowDefinitionEntry>?`
- 添加 `using TConfig;`

### 步骤 4：更新 EventBindingContext.cs

**文件**：`CsharpPrj/Share/EventBindingOrchestrator/Contexts/EventBindingContext.cs`

**变更**：
- 第 7 行：移除 `using Modules.ConfigurationModels;`（不再需要）
- 第 162 行：`WorkflowDefinitions` 属性类型改为 `Dictionary<string, TConfig.WorkflowDefinitionEntry>?`
- 添加 `using TConfig;`

### 步骤 5：重构 BindingMetadataTransformer.cs

**文件**：`CsharpPrj/Share/EventBindingOrchestrator/Transformers/BindingMetadataTransformer.cs`

**变更**：
- 第 12 行：移除 `using Modules.ConfigurationModels;`
- 添加 `using TConfig;`
- 删除 `ConvertToBindingWorkflow` 方法（第 233-255 行）— 不再需要类型转换
- 修改 `ExtractFromConfig` 方法（第 110-115 行）：直接使用 `bindings.Workflows`，不再调用 `ConvertToBindingWorkflow`
- 修改 `EnsureWorkflowDefinitions` 方法（第 155-209 行）：使用 `TConfig.WorkflowDefinitionEntry` 和 `TConfig.WorkflowNodeEntry`

**修改后的 ExtractFromConfig 片段**：
```csharp
var bindings = config.Events?.Bindings;
if (bindings?.Workflows is { Count: > 0 })
{
    // ★ 2026-07-15 重构：直接使用 TConfig.WorkflowDefinitionEntry，
    //   不再需要 ConvertToBindingWorkflow 转换
    input.WorkflowDefinitions = new Dictionary<string, TConfig.WorkflowDefinitionEntry>(
        bindings.Workflows, StringComparer.OrdinalIgnoreCase);
    
    // 验证工作流定义完整性...
}
```

**修改后的 EnsureWorkflowDefinitions 片段**：
```csharp
private static void EnsureWorkflowDefinitions(EventBindingContext input, ControlConfig config)
{
    // ...
    var workflows = input.WorkflowDefinitions
        ?? new Dictionary<string, TConfig.WorkflowDefinitionEntry>();
    
    workflows[functionName] = new TConfig.WorkflowDefinitionEntry
    {
        Id = functionName,
        Name = functionName,
        DisplayName = functionName,
        Nodes = new List<TConfig.WorkflowNodeEntry> { ... }
    };
    // ...
}
```

### 步骤 6：确认 WorkflowMethodExecutionService.cs 修改

**文件**：`CsharpPrj/Operation/WorkFlowMethod/WorkflowMethodExecutionService.cs`

**已完成的变更**：
- `_workflows` 改为非可空 `Dictionary<string, WorkflowDefinitionEntry>`
- 构造函数中初始化为空字典
- `RegisterWorkflows` 使用合并模式
- `ExecuteFunctionAsync` 只从 `_workflows` 查找，找不到则抛 `InvalidOperationException`
- 删除旧回退路径代码
- 新增 `ExecuteWorkflowAsync` 私有方法

**需要确认**：
- `ExecuteWorkflowAsync` 中每个节点调用 `_registry.TryGetConfig` — 单函数场景下，
  函数已通过 `WorkFlowMethodUsage` 注册，但 `IFunctionRegistry` 中可能没有对应配置。
  如果 registry 查找失败，整个工作流执行会失败。

### 步骤 7：更新 EventBindingOrchestrator.cs

**文件**：`CsharpPrj/Share/EventBindingOrchestrator/EventBindingOrchestrator.cs`

**变更**：
- 第 20 行：移除 `using Modules.ConfigurationModels;`（如果不再需要）
- 第 183 行：`RegisterWorkflows` 调用不再有类型冲突

---

## 4. 影响范围

| 文件 | 变更类型 | 风险 |
|------|---------|------|
| `Plate/TConfig/TrConfig/Models/WorkflowDefinitionEntry.cs` | 修改（添加 JsonPropertyName） | 低 |
| `Share/UniversalMiddleware/ConfigurationModels/Models/WorkflowDefinitionEntry.cs` | **删除** | 中 — 需确认无其他引用 |
| `Share/UniversalMiddleware/ConfigurationModels/Models/EventConfig.cs` | 修改（类型引用） | 低 |
| `Share/EventBindingOrchestrator/Contexts/EventBindingContext.cs` | 修改（类型引用） | 低 |
| `Share/EventBindingOrchestrator/Transformers/BindingMetadataTransformer.cs` | 重构 | 中 — 逻辑变更 |
| `Operation/WorkFlowMethod/WorkflowMethodExecutionService.cs` | 已修改 | 低 |
| `Share/EventBindingOrchestrator/EventBindingOrchestrator.cs` | 清理 using | 低 |

---

## 5. 验证清单

- [ ] `TConfig.WorkflowDefinitionEntry` 添加 `[JsonPropertyName]` 后 JSON 反序列化正常
- [ ] `Modules.ConfigurationModels.WorkflowDefinitionEntry` 删除后无编译错误
- [ ] `EventConfig.Workflows` 类型变更后反序列化正常
- [ ] `BindingMetadataTransformer` 不再需要 `ConvertToBindingWorkflow`
- [ ] `EventBindingContext.WorkflowDefinitions` 类型统一
- [ ] `WorkflowMethodExecutionService.ExecuteFunctionAsync` 只走工作流路径
- [ ] 工作流定义不存在时抛 `InvalidOperationException`
- [ ] 单函数自动包装为单节点工作流后执行正常
- [ ] 多步骤工作流执行正常

---

## 6. 回退方案

如果重构后出现问题，可以：
1. 恢复 `Modules.ConfigurationModels.WorkflowDefinitionEntry` 文件
2. 恢复 `EventConfig.cs` 和 `EventBindingContext.cs` 中的类型引用
3. 恢复 `BindingMetadataTransformer.ConvertToBindingWorkflow` 方法
4. 恢复 `WorkflowMethodExecutionService` 中的旧回退路径
