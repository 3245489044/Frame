# sideContainer 挂载与断言断链分析（20:03 主布局脚本日志）

> 状态：分析确认 | 日期：2026-08-04 | 关联脚本：`FileDriven/DrivenConfig/Scripts/UI/mainlayout_run_ui.json`（23 条命令）
> 现象：`validate` 四控件全 PASS，但 `assert container sideContainer count>0` 与 `validate-mount titleLabel parent=sideContainer` 均 FAIL
> 关联文档：
> - `侧边栏未加载假阳性分析_validate与控件挂载断链.md`（P7，v1.1）
> - `侧边栏假阳性_框架级结果契约与验证升格重构方案.md`（P7，S1-S5）
> - `容器逻辑聚拢点重构方案_ContainerConfluence.md`（S1-S5）
> - `FrontEndDriven_RenderPipeline_MultiScope_Defect.md` / `FrontEndDriven_ScopeRefactor_StateSummary.md`

---

## 1. 本次日志（20:03）关键片段

```
行11  PASS  validate titleLabel
行12  PASS  validate addButton
行13  PASS  validate subtractButton
行14  PASS  validate statusLabel
行15  echo  P7 挂载断言：容器归属 + 宿主内挂载（「对象存在 ≠ 挂载可见」判据）
行16  FAIL  assert container sideContainer count>0    → assert-container: 宿主容器 'sideContainer' 未挂载（MountState=Orphaned）
行17  FAIL  validate-mount titleLabel parent=sideContainer → validate-mount: 未找到宿主 'sideContainer'
行18  PASS  layout report
行21  PASS  click loadSideButton                         （LoadSidePanel → 文件对话框被自动化取消）
```

**直接矛盾**：四个控件能通过 `validate`，说明它们已挂载到**可见窗体树**（P7 判据 `IsMountedOnVisibleTree(FindForm()!=null + 可见)` 通过，`UiCommandSystems.cs:310`）；但同刻 `assert container sideContainer` 报告 `MountState=Orphaned`、`validate-mount ... parent=sideContainer` 找不到宿主。分别命中「挂载状态缺失」与「寻址口径分裂」两个层面。

---

## 2. 两条失败链的逐层根因

### 2.1 行 16 `assert container sideContainer count>0` → `MountState=Orphaned`

调用链：
```
AssertSystem.Execute
  → ContainerAssertionService.AssertContainer("sideContainer","count>0",...)
    → ResolveHandle("sideContainer")        // ✔ 找到 handle（已在 ContainerRegistry 注册）
    → handle.Control != null                // ✔ ControlPool 中有 sideContainer 控件对象
    → !handle.IsMounted && port.IsMounted(id)==false   // ← 于此短路
    → reason = "宿主容器 'sideContainer' 未挂载（MountState=Orphaned）"
```
（`ContainerAssertionService.cs:43-60`、`ContainerAccessPortAdapter.cs:33-34`）

**根因：`sideContainer` 从未被 `MarkMounted`，其挂载状态停留在 `Orphaned`。**

`MountState` 的全部写入点（`ContainerRegistry.cs:115-131`、`WinFormApplier.cs:202-206`）：
1. `Register()` → 初始 `Registered`；
2. `WinFormApplier.ApplyRelations`：**仅当某子控件被成功挂到该容器下**时才 `registry.MarkMounted(parentControl.Name)`；
3. `MarkUnmounted()`：作用域销毁时置 `Orphaned`。

`sideContainer` 为 `Orphaned` ⇒ 本次运行中，MathFunctions 渲染应用关系（`ApplyRelations`）阶段**没有任何一个控件以 `sideContainer` 为父成功挂载**，`MarkMounted("sideContainer")` 从未被调用（或被后续 `MarkUnmounted` 清回）。

### 2.2 行 17 `validate-mount titleLabel parent=sideContainer` → `未找到宿主 'sideContainer'`

调用链：
```
ValidateMountSystem.Execute
  → ContainerAssertionService.AssertMounted("titleLabel","sideContainer",...)
    → hostKey="sideContainer" 非空
    → _port.ResolveHost("sideContainer")        // ContainerAccessPortAdapter.ResolveHost → ContainerRegistry.ResolveByScope("sideContainer")
    → 返回 null                                  // 注册表中 scope="Side"、hostKey="sideContainer"，没有 scope 名为 "sideContainer"
    → reason = "validate-mount: 未找到宿主 'sideContainer'"
```
（`ContainerAssertionService.cs:90-104,148-158`、`ContainerAccessPortAdapter.cs:24-25`）

**根因：`validate-mount` 的宿主查找只走 `ResolveByScope`（按 scope 字符串），而注册表里 scope 是 `"Side"`、hostKey 才是 `"sideContainer"`——没有 hostKey 反查兜底。**

对比 `assert container` 的 `ResolveHandle` 有 hostKey 兜底（`sideContainer` 能被解析到），`validate-mount` 的 `ResolveInHost` 只有 scope 查找——**同一个字符串 `sideContainer` 在两条断言链中寻址口径不一致**。这是「挂载可见 ≠ 可被断言寻址」的寻址口径分裂（ContainerConfluence 方案 S3 目标本应消除，此处仍残留）。

### 2.3 为什么行 11-14 `validate` 能过，而挂载断言不行

`ValidateSystem.Execute`（`UiCommandSystems.cs:299-317`）：
- `ResolveScopedControl("titleLabel")`（无 scope）→ `FindAcrossForms(可见 form, FindRecursive)` 在**可见窗体可见树**递归查找；
- 找到后 `ValidateSystemHelpers.IsMountedOnVisibleTree`（`FindForm()!=null` + 可见）校验通过 → PASS。

**结论**：`titleLabel` 等 4 个控件**确实存在于可见窗体树、并被挂载到了某个容器**，但：
1. 它们**不是以 `sideContainer` 为父经 `WinFormApplier.ApplyRelations` 挂载**（否则会触发 `MarkMounted("sideContainer")`），或挂载后被 `MarkUnmounted` 清回 → `sideContainer` 保持 `Orphaned`；
2. 断言链对 `sideContainer` 的寻址又因 scope/hostKey 分裂而无法命中。

因此，validate 通过 **不构成** "sideContainer 内已加载 MathFunctions" 的证据——这是本次日志唯一能得出的、不容混淆的事实。

---

## 3. 最可能的真实触发机制（时序竞态/重放）

结合 `FrontEndDriven_RenderPipeline_MultiScope_Defect.md`（§2/§3）与 `FrontEndDriven_ScopeRefactor_StateSummary.md`（§2/§3），本次 20:03 运行最贴合的解释：

1. 双击 `configListView` → `LoadConfigToTarget(configPath, "sideContainer")`
   → `ResolveScopeFromContainer("sideContainer") = "Side"`
   → `ILoadConfigPort.LoadConfigAsync(path, "Side")`
   → `ContainerCommandExecutor.LoadToScopeAsync("Side", path)`
   → `ContainerProjector.RenderAsync("Side", state)`（`UIToolbox.cs:325-349` + `ContainerCommandExecutor.cs:42` + `ContainerProjector.cs:47`）。
2. 渲染内 `EcsGate.DoLoadAndRender("Side")`（`EcsGate.cs:114-132`）：
   - 先 `DestroyScopeAsync("Side")` → `DestroyScopeEntities("Side")` 清旧 Side 实体/状态（`EcsGate.cs:126`；副作用见缺陷1：全局 ControlPool/EntityManager 清重建）；
   - 再 `_composition.Execute(config, host, "Side")` + `ExecuteRenderStage("Side")`。
3. 若 `DestroyScopeAsync` 期间对 `sideContainer` 执行了 `MarkUnmounted`（`ContainerRegistry.cs:120-131`），而后续 `ApplyRelations` 因**宿主/关系父容器解析未落到 sideContainer**，未再补 `MarkMounted` ⇒ `sideContainer` 永久 `Orphaned`。
4. `LoadConfigToTarget` 完成后 `FunctionExecutedRenderBridge` 依事件 scope 再触发一次重渲染（`ExecuteRenderStage(scope)`）→ 与投影器渲染在毫秒级叠加（缺陷2/缺陷5 竞态，`FrontEndDriven_ScopeRefactor_StateSummary.md` §3）→ 控件对象得以保留（validate 可见树上能找到），但容器挂载状态/关系未按期望投影。

> ⚠️ **日志充分性缺口**：本次 20:03 日志未打印 `WinFormApplier_ApplyResult_summary`（allMounted/SkipReasons）与 `EcsGate_audit_after`（MountedCount/OrphanCount/Succeeded）——这两个在 `WinFormApplier.cs:58` 与 `EcsGate.cs:220-231` 中理应存在的挂载统计日志，本次片段未捕获。因此无法从日志直接判定是「Side 根本没渲染」（`RenderExecuted=false`，配置加载短路或 null 返回，`ContainerProjector.cs:66-67`）还是「渲染后被清重建」。须按 §5 补齐日志再下最终结论。

---

## 4. 错误归属汇总

| # | 层面 | 缺陷 | 证据位置 |
|---|------|------|---------|
| 1 | 挂载状态 | `sideContainer` 从未被 `MarkMounted`，停留在 `Orphaned`（MathFunctions 未真正以 sideContainer 为父挂载，或挂载后被 MarkUnmounted 清回） | `WinFormApplier.cs:202-206`、`ContainerRegistry.cs:115-131`、`EcsGate.cs:126` |
| 2 | 寻址口径分裂 | 同一字符串 `sideContainer`：`assert container` 有 hostKey 兜底能解析，`validate-mount` 只走 `ResolveByScope`（scope=Side）找不到 → 两条链认知不一致 | `ContainerAssertionService.cs:43-58 / 90-104 / 148-158`、`ContainerAccessPortAdapter.cs:24-25` |
| 3 | validate 语义 | `validate` 只证明「控件在可见树」，不证明「在 sideContainer 下」；行 11-14 PASS 说明控件被挂到了某容器，但不一定是 sideContainer | `UiCommandSystems.cs:299-317` |
| 4 | 作用域销毁→状态未回写（候选） | `DestroyScopeAsync` 对容器显式/连带 `MarkUnmounted`，但投影器侧未保证渲染后重新 `MarkMounted`，依赖 `ApplyRelations` 隐式回写易漏 | `ContainerRegistry.cs:120-131`、`EcsGate.cs:148-168`、`ContainerProjector.cs:47/123-133` |
| 5 | 日志充分性 | 缺少 `WinFormApplier_ApplyResult_summary` / `EcsGate_audit_after` / `layout report` 容器归属统计，无法交叉确认 Side 挂载统计 | `WinFormApplier.cs:58`、`EcsGate.cs:220-231` |


---

## 5. 验证 / 修复方向

1. **补齐本次日志**：确认 `WinFormApplier_ApplyResult_summary`（allMounted/SkipReasons）、`EcsGate_audit_after`（MountedCount/OrphanCount/Succeeded）、`layout report` 容器归属行——用挂载统计而非「控件存在」判断 Side 是否真挂载。这一步是 A/B 分支判据（§3 第 3 点）。
2. **统一寻址口径**：`validate-mount` 的宿主解析补 `ResolveByHostKey` 兜底（与 `assert container` 的 `ResolveHandle` 一致），或把字符串 `sideContainer` 收敛为强类型 `ContainerId`（对齐 ContainerConfluence S3「字符串匹配点 6→2」目标）。
3. **挂载状态闭环**：在 `ContainerProjector.ResolveScopeHost` 或 `EcsGate.DoLoadAndRender` 宿主解析成功后，显式确认/设置宿主 `MountState=Mounted`，不再仅依赖 `WinFormApplier.ApplyRelations` 的隐式 `MarkMounted`（该依赖在「重放/销毁竞态」下会漏）。
4. **消除重放竞态**：`FunctionExecutedRenderBridge` 的 scope 重渲染与投影器渲染加互斥/去重，避免 Side 在毫秒级被重放清重建（对应既有缺陷2/缺陷5 残余）。

---

## 6. 相关代码索引

| 位置 | 关键点 |
|------|--------|
| `CsharpPrj/Operation/BackEndDriven/CommandExecution/UiCommandSystems.cs` | `ValidateSystem`（挂载可见校验 L310）、`ValidateMountSystem`、`AssertSystem.AssertContainer` |
| `CsharpPrj/Share/FrameworkAbstractions/ContainerAbstractions/ContainerAssertionService.cs` | `AssertContainer`（MountState 判定 L56-60）、`AssertMounted`（ResolveInHost 仅 scope 查找 L148-158） |
| `CsharpPrj/Operation/FrontEndDriven/WinForms/Adapter/ContainerAccessPortAdapter.cs` | `ResolveHost(scope)` → `ResolveByScope`（No hostKey 反查） |
| `CsharpPrj/Operation/FrontEndDriven/WinForms/Container/ContainerRegistry.cs` | `Register/MarkMounted/MarkUnmounted/ResolveByScope/ResolveByHostKey` |
| `CsharpPrj/Operation/FrontEndDriven/State/Projection/ContainerProjector.cs` | `ResolveScopeHost`（L86-97）、`RenderOnUIThreadAsync` |
| `CsharpPrj/Operation/FrontEndDriven/Gate/EcsGate.cs` | `DoLoadAndRender`（host=null 校验 + Destroy 时序）、`AuditLogInterceptor` |
| `CsharpPrj/Operation/FrontEndDriven/WinForms/Applier/WinFormApplier.cs` | `ApplyRelations` → `MarkMounted`（L202-206）、Skip 升 Warning |
| `FileDriven/FileDriven.Business/Registrations/UI/UIToolbox.cs` | `LoadConfigToTarget`（L325-349）、`ResolveScopeFromContainer`（L384-394） |
| `FileDriven/FileDriven.Execution/Ports/LoadConfigPortAdapter.cs` | `LoadConfigAsync(path, scopeName)` → `ContainerCommandExecutor` |
| `CsharpPrj/Operation/FrontEndDriven/State/Commands/ContainerCommandExecutor.cs` | `LoadToScopeAsync`（Succeeded 判据） |
| `FileDriven/DrivenConfig/JsonConfig/MainLayout.json` | `sideContainer` `sourceType:"state"/stateScope:"Side"` |
| `FileDriven/DrivenConfig/Scripts/UI/mainlayout_run_ui.json` | 行16/17 断言 |


