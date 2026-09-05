# 侧边栏未加载假阳性分析：validate 存在性校验与控件挂载断链

> 版本：v1.1（v1.0 草案 + P7 核验修正）| 日期：2026-08-04
> 范围：FileDriven `mainlayout_run_ui.json`（9 号校验脚本）+ FrontEndDriven 渲染/校验/断言链
> 现象：脚本 20 条命令全 PASS、`Success=true`、validate 4 控件成功、快照 19 控件、审计 `result=True`，**但侧边栏（sideContainer）实际未显示任何 MathFunctions UI**
> 关联：重构方案见 `侧边栏假阳性_框架级结果契约与验证升格重构方案.md`（S1-S5 已实施）

---

## 1. 问题现象

运行 `echo 9 | EnableMent\交互启动.bat`（`UI\mainlayout_run_ui.json`）的日志（18:00 运行）关键片段：

```
[18:00:24.??] RenderPipelineComposition: scope=Side, rootEntityId=34, collectedCount=0, appliedCount=0
[18:00:24.272] CsgEntryPoint: WinFormSnapshot 收集完成 — scope=Side, controlCount=19, relationCount=18
[18:00:24.272] WinFormsOutputPort: 开始应用 WinFormSnapshot, controlCount=19, relationCount=18
[18:00:24.302] EventBindingSystem: 开始绑定: 6 个待绑定 Entity → boundCount=6
[18:00:24.303] EcsGate: 审计(后) — LoadAndRender, elapsedMs=54, result=True
[18:00:24.728-732] 校验成功: titleLabel / addButton / subtractButton / statusLabel
[18:00:24.736] 布局报告: 表单数量: 1, 控件数量: 50
[18:00:25.334] GUI 模式脚本执行完成: success=true, totalCommands=20
[18:00:25.337] 工作流执行完成: Success=true
```

**事实：侧边栏（sideContainer）空白，无任何 MathFunctions 控件。**

## 2. 结论：日志"通过"是假阳性

三层判定（validate 校验、快照/报告计数、命令/审计成功）**全部基于"ECS 实体 + ControlPool 控件对象"的存在性，没有任何一层校验"控件已挂载到 sideContainer 容器且可见"**。挂载环节失败 → 侧边栏空；但 ECS 世界照常创建实体、控件对象照常注册入池 → validate 找到"控件"、快照数出 19、审计 `result=True`、脚本全 PASS。

## 3. 三层假绿机制（代码证据 + v1.1 核验修正）

### 3.1 validate 被架空为"存在性检查"（结论成立）

- `validate-text` 模板展开确实带出 expectedText（`ScriptTemplateExpander.cs:88-95` → `Value = "expectedText=..."`）。
- 但旧 `ValidateSystem.Execute` 仅当 `meta.Parameters["expectedText"]` 存在**且非空**时才做文本比对（`UiCommandSystems.cs:254-265`），否则直达 `Command_Validate_Success` → PASS。
- **v1.1 补充证据**：`Command_Validate_TextMatch`/`PositionMatch` **未注册消息字典**（backend_log_messages.json 零命中）——若分支执行必打"消息未找到" WARN，18:00 日志无此 WARN → 分支确实未执行。
- **v1.1 根因精确化**：`ScriptTemplateExpander` 对 `ExpectedText==null` 展开 `expectedText=`（**空串**）→ 旧代码 `!string.IsNullOrEmpty` 短路 → 静默降级。**已修复**（P7 S3）：空串也参与比对，非空控件必然失配 → fail-fast。
- 存在性查找走 `ControlRegistryAwareFinder.FindByName`（顺序 ControlPool → 实体树 → 注册表），不检查挂载/可见。**已修复**（P7 S3）：validate 默认可见窗体树查找 + `FindForm()!=null` 挂载校验，孤儿控件判失败。

### 3.2 快照/布局报告的"19 / 50"是 ECS/可见树口径，非"Side 已挂载"证据

- `CsgEntryPoint.CollectSnapshot()` 从 **ECS 实体**（`GetEntities<ControlTypeComponent>`）收集 → `controlCount=19` = Side 作用域 **ECS 实体数**（**结论成立**）。
- **v1.1 修正**：`layout report`（`LayoutInspectSystem.ExecuteReport`）实际遍历 **WinForms 可见控件树**（`GetAllControlsFrom` 递归 `parent.Controls`）——"控件数量: 50"是 MainLayout+Content 的**可见控件数**，Side 的 19 个控件不在其中（即未挂载的证据）。原 v1.0 断言"布局报告同理 ECS/池视角"**不准确**。
- 真实缺陷：报告**不按容器分组**，sideContainer 为空被淹没在 50 总数里。**已修复**（P7 S4）：`layout report` 追加「容器归属统计」，空容器显式标注 `← 空容器`。

### 3.3 命令/审计成功只反映"无异常"（结论成立，路径补充）

- `FunctionCallFacade: isSuccess=true`（双击链）= 加载函数返回 true（fire-and-forget），加载/渲染是否真实完成无回传。
- `EcsGate` 审计 `result=True` = 操作未抛异常（`AuditLogInterceptor`），不校验任何 UI 状态。**已修复**（P7 S2）：审计携带 `mountedCount/orphanCount/succeeded`。
- **v1.1 补充**：18:00 运行实际走 `sink==null` 回退 → `ILoadConfigPort` → 状态管线（EcsGate LoadAndRender 审计出现即证）。矩阵 `MatrixEventSink.EmitLoadConfig` 因 `ITaskExecutorPool` 无注册点而**从不生效**（`MainFormGateFactory.RegisterMatrixHandlers` 恒提前 return）。**已修复**（P7 S5）：发射链统一走 `ILoadConfigPort`，矩阵路径删除。

## 4. 侧边栏为空的挂载失败点

1. `EcsGate.DoLoadAndRender` **不校验 `hostControl` 为 null**——null 也照样 `_composition.Execute(...)`。**已修复**（P7 S2）：host=null 显式抛 `InvalidOperationException`。
2. 挂载靠 `WinFormApplier.ApplyRelations`：Side 局部渲染不含父骨架 sideContainer，挂载依赖 `_controlPool.GetControl(parentEntity)` 全局查；查不到 → `WinFormApplier_Skip_RelationControlNotFound`（原 Debug 级）→ 控件成孤儿。**已修复**（P7 S1）：Skip 升 Warning + 进入 `ApplyResult.SkipReasons` + `WinFormApplier_ApplyResult_summary`（Info）。
3. `RenderPipelineComposition: collectedCount=0, appliedCount=0` 是"渲染管线没有产出增量变更"的显式降级信号（`RenderCollectStage_zero_count_degraded`）。

## 5. 待运行时确认的分叉点（v1.1 收敛）

| 分支 | 判据（S6 运行后新日志） | 处置 |
|------|---------------------------|------|
| A. 宿主解析失败 | `ContainerProjector.ResolveScopeHost` 抛"scope 未绑定宿主"（host=null → EcsGate 显式异常） | 检查 `MainLayout.json` sideContainer `stateScope:"Side"` 声明与 `BootHostBindingRegistrar` 扫描 |
| B. 关系挂载跳过 | `WinFormApplier_Skip_RelationControlNotFound` / `_ParseFailed` / `_InvalidTargetPanel`（Warning + SkipReasons） | 修正 Side 渲染关系 ParentId 与 ControlPool 条目标识对齐 |

## 6. 验证计划（P7 S6）

1. 运行 9 号脚本，核对 `WinFormApplier_ApplyResult_summary`（allMounted）、`ContainerCommandExecutor_Render_NotSucceeded`、`EcsGate_audit_after` detail（mountedCount/orphanCount）。
2. 脚本已增加 `assert container sideContainer count>0` + `validate-mount titleLabel parent=sideContainer`——唯一能区分"控件存在"与"控件已挂载可见"的判据。
3. `layout report` 容器归属统计中 sideContainer 行显式标注空。

## 7. 与既有修复的关系

上一轮已修复（上游发射链）：`UIToolbox.LoadConfigToContainerInteractive` 空路径拦截、`LoadConfigFromPath` 参数校验、`isSuccess` 契约诚实化、`fs_log_messages.json` 消息字典补全。

本次暴露的是**下游渲染链**问题：`EmitLoadConfig` 将消息交给矩阵后，**矩阵消费、宿主解析、控件挂载可见全链路无校验、无回传、无断言**——"入队成功 ≠ 渲染成功 ≠ 挂载可见"的结果契约断链。P7 重构方案（S1-S5）已从框架层面闭合该契约。

## 8. 相关代码索引（P7 后）

| 位置 | 关键点 |
|------|--------|
| `WinFormApplier.cs` | `Apply` 返回 `ApplyResult`；Skip 升 Warning（挂载统计） |
| `RenderPipelineStages.cs` | `RenderRunResult` 含 `MountedCount/OrphanCount/Succeeded` |
| `EcsGate.cs` | host=null 显式异常；审计携带挂载统计 |
| `ContainerCommandExecutor.cs` | `LoadToScopeAsync` 返回 `Succeeded` 判据 |
| `UiCommandSystems.cs` | validate 挂载可见校验 + `assert container` + `validate-mount` |
| `LayoutInspectSystem.cs` | `layout report` 容器归属统计 |
| `UIToolbox.cs` / `MainFormGateFactory.cs` | 发射链统一 `ILoadConfigPort`；矩阵路径删除 |
