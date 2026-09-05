# WorkFlowMethod 重构规划

> 目标：将散布在多个项目中的工作流核心逻辑集中到 `CsharpPrj\Operation\WorkFlowMethod`，消除跨项目调用链的复杂冗余。

---

## 一、当前现状：逻辑分散在 5 个项目

```
用户输入 → TextChanged
  → Share/EventBindingOrchestrator        ← 事件绑定、参数构建
    → BackEndDriven/WorkFlowMethodBridge   ← 执行引擎、参数转换、结果写回
      → StateBlockDriven/FunctionStorage   ← 函数注册、查找（保持不变）
        → Plate/TConfig                    ← 工作流模型定义
          → FileDriven/DrivenConfig        ← JSON 配置驱动（保持不变）
```

一个完整的工作流调用链跨越 **5 个项目**，导致：
- 调用链条复杂冗余
- 职责边界模糊
- 修改一个环节需要跨多个项目

---

## 二、目标归属划分

| 逻辑模块 | 当前所在项目 | 目标归属 | 说明 |
|---|---|---|---|
| **函数注册与查找** | `StateBlockDriven` | **保持不变** | 注册中心职责明确，不迁移 |
| **JSON 配置驱动** | `FileDriven` | **保持不变** | 配置层职责明确，不迁移 |
| **事件绑定与参数构建** | `Share/EventBindingOrchestrator` | → **`WorkFlowMethod`** | 事件触发后的参数解析是工作流核心 |
| **工作流执行引擎** | `BackEndDriven/WorkFlowMethodBridge` | → **`WorkFlowMethod`** | 执行引擎是工作流核心 |
| **工作流定义模型** | `Plate/TConfig/TrConfig/Models/ExecutionModels.cs` | → **`WorkFlowMethod`** | ExecutionPlan/ExecutionResult 等模型 |
| **控件值读写** | `Share/EventBindingOrchestrator/Invoke/WinFormsValueAccessor.cs` | → **`WorkFlowMethod`** | 输出写回逻辑 |

---

## 三、需迁移的文件清单

### 3.1 从 `Share/EventBindingOrchestrator` 迁入

| 文件 | 核心逻辑 |
|---|---|
| `Invoke/Pipeline/EventBindingStageBase.cs` | `CreateEventHandler` — 事件处理器创建、参数构建委托调用、执行引擎调用、结果处理 |
| `Invoke/WinFormsValueAccessor.cs` | 控件值读写（SetValue/GetValue），含跨线程调度 |
| `Services/DefaultParameterBuilder.cs` | 参数构建器（已标记 Obsolete，迁移时评估是否保留） |

### 3.2 从 `BackEndDriven/WorkFlowMethodBridge` 迁入

| 文件 | 核心逻辑 |
|---|---|
| `WorkflowMethodExecutionWorkflow.cs` | 参数转换（控件值→函数参数）、`IEventExecutionService.ExecuteFunctionAsync` 调用、结果写回 |
| `Models/ExecutionResult.cs` | `ExecutionResult`、`IExecutionResult` 定义 |

### 3.3 从 `Plate/TConfig/TrConfig/Models/ExecutionModels.cs` 迁入

| 模型 | 说明 |
|---|---|
| `ExecutionPlan` | 执行计划（含 Nodes、Parameters） |
| `ExecutionResult` | 执行结果（含 Value、IsSuccess、ExecutionTime） |
| `ExecutionResult<T>` | 泛型执行结果 |
| `StepDefinition` | 步骤定义 |

---

## 四、迁移后 WorkFlowMethod 项目结构（目标）

```
CsharpPrj/Operation/WorkFlowMethod/
├── Execution/                          ← 执行引擎（从 BackEndDriven 迁入）
│   ├── WorkflowMethodExecutionWorkflow.cs
│   └── ExecutionResult.cs
├── EventBinding/                       ← 事件绑定（从 Share 迁入）
│   ├── EventBindingStageBase.cs
│   └── WinFormsValueAccessor.cs
├── Models/                             ← 模型定义（从 TConfig 迁入）
│   └── ExecutionModels.cs
├── Parameters/                         ← 参数构建（已有，保持不变）
│   ├── ParameterBuilder.cs
│   ├── ParameterBuilderStages.cs
│   ├── ParameterValueResolver.cs
│   └── ...
├── Output/                             ← 输出写回（已有）
│   └── OutputHelper.cs
└── Diagnostics/                        ← 诊断日志（已有）
    └── WorkFlowMethodLog.cs
```

---

## 五、调用链简化效果

### 重构前（5 个项目跨越）
```
用户输入
  → Share.EventBindingOrchestrator (事件绑定)
    → BackEndDriven.WorkFlowMethodBridge (执行引擎)
      → StateBlockDriven.FunctionStorage (注册表)
        → Plate.TConfig.Models (模型)
          → FileDriven.DrivenConfig (配置)
```

### 重构后（3 个项目）
```
用户输入
  → WorkFlowMethod.EventBinding (事件绑定+执行引擎)
    → StateBlockDriven.FunctionStorage (注册表) ← 不变
      → FileDriven.DrivenConfig (配置) ← 不变
```

---

## 六、注意事项

1. **接口抽象保留**：`Share` 层的事件绑定接口（`IEventBinderStrategy`、`IEventBus` 等）保留在 `Share`，`WorkFlowMethod` 仅迁入实现类
2. **依赖关系**：`WorkFlowMethod` 需添加对 `StateBlockDriven`（函数注册表）和 `Modules.EventFramework`（事件框架接口）的项目引用
3. **向后兼容**：原位置保留 `[Obsolete]` 转发类，指向新位置，确保现有代码不中断
4. **`DefaultParameterBuilder`**：已标记 `[Obsolete("请使用 WorkFlowMethod.ParameterBuilder")]`，迁移时直接删除
