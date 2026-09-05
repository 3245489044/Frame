# sideContainer 挂载状态与寻址断链根因分析

> 版本：v1.0 | 日期：2026-08-04
> 范围：`FileDriven/DrivenConfig/Scripts/UI/mainlayout_run_ui.json`（9 号校验脚本，20:03 运行）+ FrontEndDriven 渲染/挂载/断言链 + ContainerRegistry 容器聚拢点
> 现象：`assert container sideContainer count>0` FAIL（`MountState=Orphaned`）+ `validate-mount titleLabel parent=sideContainer` FAIL（`未找到宿主`），**但 `validate titleLabel/addButton/subtractButton/statusLabel` 全部 PASS**——控件挂载可见，容器挂载状态却为未挂载
> 关联：`侧边栏未加载假阳性分析_validate与控件挂载断链.md`（v1.1，输入/宿主寻址断链）→ 本文档聚焦**出口/挂载状态回写断链**；下游架构方案见 `容器逻辑聚拢点重构方案_ContainerConfluence.md`

---

## 一、问题现象（20:03 运行日志关键片段）

```
[20:03:55.000] ... doubleclick configListView   ← 步骤10: ConfigSelector 双击→ LoadConfigToTarget → 渲染 MathFunctions 到 sideContainer
[20:03:55.2xx] PASS, validate titleLabel / addButton / subtractButton / statusLabel   ← 控件确实挂载可见
[20:03:55.2xx] FAIL, assert container sideContainer count>0
                → assert-container: 宿主容器 'sideContainer' 未挂载（MountState=Orphaned）
[20:03:55.2xx] FAIL, validate-mount titleLabel parent=sideContainer
                → validate-mount: 未找到宿主 'sideContainer'
[20:03:55.256] [INFO] [UIToolbox] 加载配置已取消: scopeName=Side        ← 行21 点击 loadSideButton 弹对话框被取消
[20:03:55.258] LoadSidePanel, isSuccess=false ... 注册函数返回 false
```

**核心矛盾**：
- `titleLabel` 等 4 个控件**必须作为 `sideContainer`（或其子树）的后代**才能通过可见树 + `FindForm()!=null` 挂载校验 → 它们可见 ⇒ **`sideContainer` 必然挂在可见窗体树中**；
- 但容器断言链（读 `ContainerRegistry.MountState` / 按 hostKey 解析宿主）**判定 `sideContainer` 未挂载**。

两套口径对同一棵控件树的认知不一致 —— 这是**框架级寻址口径分裂**，而非单一函数 bug。

---

## 二、结论：`挂载可见 ≠ 容器挂载状态`（MountState 回写断链）

本次与 19:12 / 18:00 日志不同的**新证据**：断言已从「未找到宿主容器」（容器压根未注册）升级为「`MountState=Orphaned`」（**宿主已登记，但挂载状态非 Mounted**）。

已知修复（P7 / 容器聚拢点 S1-S4，注册链）已让 sideContainer **可被 Registry 解析到**，但暴露了**下一层**问题：

> **控件挂载可见时，`sideContainer` 自身的 `MountState` 从未/未被 `<code>MarkMounted</code>` 置为 `Mounted`。**

根因在「挂载状态唯一写入点」存在结构性缺陷——见 §三。

---

## 三、根因分析

### 根因 1（主因）：`WinFormApplier.ApplyRelations` 的 `MarkMounted` 只标记「父容器」，从不标记「被挂载的子容器」

```csharp
// WinFormApplier.ApplyRelations（L198-206）
result.RelationsApplied++;

// S4：挂载状态回写——父控件为已登记容器时，标记 MountState=Mounted（读状态 ≠ 树推断）。
if (!string.IsNullOrEmpty(parentControl?.Name))
{
    var registry = AppServiceProvider.Resolve<ContainerRegistry>();
    registry?.MarkMounted(parentControl.Name);   // ★ 只标记每个关系的【父】, 从不标记 【child 容器】
}
```

对照配置树（`MainLayout.json`）：

```
mainSplitter (SplitContainer)
  ├─ contentSplitter ─> contentContainer   (Content 宿主, child)
  └─ sideContainer                          (Side 宿主, child ← 关键)
              └─ (MathFunctions/MainView 挂载进来, 以 sideContainer 为 host/root)
```

- `sideContainer` 在配置树中是**作为 child**（挂到 `mainSplitter.Panel2`）。
- `ApplyRelations` 处理 `mainSplitter → sideContainer` 关系时：`parentControl = mainSplitter`、`childControl = sideContainer`，执行的是 **`MarkMounted("mainSplitter")`** → 只把 **mainSplitter** 标为 Mounted，**child 的 sideContainer 从不被 `MarkMounted`**。
- MathFunctions（Side scope）渲染时 `sideContainer` 作为 **host/root**，不出现在任何关系的「父」位 → `ApplyRelations` 同样不会对 `sideContainer` 调用 `MarkMounted`。

**后果**：sideContainer 作为一个「被挂载的容器」永远无法被 `MarkMounted`。其 `MountState` 停在 `Registered`（注册默认）或 `Orphaned`。

### 根因 2：`ContainerRegistry` 无对「child 容器」的挂载写路径，`Orphaned` 来源不明

```csharp
// ContainerRegistry.cs
public void MarkMounted(string hostKey)              // 仅 _byHostKey 命中且会把该键 State 置 Mounted
public MountState GetMountState(ContainerId id)      // 未命中键 → 默认回退 Orphaned
```

- 写 Mounted 的**唯一入口**是 `ApplyRelations` 中的 `MarkMounted(parentControl.Name)`，而该键 = **父容器名**。
- 没有任何代码对「被挂载进父容器的子容器」触发 `MarkMounted`。
- `MarkUnmounted`（Registered → Orphaned）在**当前工作区源码中零调用点**（仅有方案文档约定 S4 应由 `EcsGate.DestroyScopeEntities` 接线，但 `EcsGate.cs` 未见该接线）。日志打印 `Orphaned` 说明**运行产物**中该转移已发生（S4 部分落地但源码未同步回填），或 `GetMountState` 默认值路径生效。

### 根因 3：容器寻址链（`validate-mount` / `assert-container`）依赖 Registry + ControlPool，宿主控件键解不到

`ContainerAssertionService` 两处判定：

```csharp
// assert container: handle 有，但 IsMounted 逻辑
if (!handle.IsMounted && _port.IsMounted(handle.Id) == false)
    reason = $"未挂载（MountState={handle.State}）";        // ← 走中（State=Orphaned/Registered）

// validate-mount: 须 hostKey 解析出宿主控件
var (control, hostId) = ResolveInHost(controlName, hostKey);  // hostKey="sideContainer"
if (control == null) reason = $"未找到宿主 '{hostKey}'";      // ← 走中
```

- `ResolveInHost` → `_port.ResolveHost("sideContainer")` → handle 能命中（已登记），但 `handle.Control` 经 `_controlPool.GetControl("sideContainer")` 解析 → **ControlPool 无该控件键或名不符 ⇒ `handle.Control == null`** → 返回 `(null,null)` → 报 `未找到宿主`。
- 而普通控件 `validate` 走 `_findOrdinaryControl`（可见树递归查找），**不依赖 Registry/ControlPool 的容器键** → 命中 → PASS。

**两链对「宿主」认知不一致**：容器断言需 `Registry.MountState==Mounted` + `ControlPool` 可解 `sideContainer`；普通 validate 只看真实可见树。

### 根因 4（非 bug，脚本预期）：`加载配置已取消` / `LoadSidePanel isSuccess=false`

- 来自脚本行 21 `click loadSideButton`：绑定 `LoadSidePanel → LoadConfigToContainerInteractive(SideContainer)` → 弹 `OpenFileDialog`，脚本无人选文件 → `IsNullOrWhiteSpace(selectedFile)` 命中 → 记 `UIToolbox_LoadConfigToScope_cancelled` + 返回 `false`。
- **这是"取消=失败"的诚实上报，不是挂载逻辑缺陷**（与 19:12 那次 `composition disposed` 并发故障性质不同）。
- 注意：**行 16/17 的 FAIL 发生在行 21 点击之前**，即双击 configListView（渲染 MathFunctions 成功、validate PASS）之后 `sideContainer` 已 `Orphaned` —— 排除「取消导致未渲染」的解释。

---

## 四、证据代码索引

| 位置 | 关键点 |
|------|--------|
| `MainLayout.json` L157-163 | `sideContainer` 是 `mainSplitter.panel2Controls` 下的 **child** 容器，`container.stateScope="Side"` |
| `MathFunctions.json` L8-12 | 顶层为 `MainView`（其下 titleLabel/inputGroupBox/...），**无显式父关系指向 sideContainer**（渲染以 sideContainer 为 host） |
| `WinFormApplier.cs` L200-206 | `MarkMounted(parentControl.Name)` 只标记父容器 |
| `ContainerRegistry.cs` L114-131 | `MarkMounted`/`MarkUnmounted` 实现；`GetMountState` 未命中默认 `Orphaned` |
| `ContainerRegistry.cs` L52-75 | `Register(scope, hostKey)` 默认 `MountState.Registered` |
| `ContainerAssertionService.cs` L50-60 | `assert-container` 挂载校验：`!IsMounted && port.IsMounted(id)==false` |
| `ContainerAssertionService.cs` L148-162 | `validate-mount` `ResolveInHost`：hostKey 解析 null → `未找到宿主` |
| `ContainerAccessPortAdapter.cs` L33-34 | `IsMounted` = `GetMountState(id)==Mounted` |
| `EcsGate.cs` L148-168 | `DestroyScopeAsync`；**未见 MarkUnmounted 接线**（源码未同步 S4 改动） |
| `UIToolbox.cs` L421-427 | `LoadConfigToContainerInteractive` 取消分支 → `加载配置已取消` |


---

## 五、修复建议（对齐既有设计 S4 缺口）

1. **`WinFormApplier.ApplyRelations`**：成功 `TryAddControl` 后，除 `MarkMounted(parentControl.Name)` 外，当 `childControl` 为已登记容器时同步 `MarkMounted(childControl.Name)` —— 使「被挂载的子容器」也能权威置为 Mounted。
2. **`ContainerRegistry.Register`**：允许经 `ControlPool` 直接解析宿主控件，使 `handle.Control` 可解（避免 `未找到宿主`）。
3. **`EcsGate.DestroyScopeEntities → MarkUnmounted` 接线同步回填**（当前源码未含），让 `Registered→Orphaned` 转移有明确来源，杜绝「从未 Mounted 却被报 Orphaned」的语义噪音。
4. **验收**：`assert container sideContainer count>0` PASS 且 count 与 `layout report` 归属一致；`validate-mount titleLabel parent=sideContainer` PASS —— 使容器断言链与 `validate` 同口径。

---

## 六、验证计划

1. 运行 9 号脚本，核对 `assert-container` 从「`MountState=Orphaned`」转为「PASS」且 count 与 `layout report` 容器归属行一致。
2. 核对 `EcsGate_audit_after` 的 `mountedCount` / `orphanCount` 与 `WinFormApplier` `controls` 数同源一致（消除 18/19 错位）。
3. 首次干净运行与重复运行结果一致（消除 `+0/~19` 重放依赖）。

---

## 七、与既有修复/方案的关系

| 层级 | 责任 | 状态 |
|------|------|------|
| `侧边栏未加载假阳性分析_validate与控件挂载断链.md` | 输入/宿主寻址链（`EmitLoadConfig` 死胡同、`host=null` 静默、`validate` 存在性） | 已修复（P7 S1-S5） |
| `容器逻辑聚拢点重构方案_ContainerConfluence.md` | 容器注册/寻址聚拢（Registry/Port/Assertion、MountState 设计） | 方案中 S4（挂载状态写入）**未闭环** |
| 本文档 | 定位 **「挂载可见 ≠ MountState=Mounted」** 的回写断链 | 本分析 |

> 维护者：WorkFlowPrj Team
> 关联文档：`侧边栏未加载假阳性分析_validate与控件挂载断链.md`、`容器逻辑聚拢点重构方案_ContainerConfluence.md`、`FrontEndDriven_ScopeRefactor_StateSummary.md`

