# WdUI 管线重构方案 — 基于控制反转（IoC）迁移到 BackEndDriven

> **目标：** 将 WdUI 中的管线编排逻辑迁移到 BackEndDriven，WdUI 只保留纯 UI 渲染职责。
>
> **原则：** 控制反转（IoC）— BackEndDriven 定义抽象接口，WdUI 提供 WinForms 实现，依赖方向为 WdUI → BackEndDriven。

---

## 1. 现状分析

### 1.1 WdUI 当前管线职责全景

```
WinFormsRenderPipeline (WdUI.RenderPipeline.cs)
├── 三阶段生命周期（继承 RenderPipelineBase<Control>）
│   ├── ExecuteCreateStage      → 创建 LayoutPanel + 订阅 Resize
│   ├── ExecuteLayoutStage      → 委托 LayoutPipeline 执行两遍 TreeWalker 布局
│   └── ExecuteBindEventsStage  → 委托 EventBindingOrchestrator 执行事件绑定
├── 分步 API
│   ├── Render(target, config)           → 创建阶段
│   ├── ApplyLayout(target)              → 布局阶段
│   ├── BindEventsAsync(target)          → 事件绑定阶段
│   └── RenderFullAsync(target, config)  → 三阶段合一
├── 会话管理（_sessions 字典 + GetActiveSession）
├── 配置加载（LoadConfigAsync → IConfigLoader）
└── 资源清理（DisposeSession → 解绑事件 + 清理 LayoutPanel + 清理重入保护）

LayoutPipeline (WdUI/Layout/LayoutPipeline.cs)
├── 两遍 TreeWalker 遍历布局
├── SplitterDistance 管理
├── Resize → Relayout 链路
└── 重入保护

ConfigContainer (WdUI/UI/WdUI.Container.cs)
├── 容器管理（子容器创建/注册/查找）
├── 渲染编排（委托 ContainerOrchestrator）
└── 事件管理（ContainerEventManager）
```

### 1.2 职责分类

#### 应迁移到 BackEndDriven（管线编排）

| 职责 | 当前在 WdUI 的位置 | 迁移理由 |
|------|-------------------|---------|
| 三阶段生命周期编排 | `WinFormsRenderPipeline` 继承 `RenderPipelineBase<Control>` | 通用的"执行管线"模式，与 UI 框架无关 |
| 事件绑定编排 | `ExecuteBindEventsStage` → `EventBindingOrchestrator` | 事件绑定已抽象到 Share 层，编排逻辑不应在 WdUI |
| 会话管理 | `_sessions` 字典 + `GetActiveSession` | 会话是"执行上下文"，不是 UI 概念 |
| 配置加载管线 | `LoadConfigAsync` → `IConfigLoader` | 配置加载是后端逻辑 |
| 资源清理编排 | `DisposeSession` → 解绑事件 + 清理 | 清理编排是后端职责 |
| DiagnosticContext 快照输出 | `ExecuteBindEventsStage` 末尾 | 诊断日志不应在 WdUI |

#### 应留在 WdUI（纯 UI 渲染）

| 职责 | 说明 |
|------|------|
| `LayoutPipeline` | 两遍 TreeWalker 布局、SplitterDistance、Resize→Relayout — 纯 WinForms 布局逻辑 |
| `CreateLayoutPanelSkeleton` | 创建 Panel 控件 — 纯 UI 操作 |
| `ConfigContainer` | Panel 容器管理 — WinForms 控件 |
| `ControlFactory` | 控件工厂 — UI 创建 |
| `ViewManager` | Form 管理 — UI 生命周期 |
| WinForms 特有适配器 | ControlFinder、ControlValueAccessor — WinForms 平台实现 |

---

## 2. 架构设计

### 2.1 依赖方向

```
BackEndDriven (Operation 层)
├── 定义 IRenderPipeline 接口（Abstractions）
├── 定义 RenderPipelineOrchestrator（编排器）
└── 依赖：Share.EventBindingOrchestrator

WdUI (Operation 层)
├── 实现 IRenderPipeline 接口
├── 保留：LayoutPipeline、ConfigContainer、ControlFactory
├── 移除：三阶段编排、会话管理、配置加载
└── 依赖：BackEndDriven（接口）

FileDriven.UI (应用层)
├── 通过 DI 注册 IRenderPipeline → WinFormsRenderPipeline
└── 调用 RenderPipelineOrchestrator
```

### 2.2 接口定义

```csharp
// BackEndDriven/Abstractions/IRenderPipeline.cs
namespace BackEndDriven.Abstractions;

/// <summary>
/// 渲染管线接口 — 由 UI 框架（如 WdUI）实现
/// <para>BackEndDriven 通过此接口编排渲染流程，不依赖具体 UI 框架。</para>
/// </summary>
public interface IRenderPipeline
{
    /// <summary>创建阶段：创建容器/面板等 UI 元素</summary>
    Task ExecuteCreateStageAsync(object target, object config);

    /// <summary>布局阶段：执行控件布局</summary>
    Task ExecuteLayoutStageAsync(object target);

    /// <summary>清理指定目标的会话</summary>
    void DisposeSession(object target);
}
```

### 2.3 编排器设计

```csharp
// BackEndDriven/Pipeline/RenderPipelineOrchestrator.cs
namespace BackEndDriven.Pipeline;

/// <summary>
/// 渲染管线编排器 — 控制三阶段生命周期
/// <para>通过 IoC 注入 IRenderPipeline 实现，不依赖具体 UI 框架。</para>
/// </summary>
public class RenderPipelineOrchestrator
{
    private readonly IRenderPipeline _renderPipeline;
    private readonly EventBindingOrchestrator _eventOrchestrator;

    public RenderPipelineOrchestrator(
        IRenderPipeline renderPipeline,
        EventBindingOrchestrator eventOrchestrator)
    {
        _renderPipeline = renderPipeline;
        _eventOrchestrator = eventOrchestrator;
    }

    /// <summary>
    /// 完整三阶段渲染
    /// </summary>
    public async Task RenderFullAsync(object target, object config)
    {
        // 阶段 1：创建
        await _renderPipeline.ExecuteCreateStageAsync(target, config);

        // 阶段 2：布局
        await _renderPipeline.ExecuteLayoutStageAsync(target);

        // 阶段 3：事件绑定
        await _eventOrchestrator.ExecuteWithWalkAsync(target, config);

        // 阶段 4：诊断快照输出（通过 BackEndLog）
        var snapshot = EventBindingDiagnosticContext.GetSnapshotString();
        if (!string.IsNullOrWhiteSpace(snapshot))
        {
            BackEndLog.Log("RenderPipelineOrchestrator_DiagnosticSnapshot",
                new { snapshot });
        }
    }

    /// <summary>
    /// 仅事件绑定（sideload 场景）
    /// </summary>
    public async Task BindEventsAsync(object target, object config)
    {
        await _eventOrchestrator.ExecuteWithWalkAsync(target, config);

        var snapshot = EventBindingDiagnosticContext.GetSnapshotString();
        if (!string.IsNullOrWhiteSpace(snapshot))
        {
            BackEndLog.Log("RenderPipelineOrchestrator_DiagnosticSnapshot",
                new { snapshot });
        }
    }

    /// <summary>
    /// 清理会话
    /// </summary>
    public void DisposeSession(object target)
    {
        // 1. 解绑事件
        _eventOrchestrator.UnbindEventsAsync(target).GetAwaiter().GetResult();
        // 2. 清理 UI 资源
        _renderPipeline.DisposeSession(target);
    }
}
```

---

## 3. 迁移路线图

### Phase 1：接口定义（BackEndDriven）

1. 新建 `BackEndDriven/Abstractions/IRenderPipeline.cs`
2. 新建 `BackEndDriven/Pipeline/RenderPipelineOrchestrator.cs`
3. 在 `backend_log_messages.json` 注册 `RenderPipelineOrchestrator_DiagnosticSnapshot` 等日志 key

### Phase 2：WdUI 适配

1. `WinFormsRenderPipeline` 实现 `IRenderPipeline`
   - `ExecuteCreateStageAsync` → 现有 `ExecuteCreateStage`
   - `ExecuteLayoutStageAsync` → 现有 `ExecuteLayoutStage`
   - `DisposeSession` → 现有 `DisposeSession`（移除事件解绑逻辑）
2. 移除 `WinFormsRenderPipeline` 中的三阶段编排逻辑
   - 删除 `RenderFullAsync`（由 Orchestrator 替代）
   - 删除 `BindEventsAsync`（由 Orchestrator 替代）
   - 删除 `GetActiveSession`（由 Orchestrator 管理会话）
   - 删除 `_sessions` 字典
   - 删除 `LoadConfigAsync`（配置加载由调用方负责）
3. 移除 `ExecuteBindEventsStage` 中的 `DiagnosticSnapshot` 输出
4. 移除 `DisposeSession` 中的事件解绑逻辑（由 Orchestrator 负责）

### Phase 3：调用方迁移

1. **MainForm.ExecuteSideloadPipelineAsync**
   ```csharp
   // 之前
   await WinFormsRenderPipeline.Instance.BindEventsAsync(_sideContainer);

   // 之后
   await _renderOrchestrator.BindEventsAsync(_sideContainer, config);
   ```

2. **ConfigContainer.RenderConfigAsync**
   ```csharp
   // 之前
   await _orchestrator.RenderFullAsync(this, config, null);

   // 之后
   await _renderOrchestrator.RenderFullAsync(this, config);
   ```

3. **ConfigContainer.ClearResources**
   ```csharp
   // 之前
   _orchestrator.DisposeSession(this);

   // 之后
   _renderOrchestrator.DisposeSession(this);
   ```

### Phase 4：清理

1. 删除 `wdui_log_messages.json` 中与管线编排相关的日志 key：
   - `RenderPipeline_ExecuteBindEventsStage_start/done`
   - `RenderPipeline_BindEventsAsync_session_check`
   - `DiagnosticSnapshot`
   - `RenderPipeline_RenderSessionOnly_skipped/done`
   - `RenderPipeline_ApplyLayout_noSession`
   - `RenderPipeline_ApplyLayoutAsync_noSession`
   - `RenderPipeline_Relayout_noSession`
   - `RenderPipeline_ExecuteCreateStage_noSession/done`
   - `RenderPipeline_ExecuteLayoutStage_done`
   - `RenderPipeline_DisposeSession_UnbindError`
2. 删除 `WdUILog.cs` 中不再使用的日志方法（如有）
3. 更新 `WdUI.csproj` 移除不再需要的 ProjectReference（如 `EventBindingOrchestrator.csproj`）

---

## 4. 关键设计决策

| 决策 | 选择 | 理由 |
|------|------|------|
| 接口定义位置 | BackEndDriven.Abstractions | 保持依赖方向：WdUI → BackEndDriven |
| 会话管理归属 | RenderPipelineOrchestrator | 会话是执行上下文，不是 UI 概念 |
| LayoutPipeline 归属 | 留在 WdUI | 纯 WinForms 布局逻辑，与 UI 框架强耦合 |
| DiagnosticContext 输出 | BackEndLog | WdUI 日志被过滤，诊断日志应走 BackEndDriven 日志系统 |
| 事件解绑归属 | RenderPipelineOrchestrator | 事件绑定/解绑是编排职责，与 UI 无关 |
| 配置加载归属 | 调用方（FileDriven.UI） | 配置加载是应用层职责，不是管线职责 |

---

## 5. 风险与注意事项

1. **向后兼容：** `WinFormsRenderPipeline` 的公开 API（`Render`、`ApplyLayout`、`Relayout`）应保留，内部实现委托到 Orchestrator
2. **单例模式：** `WinFormsRenderPipeline.Instance` 仍保留，但内部不再管理会话和编排
3. **LayoutPipeline 依赖：** `LayoutPipeline` 仍通过 `WinFormsRenderPipeline` 暴露，供 `LayoutService` 等外部组件使用
4. **ConfigContainer 的 _orchestrator：** 当前 `ConfigContainer` 内部使用 `ContainerOrchestrator`，迁移后改为 `RenderPipelineOrchestrator`
5. **测试覆盖：** 迁移过程中保持现有测试通过，新增 Orchestrator 单元测试

---

## 6. 附录：文件变更清单

### 新增文件

| 文件 | 说明 |
|------|------|
| `BackEndDriven/Abstractions/IRenderPipeline.cs` | 渲染管线接口 |
| `BackEndDriven/Pipeline/RenderPipelineOrchestrator.cs` | 渲染管线编排器 |

### 修改文件

| 文件 | 变更 |
|------|------|
| `WdUI/WdUI.RenderPipeline.cs` | 实现 IRenderPipeline，移除编排逻辑 |
| `WdUI/UI/WdUI.Container.cs` | 改用 RenderPipelineOrchestrator |
| `FileDriven.UI/Core/MainForm.cs` | 改用 RenderPipelineOrchestrator |
| `BackEndDriven/backend_log_messages.json` | 注册新日志 key |
| `BackEndDriven/WorkFlowMethodBridge/BackEndDrivenBuilder.cs` | 新增 WithRenderPipeline 方法 |

### 删除内容

| 内容 | 说明 |
|------|------|
| `wdui_log_messages.json` 中编排相关 key | 日志迁移到 BackEndLog |
| `WinFormsRenderPipeline._sessions` | 会话管理迁移到 Orchestrator |
| `WinFormsRenderPipeline.RenderFullAsync` | 由 Orchestrator 替代 |
| `WinFormsRenderPipeline.BindEventsAsync` | 由 Orchestrator 替代 |
| `WinFormsRenderPipeline.GetActiveSession` | 由 Orchestrator 管理 |
| `WinFormsRenderPipeline.LoadConfigAsync` | 由调用方负责 |
| `ExecuteBindEventsStage` 中的 DiagnosticSnapshot | 迁移到 Orchestrator |
| `DisposeSession` 中的事件解绑 | 迁移到 Orchestrator |
