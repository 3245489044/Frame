# 重构方案：基于 InternalStorage 的作用域隔离

> 状态：实施中（S3 + 缺陷2 完成） | 关联缺陷：`docs/analysis/FrontEndDriven_RenderPipeline_MultiScope_Defect.md`

## 目标
将作用域（MainLayout/Content/Side）从「实体标签」提升为「存储基础设施分区」，根治 5 个缺陷。

## 阶段划分

| 阶段 | 内容 | 状态 |
|------|------|------|
| S1 | ControlPool 复用 `StorageBidirectionalExtensions`，消除内嵌双向映射重写 | 待实施（纯重构，无行为变化） |
| S2 | ControlPool 引入 scope→Cell 分区，`Clear`→`UnregisterScope(scope)` | 待实施（影响 Register 签名，需评估 62 处调用） |
| S3 | 收集器 `CollectSnapshot(scope)` + RenderContext 按 scope 隔离 | ✅ 已完成（2026-08-03） |
| S4 | 生命周期所有权重构（容器不 Dispose 共享 composition） | 待实施 |

## 已实施内容（编译验证通过）

### S3：收集器作用域感知

| 文件 | 改动 |
|------|------|
| `Ports.cs` | `CollectSnapshot(string? scope=null)` 按 `ScopeComponent` 过滤；`EnumerateControls(scope)` 作用域感知枚举（无参=全量兼容） |
| `RenderPipelineStages.cs` | `RenderCollectStage` 传 `ctx.Scope` 给 `CollectSnapshot`，消除「Side 渲染却收集 MainLayout 全量」 |
| `RenderStageRunner.cs` | `ExecuteRenderStage(string? scope=null)`：显式 scope 时局部渲染，无参保持全量语义 |
| `RenderPipelineComposition.cs` | `ExecuteRenderStage(scope)` 透传 |
| `EcsGate.cs` | `DoLoadAndRender` 重渲染传 `op.Scope`（Side 加载后仅重渲染 Side） |

### 缺陷2：函数执行重渲染 scope 化

| 文件 | 改动 |
|------|------|
| `EventPlatform.cs` | `FunctionExecutedPayload` 携带 `Scope`；`PlatformEvent.FunctionDone(scope)` 支持作用域；`InvokeFunctionAsync/InvokeFunction` 加 `scope` 参数（默认空=全量兼容） |
| `IEventPlatform.cs` | 接口签名同步（加 `scope=""` 可选参数） |
| `FunctionExecutedRenderBridge.cs` | `OnFunctionExecutedAsync` 读事件 `Scope`，非空时 `ExecuteRenderStage(scope)` 局部重渲染，空保持全量 |

## S3 有效性论证
- `ConfigDrivenControlFactory.AddPendingComponents` 对**每个实体**打 `ScopeComponent.Scope = _currentScope`（已验证）→ 按 scope 过滤不遗漏
- Side 局部渲染时父骨架（sideContainer）不在快照，但 `ApplyRelations` 经 `ControlPool.GetControl(parentEntity)` 全局查找（已验证）→ 正确挂载，不重建静态骨架
- 无参 `ExecuteRenderStage()` 保持全量语义 → 兼容 `FunctionExecutedRenderBridge`/`WinFormsRenderBridge` 等既有调用

## 缺陷2 修复有效性论证
- `EventPlatform.FunctionDone` 携带事件触发作用域（默认空=向后兼容）
- `FunctionExecutedRenderBridge` 按事件 scope 限定重渲染——Side 作用域的函数执行（如 LoadConfigToTarget）触发重渲染时仅重渲染 Side，不再全量重放 MainLayout（内容区不再被误清）
- 通用调用（LifecycleHook/CommandInputPort）无 scope 上下文，保持空=全量语义，不回归

## 关键技术决策
- `ControlPool.Register/Unregister` 双映射底层改用 `StorageBidirectionalExtensions`（`MapForward/Unmap`），存储格式一致（`StorageLayer.Session`）
- scope 隔离基于 `StorageEngine.CreateSession(scope)` 的子单元 Parent 链
- 内存字典 `_nameToControl`/`_entityToName` 保留（GoControl 快速查询），仅底层存储操作复用扩展