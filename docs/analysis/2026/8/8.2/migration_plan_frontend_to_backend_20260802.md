# FrontEndDriven → BackEndDriven 迁移实施计划（2026-08-02）

> **文档日期**：2026-08-02
> **分析源**：`migration_analysis_20260802`（FrontEndDriven 函数逻辑职责分析）
> **适用范围**：`CsharpPrj/Operation/FrontEndDriven` → `CsharpPrj/Operation/BackEndDriven`
> **目标**：将职责上属于后端执行层的逻辑从 FrontEndDriven（UI 驱动框架）迁移至 BackEndDriven（后端执行框架），保持单向依赖（FrontEndDriven → BackEndDriven），消除职责重叠。
> **状态图例**：⬜ = 未开始，🔄 = 部分完成/进行中，✅ = 已完成
>
> **★ 2026-08-02 实施记录**：**阶段 2（函数调用门面迁移）已完成** ✅
> - 迁移 `FunctionCallFacade` / `FunctionCallRequest` / `FunctionCallResult` → `BackEndDriven/Gate/`
> - 删除 FrontEndDriven 原 `Gate/` 三文件
> - 更新调用方：`EventPlatform.cs`、`CommandSystems.cs` ExecSystem、`FileDriven`（UIToolbox/CommandExecutionService/EventBindingInitializer）→ `BackEndDriven.Gate`
> - 日志切换：`FrontEndDrivenLog` → `BackEndLog`
> - 修复 CS0419 cref 警告（`Task.Run` 歧义 → `System.Threading.Tasks.Task`）
> - 验证：BackEndDriven.csproj + FrontEndDriven.csproj + MainProj.sln + **FileDriven.sln** 构建均 **0 警告 0 错误**
>
> **★ 2026-08-02 实施记录**：**阶段 1（命令执行层迁移）已完成** ✅（用户确认方案 B'：保留 ECS 逻辑直接迁移 + 接口放共享层）
> - 共享层新增：`FrameworkAbstractions/WinFormsAbstractions/IControlFinder.cs` + `IUiThreadDispatcher.cs`
> - FrontEndDriven 实现：`ControlFinderAdapter.cs` + `UiThreadDispatcherAdapter.cs`（注册于 ControlServiceCollection）
> - BackEndDriven 新增 `CommandExecution/` 目录（7 文件）：
>   - `CommandComponents.cs`（命令状态机/组件）· `CommandCoordinator.cs`（协调器+基类）· `CommandSystems.cs`（10 纯逻辑命令）
>   - `UiCommandSystems.cs`（Discover/Assert/Validate 经 IControlFinder）· `ClickSetGetSystems.cs`（Click/Set/Get 经接口）
>   - `CommandInputPort.cs`（端口）· `EntityManagerExtensions.cs`（AddOrSet 等价内联）
> - 删除 FrontEndDriven 原 4 命令文件（CommandSystems/ClickSetGetSystems/CommandComponents/CommandInputPort）
> - **LayoutInspectSystem 暂留 FrontEndDriven**（UI 专属，依赖 ECS 渲染组件），经 `CommandSystemCoordinator.RegisterSystem` 扩展点注册（FileDrivenPipelineRunner）
> - 修复：PathMananger 循环依赖（改内部自包含）、IControlFinder 命名歧义、IControlValueAccessor 无 TrySetComboBoxValue（改 SetValue）
> - 验证：BackEndDriven.csproj + FrontEndDriven.csproj + FileDriven.sln + MainProj.sln **四链路均 0 警告 0 错误**

---

## 一、迁移范围判定

### 1.1 应迁移（职责属于后端执行层）

| 类别 | 源文件 | 迁移目标 | 理由 |
|------|--------|---------|------|
| 命令系统实体 | `FrontEndDriven/CommandExecution/ECS/CommandSystems.cs` | `BackEndDriven/CommandExecution/` | 命令解析→执行→结果收集属后端框架职责，与 BackEndDriven `ExecuteCommandStage` 重叠 |
| 点击/设置/获取系统 | `FrontEndDriven/CommandExecution/ECS/ClickSetGetSystems.cs` | `BackEndDriven/CommandExecution/` | 同上（UI 依赖经接口化） |
| 命令组件 | `FrontEndDriven/CommandExecution/ECS/CommandComponents.cs` | `BackEndDriven/CommandExecution/` | 命令状态机（Pending/Executing/Success/Failed/ParseError）是执行框架语义 |
| 命令输入端口 | `FrontEndDriven/CommandExecution/Ports/CommandInputPort.cs` | `BackEndDriven/Execution/` | 命令聚拢入口属后端框架；BackEndDriven `ExecutionEngine` 已有同级能力 |
| 函数调用门面 | `FrontEndDriven/Gate/FunctionCallFacade.cs` | `BackEndDriven/Gate/` | 统一调度 Sync/Background 调用 `IEventExecutionService`，属后端调度层 |
| 调用请求模型 | `FrontEndDriven/Gate/FunctionCallRequest.cs` | `BackEndDriven/Gate/` | 门面输入模型随门面迁移 |
| 调用结果模型 | `FrontEndDriven/Gate/FunctionCallResult.cs` | `BackEndDriven/Gate/` | 门面输出模型随门面迁移 |

### 1.2 不应迁移（保留在 FrontEndDriven — UI 专属）

| 类别 | 文件 | 理由 |
|------|------|------|
| WinForms 控件操作 | `WinForms/` 目录全部 | 纯 UI 层，依赖 WinForms API |
| ECS 渲染管线 | `Ports.cs` / `RenderPipelineComposition` / `EcsGate`（ECS 部分） | UI 渲染管线（Entity→控件映射）属前端 |
| 事件绑定 | `ControlInteraction/EventBinding/` / `Validation/Validators/EventBindingValidator.cs` | UI 事件交互属前端 |
| 配置渲染 | `Initial/RenderPipeline/` / `Shared/ContractBuilder.cs` | UI 配置→控件渲染管线属前端 |
| **布局检查系统** | `CommandExecution/ECS/LayoutInspectSystem.cs` | ⭐ 强依赖 WinForms `Application.OpenForms` + ECS 组件（`ControlInfoComponent`/`ParentEntityComponent`/`EntityManager`），**暂留 FrontEndDriven**（UI 专属命令） |
| 生命周期管理 | `Lifecycle/ConfigurationLifecycleManager.cs` | 绑定 UI 装配生命周期 |

---

## 二、前置约束（已核实）

| 约束 | 现状 | 迁移影响 |
|------|------|---------|
| 依赖方向 | **FrontEndDriven → BackEndDriven**（FrontEndDriven.csproj 已引用 BackEndDriven.csproj） | 迁移后保持单向依赖；BackEndDriven **不得**引用 FrontEndDriven |
| `ControlFinder` | `FrontEndDriven.WinForms.Finder.ControlFinder`（静态类，WinForms 强相关） | **需抽象为接口**供 BackEndDriven 命令系统调用 |
| `WinFormsControlValueWriter` | `FrontEndDriven.WinForms.ValueSync`（实现 `TConfig.Controls.IControlValueAccessor`） | 接口已在共享层（TConfig），**无需抽象**，命令系统改经接口 |
| `AppServiceProvider` | `Share/FrameworkAbstractions/ComponentRegistry/AppServiceProvider.cs`（共享层） | 两端均可用，无迁移障碍 |
| `ExecutionLog` | `FrontEndDriven/CommandExecution/Diagnostics/ExecutionLog.cs` | 迁移到 BackEndDriven 时改用 `BackEndDriven.Diagnostics.BackEndLog` |
| `IEventExecutionService` | `TConfig`（共享层，由 `WorkflowMethodExecutionService` 实现） | 门面调用点不变，仅门面实体移位 |
| `EntityManager`/ECS | `Modules/ECS`（依赖） | 命令系统依赖 ECS 组件类型；BackEndDriven 需确认 ECS 依赖可用性 |

---

## 三、分阶段迁移步骤

### 阶段 0：前置依赖抽象（无迁移，消除循环引用障碍）

| 步骤 | 动作 | 文件 |
|------|------|------|
| 0.1 | 新建 `IControlFinder` 接口（`FindByName<T>` / `FindAcrossForms` / `FindRecursive`） | `FrontEndDriven/WinForms/Abstractions/IControlFinder.cs` |
| 0.2 | `ControlFinder` 实现 `IControlFinder` | `FrontEndDriven/WinForms/Finder/ControlFinder.cs` |
| 0.3 | 确认 `TConfig.Controls.IControlValueAccessor` 已覆盖 ComboBox 读写（`TrySetComboBoxValue`），若不覆盖则扩展接口 | `TConfig` |
| 0.4 | 将命令系统对静态 `ControlFinder` / `WinFormsControlValueWriter` 的直接调用改为经 `AppServiceProvider.Resolve<IControlFinder>()` / `Resolve<IControlValueAccessor>()` | 命令 System 各文件 |

**验证**：`dotnet build FrontEndDriven.csproj` 0 错误。

---

### 阶段 1：命令执行层迁移（BackEndDriven/CommandExecution/）✅ 已完成

| 步骤 | 动作 | 源 → 目标 |
|------|------|----------|
| 1.1 | 新建 `BackEndDriven/CommandExecution/` 目录并迁移命令实体 | `CommandSystems.cs` + `ClickSetGetSystems.cs` → `BackEndDriven/CommandExecution/` |
| 1.2 | 迁移命令协调器与接口 | `ICommandSystem` / `CommandSystemBase` / `CommandSystemCoordinator` → `BackEndDriven/CommandExecution/` |
| 1.3 | 迁移命令组件 | `CommandComponents.cs`（`CommandMetaComponent`/`CommandStatusComponent`/`CommandState`/`ExecutionResultComponent`）→ `BackEndDriven/CommandExecution/` |
| 1.4 | 迁移命令输入端口 | `CommandInputPort.cs` → `BackEndDriven/Execution/CommandInputPort.cs` |
| 1.5 | UI 依赖接口化改写 | Click/Set/Get 系统 → `IControlFinder` / `IControlValueAccessor` |
| 1.6 | 日志切换 | `ExecutionLog` → `BackEndLog`（保留执行语义） |
| 1.7 | **暂留 FrontEndDriven** | `LayoutInspectSystem.cs`（UI 专属，依赖 WinForms + ECS）——注册时排除 |

**注意**：
- `ExecSystem`（exec 命令执行函数）已通过 `FunctionCallFacade` 调用 `IEventExecutionService`——迁移后 `ExecSystem` 调用 `BackEndDriven.Gate.FunctionCallFacade`。
- 命令系统的 ECS 依赖（`EntityManager`/`Entity`/组件）需确认 BackEndDriven 项目已引用 `Modules/ECS`（或考虑命令系统去 ECS 化，改用纯 POCO 结果——**推荐，与 BackEndDriven `ExecuteCommandStage` 对齐**）。

**关键设计决策（命令系统 ECS 去留）**：
- **方案 A（去 ECS 化，推荐）**：命令系统改为纯 C# 服务（`ICommandSystem.Execute(CommandContext)` 返回结果对象），与 BackEndDriven `ExecuteCommandStage` 的 `CommandContext` 模型对齐，消除 ECS 依赖。
- **方案 B（保留 ECS）**：BackEndDriven 须添加 ECS 项目引用，命令系统保持 Entity/Component 风格。

> 建议方案 A——BackEndDriven 已有 `CommandContext`（`CommandFramework` 共享层），命令系统迁移时应与 `ExecuteCommandStage` 的 set/click/非事件路由统一。

---

### 阶段 2：函数调用门面迁移（BackEndDriven/Gate/）✅ 已完成

| 步骤 | 动作 | 源 → 目标 |
|------|------|----------|
| 2.1 | 迁移门面实体 | `FunctionCallFacade.cs` → `BackEndDriven/Gate/FunctionCallFacade.cs` |
| 2.2 | 迁移请求/结果模型 | `FunctionCallRequest.cs` / `FunctionCallResult.cs` → `BackEndDriven/Gate/` |
| 2.3 | 门面日志切换 | `FrontEndDrivenLog` → `BackEndLog` |
| 2.4 | FrontEndDriven 侧删除原门面 | 删除 `Gate/FunctionCallFacade.cs` + `Gate/FunctionCallRequest.cs` + `Gate/FunctionCallResult.cs` |
| 2.5 | 调用方改命名空间 | `FrontEndDriven` 内调用门面的文件（`ExecSystem`/`EventBindingSystem`/`WinFormsOutputPort` 等）改 `using BackEndDriven.Gate;` |

**验证**：`dotnet build` 三步（BackEndDriven → FrontEndDriven → MainProj.sln）0 错误。

---

### 阶段 3：命令端口与 ExecutionEngine 收敛（可选，后续批次）

| 步骤 | 动作 |
|------|------|
| 3.1 | `CommandInputPort` 迁入 `BackEndDriven.Execution/` 后，`ExecutionEngine.ExecuteCommand` 委托给新端口 |
| 3.2 | `FileDriven.Execution.CommandExecutionSkeleton.ExecuteCommands` 的调用改指向 BackEndDriven 端口 |
| 3.3 | 统一 `ICommandEntry` / `ICommandPipeline` / `ICommandRunner` 双轨接口（refactoring_plan_l2.md AD-3） |

---

### 阶段 4：清理与终验

| 步骤 | 动作 |
|------|------|
| 4.1 | FrontEndDriven 删除迁移后残留：`CommandExecution/ECS/CommandSystems.cs` + `ClickSetGetSystems.cs` + `CommandInputPort.cs` + `Gate/` 三文件 |
| 4.2 | 检查 `FrontEndDriven.csproj` 中仅因命令系统存在的项目引用（如 `Pipeline`/`Container`/`ExecutionFramework`），视需要移除 |
| 4.3 | 构建验证：`BackEndDriven.csproj` → `FrontEndDriven.csproj` → `MainProj.sln` → `FileDriven.sln` 全部 0 错误 0 警告 |
| 4.4 | grep 复核被删除符号无残留引用 |

---

## 四、依赖反转原则（符合 `.clinerules/axioms.md` L1 层级约束）

```
迁移前（职责重叠）:
  FrontEndDriven ──命令执行──▶ (命令系统内嵌)
  FrontEndDriven ──门面──▶ IEventExecutionService

迁移后（单向依赖，接口反转）:
  FrontEndDriven（UI 层）
      │  调用
      ▼
  BackEndDriven（执行框架）
      │  依赖接口（IControlFinder / IControlValueAccessor）
      ▼
  FrontEndDriven 侧实现（WinForms）
```

- **BackEndDriven 不依赖 FrontEndDriven**（保持编译级单向依赖）。
- UI 相关操作（click/set/get 控件）通过 `IControlFinder` / `IControlValueAccessor` 接口由 **FrontEndDriven 侧实现注入**。
- 命令系统实体（后端职责）移入 BackEndDriven，FrontEndDriven 仅保留对命令入口的**调用**。
- **L1 层级约束**：命令执行架构属 L1，迁移需元级外部见证授权（见 `.clinerules/axioms.md` `AxiomHierarchyStructure.invariants(L1)`）。

---

## 五、风险清单

| 风险 | 等级 | 缓解 |
|------|------|------|
| 命令系统 ECS 依赖在 BackEndDriven 不可用 | 高 | 方案 A：命令系统去 ECS 化，与 `ExecuteCommandStage`/`CommandContext` 对齐 |
| `ControlFinder` 接口化需改全部调用方 | 中 | 阶段 0 单独 commit，构建验证通过后再继续 |
| 门面命名空间变更波及 FrontEndDriven 内部调用方 | 中 | 统一 `using BackEndDriven.Gate;`，编译器报错指引遗漏点 |
| `FileDriven` 外部对 `CommandInputPort`/`ExecutionLog` 的引用 | 中 | 迁移前 grep 确认消费方；`ExecutionLog` 若被外部依赖则保留别名转发 |
| BackEndDriven 依赖 `Modules/ECS` 的传递依赖 | 中 | 方案 A 消除；方案 B 则添加项目引用 |
| `LayoutInspectSystem` 的 ECS 父解析器依赖命令系统基类 | 低 | 基类（`CommandSystemBase`）迁入 BackEndDriven 后，LayoutInspectSystem 需改用基类（保持通过继承）或独立实现 |

---

## 六、验证清单

- [ ] `dotnet build CsharpPrj/Operation/BackEndDriven/BackEndDriven.csproj` 0 错误
- [ ] `dotnet build CsharpPrj/Operation/FrontEndDriven/FrontEndDriven.csproj` 0 错误
- [ ] `dotnet build CsharpPrj/MainProj.sln` 0 错误
- [ ] `dotnet build FileDriven/FileDriven.sln` 0 错误
- [ ] grep 复核：`FunctionCallFacade` / `FunctionCallRequest` / `FunctionCallResult` / `CommandInputPort` / `CommandSystemCoordinator` 无 FrontEndDriven 残留引用
- [ ] UI 冒烟：加载配置 + `click`/`set`/`get`/`exec`/`layout` 命令行为正常