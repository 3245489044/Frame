# Share.ControlLookup — 通用控件查找框架

> **文档性质**：重构方案 + 实施记录（重构文档）
> **日期**：2026-08-05
> **范围**：`CsharpPrj/Share/ControlLookup`（新项目）+ 应用侧 `FrontEndDriven` finder 族群收敛
> **目标**：将跨项目散布的「控件查找 finder 族群」（至少 6 实现 / 4 份同名 `IControlFinder`）收敛为「单一查找内核 + 多查找源 + 薄适配面」

---

## 一、背景与动机

项目存在多套并行的「按名找控件」能力（Anti-SingleImplementation 违规，历史 `docs/analysis/2026/7/7.31` 与 `2026/8/8.5` 已登记 R2）：

| 接口 | 签名风格 | 现实现 | 消费者 |
|------|---------|--------|--------|
| `FrameworkAbstractions.WinFormsAbstractions.IControlFinder` | `Control` + 泛型（**官方片面**） | `ControlFinderAdapter` → 转发静态 `ControlFinder` | BackEndDriven `UiCommandSystems`、`StateServiceCollection` |
| `FrameworkAbstractions.Controls.IControlFinder` | `object`（遗留，标废弃） | `ControlRegistryAwareFinder`、`WinFormsControlFinder` | FileDriven 等 |
| `TConfig.Abstractions.IControlFinder` | `object`（遗留，标废弃） | `ControlRegistryAwareFinder` | WdUI / FileDriven |
| `Share/ControlLookup.IControlLookupSource` | `object`/`Control`（**本框架新增**） | `ControlPoolSource` / `EntityTreeLookupSource` / `ControlIndexSource` / `TreeWalkSource` | 内核聚合器 |

**核心矛盾**：
1. 「多级查找（ControlPool→EntityTree→Registry）」与「递归深搜（Controls.Find）」是**不同查找策略**，非重复代码，强行归一会丢能力。
2. `object` 签名遗留接口（TConfig / FrameworkAbstractions.Controls）与 `Control`/泛型单一面**签名不兼容**，物理删除会破坏 CommandFramework/FileDriven/WdUI，需逐消费方迁移。
3. 遍历原语散落：`Controls.Find`、手写 DFS、`TreeWalker`、`ControlTreeWalker`（多套）互不相通。

**本框架不删除接口，而是建立「可组合查找内核」，令所有查找口径收敛为可插拔「源」、由统一聚合器调度。**

---

## 二、设计

### 2.1 核心抽象

```
                    IControlLookupSource（查找源策略）
                    └─ Name / Priority / IsReady
                       └─ FindByName(string)
                       └─ FindByName(Control root, string)
                     
                    ControlLookupRegistry（唯一聚合器）
                    └─ AddSource(按 Priority 升序)
                    └─ FindByName() → ControlLookupResult{Control, Source}   首个命中即返回
```

### 2.2 内置源（`Sources/`）

| 源 | Priority | 语义 | 依赖 |
|----|----------|------|------|
| `ControlPoolSource` | 100 | 按名字槽直接取（O(1)），实体已注册最可靠 | `ECS.Bridge.ControlPool` |
| `EntityTreeLookupSource` | 300 | 跨 `IEntityTreeRoot` 宿主 Form，经实体树按名查 | `IEntityTreeService`、`UniversalMiddleware.Abstractions` |
| `ControlIndexSource` | 1000 | 普通控件名索引（Registry 兜底） | `IControlRegistry`（TConfig） |
| `TreeWalkSource` | 10000 | WinForms 控件树递归深搜（`Controls.Find` + 显式栈 DFS） | 无（框架内 `ControlTraverser`） |

> Priority 语义 = 可信度/首选度，升序尝试；`IsReady=false` 的源被跳过（支持 ControlPool 等延迟就绪的惰性注入）。

### 2.3 统一遍历原语（`Core/ControlTraverser`）

一处收敛「按名 / 谓词 / 祖先 / 后代」递归：
- `FindByName` → 统一 `Controls.Find(name, true)`（与官方单一面语义对齐）；
- `FindFirst/FindAll` → **显式栈 DFS**（避免深层控件树递归溢出）；
- `FindNearestAncestor / IsDescendantOf / GetDepth / FindForm`。

### 2.4 依赖与位置

- **位置**：`CsharpPrj/Share/ControlLookup/`（Share 层新项目），`AssemblyName/RootNamespace = Share.ControlLookup`。
- **依赖**：`FrameworkAbstractions`（官方片面/工具）、`BasicMethod`（ECS.Bridge.ControlPool）、`TConfig`（IControlRegistry）、`UniversalMiddleware`（IEntityTreeService/IEntityTreeRoot）。
- **不反向依赖**应用层（FrontEndDriven 等），从而可被 Share/FrontEndDriven/FileDriven 跨项目复用，避免循环依赖。

---

## 三、构建（已通过）

```bash
dotnet build CsharpPrj/Share/ControlLookup/ControlLookup.csproj -c Debug   # 0 警告 0 错误
```

## 四、应用（FrontEndDriven 三处查找收敛，已通过）

> 三处查找全部应用通用查找框架，框架构建通过 + FrontEndDriven 0 警告 0 错误。

### 4.1 处2 `ControlRegistryAwareFinder`（多级查找）
- 内部持 `ControlLookupRegistry _lookup`；构造注册 `ControlIndexSource`；
- `SetEntityTreeService(entityManager, controlPool)`（惰性，ControlPool 就绪后）注册 `ControlPoolSource` + `EntityTreeLookupSource`；
- `FindByName(string)` 由「手写 ControlPool→EntityTree→Registry 三层 fallback」收敛为一句 `_lookup.FindByName(name)`，保留 `GetEntityTreeService()` 守卫（未注入抛异常）。
- 查找顺序经 Priority（100→300→1000）与改造前完全一致。

### 4.2 处1 `ControlFinder`（静态工具）
- `FindRecursive(parent, name, policy)` 的「按名递归深搜」统一到 `ControlTraverser`（`RecursionPolicy.MaxDepth → ControlTraverser.maxDepth` 对齐）；
- `FindAcrossOpenForms` / `FindByName<T>` / `FindInForm` 均经 `FindRecursive` 走框架；
- 谓词/类型/祖先遍历（`FindAll` / `FindByType` / `IsDescendantOf` / `GetDepth`）**保留** `TreeWalker`/`TreePathFinder`——因其 `IncludeRoot`/`MaxDepth` 口径与框架遍历不同，合并存在行为回归风险，列入未来方向。

### 4.3 处3 `ControlFinderAdapter`（官方单一面）
- 取消对静态 `ControlFinder` 的**纯透传**，改走框架 `TreeWalkSource` / `ControlTraverser`：
  - `FindByName<T>` → `TreeWalkSource.FindByName`（跨 Form 按名）；
  - `FindRecursive` → `ControlTraverser.FindByName`；
  - `FindAcrossForms` 改为框架同款同步 `Application.OpenForms` 遍历（`form.IsDisposed` 过滤），不再依赖应用层 `SafeEnumerator`。

- 构建：`dotnet build FrontEndDriven.csproj -c Debug` → **0 警告 0 错误**。


## 五、行为等价性说明

- `ControlPoolSource.FindByName` 用 `ControlPool.GetControl(name)`（名字槽）——与原 `ControlRegistryAwareFinder` 一致。
- `EntityTreeLookupSource` 跨 `IEntityTreeRoot` 宿主 Form 遍历 —— 与原逻辑一致（框架内以 `Application.OpenForms` 提供 Form，不依赖应用层 `SafeEnumerator`，属有意解耦；若需并发安全枚举可在源内部注入 Form 提供器）。
- `ControlIndexSource` 用 `IControlRegistry.Find(name)` —— 与原 Registry 兜底一致。

## 六、未来方向（不在本次范围）

1. **`ControlFinderAdapter`（WinFormsAbstractions 单一面）** 逐步迁移到 `ControlLookupRegistry` 内核（现为静态 `ControlFinder` 转发），使单一面与多级查找共用同一内核。
2. **`TConfig.IControlFinder` / `FrameworkAbstractions.Controls.IControlFinder`** 的 object 参数消费方（WdUI/FileDriven）逐一迁移到 `WinFormsAbstractions.IControlFinder` 单一面后即可物理删除（已在两接口头注释标注废弃方向）。
3. **`ControlTraverser` 收敛 `TreeWalker` / `ControlTreeWalker`（多套）** 的重复 DFS（保留各自公共 API 签名）。
4. 将 `EntityTreeLookupSource`/`TreeWalkSource` 的 Form 遍历统一注入安全的 `SafeEnumerator` 式遍历（跨线程建图场景）。
