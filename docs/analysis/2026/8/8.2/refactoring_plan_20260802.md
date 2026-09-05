# FrontEndDriven 重构计划（2026-08-02）— 基于 2026-08-02 分析报告

> **文档日期**：2026-08-02
> **分析源**：`analysis_report_20260802.md`（重复/冗余/错误逻辑全量分析）
> **适用范围**：`CsharpPrj/Operation/FrontEndDriven`
> **目标**：按批次收敛重复实现、移除死代码/死链、修复自相矛盾的逻辑，最终验证 `dotnet build` 0 错误
> **状态图例**：⬜ = 未开始，🔄 = 部分完成/进行中，✅ = 已完成
> **★ 2026-08-02 实施记录**：批次 0/1/2/3/4(Step A)/6 已完成；批次 7 已通过 MainProj.sln + FileDriven.sln 双链路构建（0 错误 0 警告）；批次 5（跨项目接口拆分）与批次 7 R-2（装配确认）留待后续决策。
>
> **★ 2026-08-02 追加实施（用户决策）**：
> - ✅ **备用链路整体删除（批次 4 Step B + R-3 彻底化）**：删除 `EnqueueFunctionOperation` / `ExecuteFunctionOperation` / `FunctionExecutionResult` / `ExecutionResultReadyEvent` / `SubmitToMatrixOperation` / `StartExecutionLoopOperation` / `FunctionExecutionSystem.cs` / `ExecutionLoopManager.cs` / Matrix "ExecuteFunction" Handler 注册；同时删除继承的 `EcsGate.ExecuteFunctionAndWaitAsync` / `DoStartLoop` / `_matrixEngine`（构造签名简化）。命令执行改由 `CommandExecutionService` 经 `FunctionCallFacade.ExecuteSyncRequest`（Source=ExecCommand → Sync）直达；`MainForm` 移除 `StartExecutionLoopOperation` 调用；`EventBindingInitializer`/`UIToolbox` 已是门面路径（仅注释残留）。验证：FrontEndDriven.csproj / FileDriven.sln / MainProj.sln 三步构建 0 错误 0 警告。
> - ✅ **D-3 合并（批次 5 跨项目接口拆分）**：以 `TConfig.IEventBindingService` 为全仓唯一基准，删除 `UniversalMiddleware.Abstractions.IEventBindingService`（全仓无实现/无引用死接口），`EventBindingOrchestrator` 继续实现 TConfig 基准；在 TConfig 接口头部补充唯一基准声明。

---

## 总览

| 批次 | 内容 | 对应分析项 | 风险 | 依赖 |
|------|------|-----------|------|------|
| 批次 0 | 前置验证与构建基线 | — | 低 | 无 |
| 批次 1 | 死代码 / 死链清理（低风险） | E-1, E-2, R-1, R-4, R-6 | 低 | 批次 0 |
| 批次 2 | UI 线程调度收敛 | D-1, D-4, E-3 | 中 | 批次 1 |
| 批次 3 | FunctionCallFacade 双路径合并 | D-2, R-5 | 低 | 批次 1 |
| 批次 4 | 废弃 ECS 操作链路收敛 | R-3 | 中 | 批次 1 |
| 批次 5 | 事件绑定接口拆分 + 命名空间清理 | D-3 | 高（跨项目） | 批次 2 |
| 批次 6 | 运行时行为修复 | E-4, E-5 | 中 | 批次 4 |
| 批次 7 | 装配确认与终验 | R-2 | 低 | 全部 |

> 实施顺序建议：批次 1 → 批次 3 → 批次 2 → 批次 4 → 批次 6 → 批次 7。批次 5（D-3 跨项目接口拆分）与批次 7（R-2 装配确认）需与用户确认范围后另行独立进行。
>
> **★ 实施完成情况（2026-08-02）**：
> - ✅ 批次 0：前置验证（Async 别名/字符串键/InvokeOnUiThread/命名空间全仓 grep）
> - ✅ 批次 1：E-1 死代码双写 / E-2 死链移除 / R-1 死代码 / R-4 转发链 / R-6 命名空间
> - ✅ 批次 2：D-1 收敛（删 Threading 套件 + 收敛 InvokeOnUiThread）/ D-4 统一 GetTargetForm / E-3 随删解决
> - ✅ 批次 3：D-2 ExecuteCore 合并双路径 / R-5 删 Async 别名
> - ✅ 批次 4 (Step A)：R-3 显式失败 + Obsolete 标注（Step B 删整链待确认）
> - ✅ 批次 6：E-4 DestroyScope 异步化 / E-5 审计边界声明
> - ✅ 批次 7：跨项目修复（MainFormGateFactory 改用 IUIDispatcher；JsonConfigBuilder 补 using）→ MainProj.sln + FileDriven.sln 构建均 0 错误 0 警告
> - ✅ 批次 5：D-3 跨项目接口拆分（2026-08-02 追加：以 TConfig 为基准，删除 UniversalMiddleware 死接口）
> - ✅ 批次 4 (Step B)：备用链路整体删除（2026-08-02 追加：删除 Entity→Idle→Matrix→Handler 整链）
> - ✅ 批次 7 R-2：ConfigurationLifecycleManager 装配确认（由 MainFormGateFactory 注册，**已接线**）
>
> **★ 实施中的补充发现（已修复）**：`Threading/` 套件实为 FileDriven 项目「MainFormGateFactory」的真实消费者（首轮 grep 限定 CsharpPrj 目录遗漏）；已将其切换到 `IUIDispatcher`/`WinFormsUIDispatcher`。`EventHelper` 命名空间归位波及 FileDriven.JsonRefer 的 `JsonConfigBuilder`，已补 `using FrontEndDriven.Shared;`。

---

## 批次 1：死代码 / 死链清理（低风险，先做）

### 1.1 E-1 — `EventBindingSystem` 构造函数 Fail-Fast 前段死代码

**文件**：`ControlInteraction/EventBinding/EventBindingSystem.cs`

**现状**（第 77-96 行）：catch 分支写 Warn 日志后立即被下方 `throw` 吞没；`Update()` 第 118-123 行防缺失分支同样不可达。

**方案**：采用「Fail-Fast + 保留 catch 内日志」的矛盾消除——改为 Fail-Fast 且删除冗余 catch 日志：

```csharp
// 构造函数
_orchestrator = AppServiceProvider.Resolve<Share.EventBindingOrchestrator.EventBindingOrchestrator>();
// 未注册时直接抛出（Fail-Fast，替代"仅日志告警 + 运行时静默跳过"）
// 保留异常信息，删除 catch + Warn + 二次 if null 检查
```

同时删除 `Update()` 中 `_orchestrator == null` 分支（字段已由构造函数保证非 null），并将 `_orchestrator` 字段类型由 `...?` 改为非空引用类型。

**验证**：构造失败场景（未注册 Orchestrator）→ 启动阶段立即抛异常；正常场景行为无变化。

---

### 1.2 E-2 — `UnifiedControlService` + `NullEventBindingService` 死链移除

**文件**：`Initial/Registration/ControlServiceCollection.cs`

**现状**：步骤 4 注册 `NullEventBindingService`（无消费方）；步骤 6 注册 `UnifiedControlService`（实现已 Obsolete 的 `MetaCommand.IControlService`，无任何 `Resolve` 消费方）。

**方案**：
1. 删除步骤 4（`IEventBindingService` 注册）与步骤 6（`IControlService` 注册）及其相关联的 `using`/局部变量。
2. 保留步骤 1/2/3/5/7 的注册（`IControlFinder` / `IControlValueAccessor` / `IControlRegistry` / `IWinFormsControlAdapter` / `IUIDispatcher`——均已确认有实际消费方）。
3. `WinForms/Services/UnifiedControlService.cs`、`ControlInteraction/EventBinding/Implementation/NullEventBindingService.cs`、`ControlInteraction/EventBinding/IEventBindingService.cs`（FrontEndDriven.Abstractions 版）移入 `_deprecated/` 或删除。
   - 注意：`IEventBindingService` 接口若 `UnifiedControlService` 删除后仍无引用，一并移入；若 `EventSubscriptionManager` 等需保留，则仅移两个实现文件。
4. 检查 `ControlServiceCollection.cs` 顶部 `using FrontEndDriven.Implementation;` 是否仅剩需保留引用。

**风险**：若后续有后端（BackEndDriven/PowerShell 侧）按字符串键消费 `"FrontEndDriven.IControlService"`，需 grep 全仓再确认。**前置动作（批次 0）必须全仓 grep**。

---

### 1.3 R-1 — `WinFormsEventAdapter` 死代码移除

**文件**：`WinForms/Adapter/WinFormsEventAdapter.cs`（91 行，零调用方）

**方案**：按项目废弃规范（`.clinerules/project.md` §1.3：`move_and_exclude`）移入 `CsharpPrj/Operation/FrontEndDriven/_deprecated/`，并确认无需 `<Compile Remove>` 配置（FrontEndDriven.csproj 已有 exclude 或直接编译，若目录结构变化则检查 csproj）。

---

### 1.4 R-4 — `ExecuteFireAndForget` 无意义转发链

**文件**：`Gate/FunctionCallFacade.cs`

**现状**：`ExecuteAsync` 中 `mode == FireAndForget` 分支调用 `[Obsolete]` 转发方法（仅转回 `ExecuteAsyncInternal`）。

**方案**：直接删除 `ExecuteFireAndForget` 方法，`ExecuteAsync` 将 `FireAndForget` 归一化为 Background 调用（`ExecuteAsyncInternal`），并加注释说明「无结果消费通道 → 降级 Background」。

---

### 1.5 R-6 — `Shared/EventHelper.cs` 命名空间错位

**文件**：`Shared/EventHelper.cs`（`namespace FileDriven.Abstractions;` → 应为 `namespace FrontEndDriven.Shared;`）

**消费方**（3 个）：
- `Validation/Validators/EventBindingValidator.cs`（`using FileDriven.Abstractions;`）
- `Shared/EventBindingFormatter.cs`（`using FileDriven.Abstractions;`）
- `Shared/ContractBuilder.cs`（`using FileDriven.Abstractions;`）

**方案**：
1. `EventHelper.cs` 命名空间改为 `namespace FrontEndDriven.Shared;`。
2. 三个消费方删除 `using FileDriven.Abstractions;`（若该 using 仅为 EventHelper 存在）；若文件中还有其他类来自 `FileDriven.Abstractions`，仅删除引用 EventHelper 的部分并改为 `using FrontEndDriven.Shared;`。
3. **风险**：`FileDriven.Abstractions` 可能还有同名的其他类（需 grep 确认），改名后若消费方缺少 using 会编译失败——批次 0 先 grep `FileDriven.Abstractions` 命名空间成员清单。

---

## 批次 3：FunctionCallFacade 双路径合并

### 3.1 D-2 — 提取 `ExecuteCore` 单一核心

**文件**：`Gate/FunctionCallFacade.cs`

**方案**：将 `ExecuteSync` / `ExecuteAsyncInternal` 合并为：

```csharp
private static async Task<FunctionCallResult> ExecuteCore(
    FunctionCallRequest request, Stopwatch sw, ExecutionMode mode, bool runInBackground)
{
    try
    {
        var executionService = ResolveExecutionService();
        if (executionService == null) { ... Failure(...); }

        var timeoutMs = request.TimeoutMs > 0 ? request.TimeoutMs : 30_000;

        Func<Task<ExecutionResult?>> executor = () => ExecuteWithAudit(request, mode,
            () => executionService.ExecuteFunctionAsync(request.FunctionName,
                request.Parameters ?? new Dictionary<string, object?>()));

        if (runInBackground)
        {
            var bgService = executionService; // 捕获避免闭包歧义
            executor = () => Task.Run(() => ExecuteWithAudit(request, mode,
                () => bgService.ExecuteFunctionAsync(request.FunctionName,
                    request.Parameters ?? new Dictionary<string, object?>())));
        }

        var result = await ExecuteWithTimeout(request, mode, timeoutMs, executor).ConfigureAwait(false);
        ...
    }
    catch (Exception ex) { ... }
}
```

`ExecuteSync` → `ExecuteCore(request, sw, Sync, false)`；`ExecuteAsyncInternal` → `ExecuteCore(request, sw, Background, true)`。

**验证**：`dotnet build` 0 错误；原有 `exec` 命令/按钮点击路径可用 `FunctionCallFacade` 的 Sync/Background 模式行为一致（Background 模式有 `Task.Run`，Sync 无）。

---

### 3.2 R-5 — `ExecutionMode.Async` 兼容别名残留

**文件**：`Gate/FunctionCallRequest.cs`、`Gate/FunctionCallFacade.cs`

**方案**：grep 全仓确认无调用方传 `ExecutionMode.Async`（批次 0 检查）。若确认零引用：
- 删除 `ExecutionMode.Async` 枚举成员。
- 删除 `ResolveMode` 中 `Async → Background` 映射分支。
- 更新枚举注释。

**风险**：若外部项目（BackEndDriven等）仍传 Async，编译会失败。故必须先全仓 grep。

---

## 批次 2：UI 线程调度收敛

### 2.1 D-1 — 收敛 4 套实现为 1 套

**方案**（分层，不做跨项目大重构）：

**Step A — 删除未接线的 `Threading/` 套件**：
- `Threading/IUiThreadDispatcher.cs`、`Threading/WinFormsUiThreadDispatcher.cs` 移入 `_deprecated/`（E-3 问题随之消失）。
- 检查 `Threading/` 目录是否仅这两个文件；`ConfigurationLifecycleManager`（R-2）若保留则改依赖 `IUIDispatcher`。

**Step B — 收敛 `WinFormsControlAdapter.InvokeOnUiThread`**：
- 方法体改为调用 `UiThreadHelper.ExecuteOnUiThread(control, action)`，消除 `c.Invoke` 同步阻塞风险。
- 若 `IWinFormsControlAdapter` 接口中该方法已无调用方（grep），则连同接口声明一起删除或标记 Obsolete。

**Step C — 明确 `UiThreadHelper` 为唯一真源**：
- 在 `UiThreadHelper` 类注释中声明「唯一 UI 线程调度实现」，后续新增调度一律使用此静态类或 `IUIDispatcher`。
- 保留 `IUIDispatcher`（已注册、WinFormsRenderBridge 使用）作为容器接口；`WinFormsUIDispatcher.Post/Send/InvokeAsync` 内部委托 `UiThreadHelper` 对应逻辑（若可行，避免两套 BeginInvoke/TCS 细节漂移）。

### 2.2 D-4 — 统一主窗体查找

**方案**：在 `UiThreadHelper` 新增 `public static Form? GetTargetForm()`（可见→非可见两级，与 `EventBindingSystem.GetTargetForm` 一致），并：
- `EventBindingSystem.GetTargetForm()` 改为调用公共方法（删除私有实现）。
- `ConfigurationLifecycleManager.ResolveHost()` 兜底分支调用 `UiThreadHelper.GetTargetForm()`。
- （`Threading/` 类的 GetHostControl 随 Step A 删除。）

### 2.3 E-3 — 随 Step A 解决的问题

删除未接线 `WinFormsUiThreadDispatcher` 后，其缺超时问题不再存在（已随文件移除）。若用户选择保留该类（而非删除），则需补 `InvokeAsync<T>` 的 5 秒超时 + TCS `RunContinuationsAsynchronously`，与 `DispatchIfNeeded<T>` 对齐——默认走删除路径。

---

## 批次 4：废弃 ECS 操作链路收敛

### 4.1 R-3 — 收敛 `ExecuteFunctionOperation` 活跃分支

**文件**：`Gate/EcsGate.cs`、`Gate/EcsOperation.cs`

**现状**：`ExecuteFunctionOperation` 标 `[Obsolete]`（warning）却在 `DispatchAsync` switch 中活跃；`ExecuteFunctionAndWaitAsync` 备用依赖 Idle 轮询。

**方案（分两步，先低风险）**：

**Step A**：
- `EcsGate.DispatchAsync` 中 `ExecuteFunctionOperation o =>` 分支改为**抛异常**（`throw new NotSupportedException("请使用 FunctionCallFacade.ExecuteAsync")`），保留操作定义但使调用方显式失败而非静默走废弃路径。
- `EcsGate.ExecuteFunctionAndWaitAsync` 标 `[Obsolete("请使用 FunctionCallFacade.ExecuteAsync", false)]`。

**Step B（需用户确认）**：
- 若全仓确认 `EnqueueFunctionOperation`/`ExecuteFunctionOperation`/`SubmitToMatrixOperation` 零构造调用方，则删除这三个操作定义 + `FunctionExecutionSystem` 链路（连同 `EcsGate` 中对应 switch 分支与 `DoExecuteFuncViaFacade`），整体移入 `_deprecated/`。

### 4.2 相关：`FunctionExecutionSystem` 的保留决策

文件自身注释承认「备用通道，未来可整条删除」。批次 4 Step B 一并处理；若用户决定保留 Matrix 编排能力，则至少将 `ExecuteFunctionOperation` 分支改为显式失败（Step A 已完成），避免双路径并存误导。

---

## 批次 6：运行时行为修复

### 6.1 E-4 — `EcsGate.DestroyScope` 同步阻塞发布事件

**文件**：`Gate/EcsGate.cs`

**现状**：`DestroyScope()` 调 `platform.Publish(...)`（内部 `PublishAsync(...).GetAwaiter().GetResult()` 同步阻塞）。

**方案**：
- 新增 `private async Task DestroyScopeAsync(string scopeName)`，内部 `await platform.PublishAsync(...)`。
- `DoLoadConfig`（已有 async 上下文）改为 `await DestroyScopeAsync(...)`。
- `DoClearScope`（同步上下文）保留调用同步 `DestroyScope`（其中 `Publish` 为同步便捷方法），但若 `DoClearScope` 也走 UI 线程则同样改异步——**需批次 0 确认 DoClearScope 调用方线程模型**。

### 6.2 E-5 — `EcsInputPort.ApplyDataChange` 审计对齐

**文件**：`Ports.cs`（`EcsInputPort.ApplyDataChange<T>`，第 989-1024 行）

**方案（保守，不改架构）**：在方法注释顶部显式声明「**本方法直写 Component，不经过 EcsGate 审计拦截管线**」，并说明如需审计应经 `EcsGate` 新增操作类型。若后续确认所有数据变更都需审计，则升级为经 `EcsGate` 封装。

---

## 批次 5：事件绑定接口拆分（跨项目，需确认后单独执行）

### D-3 — `IEventBindingService` 同名接口收敛

**现状**：4 个命名空间各有 `IEventBindingService`：`FrontEndDriven.Abstractions`（事件+线程混合，且随 E-2 可能删除）、`TConfig`（异步绑定）、`UniversalMiddleware.Abstractions`（事件映射）、`Share.EventBindingOrchestrator`（统一编排）。

**方案（低风险子集，先做）**：
1. 若批次 1.2 已删除 `FrontEndDriven.Abstractions.IEventBindingService`（无消费方），则此接口自然消除，问题降级为「TConfig / UniversalMiddleware / Share 三处共存」的文档级澄清——在 `TConfig` 版本注释中标注「与 UniversalMiddleware/Share 版本职责不同，勿混淆」。
2. UI 线程调度方法从事件绑定接口剥离：现有 `UnifiedControlService`/`NullEventBindingService` 被删除后，`IControlInvoker`（MetaCommand）与 `IEventBindingService` 中残留的调度方法统一由调用方改用 `UiThreadHelper`/`IUIDispatcher`（批次 2 后的唯一入口）。

**高风险子集（不建议本期执行）**：合并 TConfig/UniversalMiddleware/Share 三接口为单一接口——涉及跨项目使用点，需用户明确授予范围。

---

## 批次 7：装配确认与终验

### 7.1 R-2 — `ConfigurationLifecycleManager` 装配状态

**现状**：234 行类零 `new` 调用方。

**方案**：
- grep 全仓确认无反射/字符串方式构造（`Activator`/`GetType().Assembly`）。
- 与用户确认设计意图：
  - 若启用：在 `FrameworkInitializer`/`MainForm` 装配点补 `new ConfigurationLifecycleManager(EcsGate, EventPlatform, IUiThreadDispatcher, configLoader)` 并接线 `BootstrapAsync`。
  - 若弃用：移入 `_deprecated/`。

### 7.2 终验

- `dotnet build CsharpPrj/Operation/FrontEndDriven/FrontEndDriven.csproj` 0 错误 0 警告（或与基线一致）。
- 本项目与依赖项目（FileDriven/BackEndDriven/Share）联动构建。
- grep 复核所有被删除/改造的符号无残留引用。
- 运行既有测试（若有）。

---

## 各批次依赖与顺序小结

```
批次0(基线) → 批次1(死代码清理, 低风险) → 批次3(门面合并) → 批次2(线程收敛)
    → 批次4(废弃链路) → 批次6(运行时修复) → 批次7(终验)
批次5(D-3跨项目) 独立进行，需用户确认范围
R-2(批次7) 语义确认需用户参与
```

## 待用户确认项

1. **R-2**：`ConfigurationLifecycleManager` 保留启用（补装配点）还是移入 `_deprecated/`？
2. **R-3 Step B**：是否允许删除 `EnqueueFunctionOperation`/`ExecuteFunctionOperation`/`SubmitToMatrixOperation` 与 `FunctionExecutionSystem` 整条 Entity→Matrix→Handler 备用链路？
3. **E-2 前置**：`"FrontEndDriven.IControlService"` 字符串键是否可能在 BackEndDriven/PowerShell 等外部消费（需要你确认或授权我全仓 grep）？
4. **D-3 整合程度**：`TConfig`/`UniversalMiddleware`/`Share` 三处 `IEventBindingService` 是否本期合并（高风险）还是仅文档级澄清（低风险）？