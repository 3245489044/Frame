# WorkFlowMethod 执行架构与修复总结

> 更新日期：2026-07-06
> 关联分析文档：[卡死原因分析-MathFunctions_SetValue.md](./卡死原因分析-MathFunctions_SetValue.md)

---

## 一、当前执行架构

### 1.1 整体调用链

```
UI 事件触发
  → WinFormsEventBinder.EventTriggered
    → EventBinder_ResolvingService（查找服务）
      → EventBinder_ServiceResolved（定位到 WorkflowMethodExecutionService）
        → EventBinder_ExecutingFunction
          → WorkflowMethodExecutionService.ExecuteFunctionAsync
            → 注册表查找（BasicRegistry / WorkFlowRegistry）
            → 参数转换（ConvertToTargetType）
            → 创建 PipelineContext
            → 设置参数到上下文（SetData）
            → 创建 NodeDefinition
            → NodeExecutor.ExecuteNodeAsync(node, context)
              → 执行目标函数
              → 返回结果
            → 返回 ExecutionResult
```

### 1.2 核心组件

| 组件 | 文件路径 | 职责 |
|------|---------|------|
| `WorkflowMethodExecutionService` | `Operation/WorkFlowMethod/WorkflowMethodExecutionService.cs` | 入口服务，注册表查找、参数转换、执行调度 |
| `PipelineContext` | `Operation/WorkFlowMethod/WorkflowExecution/Pipeline/WorkflowContext.cs` | 统一状态容器，继承 Modules.Pipeline.PipelineContext |
| `NodeExecutor` | `Operation/WorkFlowMethod/WorkflowExecution/Engine/` | 节点执行器，支持超时/重试 |
| `NodeDefinition` | `Operation/WorkFlowMethod/WorkflowExecution/Models/` | 节点定义模型 |
| `WorkFlowMethodLog` | `Operation/WorkFlowMethod/Diagnostics/` | 结构化日志系统 |

### 1.3 PipelineContext 继承链

```
SkeletonContext（BasicMethod/FrameMethod/SkeletonFlow.cs）
  └─ _items: ConcurrentDictionary<string, object>
  └─ Items: IReadOnlyDictionary<string, object> => _items
  └─ SetItem/RemoveItem
  │
  └─ Modules.Pipeline.PipelineContext（Modules/Pipeline/Core.cs）
      └─ Mode / StageSuccess / StageErrors / SharedData
      └─ SetStageResult / GetStageSuccess / GetStageError / Reset()
      │
      └─ WorkflowExecution.Pipeline.PipelineContext（当前类）
          └─ _sharedData: ConcurrentDictionary<string, object>（独立字段）
          └─ Items: new IDictionary<string, object>（ObservableDictionaryWrapper 包装）
          └─ GraphSnapshot / Session / CurrentNodeKey / NextNodeKey
          └─ SetData / GetData / TryGetData / RemoveData
          └─ Parameters（带脏标记的缓存）
          └─ CreateSubContext / CreateSnapshot / RestoreFromSnapshot
```

---

## 二、已修复问题

### 2.1 无限递归 Bug（2026-07-06）

**根因：** `_sharedData` 属性 getter 通过 `Items` 属性映射到父类的 `ConcurrentDictionary`，但 `Items` 被 `new` 隐藏后返回 `ObservableDictionaryWrapper`。`Items` getter 中 `new ObservableDictionaryWrapper(_sharedData)` 的参数求值又调用 `_sharedData` getter，形成无限递归。

```
SetData → _sharedData 属性 getter
  → (ConcurrentDictionary)Items
    → Items getter（new 隐藏版本）
      → _itemsWrapper ??= new ObservableDictionaryWrapper(_sharedData, ...)
        → 参数求值 _sharedData → _sharedData 属性 getter
          → (ConcurrentDictionary)Items → Items getter → ... 无限递归！
```

**修复方案：** 将 `_sharedData` 从属性映射改为独立 `readonly` 字段。

```csharp
// 修复前
private ConcurrentDictionary<string, object> _sharedData
    => (ConcurrentDictionary<string, object>)Items;

// 修复后
private readonly ConcurrentDictionary<string, object> _sharedData = new();
```

**影响范围：**
- `SetData` / `GetData` / `TryGetData` / `RemoveData` — 直接操作 `_sharedData` 字段
- `GraphSnapshot` / `Session` 属性 — 直接操作 `_sharedData` 字段
- `Parameters` 属性 — 遍历 `_sharedData` 字段
- `CreateSubContext` / `CreateSnapshot` / `RestoreFromSnapshot` — 直接操作 `_sharedData` 字段
- `Reset()` — 新增重写，同时清空 `_sharedData`

### 2.2 遗留诊断日志

`Items` 属性 getter 中保留了 5 个诊断日志点，用于排查 `Items` 访问相关问题：

| 日志点 | 位置 | 用途 |
|--------|------|------|
| `Items_Getter_Enter` | 进入 getter | 确认是否进入，输出 `_itemsWrapper` 状态 |
| `Items_Getter_BeforeNew` | `_itemsWrapper == null` 分支 | 确认进入初始化分支 |
| `Items_Getter_AfterSharedData` | `_sharedData` 求值后 | 确认 `_sharedData` 类型 |
| `Items_Getter_AfterNew` | `new ObservableDictionaryWrapper` 后 | 确认包装器创建成功 |
| `Items_Getter_Exit` | 返回前 | 确认 getter 正常退出 |

---

## 三、当前能力与限制

### 3.1 当前支持

- ✅ 单节点函数执行（SetValue、Add、Subtract 等）
- ✅ 注册表驱动的函数查找（BasicRegistry / WorkFlowRegistry）
- ✅ 参数类型转换
- ✅ 超时控制（默认 10s）
- ✅ 重试机制
- ✅ 结构化日志（WorkFlowMethodLog）
- ✅ 线程安全的共享数据存储
- ✅ 上下文快照（CreateSnapshot / RestoreFromSnapshot）

### 3.2 当前限制

- ❌ 不支持多节点工作流图遍历
- ❌ 不支持节点间数据传递（当前每次调用创建独立 PipelineContext）
- ❌ 不支持条件转换（transitions）
- ❌ 不支持工作流生命周期管理（暂停/恢复/取消）
- ❌ 不支持执行历史记录

---

## 四、多节点工作流扩展基础

### 4.1 可直接复用的组件

| 组件 | 复用方式 |
|------|---------|
| `PipelineContext` | 作为工作流上下文，跨节点共享状态 |
| `_sharedData` | 节点间数据传递的存储介质 |
| `Parameters` | 参数缓存（带脏标记，适合频繁读写场景） |
| `CreateSubContext()` | 每个节点创建子上下文，隔离节点状态 |
| `NodeExecutor.ExecuteNodeAsync` | 执行单个节点，支持超时/重试 |
| `NodeDefinition` | 节点定义模型，可直接扩展 |
| `WorkFlowMethodLog` | 工作流级别的日志追踪 |

### 4.2 需要补充的组件

```
工作流引擎（WorkflowEngine）
  ├─ 图加载器（GraphLoader）— 从配置加载节点和连线
  ├─ 节点调度器（NodeScheduler）— 决定下一个执行节点
  ├─ 转换评估器（TransitionEvaluator）— 评估条件转换
  ├─ 数据映射器（DataMapper）— 节点间输入输出映射
  └─ 生命周期管理器（LifecycleManager）— 启动/暂停/恢复/取消
```

### 4.3 建议的扩展路径

1. **第一阶段** — 顺序执行：支持节点列表按顺序执行，前一个节点的输出作为后一个节点的输入
2. **第二阶段** — 条件分支：支持根据条件选择不同的执行路径
3. **第三阶段** — 并行执行：支持多个节点同时执行
4. **第四阶段** — 完整生命周期：暂停/恢复/取消/历史记录

---

## 五、配置文件参考

### 5.1 当前单节点配置（MathFunctions.json）

```json
{
  "description": "内置数学函数测试",
  "form": {
    "title": "Built-in Math Functions Test",
    ...
  },
  "events": [
    {
      "control": "addButton",
      "event": "click",
      "function": "Add",
      "params": { "a": "...", "b": "..." }
    }
  ]
}
```

### 5.2 未来多节点配置示例

```json
{
  "description": "多节点工作流示例",
  "workflow": {
    "nodes": [
      { "id": "input", "function": "SetValue", "params": { "value": "${input}" } },
      { "id": "process", "function": "Add", "params": { "a": "${input.result}", "b": "10" } },
      { "id": "output", "function": "DisplayResult", "params": { "value": "${process.result}" } }
    ],
    "transitions": [
      { "from": "input", "to": "process", "condition": "always" },
      { "from": "process", "to": "output", "condition": "always" }
    ]
  }
}
```

---

## 六、相关文件索引

| 文件 | 说明 |
|------|------|
| `Operation/WorkFlowMethod/WorkflowMethodExecutionService.cs` | 执行入口服务 |
| `Operation/WorkFlowMethod/WorkflowExecution/Pipeline/WorkflowContext.cs` | 管道上下文 |
| `Operation/WorkFlowMethod/WorkflowExecution/Engine/NodeExecutor.cs` | 节点执行器 |
| `Operation/WorkFlowMethod/workflowmethod_log_messages.json` | 日志消息定义 |
| `Operation/WorkFlowMethod/Diagnostics/WorkFlowMethodLog.cs` | 日志工具类 |
| `Modules/Pipeline/Core.cs` | 父类 PipelineContext |
| `BasicMethod/FrameMethod/SkeletonFlow.cs` | 基类 SkeletonContext |
| `docs/analysis/卡死原因分析-MathFunctions_SetValue.md` | 卡死分析文档 |
