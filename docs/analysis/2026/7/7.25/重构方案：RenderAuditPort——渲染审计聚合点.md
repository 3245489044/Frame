# 重构方案：RenderAuditPort——渲染审计聚合点

> **副标题**：为 WdUI 渲染管线构建可量化的审计能力
>
> **版本**：v1.0 | **日期**：2026-07-24
> **关键词**：渲染审计、面聚合、可审计性、RenderAuditPort、聚合架构
> **关联文档**：[聚合架构设计规范：从三收拢点到多元聚合的工程范式](./聚合架构设计规范：从三收拢点到多元聚合的工程范式.md)

---

## 目录

- [重构方案：RenderAuditPort——渲染审计聚合点](#重构方案renderauditport渲染审计聚合点)
  - [目录](#目录)
  - [一、问题背景](#一问题背景)
    - [1.1 当前问题](#11-当前问题)
    - [1.2 问题定位](#12-问题定位)
    - [1.3 根因分析](#13-根因分析)
  - [二、方案概述](#二方案概述)
    - [2.1 方案定位](#21-方案定位)
    - [2.2 设计目标](#22-设计目标)
    - [2.3 非目标](#23-非目标)
  - [三、设计规范对照](#三设计规范对照)
    - [3.1 聚合点识别](#31-聚合点识别)
    - [3.2 聚合边界定义](#32-聚合边界定义)
    - [3.3 聚合度评估](#33-聚合度评估)
  - [四、详细设计](#四详细设计)
    - [4.1 拓扑结构](#41-拓扑结构)
    - [4.2 输出契约：RenderAuditReport](#42-输出契约renderauditreport)
    - [4.3 核心方法：Collect](#43-核心方法collect)
    - [4.4 数据源映射](#44-数据源映射)
    - [4.5 调用位置](#45-调用位置)
  - [五、实现计划](#五实现计划)
    - [5.1 步骤分解](#51-步骤分解)
    - [5.2 文件清单](#52-文件清单)
    - [5.3 依赖关系](#53-依赖关系)
  - [六、验证方案](#六验证方案)
    - [6.1 单元测试](#61-单元测试)
    - [6.2 集成验证](#62-集成验证)
    - [6.3 验收标准](#63-验收标准)
  - [七、风险与应对](#七风险与应对)

---

## 一、问题背景

### 1.1 当前问题

在 FileDriven 框架的 CLI 模式执行过程中，WdUI 渲染管线执行完毕后，**无法通过数据判断 UI 是否真正渲染到了 WinForms 控件树**。当前日志仅记录了：

```
[DEBUG] CsgEntryPoint: ECS World 状态收集完成, entityCount=23
[DEBUG] WinFormsOutputPort: 开始应用 CanonicalForm, isFirstApply=true
```

这些日志只反映了**过程状态**，没有反映**结果状态**——即控件树的实际渲染效果。

### 1.2 问题定位

渲染管线中有 6 个关键数据节点，各自输出不同维度的数据，但**没有统一的消费入口**：

| # | 节点 | 位置 | 当前日志状态 |
|---|------|------|-------------|
| 1 | ConfigDrivenControlFactory.Convert | `.../Creation/ConfigDrivenControlFactory.cs:70` | 无日志 |
| 2 | EcsCapabilityApplier.Update | `.../Systems/EcsCapabilityApplier.cs` | 无日志 |
| 3 | CsgEntryPoint.Collect | `.../Ports.cs:64` | 仅 entityCount |
| 4 | WinFormsOutputPort.Apply | `.../Ports.cs:340` | 仅 isFirstApply |
| 5 | UIReconciler.Apply | `.../RenderPipeline/UIReconciler.cs:50` | 仅空跳过 |
| 6 | WinFormsUIAdapter.ToCanonical | `.../Adapter/WinFormsUIAdapter.cs:56` | 无日志 |

### 1.3 根因分析

对照[聚合架构设计规范](./聚合架构设计规范：从三收拢点到多元聚合的工程范式.md)，当前状态属于**分散聚合（聚合不足）** 反模式：

> **症状**：应该聚合的操作分散在多个地方，没有统一的入口。
>
> **理论分析**：违反了**收敛度**要求。分散聚合的收敛度低（<50%），聚合点形同虚设。

6 个数据节点各自输出不同格式的数据，调用方需要了解 6 个不同类的内部细节才能获取完整的渲染状态。这违反了聚合架构的**可审计性**核心价值——"所有关键操作都经过明确的入口/出口，形成可追踪的审计链"。

---

## 二、方案概述

### 2.1 方案定位

构建一个**面聚合**类型的审计收拢点——`RenderAuditPort`，作为渲染管线执行完毕后，收集所有渲染量化数据的**唯一入口**。

```
渲染管线执行完毕
  ↓
RenderAuditPort.Collect()  ← 面聚合：一次调用收集所有渲染数据
  ├── Entity 创建统计
  ├── 控件实例统计
  ├── CanonicalForm 快照
  ├── Apply 结果
  ├── 差异比较摘要
  └── 控件树实际状态
  ↓
输出统一的 RenderAuditReport（结构化数据）
```

### 2.2 设计目标

| 目标 | 说明 | 衡量标准 |
|------|------|---------|
| **可审计** | 一次调用获取完整的渲染状态 | 调用方只需了解 `RenderAuditPort` 一个类 |
| **零侵入** | 不修改现有聚合点的内部实现 | 只读取现有数据，不增加新依赖 |
| **可扩展** | 新增审计维度只需扩展 `Collect()` | 不修改调用方代码 |
| **低开销** | 审计不影响渲染性能 | 仅在调试/诊断模式下启用 |

### 2.3 非目标

- ❌ 不参与渲染管线的主流程
- ❌ 不修改任何 ECS World 状态
- ❌ 不触发任何控件操作
- ❌ 不替代现有的日志系统

---

## 三、设计规范对照

### 3.1 聚合点识别

对照[聚合点识别原则](../聚合架构设计规范：从三收拢点到多元聚合的工程范式.md#一聚合点识别原则)：

| 原则 | 判断 | 说明 |
|------|------|------|
| **原则 1：外部依赖收敛** | ✅ 适用 | 6 个数据节点分布在 6 个不同类中，调用方需要了解所有 6 个类的内部细节 |
| **原则 2：数据流边界收敛** | ✅ 适用 | 渲染管线执行完毕是"ECS World → 控件树"的数据流边界，审计数据在此边界处被消费 |
| **原则 3：能力出口收敛** | ✅ 适用 | "渲染审计"是一个完整的能力域，需要一个统一的入口 |
| **原则 4：变更热点收敛** | ⚠️ 部分适用 | 当前变更频率不高，但每次新增审计维度都需要修改多个调用方 |

### 3.2 聚合边界定义

| 维度 | 内容 |
|------|------|
| **名称** | `RenderAuditPort` |
| **拓扑类型** | 面聚合（Facade Aggregation） |
| **职责** | 收集渲染管线各阶段的量化数据，输出统一审计报告 |
| **不负责** | 不修改任何状态，不触发任何渲染操作 |
| **契约** | `RenderAuditReport Collect(...)` — 纯读取，无副作用 |
| **位置** | `FrontEndDriven/Diagnostics/RenderAuditPort.cs` |

### 3.3 聚合度评估

| 维度 | 评估 | 说明 |
|------|------|------|
| **内聚度** | 功能内聚 ✅ | 所有方法服务于"渲染审计"这一单一功能 |
| **收敛度** | 完全收敛 ✅ | 所有渲染校验数据都经过这一个点输出 |
| **边界清晰度** | 清晰 ✅ | 只读不写，不与其他聚合点职责重叠 |

**聚合度评分**：9/9（优秀）

---

## 四、详细设计

### 4.1 拓扑结构

```
RenderAuditPort (面聚合)
  │
  ├── 依赖 EcsInputPort       → Entity 创建统计
  ├── 依赖 ControlPool        → 控件实例统计
  ├── 依赖 CsgEntryPoint      → CanonicalForm 快照
  ├── 依赖 WinFormsOutputPort → Apply 结果
  ├── 依赖 UIReconciler       → 差异比较摘要
  └── 依赖 WinFormsUIAdapter  → 控件树实际状态
```

与三收拢点的关系（旁路审计）：

```
EcsInputPort → [ECS World] → CsgEntryPoint → WinFormsOutputPort → 控件树
                                                                    ↑
                                                              RenderAuditPort
                                                              面聚合，纯读取
```

### 4.2 输出契约：RenderAuditReport

```csharp
/// <summary>
/// 渲染审计报告 — 包含渲染管线各阶段的量化数据
/// <para>由 RenderAuditPort.Collect() 生成，纯数据结构，无行为。</para>
/// </summary>
public class RenderAuditReport
{
    // ========== 元数据 ==========
    /// <summary>审计时间戳</summary>
    public DateTime Timestamp { get; set; }
    
    /// <summary>管线名称</summary>
    public string PipelineName { get; set; } = string.Empty;
    
    /// <summary>是否为首次渲染</summary>
    public bool IsFirstApply { get; set; }

    // ========== ① Entity 层 ==========
    /// <summary>Entity 总数</summary>
    public int TotalEntities { get; set; }
    
    /// <summary>控件类型分布（type → count）</summary>
    public Dictionary<string, int> EntityTypeDistribution { get; set; } = new();

    // ========== ② 控件实例层 ==========
    /// <summary>成功创建的控件数</summary>
    public int TotalControlsCreated { get; set; }
    
    /// <summary>创建失败的控件数</summary>
    public int FailedControls { get; set; }
    
    /// <summary>已注册到 ControlPool 的控件数</summary>
    public int ControlsInPool { get; set; }

    // ========== ③ CanonicalForm 层 ==========
    /// <summary>CanonicalForm 节点数</summary>
    public int CanonicalNodeCount { get; set; }
    
    /// <summary>有布局信息的节点数</summary>
    public int NodesWithLayout { get; set; }
    
    /// <summary>有样式信息的节点数</summary>
    public int NodesWithStyle { get; set; }
    
    /// <summary>有事件绑定的节点数</summary>
    public int NodesWithEvents { get; set; }

    // ========== ④ 渲染应用层 ==========
    /// <summary>成功应用的节点数</summary>
    public int AppliedNodeCount { get; set; }
    
    /// <summary>未找到对应控件的节点数</summary>
    public int MissedNodeCount { get; set; }

    // ========== ⑤ 差异比较层 ==========
    /// <summary>新增节点数</summary>
    public int AddedNodes { get; set; }
    
    /// <summary>删除节点数</summary>
    public int RemovedNodes { get; set; }
    
    /// <summary>属性变更数</summary>
    public int ChangedProperties { get; set; }

    // ========== ⑥ 控件树实际状态快照 ==========
    /// <summary>控件快照列表</summary>
    public List<ControlSnapshot> ControlSnapshots { get; set; } = new();
}

/// <summary>
/// 控件快照 — 从 WinForms 控件树直接读取的实际状态
/// </summary>
public class ControlSnapshot
{
    /// <summary>控件名称</summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>控件类型（如 Button、Panel、TextBox）</summary>
    public string Type { get; set; } = string.Empty;
    
    /// <summary>位置 X</summary>
    public int Left { get; set; }
    
    /// <summary>位置 Y</summary>
    public int Top { get; set; }
    
    /// <summary>宽度</summary>
    public int Width { get; set; }
    
    /// <summary>高度</summary>
    public int Height { get; set; }
    
    /// <summary>文本</summary>
    public string Text { get; set; } = string.Empty;
    
    /// <summary>是否可见</summary>
    public bool Visible { get; set; }
    
    /// <summary>是否启用</summary>
    public bool Enabled { get; set; }
}
```

### 4.3 核心方法：Collect

```csharp
/// <summary>
/// 渲染审计聚合点 — 收集渲染管线各阶段的量化数据
/// <para>面聚合类型，纯读取，无副作用。</para>
/// </summary>
public static class RenderAuditPort
{
    /// <summary>
    /// 收集渲染审计数据
    /// </summary>
    /// <param name="entityManager">Entity 管理器（用于统计 Entity）</param>
    /// <param name="controlPool">控件池（用于统计控件实例）</param>
    /// <param name="csgEntryPoint">CSG 入口（用于获取 CanonicalForm 快照）</param>
    /// <param name="winFormsOutputPort">WinForms 输出端口（用于获取 Apply 结果）</param>
    /// <param name="reconciler">UI 差异比较引擎（用于获取差异摘要）</param>
    /// <param name="winFormsAdapter">WinForms UI 适配器（用于获取控件树实际状态）</param>
    /// <returns>统一的渲染审计报告</returns>
    public static RenderAuditReport Collect(
        EntityManager entityManager,
        ControlPool controlPool,
        CsgEntryPoint csgEntryPoint,
        WinFormsOutputPort winFormsOutputPort,
        UIReconciler reconciler,
        WinFormsUIAdapter winFormsAdapter)
    {
        var report = new RenderAuditReport
        {
            Timestamp = DateTime.UtcNow,
            IsFirstApply = winFormsOutputPort?.IsFirstApply ?? true
        };

        // ① Entity 层统计
        CollectEntityStats(report, entityManager);

        // ② 控件实例层统计
        CollectControlStats(report, controlPool);

        // ③ CanonicalForm 层统计
        CollectCanonicalFormStats(report, csgEntryPoint);

        // ④ 渲染应用层统计
        CollectApplyStats(report, winFormsOutputPort);

        // ⑤ 差异比较层统计
        CollectDiffStats(report, reconciler);

        // ⑥ 控件树实际状态快照
        CollectControlSnapshots(report, winFormsAdapter);

        return report;
    }

    private static void CollectEntityStats(RenderAuditReport report, EntityManager entityManager) { /* ... */ }
    private static void CollectControlStats(RenderAuditReport report, ControlPool controlPool) { /* ... */ }
    private static void CollectCanonicalFormStats(RenderAuditReport report, CsgEntryPoint csgEntryPoint) { /* ... */ }
    private static void CollectApplyStats(RenderAuditReport report, WinFormsOutputPort winFormsOutputPort) { /* ... */ }
    private static void CollectDiffStats(RenderAuditReport report, UIReconciler reconciler) { /* ... */ }
    private static void CollectControlSnapshots(RenderAuditReport report, WinFormsUIAdapter winFormsAdapter) { /* ... */ }
}
```

### 4.4 数据源映射

| 审计维度 | 数据来源 | 获取方式 | 当前状态 |
|---------|---------|---------|---------|
| Entity 总数 | `EntityManager.GetEntities<ControlTypeComponent>()` | 遍历计数 | 无 |
| 控件类型分布 | `ControlTypeComponent.Type` | 分组统计 | 无 |
| 控件创建数 | `ControlPool.GetAllEntries().Count()` | 直接读取 | 无 |
| CanonicalForm 节点数 | `CsgEntryPoint.Collect().Nodes.Count` | 调用 Collect | 仅 entityCount |
| 布局/样式/事件覆盖率 | CanonicalForm 节点属性 | 属性存在性检查 | 无 |
| Apply 应用数 | `WinFormsOutputPort.Apply` 返回值 | 记录返回值 | 无 |
| 差异比较摘要 | `UIReconciler.GetDiffSummary()` | 调用 GetDiffSummary | 无 |
| 控件树实际状态 | `WinFormsUIAdapter.ToCanonical()` | 调用 ToCanonical | 无 |

### 4.5 调用位置

在 `EcsRenderPipeline.ExecutePipeline()` 末尾，`PipelineCompleted?.Invoke()` 之后插入：

```csharp
// 文件：CsharpPrj/Operation/FrontEndDriven/Initial/RenderPipeline/EcsRenderPipeline.cs
// 方法：ExecutePipeline()，第 345 行之后

PipelineCompleted?.Invoke();

// ★ 2026-07-24 新增：渲染审计 — 收集渲染管线各阶段的量化数据
if (_csgEntryPoint != null && _winFormsOutputPort != null)
{
    var auditReport = RenderAuditPort.Collect(
        _entityManager!, _controlPool!, _csgEntryPoint,
        _winFormsOutputPort, _uiReconciler!, _winFormsAdapter!);
    FrontEndDrivenLog.Log("EcsRenderPipeline_AuditReport", auditReport);
}
```

---

## 五、实现计划

### 5.1 步骤分解

| 步骤 | 内容 | 涉及文件 | 预估工时 |
|------|------|---------|---------|
| 1 | 创建 `RenderAuditReport` 数据类 | `新建：FrontEndDriven/Diagnostics/RenderAuditReport.cs` | 0.5h |
| 2 | 创建 `ControlSnapshot` 数据类 | 同上文件 | 0.2h |
| 3 | 创建 `RenderAuditPort` 静态类 | `新建：FrontEndDriven/Diagnostics/RenderAuditPort.cs` | 1h |
| 4 | 实现 6 个 `Collect*` 私有方法 | 同上文件 | 2h |
| 5 | 在 `EcsRenderPipeline.ExecutePipeline()` 中插入调用 | `修改：.../RenderPipeline/EcsRenderPipeline.cs` | 0.3h |
| 6 | 编译验证 | — | 0.5h |
| 7 | 运行 CLI 模式验证日志输出 | — | 0.5h |

**总计预估工时**：约 5 小时

### 5.2 文件清单

| 操作 | 文件路径 |
|------|---------|
| **新建** | `CsharpPrj/Operation/FrontEndDriven/Diagnostics/RenderAuditReport.cs` |
| **新建** | `CsharpPrj/Operation/FrontEndDriven/Diagnostics/RenderAuditPort.cs` |
| **修改** | `CsharpPrj/Operation/FrontEndDriven/Initial/RenderPipeline/EcsRenderPipeline.cs` |

### 5.3 依赖关系

```
RenderAuditPort
  ├── 依赖 ECS.Core.EntityManager（已有）
  ├── 依赖 ECS.Bridge.ControlPool（已有）
  ├── 依赖 Ports.CsgEntryPoint（已有）
  ├── 依赖 Ports.WinFormsOutputPort（已有）
  ├── 依赖 FrontEndDriven.RenderPipeline.UIReconciler（已有）
  └── 依赖 FrontEndDriven.WinForms.Adapter.WinFormsUIAdapter（已有）
```

**零新增依赖**：所有依赖都是现有类型，`RenderAuditPort` 只引用不修改。

---

## 六、验证方案

### 6.1 单元测试

| 测试用例 | 验证点 | 预期结果 |
|---------|--------|---------|
| 空 ECS World 的审计报告 | Entity 数为 0 时的输出 | TotalEntities=0, ControlSnapshots=空 |
| 单控件的审计报告 | 一个 Button 的渲染数据 | TotalEntities=1, 控件类型=Button |
| 多控件的审计报告 | 多个不同类型控件 | 类型分布正确 |
| 首次渲染的审计报告 | IsFirstApply=true | AppliedNodeCount > 0 |
| 增量渲染的审计报告 | IsFirstApply=false | 差异比较数据正确 |

### 6.2 集成验证

1. 运行 CLI 模式（`math_browser_simple_test.json`）
2. 检查日志中是否包含 `EcsRenderPipeline_AuditReport` 条目
3. 验证报告中的 `ControlSnapshots` 是否包含预期的控件列表
4. 对比 `CanonicalNodeCount` 和 `TotalControlsCreated` 是否一致

### 6.3 验收标准

| # | 标准 | 验证方式 |
|---|------|---------|
| 1 | `RenderAuditPort.Collect()` 可在任意时刻调用，不抛出异常 | 单元测试 |
| 2 | 审计报告包含 6 个维度的完整数据 | 日志检查 |
| 3 | 审计报告中的控件快照与 WinForms 控件树实际状态一致 | 手动对比 |
| 4 | 审计不影响渲染管线的主流程 | 管线执行时间无明显增加 |
| 5 | 新增审计维度只需扩展 `Collect()` 方法 | 代码审查 |

---

## 七、风险与应对

| 风险 | 概率 | 影响 | 应对措施 |
|------|------|------|---------|
| `WinFormsUIAdapter.ToCanonical()` 在非 UI 线程调用时抛出异常 | 低 | 高 | 在 `Collect()` 中添加 try-catch，失败时记录错误而非抛出 |
| 审计数据过大影响日志性能 | 低 | 中 | 控件快照超过 50 个时只输出摘要统计，不输出完整列表 |
| 新增依赖导致循环引用 | 低 | 高 | `RenderAuditPort` 只引用现有类型，不新增项目级依赖 |
| 审计报告与控件树实际状态不一致 | 中 | 中 | 在 `Collect()` 中直接调用 `ToCanonical()` 获取实时状态，而非缓存状态 |
