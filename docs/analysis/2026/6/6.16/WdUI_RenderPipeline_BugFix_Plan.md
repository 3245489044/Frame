# WdUI 渲染管线 Bug 修复方案

## 问题描述

在 GUI 模式启动时，`MainLayout.json` 加载完成后报错：

```
[ApplyLayout] 在目标控件 'rootContainer' 中找不到名为 'MainView' 的 Panel。
```

错误信息提示三个可能原因：
1. `CreateLayoutPanelSkeleton` 未正确创建该 Panel
2. Panel 名称与配置中的 View.Name 不匹配
3. Render 管线执行顺序错误（CreateLayoutPanel 应在 ApplyLayout 之前执行）

## 根因分析

### 调用链

```
MainForm.OnInitializeAsync()
  └── ConfigLoader.LoadConfigAsync(_rootContainer, MainLayoutPath)
        └── ConfigContainer.LoadConfigAsync(config)
              └── RenderConfigAsync(config)
                    ├── _orchestrator.RenderFullAsync(this, config, null)  ← 异常发生在此处
                    │     ├── DisposeSession()
                    │     ├── RenderAsync()          ← 基类三阶段：Create→Layout→BindEvents
                    │     │     ├── CreateStage      → ExecuteCreateStage()
                    │     │     │     └── 检查 _sessions[target] → ❌ 找不到活跃会话 → 静默跳过
                    │     │     ├── LayoutStage      → ExecuteLayoutStage()
                    │     │     └── BindEventsStage  → ExecuteBindEventsStage()
                    │     ├── RegisterSubContainers()
                    │     ├── ApplyLayoutAsync()     ← 第二次 ApplyLayout
                    │     └── BindEventsAsync()      ← 第二次 BindEvents
                    └── pipeline.ApplyLayout(this)   ← 第三次 ApplyLayout
```

### 三个框架缺陷

#### 缺陷 1：`_sessions` 字典与基类 `IRenderSession` 的双轨制

**文件：** `Operation/WdUI/WdUI.RenderPipeline.cs`

`WinFormsRenderPipeline` 维护了自己的 `_sessions` 字典：
```csharp
private readonly ConcurrentDictionary<Control, WdUI.Layout.RenderSession> _sessions = new();
```

但基类 `RenderPipelineBase.RenderAsync()` 通过 `CreateSession()` 创建的是另一个 `IRenderSession` 实例：
```csharp
// RenderPipelineBase.cs
public async Task RenderAsync(object target, object config)
{
    var session = CreateSession(target, config);  // 创建基类 session
    // ...
    foreach (var stage in stagesSnapshot)
    {
        await stage.ExecuteAsync(session);  // 传入基类 session
    }
}
```

而 `ExecuteCreateStage` 检查的是 `_sessions` 字典：
```csharp
// WinFormsRenderPipeline.cs
protected override Task ExecuteCreateStage(Control target, IRenderSession session)
{
    if (!_sessions.TryGetValue(target, out var wdSession) ||  // ← 检查 _sessions 字典
        wdSession.Status != WdUI.Layout.RenderSessionStatus.Active)
    {
        // ← 找不到 → 静默跳过，什么都不做！
        return Task.CompletedTask;
    }
    // ... 执行创建逻辑
}
```

**结果：** Create 阶段被静默跳过，`MainView` Panel 从未被创建。

#### 缺陷 2：`ContainerOrchestrator.RenderFullAsync()` 职责重叠

**文件：** `Share/UniversalMiddleware/Container/ContainerOrchestrator.cs`

`RenderFullAsync()` 内部调用 `RenderAsync()`（已包含三阶段），然后又额外调用 `ApplyLayoutAsync()` 和 `BindEventsAsync()`，导致 Layout 和 BindEvents 被重复执行。

#### 缺陷 3：`ConfigContainer.RenderConfigAsync()` 中 ResumeLayout 后的重复 ApplyLayout

**文件：** `Operation/WdUI/UI/WdUI.Container.cs`

`RenderConfigAsync()` 在 `_orchestrator.RenderFullAsync()` 之后又调用了 `pipeline.ApplyLayout(this)`，理由是"ResumeLayout 后父容器有正确尺寸"。但 `RenderFullAsync` 内部已经调用了一次。

---

## 修复方案

### 修改 1：`WinFormsRenderPipeline.cs` — 重写 `CreateSession`

**目的：** 解决 `_sessions` 字典与基类 session 的双轨制问题。

**位置：** `Operation/WdUI/WdUI.RenderPipeline.cs`

**修改内容：** 在 `WinFormsRenderPipeline` 类中重写 `CreateSession` 虚方法：

```csharp
/// <summary>
/// ★ 框架修复：重写 CreateSession，确保基类创建 session 时 WdUI 会话也被注册到 _sessions 字典
/// <para>解决 RenderPipelineBase.RenderAsync() 通过 CreateSession 创建 session 后，
/// ExecuteCreateStage 检查 _sessions 字典时找不到活跃 WdUI 会话的问题。</para>
/// </summary>
protected override IRenderSession CreateSession(object target, object? config)
{
    if (target is Control control && config is UIConfiguration uiConfig)
    {
        // 清理旧会话（防止重复创建）
        DisposeSession(control);
        // 创建新 WdUI 会话并注册到 _sessions 字典
        var wdSession = new WdUI.Layout.RenderSession(uiConfig);
        _sessions[control] = wdSession;
    }
    return base.CreateSession(target, config);
}
```

### 修改 2：`RenderPipelineBase.cs` — 添加 `ExecuteCreateStageOnlyAsync`

**目的：** 提供仅执行 Create 阶段的方法，避免 `ContainerOrchestrator` 调用 `RenderAsync()` 导致 Layout/BindEvents 重复执行。

**位置：** `Share/UniversalMiddleware/Rendering/RenderPipelineBase.cs`

**修改内容：** 添加新方法：

```csharp
/// <summary>
/// ★ 框架修复：仅执行创建阶段（不执行 Layout 和 BindEvents）
/// <para>供 ContainerOrchestrator 分步调用，避免三阶段重复执行。</para>
/// <para>ContainerOrchestrator.RenderFullAsync 应调用此方法替代 RenderAsync，</para>
/// <para>然后自行控制 Layout 和 BindEvents 的调用时机。</para>
/// </summary>
public async Task ExecuteCreateStageOnlyAsync(object target, object config)
{
    var session = CreateSession(target, config);
    EnsureStagesInitialized();
    await ExecuteCreateStage((TTarget)target, session);
}
```

### 修改 3：`ContainerOrchestrator.cs` — 分步调用

**目的：** 消除 `RenderFullAsync` 中 Layout/BindEvents 的重复执行。

**位置：** `Share/UniversalMiddleware/Container/ContainerOrchestrator.cs`

**修改内容：** 将 `RenderFullAsync` 中的 `RenderAsync` 替换为 `ExecuteCreateStageOnlyAsync`：

```csharp
public virtual async Task<(string? applyLayoutError, bool eventsBoundSuccess, string? eventsBoundError)> RenderFullAsync(
    IContainer container, object config, object? sessionContext = null)
{
    if (container == null) throw new ArgumentNullException(nameof(container));
    if (config == null) throw new ArgumentNullException(nameof(config));

    // 1. 清理旧会话
    DisposeSession(container);

    // 2. ★ 框架修复：仅执行创建阶段，避免 Layout/BindEvents 重复
    await _pipeline.ExecuteCreateStageOnlyAsync(container, config);

    // 3. 注册子容器（如果提供了会话上下文）
    if (sessionContext != null)
        RegisterSubContainers(container, sessionContext);

    // 4. 执行布局阶段
    string? applyLayoutError = null;
    try
    {
        await ApplyLayoutAsync(container).ConfigureAwait(false);
    }
    catch (Exception ex)
    {
        applyLayoutError = ex.Message;
    }

    // 5. 执行事件绑定阶段
    bool eventsBoundSuccess = true;
    string? eventsBoundError = null;
    try
    {
        await BindEventsAsync(container).ConfigureAwait(false);
    }
    catch (Exception ex)
    {
        eventsBoundSuccess = false;
        eventsBoundError = ex.Message;
    }

    return (applyLayoutError, eventsBoundSuccess, eventsBoundError);
}
```

### 修改 4（可选）：`IRenderPipeline` 接口 — 添加 `ExecuteCreateStageOnlyAsync`

**目的：** 如果 `ContainerOrchestrator` 通过 `IRenderPipeline` 接口调用，需要在接口中声明新方法。

**位置：** `Share/UniversalMiddleware/Abstractions/IRenderPipeline.cs`

**修改内容：** 添加接口方法声明：

```csharp
/// <summary>
/// 仅执行创建阶段
/// <para>★ 框架修复：供 ContainerOrchestrator 分步调用 ★</para>
/// </summary>
Task ExecuteCreateStageOnlyAsync(object target, object config);
```

---

## 修复后的调用链

```
ConfigContainer.RenderConfigAsync()
  └── _orchestrator.RenderFullAsync(this, config, null)
        ├── DisposeSession()
        ├── _pipeline.ExecuteCreateStageOnlyAsync()   ← ★ 仅创建阶段
        │     └── CreateSession()                     ← ★ 重写：同时注册 WdUI 会话到 _sessions
        │           └── ExecuteCreateStage()
        │                 └── CreateLayoutPanelSkeleton
        │                       └── 创建 MainView Panel ✓
        ├── RegisterSubContainers()
        ├── ApplyLayoutAsync()                        ← 第一次布局（ResumeLayout 后，尺寸正确）
        │     └── ExecuteLayoutStage()
        │           └── ApplyLayoutSkeleton
        │                 └── ControlFinder.FindRecursive(input, "MainView") → 找到 Panel ✓
        └── BindEventsAsync()                         ← 事件绑定
  └── pipeline.ApplyLayout(this)                      ← 第二次布局（ResumeLayout 后，可保留）
```

---

## 影响范围

| 文件 | 修改类型 | 影响 |
|------|---------|------|
| `Operation/WdUI/WdUI.RenderPipeline.cs` | 新增方法重写 | 仅影响 `WinFormsRenderPipeline` 类 |
| `Share/UniversalMiddleware/Rendering/RenderPipelineBase.cs` | 新增方法 | 仅影响 `RenderPipelineBase` 子类 |
| `Share/UniversalMiddleware/Container/ContainerOrchestrator.cs` | 修改方法实现 | 仅影响 `RenderFullAsync` 调用路径 |
| `Share/UniversalMiddleware/Abstractions/IRenderPipeline.cs` | 新增接口方法（可选） | 影响所有 `IRenderPipeline` 实现 |

所有修改均为**新增或修改内部实现**，不改变任何公开 API 签名，完全向后兼容。

---

## 验证方法

修复后，启动 GUI 模式应看到：

1. 日志中不再出现 `找不到名为 'MainView' 的 Panel` 错误
2. `CreateLayoutPanelSkeleton_ExecuteCreateLayoutPanel_start` 日志正常输出
3. `CreateLayoutPanelSkeleton_ExecuteCreateLayoutPanel_after_resume` 日志显示 `MainView` Panel 已创建
4. `ApplyLayoutSkeleton` 成功找到 `MainView` Panel 并应用布局
5. `ConfigContainer_RenderConfigAsync_ApplyLayout_done` 日志正常输出
