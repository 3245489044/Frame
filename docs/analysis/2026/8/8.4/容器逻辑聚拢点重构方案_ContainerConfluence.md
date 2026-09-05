# 容器逻辑聚拢点重构方案（Container Confluence）

> **版本**：v1.0 | **日期**：2026-08-04 | **状态**：方案待实施
> **前置分析**：
> - `侧边栏未加载假阳性分析_validate与控件挂载断链.md`（v1.1，8.4）——P7 前置根因分析
> - `侧边栏假阳性_框架级结果契约与验证升格重构方案.md`（P7，S1-S5 已实施、S6 运行回归待执行）
> **触发**：P7 S6 运行回归（19:12 日志）——挂载契约已成功（`allMounted=true`、`Succeeded=True`、控件可见校验 PASS）但**容器断言仍 FAIL**（`未找到宿主容器 'sideContainer'`）。
> **对齐**：`docs/analysis/2026/8/调用链聚拢点重构方案.md`（Modules/Pipeline）+ `docs/KnowLedge/聚合架构设计/聚合架构设计规范：从三收拢点到多元聚合的工程范式.md` + 三聚拢点模式（输入/执行/输出）+ P0-P7 重构线。

---

## 目录

- [容器逻辑聚拢点重构方案（Container Confluence）](#容器逻辑聚拢点重构方案container-confluence)
  - [目录](#目录)
  - [一、背景：S6 回归暴露的架构级缺陷](#一背景s6-回归暴露的架构级缺陷)
    - [1.1 现象（19:12 运行日志关键片段）](#11-现象1912-运行日志关键片段)
    - [1.2 直接矛盾：控件可见但宿主不可达](#12-直接矛盾控件可见但宿主不可达)
    - [1.3 P7 是症状级修复，本文档是架构级重构](#13-p7-是症状级修复本文档是架构级重构)
  - [二、现状盘点：容器逻辑 16 个散落点](#二现状盘点容器逻辑-16-个散落点)
    - [2.1 四套独立索引/存储（同一份 name→control 事实，四处维护）](#21-四套独立索引存储同一份-namecontrol-事实四处维护)
    - [2.2 六处字符串 Name 匹配](#22-六处字符串-name-匹配)
    - [2.3 两处 scope→hostName 硬编码映射](#23-两处-scopehostname-硬编码映射)
    - [2.4 四条验证/断言查找路径（口径各不相同）](#24-四条验证断言查找路径口径各不相同)
    - [2.5 散落点总表](#25-散落点总表)
    - [2.6 根因抽象](#26-根因抽象)
  - [三、重构原则：聚拢点收敛模型](#三重构原则聚拢点收敛模型)
    - [3.1 核心认知：聚拢点 ≠ 又一个字典](#31-核心认知聚拢点--又一个字典)
    - [3.2 与既有聚拢点/公理体系对齐](#32-与既有聚拢点公理体系对齐)
  - [四、聚拢点架构设计（四层）](#四聚拢点架构设计四层)
    - [4.0 架构总览](#40-架构总览)
    - [4.1 Layer 0：ContainerIdentity（强类型身份，消灭字符串）](#41-layer-0containeridentity强类型身份消灭字符串)
    - [4.2 Layer 1：ContainerRegistry（容器聚拢点 = 唯一事实源）](#42-layer-1containerregistry容器聚拢点--唯一事实源)
    - [4.3 Layer 2：ContainerProjector 改造（宿主解析单一来源）](#43-layer-2containerprojector-改造宿主解析单一来源)
    - [4.4 Layer 3：IContainerAccessPort（容器访问端口，前后端共用）](#44-layer-3icontaineraccessport容器访问端口前后端共用)
    - [4.5 Layer 4：MountState（挂载状态权威，终结「推断 ≠ 事实」）](#45-layer-4mountstate挂载状态权威终结推断--事实)
    - [4.6 验证链聚拢：ContainerAssertionService（单一断言权威）](#46-验证链聚拢containerassertionservice单一断言权威)
  - [五、分阶段实施计划（S1-S5）](#五分阶段实施计划s1-s5)
  - [六、收敛度目标与验收](#六收敛度目标与验收)
  - [七、风险与边界](#七风险与边界)
  - [八、改动文件清单](#八改动文件清单)
  - [九、与既有方案/规范对齐](#九与既有方案规范对齐)

---

## 一、背景：S6 回归暴露的架构级缺陷

### 1.1 现象（19:12 运行日志关键片段）

P7 重构（S1-S5）实施后首次运行 9 号脚本（`mainlayout_run_ui.json`）的 19:12 日志：

| 时间 | 日志 | 结果 |
|------|------|------|
| 19:12:17.440 | `[UIToolbox] 加载配置到目标容器: configPath=MathFunctions.json, targetContainer=sideContainer` | ✅ |
| 19:12:17.504 | `WinFormApplier 挂载结果: controls=19(+0/~19/!0), relations=18/18 mounted, skipped=0, allMounted=true` | ✅ 挂载契约通过 |
| 19:12:17.507 | `EcsGate 审计(后): MountedCount=18, OrphanCount=0, ControlsSkippedCount=0, Succeeded=True` | ✅ 结果契约通过 |
| 19:12:17.935-941 | `校验成功: titleLabel / addButton / subtractButton / statusLabel`（可见树 + FindForm 挂载校验 + 文本比对） | ✅ 控件**确实挂载可见** |
| 19:12:17.936→行16 | `assert container sideContainer count>0` → `assert-container: 未找到宿主容器 'sideContainer'` | ❌ **FAIL** |
| 19:12:17.937→行17 | `validate-mount titleLabel` → `validate-mount: 未找到宿主 'sideContainer'` | ❌ **FAIL** |
| 19:12:17.961 | `布局报告: 表单数量: 1, 控件数量: 50, 命名容器数: 18` | 可见树口径 |

### 1.2 直接矛盾：控件可见但宿主不可达

同一时刻出现互斥的两组结果：

- titleLabel 等 4 个控件**必须作为 sideContainer（或其子树）的后代**才能被挂载到可见窗体树；它们既然通过可见树查找 + `FindForm() != null` 挂载校验，**sideContainer 就必然在可见窗体树中**。
- 但断言链的两条查找路径（`FindByName<Control>("sideContainer")` / `FindAcrossForms(visible, FindRecursive(form, "sideContainer"))`）**都找不到 sideContainer**。

**两条链对同一控件树的认知不一致**——这是框架级寻址口径分裂，而非单一函数的 bug。

### 1.3 P7 是症状级修复，本文档是架构级重构

P7（`侧边栏假阳性_框架级结果契约与验证升格重构方案.md`）解决了"**渲染执行 ≠ 挂载可见**"的结果契约问题（`allMounted`/`Succeeded` 判据已生效——本次挂载契约确实通过）。

但本次暴露的是**下一个层级**的问题："**挂载可见 ≠ 可被断言/验证寻址**"：

- 渲染/挂载链（FrontEndDriven）基于 **ControlPool 双向映射**（name/entity 字典）寻址 → 成功；
- 验证/断言链（BackEndDriven）基于 **WinForms 控件树遍历 + `c.Name == name` 字符串匹配**寻址 → 失败。

两链寻址口径不同，且背后是**容器逻辑 16 个散落点**（见 §二）。本文档从架构层收敛。

---

## 二、现状盘点：容器逻辑 16 个散落点

### 2.1 四套独立索引/存储（同一份 name→control 事实，四处维护）

| # | 载体 | 位置 | 键模型 | 字符串脆弱 |
|---|------|------|--------|:---:|
| 1 | `ControlPool._nameToControl` | `BasicMethod/ECS/Bridge/ControlPool.cs` L28 | name→Control（`OrdinalIgnoreCase`） | ★ |
| 2 | `ControlPool` entity↔name 双向 | 同上 L30 + `IStorageCell` 双向映射 | Entity↔name | ★ |
| 3 | `ContainerRegistry` + `ControlIndex` | `WinForms/Container/ContainerRegistry.cs` | name→Control（**第二个索引**） | ★ |
| 4 | `HostBindingStore` | `Modules/InternalStorage/State/HostBindingStore.cs` | scope↔hostKey 双向 | ★ |

> 同一份「name→控件」事实在 ControlPool、ContainerRegistry、实体树索引、可见树遍历四处各存/各查，无单一事实源。

### 2.2 六处字符串 Name 匹配

| # | 位置 | 匹配逻辑 |
|---|------|---------|
| 5 | `ControlFinder.FindRecursive`（`WinForms/Finder/ControlFinder.cs` L78） | `c.Name == name` 树遍历 |
| 6 | `ControlFinderAdapter.FindByName`（`WinForms/Adapter/`） | 委托 #5，全 Form 遍历 |
| 7 | `ControlRegistryAwareFinder.FindByName` | ControlPool → 实体树 → 注册表三级链 |
| 8 | `EntityTreeService.FindControlByName` | 实体树按 name |
| 9 | `LayoutInspectSystem.FindEntityByName`（`ControlRender/Commands/LayoutInspectSystem.cs` L278） | 实体遍历比 `info.Name` |
| 10 | `ContainerFinder.FindContainerControl`（`WinForms/Container/ContainerFinder.cs` L36-37） | 硬编码 `"contentContainer"/"sideContainer"` |

### 2.3 两处 scope→hostName 硬编码映射

| # | 位置 | 硬编码 |
|---|------|--------|
| 11 | `UiCommandSystems.cs` L406-411（`ValidateSystem.ResolveScopedControl`） | `"Side" => "sideContainer"`、`"Content" => "contentContainer"` |
| 12 | `BootHostBindingRegistrar.cs` L97（`BindAuthoritative`） | `Bind(scope, control.Name)`——配置字符串即 hostKey |

### 2.4 四条验证/断言查找路径（口径各不相同）

| # | 位置 | 查找路径 | 池兜底 |
|---|------|---------|:---:|
| 13 | `ValidateSystem.ResolveScopedControl`（L426-438） | 可见树 → 池直查降级 | ✅ |
| 14 | `AssertSystem.AssertContainer`（L226） | 仅 `FindByName<Control>` | ❌ |
| 15 | `ValidateMountSystem.Execute`（L568-575） | 仅 `FindAcrossForms + FindRecursive` | ❌ |
| 16 | `LayoutInspectSystem.ExecuteReport` | 可见树遍历 + `FindForm() != null` 推断 | ❌ 推断非事实 |

> #14/#15 与 #13 同为 P7 新增，但**只有 #13 做了池兜底**——容器控件（`EcsConfigContainer`）恰为"池中有、树中 Name 可能不一致"的类型，故 #14/#15 必然失配（本次 FAIL 的直接代码机制）。

### 2.5 散落点总表

| 维度 | 散落点 | 数量 |
|------|--------|:---:|
| 独立索引/存储 | #1-#4 | 4 |
| 字符串 Name 匹配 | #5-#10 | 6 |
| scope→hostName 硬编码 | #11-#12 | 2 |
| 验证/断言查找路径 | #13-#16 | 4 |
| **合计** | | **16** |

### 2.6 根因抽象

```
容器寻址四元组 (scope, hostKey, entity, control) 之间
  ├─ 4 处独立存储维护（无单一事实源）
  ├─ 6 处字符串 Name 匹配（无强类型身份）
  ├─ 2 处 scope→hostName 硬编码（无注册表反查）
  └─ 4 条验证查找路径（口径不一致）
+ 挂载可见性 = 运行时树遍历 / FindForm() 推断，非状态记录
  →「推断 ≠ 事实」是假阳性的哲学根源
```

---

## 三、重构原则：聚拢点收敛模型

### 3.1 核心认知：聚拢点 ≠ 又一个字典

> 聚拢点 = **单一权威** + **唯一入口** + **强类型身份**。
> 容器聚拢点要收敛的不是"代码行数"，而是**事实源数量**（4→1）、**身份模型**（字符串→强类型）、**口径数量**（4→1）、**判定方式**（推断→状态读取）。

四个收敛轴：

| 轴 | 现状 | 目标 |
|----|------|------|
| 事实源 | 4 处独立索引/存储 | **1**（ContainerRegistry） |
| 身份 | 6 处字符串 Name 匹配 + 2 处硬编码 | **强类型 ContainerId**，字符串仅注册边界出现 |
| 口径 | 4 条验证/断言查找路径 | **1**（ContainerAssertionService） |
| 判定 | 树遍历 / `FindForm()!=null` 推断 | **MountState 状态读取** |

### 3.2 与既有聚拢点/公理体系对齐

| 条目 | 对齐 |
|------|------|
| 三聚拢点模式（输入/执行/输出） | 容器聚拢点：`ContainerRegistry`（事实源）→ `IContainerAccessPort`（访问口）→ `ContainerAssertionService`（断言口） |
| `Modules/Pipeline`（StaticPipeline/CentralPipeline） | 容器生命周期 `Load → Bind → Mount → Publish` 走既有状态驱动单一路径（`ContainerCommandExecutor → ContainerProjector.RenderAsync`），内部消费 `ContainerRegistry` |
| `SingleImplementation`（唯一实现） | name→control 事实源从 4 归 1 |
| `RedundancyElimination`（无消费者即删除） | 16 点中 ≥10 点收敛后删除（§八 改动清单） |
| `StructuralGrounding`（结构性引用须经验证） | 结构性引用（scope/hostKey/控件名）经 `ContainerRegistry` 反查验证，不再裸字符串 |
| `StateSnapshot`/`StateMerge` | 挂载可见性 = `MountState` 状态读取（单一写入点），不再推断 |
| `CountableOrderBridge`（统一比较域） | 断言/报告/审计同源 `MountState` 计数，消除 18/19 口径错位 |

---

## 四、聚拢点架构设计（四层）

### 4.0 架构总览

```
┌─────────────────────────────────────────────────────────────────────┐
│  验证/断言链（BackEndDriven）      渲染/挂载链（FrontEndDriven）      │
│  UiCommandSystems (assert/validate)  WinFormApplier / EcsGate        │
│        │  薄适配器                            │  单一写入点            │
│        ▼                                    ▼                        │
│  ContainerAssertionService ──► IContainerAccessPort ◄── WinFormApplier│
│  （唯一断言权威）                   （唯一访问口径）      （挂载写入）   │
│                                     │                                │
│                                     ▼                                │
│                       ContainerRegistry（唯一事实源）                  │
│          scope ↔ hostKey ↔ entity ↔ control ↔ role ↔ MountState     │
│                        │ 实现复用                                    │
│              IStorageCell 双向映射 + ControlPool（普通控件池）          │
└─────────────────────────────────────────────────────────────────────┘
```

### 4.1 Layer 0：ContainerIdentity（强类型身份，消灭字符串）

```csharp
// Share/FrameworkAbstractions/ContainerAbstractions/ContainerId.cs
public sealed record ContainerId
{
    public required string ScopeId { get; init; }      // "Side" / "Content" / "MainLayout"
    public required string HostKey  { get; init; }      // 配置声明（注册时归一一次）
    public required EntityId Entity { get; init; }      // 实体锚点
    public required string ControlName { get; init; }   // WinForms 控件名（仅注册边界使用）
    public ContainerRole Role { get; init; }            // enum：MainLayout/Content/Side/SplitterPanel
}

// 解析规则唯一权威：经 ContainerRegistry 反查，禁止硬编码
public static class ContainerIdentity
{
    public static ContainerId? Resolve(string scope);                    // Registry.ResolveScope → HostKey → Control
    public static ContainerId? Resolve(string scope, string hostKey);    // 显式 hostKey 路径
}
```

**替换点**：#11（BackEndDriven 硬编码映射）、#12（BootHostBindingRegistrar 裸字符串绑定）、#10（ContainerFinder 硬编码）。
**原则**：字符串仅在**配置解析边界**出现一次（注册时），运行时全程走 `ContainerId`。

### 4.2 Layer 1：ContainerRegistry（容器聚拢点 = 唯一事实源）

**将 #1/#2（ControlPool 容器段）+ #3（ContainerRegistry）+ #4（HostBindingStore）三源合一**：

```csharp
// Modules/InternalStorage/Container/ContainerRegistry.cs （新聚拢点）
public sealed class ContainerRegistry
{
    // 原子写入：scope ↔ hostKey ↔ entity ↔ control ↔ role ↔ mountState 六元组
    public ContainerHandle Register(ContainerRegistration reg);
    public void Unregister(ContainerId id);

    // 挂载事实写入（唯一写入点来自 WinFormApplier / EcsGate）
    public void MarkMounted(ContainerId id, Control control);
    public void MarkUnmounted(ContainerId id);

    // 唯一解析出口（供前后端共用）
    public ContainerHandle? Resolve(ContainerId id);
    public ContainerHandle? ResolveByScope(string scope);
    public ContainerHandle? ResolveByHostKey(string hostKey);   // 反查（替代硬编码映射）
    public IReadOnlyList<ContainerHandle> GetAll();

    // 挂载事实读取（替代树遍历推断）
    public MountState GetMountState(ContainerId id);            // enum：Registered/Mounted/Orphaned
}
```

- **数据落地**：复用既有 `IStorageCell` 双向映射（`StorageBidirectionalExtensions`，TH-12 已收敛的底层）；`HostBindingStore` 的 `host_binding` Map 保留为 `ContainerRegistry` 的 scope→hostKey 内部单元，**不再对外暴露**。
- **删除**：#3 `ContainerRegistry`/`ControlIndex` 重复索引（并入）；#4 `HostBindingStore` 独立类降为内部实现。
- **保持**：`ControlPool` 作为"全量控件池"保留（普通控件 + 容器控件），`ContainerRegistry` 只承载"容器角色"段（`ContainerRole` 的控件），二者以 `ControlPool` 为唯一控件事实源。

### 4.3 Layer 2：ContainerProjector 改造（宿主解析单一来源）

现有 `ContainerCommandExecutor → ContainerProjector.RenderAsync`（状态驱动单一路径）已是入口聚拢点，**保留**。改造 `ResolveScopeHost`：

```csharp
// 改造后：不再注入 Func<string, Control?>（字符串 hostKey 解析函数）
private ContainerHandle ResolveScopeHost(string scope)
{
    var handle = _registry.ResolveByScope(scope);        // 单一来源
    if (handle == null || handle.MountState != MountState.Mounted)
        throw new InvalidOperationException($"scope '{scope}' 未绑定/未挂载宿主");
    return handle;
}
```

- 删除注入的 `_hostResolver`（`ContainerProjector.cs` L23/L95-97，字符串函数式依赖）。

### 4.4 Layer 3：IContainerAccessPort（容器访问端口，前后端共用）

**将 BackEndDriven 三条验证路径 + FrontEndDriven 查找器收敛到一个端口**：

```csharp
// Share/FrameworkAbstractions/ContainerAbstractions/IContainerAccessPort.cs
public interface IContainerAccessPort
{
    ContainerHandle? ResolveHost(string scope);                // 强类型解析（替代 FindByName/FindRecursive/硬编码）
    ContainerHandle? ResolveHost(ContainerId id);
    int DirectChildCount(ContainerId id);                      // 读 MountState/ControlPool，不遍历树
    bool IsMounted(ContainerId id);                            // 读挂载事实，替代 FindForm()!=null
    bool IsDescendantOf(ContainerId child, ContainerId host);  // 经 entity 父链判定
    string? ResolveScopeByHostKey(string hostKey);             // 反查（替代硬编码映射）
}
```

- **实现**：`FrontEndDriven` 提供 `ContainerAccessPortAdapter`（委托 `ContainerRegistry` + `ControlPool`），注册于 `AppServiceProvider`——**替换 `ControlFinderAdapter`（`FrameworkAbstractions.WinFormsAbstractions.IControlFinder`）作为 BackEndDriven 的唯一寻址入口**（循 A1 先例：接口放 Share、实现注册于 ControlServiceCollection）。
- **替换点**：#13/#14/#15 三条链全部改为经端口解析；#5/#6 字符串树遍历仅在"普通控件"场景保留（validate 控件）；#9 布局报告改读端口。

### 4.5 Layer 4：MountState（挂载状态权威，终结「推断 ≠ 事实」）

- **写入点唯一**：`WinFormApplier.ApplyRelations` 挂载成功 → `ContainerRegistry.MarkMounted`；`EcsGate.DestroyScopeEntities` → `MarkUnmounted`（随 P7 S2 生命周期）。
- **读取点全覆盖**：#13 的 `IsMountedOnVisibleTree`、#14 宿主挂载校验、#15 挂载校验、#16 容器归属统计，全部改读 `MountState`。
- **收益**：`controls=19(+0/~19/!0)` 与 `MountedCount=18` 的口径错位一并消除——挂载数从 `MountState` 计数，控件处理数与挂载数天然同源。

### 4.6 验证链聚拢：ContainerAssertionService（单一断言权威）

```csharp
// Share/ContainerAbstractions/ContainerAssertionService.cs —— 唯一断言权威
public sealed class ContainerAssertionService(IContainerAccessPort port)
{
    public bool AssertContainer(string scopeOrHostKey, string countExpr, out string reason);
    public bool AssertMounted(string controlName, string hostKey, out string reason);
    public bool AssertVisible(string controlName, out string reason);
}
```

- 脚本命令 `assert container` / `validate-mount` / `validate`（`UiCommandSystems.cs`）降为**薄适配器**（只解析命令语法，语义全部委托服务）。
- 框架自检（P7 审计 / 回归脚本）复用同一服务——**脚本断言与框架自检同口径**。

---

## 五、分阶段实施计划（S1-S5）

> 每阶段设编译门 + 运行回归门，与 P7（S1-S5 已实施、S6 待回归）衔接。

| 阶段 | 内容 | 关键产出 | 验证门 |
|------|------|----------|--------|
| **S1 身份化** | 新增 `ContainerId`/`ContainerIdentity`；`ContainerRegistry` 骨架（scope↔hostKey↔entity↔control↔role 注册/反查）；`HostBindingStore` 降为内部单元 | `ContainerRegistry` 编译通过；`ResolveByScope("Side") → sideContainer` 反向查通 | `MainProj.sln` 0 错误 |
| **S2 三源合一** | 删除 `ContainerRegistry`(ControlIndex) 重复索引；`BootHostBindingRegistrar` 改走强类型注册；`ContainerProjector.ResolveScopeHost` 改读 `ContainerRegistry` | 容器事实源 4→1；删除 #3/#4/#12 字符串路径 | 编译 + 启动日志 host 解析一致 |
| **S3 端口化** | 新增 `IContainerAccessPort` + `ContainerAccessPortAdapter`；BackEndDriven 三链（validate/assert/validate-mount）改经端口；删除 L406-411 硬编码映射 | 字符串匹配点 6→2；断言口径 4→1 | 编译 + 9 号脚本回归 |
| **S4 状态化** | `MountState` 写入/清除（WinFormApplier/EcsGate）；validate/assert/layout report 全部改读状态 | 挂载推断点→0；`MountedCount` 口径统一 | 运行回归：S6 断言全绿 |
| **S5 清理** | 删除 `ContainerFinder` 硬编码、`ControlFinderAdapter` 容器路径、`LayoutInspectSystem.FindEntityByName` 树遍历；文档更新 | 收敛度达目标 | grep 零残留 |

**关键实现顺序依赖**：S1（Registry 旁路）→ S2（切换事实源）→ S3（端口化，BackEndDriven 依赖 S1）→ S4（状态化，依赖 S2 挂载写入点）→ S5（清理）。每阶段均可独立编译、独立回滚。

---

## 六、收敛度目标与验收

### 6.1 收敛度目标

| 度量 | 现状 | 目标 | 对应公理 |
|------|:---:|:---:|----------|
| 容器事实源（独立索引/存储） | 4 | **1** | SingleImplementation |
| 字符串 Name 匹配点 | 6 | **≤2**（仅注册边界） | StructuralGrounding |
| scope→hostKey 硬编码 | 2 | **0** | RedundancyElimination |
| 验证/断言查找口径 | 4 | **1**（ContainerAssertionService） | CountableOrderBridge |
| 挂载可见性判定方式 | 推断（树遍历/FindForm） | **状态读取**（MountState） | StateSnapshot/StateMerge |
| 容器语义散布文件数 | 16 点 / 9 文件 | **4 文件**（Registry/Port/Assertion/Projector） | 聚合架构三维度 ≥12 |

### 6.2 验收标准（运行回归）

1. `assert container sideContainer count>0` → **PASS**，且 `count` 与 `layout report` 容器归属行一致；
2. `validate-mount titleLabel parent=sideContainer` → **PASS**；
3. `EcsGate_audit_after` 的 `mountedCount` 与 `WinFormApplier` `controls` 数**同源一致**（消除 18/19 错位）；
4. 首次干净运行（无历史 ControlPool 残留）与重复运行结果一致（消除 `+0/~19` 重放依赖）。

---

## 七、风险与边界

| 风险 | 缓解 |
|------|------|
| `ControlPool` 被大量既有代码直接使用，S2 改动面大 | 容器段收敛，普通控件池 API 不变；S1 先加 Registry 旁路，S2 再切换 |
| `IContainerAccessPort` 跨项目依赖方向（BackEndDriven→FrontEndDriven 运行时注册） | 端口接口放 Share（同 `WinFormsAbstractions` 先例），实现注册于 `ControlServiceCollection`，循 A1 先例（`命令系统残留清理与共享扩展提取重构方案`） |
| `MountState` 与 WinForms 真实树可能因外部操作（用户拖动/手动 `AddControl`）漂移 | 定义"框架内操作"为唯一写源；`layout report` 保留树遍历作为**诊断对照**（读状态为主、树为辅，双向校验告警） |
| 旧形态脚本/外部调用依赖 `FindByName` 字符串路径 | 端口提供 `ResolveByName` 兼容入口（仅注册边界），内部归一为强类型 |

---

## 八、改动文件清单

| 文件 | 阶段 | 改动 |
|------|------|------|
| `Share/FrameworkAbstractions/ContainerAbstractions/ContainerId.cs` | S1 | **新增**：强类型身份 + `ContainerIdentity` 解析 |
| `Modules/InternalStorage/Container/ContainerRegistry.cs` | S1 | **新增**：容器聚拢点（六元组注册/反查/挂载状态） |
| `Modules/InternalStorage/State/HostBindingStore.cs` | S2 | 降为 `ContainerRegistry` 内部单元（scope↔hostKey） |
| `Operation/FrontEndDriven/State/Projection/BootHostBindingRegistrar.cs` | S2 | 改走强类型注册（删除裸 `control.Name` 绑定） |
| `Operation/FrontEndDriven/State/Projection/ContainerProjector.cs` | S2 | `ResolveScopeHost` 改读 `ContainerRegistry`；删除 `_hostResolver` 注入 |
| `Operation/FrontEndDriven/WinForms/Container/ContainerRegistry.cs` | S2 | **删除**重复索引（并入新 Registry） |
| `Share/FrameworkAbstractions/ContainerAbstractions/IContainerAccessPort.cs` | S3 | **新增**：容器访问端口 |
| `Operation/FrontEndDriven/WinForms/Adapter/ContainerAccessPortAdapter.cs` | S3 | **新增**：端口实现（委托 Registry + ControlPool） |
| `Operation/BackEndDriven/CommandExecution/UiCommandSystems.cs` | S3 | AssertContainer/ValidateMountSystem/ValidateSystem 改经端口；删除 L406-411 硬编码映射 |
| `Share/ContainerAbstractions/ContainerAssertionService.cs` | S3 | **新增**：唯一断言权威 |
| `Operation/FrontEndDriven/WinForms/Applier/WinFormApplier.cs` | S4 | `ApplyRelations` 挂载成功 → `MarkMounted` |
| `Operation/FrontEndDriven/Gate/EcsGate.cs` | S4 | `DestroyScopeEntities` → `MarkUnmounted`；`MountedCount` 改读 `MountState` |
| `Operation/FrontEndDriven/ControlRender/Commands/LayoutInspectSystem.cs` | S4 | 容器归属统计改读 `MountState`（树遍历仅作诊断对照） |
| `Operation/FrontEndDriven/WinForms/Container/ContainerFinder.cs` | S5 | **删除**（硬编码容器名，已被端口替代） |
| `Operation/FrontEndDriven/WinForms/Adapter/ControlFinderAdapter.cs` | S5 | 容器路径移除（普通控件查找保留） |
| `Operation/FrontEndDriven/WinForms/Finder/ControlFinder.cs` | S5 | 容器相关字符串遍历入口收敛（`FindRecursive` 仅普通控件用） |
| `DrivenConfig/Scripts/UI/mainlayout_run_ui.json` | S4 | 断言语义不变；回归判据沿用（count 一致性） |
| `fde_log_messages.json` / `backend_log_messages.json` | S1-S4 | 新增 Registry/Port/Assertion 消息键 |

---

## 九、与既有方案/规范对齐

| 条目 | 对齐 |
|------|------|
| P7 结果契约（`侧边栏假阳性_框架级结果契约与验证升格重构方案.md`） | 本方案为 P7 的**下一层级**：P7 管"渲染执行 ≠ 挂载可见"（已生效），本方案管"挂载可见 ≠ 可被断言寻址" |
| 调用链聚拢点重构方案（`调用链聚拢点重构方案.md`） | 同一聚拢点方法论（单一权威/唯一入口），应用域从执行链扩展至容器寻址域 |
| 聚合架构设计规范（收敛度/内聚度/边界清晰度） | 收敛度 ≥12 目标见 §6.1；三维度评分在 S5 末复核 |
| `StorageBidirectionalExtensions`（TH-12 实例 S1） | ContainerRegistry 数据落地复用该底层，不新造映射实现 |
| 依赖环校验（references.md 检查 4） | 端口接口放 Share、实现注册于 FrontEndDriven，避免新增 def 环 |

---

> **维护者**：WorkFlowPrj Team
> **关联文档**：
> - `docs/analysis/2026/8.4/侧边栏未加载假阳性分析_validate与控件挂载断链.md`
> - `docs/analysis/2026/8.4/侧边栏假阳性_框架级结果契约与验证升格重构方案.md`
> - `docs/analysis/2026/8/调用链聚拢点重构方案.md`
> - `docs/KnowLedge/聚合架构设计/聚合架构设计规范：从三收拢点到多元聚合的工程范式.md`


