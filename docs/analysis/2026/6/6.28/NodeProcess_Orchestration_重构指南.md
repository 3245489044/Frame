# NodeProcess → Orchestration 重构指南

> **目标**：Orchestration 项目整体基于 NodeProcess 的新类型进行重构，NodeProcess 作为新基准完全自包含。

---

## 一、架构原则

```
NodeProcess（新基准，完全自包含）
    ↑ 基于
Orchestration（重构目标，引用 NodeProcess 类型）
```

- **NodeProcess** 不依赖 Orchestration 中的任何类型
- **Orchestration** 应引用 NodeProcess 中的类型（`WorkFlowMethod.Skeletons.NodeProcess.Models`）
- 所有公共类型定义在 NodeProcess 中，Orchestration 只做编排和组合

---

## 二、NodeProcess 当前状态

### 已创建的新文件（新基准）

| 文件 | 命名空间 | 说明 |
|------|---------|------|
| `Models/NodeConfig.cs` | `Skeletons.NodeProcess.Models` | 节点配置模型 |
| `Models/ChainInfo.cs` | `Skeletons.NodeProcess.Models` | 链信息泛型 |
| `Models/Dtos.cs` | `Skeletons.NodeProcess.Models` | 数据传输对象 |
| `Models/ConnectionModels.cs` | `Skeletons.NodeProcess.Models` | 连接模型 |
| `Models/NodeDefinition.cs` | `Skeletons.NodeProcess.Models` | 图节点定义 |
| `NodeConfigFactory.cs` | `Skeletons.NodeProcess` | 节点配置工厂 |
| `NodeManager.cs` | `Skeletons.NodeProcess` | 节点管理器 |
| `NodeCommandAdapter.cs` | `Skeletons.NodeProcess` | 节点命令适配器 |
| `DeduplicationProcessor.cs` | `Skeletons.NodeProcess` | 去重处理器 |
| `Identity.cs` | `Skeletons.NodeProcess` | ID 生成器 + 节点注册表 |
| `Storage.cs` | `Skeletons.NodeProcess` | 存储管理器 |
| `Diagnostics.cs` | `Skeletons.NodeProcess` | 诊断工具 |
| `Runtime/WorkflowRuntimeContext.cs` | `Skeletons.NodeProcess.Runtime` | 运行时上下文 |
| `Runtime/WorkflowRuntimeContextManager.cs` | `Skeletons.NodeProcess.Runtime` | 运行时上下文管理器 |
| `NodeChainSkeleton.cs` | `Skeletons.NodeProcess` | 节点链 Skeleton |

### 仍需处理引用问题的文件

| 文件 | 当前引用 | 问题 |
|------|---------|------|
| `NodeManager.cs` | `Orchestration.Config` | 实际未使用该命名空间下的类型，可安全删除 |
| `Graph/GraphDefinitionStage.cs` | `Orchestration.Config` | 使用了 `NodeConfig` 作为泛型参数 |
| `Graph/GraphChainUpdateStage.cs` | `Orchestration.Config` | 实际未使用，可安全删除 |
| `NodeProcessSkeleton.cs` | `Orchestration.Context.Models` | 实际未使用，可安全删除 |

---

## 三、引用修改步骤

### 步骤 1：删除未使用的 using

**NodeManager.cs** — 删除第 9 行：
```csharp
// 删除这一行：
using WorkFlowMethod.Orchestration.Config;
```

**GraphChainUpdateStage.cs** — 删除第 12 行：
```csharp
// 删除这一行：
using WorkFlowMethod.Orchestration.Config;
```

**NodeProcessSkeleton.cs** — 删除第 11 行：
```csharp
// 删除这一行：
using WorkFlowMethod.Orchestration.Context.Models;
```

### 步骤 2：修改实际使用的引用

**GraphDefinitionStage.cs** — 第 9 行改为：
```csharp
// 原：
using WorkFlowMethod.Orchestration.Config;
// 改为：
using WorkFlowMethod.Skeletons.NodeProcess.Models;
```

同时第 32 行的泛型参数自动解析到本地 `Models.NodeConfig`：
```csharp
private readonly IGraph<string, NodeConfig, object> _graph;
// NodeConfig 现在来自 Skeletons.NodeProcess.Models
```

### 步骤 3：编译验证

```bash
dotnet build CsharpPrj/Operation/WorkFlowMethod/NodeProcess/NodeProcess.csproj
```

---

## 四、Orchestration 重构路线

### 阶段 1：引用替换

将 Orchestration 中所有 `using WorkFlowMethod.Orchestration.Config` 替换为 `using WorkFlowMethod.Skeletons.NodeProcess.Models`，使用以下类型映射：

| 旧类型（Orchestration.Config） | 新类型（Skeletons.NodeProcess.Models） |
|-------------------------------|--------------------------------------|
| `NodeConfig` | `Models.NodeConfig` |
| `ChainInfo<T>` | `Models.ChainInfo<T>` |
| `IDeduplicationProcessor` | `DeduplicationProcessor.IDeduplicationProcessor` |
| `IdGenerator` | `Identity.IdGenerator` |
| `NodeRegistry` | `Identity.NodeRegistry` |
| `Diagnostics` | `Diagnostics` |
| `NodeConfigFactory` | `NodeConfigFactory` |
| `NodeManager` | `NodeManager` |
| `NodeCommandAdapter` | `NodeCommandAdapter` |
| `ChainInfoWorkflowStorageManager` | `Storage.ChainInfoWorkflowStorageManager` |
| `WebCallManager` | `Storage.WebCallManager` |
| `WorkflowRuntimeContext` | `Runtime.WorkflowRuntimeContext` |
| `WorkflowRuntimeContextManager` | `Runtime.WorkflowRuntimeContextManager` |

### 阶段 2：逻辑迁移

将 Orchestration 中的业务逻辑逐步迁移到 NodeProcess 中：

1. **配置逻辑** → `NodeConfigFactory.cs`
2. **去重逻辑** → `DeduplicationProcessor.cs`
3. **ID 管理** → `Identity.cs`
4. **存储逻辑** → `Storage.cs`
5. **运行时管理** → `Runtime/` 目录
6. **诊断验证** → `Diagnostics.cs`

### 阶段 3：删除旧代码

确认所有引用迁移完成后，删除 Orchestration 中的旧实现文件。

---

## 五、注意事项

1. **不要修改 Orchestration 目录** — 当前任务只处理 NodeProcess 内部的引用清理
2. **编译通过为第一优先级** — 每次修改后运行 `dotnet build` 验证
3. **NodeProcess 保持自包含** — 不引入对 Orchestration 的依赖
4. **Orchestration 后续重构** — 在单独的 PR/任务中完成
