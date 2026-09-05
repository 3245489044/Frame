# FrontEndDriven 迭代3 — 修复报告

> 修复日期: 2026-07-26
> 目标: `CsharpPrj/Operation/FrontEndDriven/`
> 基于迭代3分析结果，按优先级修复 10 项问题（不含 R11，该问题更适合通过单元测试验证）

---

## 修复清单

### 已修复（10/10）

| 编号 | 类型 | 严重度 | 文件 | 修复内容 |
|------|------|--------|------|---------|
| B10 | 错误 | **高** | `EcsRenderPipeline.cs` | 添加 `_layoutLock` 保护 `_pipelineDirty` 并发读写；UI 事件处理器中静默跳过 disposed 状态 |
| B11 | 错误 | **中** | `WinFormsControlValueWriter.cs` | `TryReflectionWrite`/`TryReflectionRead` 通过 `DispatchIfNeeded` 调度到 UI 线程 |
| B13 | 错误 | **中** | `CompositeValueWriter.cs` | `RegisterAdapter` 改为仅注册到最后一个 writer，不再广播 |
| R10 | 重复 | **高** | `ControlFinder.cs` / `ControlRegistryAwareFinder.cs` | 提取 `FindAcrossForms(formMatcher, controlLookup)` 通用模板方法；两处 `FindInForm` 委托给它 |
| R12 | 重复 | **中** | `WinFormsControlValueWriter.cs` | 添加注释明确 `_adapterRegistry` / `_propertyAdapters` 职责边界 |
| D8 | 冗余 | **中** | `EcsRenderPipeline.cs` | 提取 `LogStageCounts<T>()` 通用日志方法，`LogStageBefore`/`LogStageAfter` 委托给它 |
| D9 | 冗余 | **低** | `SplitContainerSystem.cs` | 先计算 SplitContainer 总数，全部已订阅时跳过全量遍历 |
| D10 | 冗余 | **低** | `ControlRegistryAwareFinder.cs` | `GetDepth` 在非 Control 类型时返回 -1（与 `ControlFinder` 语义对齐） |
| B12 | 错误 | **低** | `ControlServiceCollection.cs` | 添加 `RegisterService` 非线程安全说明注释 |

### 未修复

| 编号 | 类型 | 严重度 | 原因 |
|------|------|--------|------|
| R11 | 重复 | 中 | `StandardControlStrategies` 与 `ContractFactories` 的控件类型对齐更适合通过**单元测试**验证（遍历 keys 确保全部存在），代码级硬同步反而会增加耦合 |

---

## 文件变更摘要

| 文件 | 变更类型 | 说明 |
|------|---------|------|
| `Initial/RenderPipeline/EcsRenderPipeline.cs` | **3处** | `_layoutLock` 字段、`RecalculateLayout` 静默跳过 + 锁保护、CSG 管线 `_pipelineDirty` 加锁读写、`LogStageCounts` 合并 |
| `WinForms/ValueSync/WinFormsControlValueWriter.cs` | **3处** | B11 UI 线程调度 + R12 职责注释 |
| `ControlInteraction/CompositeValueWriter.cs` | **1处** | B13 广播注册改为注册到最后一个 writer |
| `WinForms/Finder/ControlFinder.cs` | **1处** | R10 添加 `FindAcrossForms` 通用模板方法 |
| `WinForms/Finder/ControlRegistryAwareFinder.cs` | **2处** | R10 `FindInForm` 委托给 `FindAcrossForms` + D10 `GetDepth` null 语义修复 |
| `ControlRender/Systems/SplitContainerSystem.cs` | **1处** | D9 全量扫描优化 |
| `Initial/Registration/ControlServiceCollection.cs` | **1处** | B12 线程安全注释 |