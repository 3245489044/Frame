# FrontDrivne 整理方案

> **架构演进关系**：FileDriven（原始）→ 提取通用框架到 Operation/BackEndDriven → FileDriven 引用 Operation
>
> **核心原则**：架构通用逻辑以 Share 层为主，Operation 层为通用架构标准，FileDriven 向 Operation 对齐

---

## 问题扫描

经过对当前代码的扫描，以下是整理优化后仍然存在的**抽象过度和重构不完全**的问题：

---

### 一、BackEndDriven 与 FileDriven 严重重复（P0）

**BackEndDriven** 完整复制了 FileDriven 的架构：

| 组件 | FileDriven | BackEndDriven |
|------|-----------|---------------|
| 执行门面 | `FileDrivenExecutor` | `FrameworkExecutor` |
| 模式分发 | 内联在 FileDrivenExecutor | `ModeDispatcher` |
| 执行引擎 | `ExecutionCenter/TaskExecutorPool` | `ExecutionEngine` |
| 管线入口 | `ExecutionPipelineEntry` | `BackEndExecutionPipelineEntry` |
| 管线运行器 | `FileDrivenPipelineRunner` | `BackEndPipelineRunner` |
| 脚本加载 | `ScriptLoadSkeleton` | `BackEndScriptLoadSkeleton` |
| 事件执行 | `EventExecutionService`(FileDriven) | `EventExecutionService`(BackEndDriven) |
| 生命周期 | `FrameworkInitializer`(FileDriven) | `FrameworkInitializer`(BackEndDriven) |
| 构建器 | 无 | `BackEndDrivenBuilder` |

**问题**：BackEndDriven 的 `EventExecutionService` 直接实现 `Share.IEventExecutionService`，而 FileDriven 也有自己的 `EventExecutionService`。两者功能完全一样——都是 `IFunctionRegistry.TryGetDelegate → DynamicInvoke`。**两个项目各维护一份相同逻辑。**

**方向**：FileDriven 中与 Operation/BackEndDriven 重复的后端组件应删除，统一使用 Operation 层。

---

### 二、FrontEndDriven 与 EventTriggerFramework 职责重叠（P1）

**FrontEndDriven**（`CsharpPrj/Operation/FrontEndDriven/`）和 **EventTriggerFramework**（`CsharpPrj/Operation/EventTriggerFramework/`）都提供：

- `IEventHub` / `IEventBus` — 事件总线
- `IUIEventBinder` / `IWinFormsEventBinder` — UI 事件绑定
- `IControlValueAccessor` — 控件值访问
- `IConfigLoader` — 配置加载

**问题**：FrontEndDriven 的 `WinForms/EventBinding/` 和 EventTriggerFramework 的 `WinForms/` 都实现了 WinForms 事件绑定。两者并存，但 FileDriven 实际使用的是 EventTriggerFramework 的版本。

**方向**：FrontEndDriven 应保留并作为前端驱动的通用层，FileDriven 中前端相关的逻辑整理到 FrontEndDriven。

---

### 三、Share/EventBindingOrchestrator 过度抽象（P2）

`Share/EventBindingOrchestrator/Abstractions/` 定义了 **11 个接口**：

```
ICommandExecutor, IComponentRegistry, IConfigLoader, IEventBindingContext,
IEventBindingService, IEventExecutionService, IEventNameResolver,
IRenderPipeline, IStageCoordinator, IWinFormsEventBinder, PipelineHelper
```

其中：
- `IComponentRegistry` — 已被 `AppServiceProvider` 替代，但接口未删除
- `IEventBindingContext` — 仅被 `ContainerBinderContext` 实现
- `IStageCoordinator` — 仅被 `StringStageCoordinator` 实现

**问题**：11 个接口中至少 3 个是"为了抽象而抽象"，只有一个实现类，没有多态需求。

**方向**：合并单实现接口，删除接口直接使用实现类。

---

### 四、ComponentRegistry 迁移不完全（P1）

`AppServiceProvider.cs` 第 20 行注释写着：
```
// 下一步：删除 ComponentRegistry.cs 文件
```

但 `AppServiceProvider.ValidateAll()` 第 150 行仍然委托给 `ComponentRegistry.Instance.ValidateAll()`，说明 `ComponentRegistry.cs` 文件还存在。

**问题**：新旧两套服务定位器并存，`ComponentRegistry` 未被彻底删除。

**方向**：将验证逻辑内联到 AppServiceProvider，删除 ComponentRegistry.cs。

---

### 五、EventTriggerFrameworkInitializer 的 CommandFrameworkHost 重复创建（P2）

`EventTriggerFrameworkInitializer.Initialize()` 第 131 行创建了 `CommandFrameworkHost`，但 `FrameworkInitializer`（FileDriven）在初始化时也创建了 `FunctionStorageLifecycle` 和 `FileDrivenLifecycle`，两者各自维护自己的 `FunctionRegistry` 实例。

**问题**：EventTriggerFramework 和 FileDriven 各自初始化自己的函数注册表，可能导致函数注册到不同的实例上。

---

### 六、BackEndDrivenBuilder 的 10 个可空委托（P3）

`BackEndDrivenBuilder` 定义了 10 个 `Action/Func` 委托属性，全部可为 null。`FrameworkExecutor.Run()` 中每个步骤都要 `?.Invoke()` 判空。

**问题**：这本质上是手写了一个简陋的 DI 容器 + Template Method 模式。如果使用 `IServiceProvider` + 接口注入，这些委托完全可以消除。

---

### 七、LifecycleManager + ILifecycleStep + DelegateLifecycleStep 过度设计（P2）

`BackEndDriven/Lifecycle/` 包含：
- `ILifecycleStep` 接口
- `DelegateLifecycleStep` 实现类
- `LifecycleManager` 管理器
- `FrameworkInitializer` 包装器

**问题**：实际只用了 `DelegateLifecycleStep`（一个委托包装类），`ILifecycleStep` 接口和 `LifecycleManager` 的 3 阶段（Init/Cleanup/Running）管理从未被多态使用。一个 `List<Action>` 就能解决的问题，用了 4 个类 + 1 个接口。

---

## 清理方案（6 阶段）

### 阶段一：彻底删除 ComponentRegistry（P1）

**目标**：`ComponentRegistry.cs` 已被 `AppServiceProvider` 替代，彻底删除。

**步骤**：
1. 将 `ComponentRegistry.ValidateAll()` 的验证逻辑内联到 `AppServiceProvider`
2. 将 `CriticalityLevel` / `ComponentRegistration` / `HealthCheckReport` 类型迁移到独立文件
3. 删除 `ComponentRegistry.cs`
4. 确认 `FrameworkHealthCheck.cs` 无引用

**涉及文件**：
- `Share/FrameworkAbstractions/ComponentRegistry/ComponentRegistry.cs` → **删除**
- `Share/FrameworkAbstractions/ComponentRegistry/AppServiceProvider.cs` → **修改 ValidateAll()**

---

### 阶段二：简化 BackEndDriven/Lifecycle 过度设计（P2）

**目标**：`ILifecycleStep` + `DelegateLifecycleStep` + `LifecycleManager` + `FrameworkInitializer` = 4 文件，简化为 `List<Action>`。

**步骤**：
1. 删除 `ILifecycleStep.cs`（接口只有一个实现）
2. 删除 `DelegateLifecycleStep.cs`（委托包装，直接用 `List<Action>`）
3. 简化 `LifecycleManager.cs`：`ConcurrentDictionary<LifecyclePhase, List<ILifecycleStep>>` → `List<Action>`
4. 简化 `FrameworkInitializer.cs`：移除对 ILifecycleStep 的依赖

**涉及文件**：
- `Operation/BackEndDriven/Lifecycle/ILifecycleStep.cs` → **删除**
- `Operation/BackEndDriven/Lifecycle/DelegateLifecycleStep.cs` → **删除**
- `Operation/BackEndDriven/Lifecycle/LifecycleManager.cs` → **重写**
- `Operation/BackEndDriven/Lifecycle/FrameworkInitializer.cs` → **重写**

---

### 阶段三：消除 BackEndDrivenBuilder 手写委托（P3）

**目标**：10 个 `Action/Func` 可空委托改为 `IServiceProvider` + 接口注入。

**步骤**：
1. `FrameworkExecutor` 的 10 个委托属性改为从 `IServiceProvider` 解析
2. 定义执行步骤接口（如 `IArgumentParser`、`ILoggingSetup` 等）
3. `BackEndDrivenBuilder` 改为 `IServiceCollection.AddBackEndDriven()` 扩展方法
4. 保留 Builder 作为兼容性外观，内部委托给 IServiceProvider

**涉及文件**：
- `Operation/BackEndDriven/BackEndDrivenBuilder.cs` → **重写**
- `Operation/BackEndDriven/Execution/FrameworkExecutor.cs` → **重写**

---

### 阶段四：FileDriven 前端逻辑整理到 FrontEndDriven（P1）

**目标**：FrontEndDriven 作为前端驱动的通用层，FileDriven 中前端相关的逻辑迁移过去。

**步骤**：
1. **FileDrivenWdUIBridge** → 已部分委托给 `WdUIContainerManager`，剩余逻辑（`FindAndBindContainers`、`FindConfigSelectorControls`、`ClearSideContainer`、`InjectEventBinder`）迁移到 FrontEndDriven
2. **UICommandManager** → 控件查找/值读写逻辑迁移到 FrontEndDriven
3. **FileDriven.UI/WinForms/Abstractions/** → 合并到 FrontEndDriven.WinForms.Abstractions
4. **FileDriven.UI/WinForms/Services/** → 前端服务逻辑迁移
5. **FileDriven.Execution/EventBinding/** → 事件绑定相关逻辑迁移到 FrontEndDriven 或 EventTriggerFramework

**涉及文件**：
- `FileDriven/FileDriven.UI/WinForms/Services/FileDrivenWdUIBridge.cs` → **迁移到 FrontEndDriven**
- `FileDriven/FileDriven.UI/WinForms/CommandLayer.cs` → **迁移 UICommandManager**
- `FileDriven/FileDriven.UI/WinForms/UIControlAbstractions.cs` → **合并到 FrontEndDriven**
- `FileDriven/FileDriven.Execution/EventBinding/` → **迁移到 FrontEndDriven/EventTriggerFramework**

---

### 阶段五：合并 EventBindingOrchestrator 单实现接口（P2）

**目标**：11 个接口中至少 3 个只有一个实现类，删除接口直接使用实现类。

**步骤**：
1. 删除 `IComponentRegistry.cs`（已被 AppServiceProvider 替代）
2. 合并 `IEventBindingContext` → `ContainerBinderContext`（删除接口）
3. 合并 `IStageCoordinator` → `StringStageCoordinator`（删除接口）
4. 保留 `IEventExecutionService` / `IEventBindingService` / `IWinFormsEventBinder`（多实现）

**涉及文件**：
- `Share/EventBindingOrchestrator/Abstractions/IComponentRegistry.cs` → **删除**
- `Share/EventBindingOrchestrator/Abstractions/IEventBindingContext.cs` → **删除**
- `Share/EventBindingOrchestrator/Abstractions/IStageCoordinator.cs` → **删除**

---

### 阶段六：FileDriven 后端重复组件向 Operation 对齐（P0）

**目标**：FileDriven 中与 Operation/BackEndDriven 重复的后端组件删除，统一使用 Operation 层。

**步骤**：
1. **FileDriven.Execution/Lifecycle/FrameworkInitializer.cs** → 委托给 BackEndDriven ✅ 已完成（2026-07-02）
2. **FileDriven.Execution/Lifecycle/FrameworkShutdown.cs** → 委托给 BackEndDriven ✅ 已完成（2026-07-02）
3. **FileDriven.Execution/Pipeline/** → 与 BackEndDriven/Pipeline/ 对比合并
4. **FileDriven.Execution/ExecutionHelper.cs** → 确认是否与 BackEndDriven 重复

**涉及文件**：
- `FileDriven/FileDriven.Execution/Lifecycle/FrameworkInitializer.cs` → **委托给 BackEndDriven** ✅ 已完成
- `FileDriven/FileDriven.Execution/Lifecycle/FrameworkShutdown.cs` → **委托给 BackEndDriven** ✅ 已完成
- `FileDriven/FileDriven.Execution/Pipeline/` → **与 BackEndDriven 合并**

**2026-07-02 完成内容**：
- `FrameworkInitializer.Initialize()` 内部使用 `BackEndDriven.Lifecycle.LifecycleManager` 编排 8 个初始化步骤
- `FrameworkShutdown.Shutdown()` 内部使用 `BackEndDriven.Lifecycle.LifecycleManager` 编排 5 个关闭步骤
- 不改变公开 API 签名，调用方无需修改
- 消除 FileDriven 中手写初始化/关闭顺序与 BackEndDriven.LifecycleManager 的逻辑重叠


---

## 执行顺序

| 顺序 | 阶段 | 风险 | 工作量 | 说明 |
|------|------|------|--------|------|
| 1 | 阶段一 | 低 | 小 | 删除 ComponentRegistry，纯清理 |
| 2 | 阶段二 | 低 | 小 | 简化 Lifecycle，纯清理 |
| 3 | 阶段五 | 中 | 中 | 合并接口，需更新引用方 |
| 4 | 阶段三 | 中 | 中 | Builder 改造，需确认调用方 |
| 5 | 阶段四 | 中 | 中 | 前端逻辑整理到 FrontEndDriven |
| 6 | 阶段六 | 高 | 大 | FileDriven 后端组件对齐到 Operation |

## 优先级总表

| 优先级 | 问题 | 阶段 | 建议 |
|--------|------|------|------|
| **P0** | BackEndDriven 与 FileDriven 重复 | 阶段六 | FileDriven 后端组件对齐到 Operation |
| **P1** | ComponentRegistry 迁移不完全 | 阶段一 | 彻底删除 ComponentRegistry.cs |
| **P1** | FrontEndDriven 与 EventTriggerFramework 重叠 | 阶段四 | FileDriven 前端逻辑整理到 FrontEndDriven |
| **P2** | EventBindingOrchestrator 过度抽象 | 阶段五 | 合并单实现接口 |
| **P2** | LifecycleManager 过度设计 | 阶段二 | 简化为 List\<Action\> |
| **P2** | EventTriggerFrameworkInitializer 重复创建 | 阶段六 | 统一函数注册表实例 |
| **P3** | BackEndDrivenBuilder 手写委托 | 阶段三 | 改用 IServiceProvider |
