# FileDriven JsonUI 拉伸问题 — 综合分析 & 下一步规划

## 一、问题描述

**现象**：通过 `LoadSideContainer` 加载的 JsonUI（如 ConfigSelector.json、WorkflowRunner.json），在 sideContainer 被用户拖拽拉伸（通过 mainSplitter）时，内部控件不会自适应缩放。

**影响范围**：所有通过 `ConfigContainer.LoadConfigAsync()` 动态加载的 JsonUI。

---

## 二、当前架构分析

### 2.1 渲染管线（6 阶段 Pipeline）

```
RenderPipeline.Render(target, config)
  ├── Stage 1: TrackResizeStage     // 订阅 Resize 事件
  ├── Stage 2: CreateLayoutPanelStage // 创建 LayoutPanel
  ├── Stage 3: CacheConfigStage      // 缓存配置到 State
  ├── Stage 4: ExecuteStrategyStage  // 执行布局策略
  ├── Stage 5: ProcessNestedStage    // 递归处理嵌套控件
  └── Stage 6: ApplySplitterStage    // 应用 Splitter 距离
```

### 2.2 两条管线

| 管线 | 阶段数 | 用途 |
|------|--------|------|
| `_renderPipeline` | 6 阶段 | 首次渲染（完整流程） |
| `_relayoutPipeline` | 4 阶段 | Resize 时重新布局（跳过 CreateLayoutPanel） |

### 2.3 会话管理（已实现）

```csharp
// RenderPipeline.cs
private readonly MemoryCache<Control, RenderSession> _sessions = new();

Render(target, config) {
    DisposeSession(target);           // 清理旧会话
    var session = new RenderSession(config, context, _renderPipeline);
    _sessions.GetOrAdd(target, session);
    _renderPipeline.Execute(target, config, context);
}

Relayout(target) {
    var session = GetActiveSession(target);
    if (session == null) return;
    _relayoutPipeline.ExecuteFrom(target, session.Config, session.Context, 0);
}
```

### 2.4 布局策略

| 策略 | 类型 | 职责 |
|------|------|------|
| `SplitContainerStrategy` | splitcontainer | 设置 SplitterDistance、布局 Panel1/Panel2 子控件、订阅 SplitterMoved |
| `PanelStrategy` | panel | 比例布局内部 Controls、递归处理嵌套容器 |
| `GroupBoxStrategy` | groupbox | 同 PanelStrategy |

---

## 三、问题根因分析

### 根因 1：`TrackResizeStage` 的 Resize 回调引用旧配置（已修复）

**当前代码**（TrackResizeStage.cs）：
```csharp
target.Resize += (s, e) =>
{
    if (s is Control c)
    {
        _renderPipeline.Relayout(c);  // 通过会话获取最新配置
    }
};
```

**修复状态**：✅ 已修复。`Relayout` 方法通过 `GetActiveSession(target)` 获取最新配置。

### 根因 2：`TrackResize` 方法使用了错误的 Pipeline（已修复）

**当前代码**（RenderPipeline.cs）：
```csharp
public void TrackResize(Control target)
{
    var trackStage = (TrackResizeStage)_renderPipeline.Stages[0];
    trackStage.Execute(target, null!, null!);
}
```

**修复状态**：✅ 已修复。直接使用 `_renderPipeline.Stages[0]` 获取 TrackResizeStage，不再创建临时对象。

### 根因 3：`ConvertToProportionalLayout` 修改原始配置对象（已修复）

**当前代码**（MainForm.cs）：
```csharp
var configJson = WdUIComposer.ToJson(config);
var clonedConfig = WdUIComposer.FromJson<UIConfiguration>(configJson);
ConvertToProportionalLayout(clonedConfig, _sideContainer);
```

**修复状态**：✅ 已修复。通过 JSON 序列化/反序列化实现深拷贝，不修改原始配置。

### 根因 4：`sideContainer` 没有 Dock 或布局约束（**核心问题**）

**MainLayout.json** 中 sideContainer 的定义：
```json
{
  "type": "container",
  "name": "sideContainer",
  "dock": "Fill",
  "container": {
    "sourceType": "dynamic",
    "autoLoad": false
  }
}
```

sideContainer 是 `mainSplitter`（SplitContainer）的 Panel2 中的子控件，`dock: "Fill"` 使其填满 Panel2。

**问题**：当 mainSplitter 被拖拽时，Panel2 的尺寸变化，sideContainer 作为 Panel2 的子控件会跟随变化。但 sideContainer **内部**的控件（通过 `LoadSideContainer` 动态加载的）不会自适应，因为：

1. **比例布局只在加载时计算一次**：`ConvertToProportionalLayout` 在加载时将像素值转为比例值，但比例值是基于**加载时**的容器尺寸计算的。
2. **Resize 时 Relayout 管线执行策略**：`ExecuteStrategyStage` 和 `ProcessNestedStage` 会重新执行布局策略，但策略内部使用的是**缓存到 State 中的配置**，这些配置的比例值是基于旧容器尺寸计算的。

### 根因 5：布局策略执行时的容器尺寸问题

**ExecuteStrategyStage** 和 **ProcessNestedStage** 在 Resize 时重新执行布局策略，但：

1. `PanelStrategy` 内部使用 `ApplyLayout` 对子控件应用比例布局
2. `ApplyLayout` 计算子控件位置时，使用的是**父容器当前尺寸** × **配置中的比例值**
3. 比例值在 `ConvertToProportionalLayout` 时已经基于加载时的容器尺寸计算好
4. 当容器尺寸变化时，比例值本身不变，但乘以新的父容器尺寸后，子控件位置会正确变化

**理论上应该能自适应**，但实际测试发现不能。可能的原因：

- **布局策略没有被正确触发**：Resize 时 Relayout 管线执行，但 `ExecuteStrategyStage` 可能因为某些条件判断而跳过执行
- **State 中的配置被覆盖**：`CacheConfigStage` 在 Relayout 时可能覆盖了 State 中的配置
- **SplitContainer 的 Panel2 内部布局问题**：SplitContainer 的 Panel2 本身是一个 Panel，其内部控件的布局可能受 SplitContainer 自身布局逻辑影响

---

## 四、当前代码状态

### 4.1 已完成的修复

| 修复项 | 文件 | 状态 |
|--------|------|------|
| 会话管理（RenderSession） | `WdUI/Layout/RenderSession.cs` | ✅ |
| RenderPipeline 改造（会话管理） | `WdUI/Layout/RenderPipeline.cs` | ✅ |
| TrackResizeStage 改造（通过会话获取配置） | `WdUI/Layout/Pipeline/Stages/TrackResizeStage.cs` | ✅ |
| ConfigContainer 改造（清理旧会话） | `WdUI/UI/ConfigContainer.cs` | ✅ |
| ConvertToProportionalLayout 深拷贝 | `FileDriven/FileDriven.UI/Core/MainForm.cs` | ✅ |
| 通用 Pipeline 框架提取 | `BasicMethod/Pipeline/` | ✅ |

### 4.2 当前构建状态

| 项目 | 状态 |
|------|------|
| `BasicMethod.Pipeline` | ✅ 构建成功 |
| `WdUI` | ❌ 构建失败（`UniversalJsonMiddleware` 中的 `JsonPipelineBuilder` 类型找不到） |
| `FileDriven.UI` | ❌ 依赖 WdUI |

### 4.3 构建失败原因

`UniversalJsonMiddleware` 项目引用了 `BasicMethod.Pipeline`，而 `BasicMethod.Pipeline` 之前引用了 `Common.csproj`（RootNamespace 为 `Common`），导致 `Common` 命名空间冲突。已修改为引用 `BasicMethod.csproj`，但 `UniversalJsonMiddleware` 中的 `JsonPipelineBuilder` 类型找不到——该类型在项目中未定义，可能之前来自 `Common` 程序集或其他地方。

---

## 五、下一步规划

### 阶段 1：修复构建

| 任务 | 说明 | 优先级 |
|------|------|--------|
| 查找 `JsonPipelineBuilder` 定义 | 搜索整个项目，确认该类型来源 | 🔴 高 |
| 修复 `UniversalJsonMiddleware` 构建 | 添加缺失的类型定义或引用 | 🔴 高 |
| 构建验证 | `dotnet build WdUI` + `dotnet build FileDriven.UI` | 🔴 高 |

### 阶段 2：诊断拉伸问题

| 任务 | 说明 | 优先级 |
|------|------|--------|
| 添加调试日志 | 在 `ExecuteStrategyStage` 和 `ProcessNestedStage` 中添加日志，确认 Resize 时是否被调用 | 🔴 高 |
| 验证 `Relayout` 调用链 | 确认 `TrackResizeStage` 的 Resize 回调 → `RenderPipeline.Relayout` → `_relayoutPipeline.ExecuteFrom` 链路完整 | 🔴 高 |
| 检查 `ExecuteStrategyStage` 的 Relayout 路径 | 确认 `__pendingPanels` 不存在时，从 `target.Controls` 查找 Panel 的逻辑正确 | 🟡 中 |
| 检查 `PanelStrategy.ApplyLayout` | 确认比例布局计算逻辑在容器尺寸变化时能正确重新计算 | 🟡 中 |

### 阶段 3：修复拉伸问题

根据诊断结果，可能的修复方案：

| 方案 | 说明 | 复杂度 |
|------|------|--------|
| **方案 A**：确保 Relayout 管线正确执行 | 修复 `ExecuteStrategyStage` 和 `ProcessNestedStage` 在 Relayout 路径下的逻辑 | 低 |
| **方案 B**：在 `sideContainer` 上设置 Dock/Anchor | 让 sideContainer 自身跟随 SplitContainer Panel2 变化 | 低 |
| **方案 C**：Resize 时重新计算比例值 | 在 Relayout 时根据当前容器尺寸重新计算比例值 | 中 |
| **方案 D**：使用 `LayoutMediator` 统一管理布局 | 将比例布局计算逻辑集中到 `LayoutMediator`，Resize 时统一触发 | 高 |

### 阶段 4：测试验证

| 任务 | 说明 |
|------|------|
| 编写单元测试 | 测试 `RenderPipeline.Relayout` 在容器尺寸变化时的行为 |
| 编写集成测试 | 测试 `ConfigContainer.LoadConfigAsync` + Resize 场景 |
| 手动测试 | 运行 FileDriven 应用，加载 ConfigSelector.json，拖拽 mainSplitter 验证拉伸效果 |

---

## 六、架构图

```
┌─────────────────────────────────────────────────────────────────────┐
│                        FileDriven MainForm                          │
│                                                                     │
│  ┌──────────────────────────────────────────────────────────────┐  │
│  │                    rootContainer (ConfigContainer)             │  │
│  │  ┌────────────────────────────────────────────────────────┐  │  │
│  │  │                    mainSplitter (SplitContainer)        │  │  │
│  │  │  ┌──────────────────────┐  ┌────────────────────────┐  │  │  │
│  │  │  │      Panel1          │  │      Panel2            │  │  │  │
│  │  │  │  ┌────────────────┐  │  │  ┌──────────────────┐  │  │  │  │
│  │  │  │  │ contentSplitter│  │  │  │ sideContainer    │  │  │  │  │
│  │  │  │  │ (SplitContainer)│  │  │  │ (ConfigContainer)│  │  │  │  │
│  │  │  │  │ ┌────────────┐ │  │  │  │ ┌──────────────┐ │  │  │  │  │
│  │  │  │  │ │content     │ │  │  │  │ │ Loaded UI   │ │  │  │  │  │
│  │  │  │  │ │Container   │ │  │  │  │ │ (动态加载)  │ │  │  │  │  │
│  │  │  │  │ └────────────┘ │  │  │  │ └──────────────┘ │  │  │  │  │
│  │  │  │  │ ┌────────────┐ │  │  │  └──────────────────┘  │  │  │  │
│  │  │  │  │ │terminal    │ │  │  │                        │  │  │  │
│  │  │  │  │ │Panel       │ │  │  │                        │  │  │  │
│  │  │  │  │ └────────────┘ │  │  │                        │  │  │  │
│  │  │  │  └────────────────┘  │  │                        │  │  │  │
│  │  │  └──────────────────────┘  └────────────────────────┘  │  │  │
│  │  └────────────────────────────────────────────────────────┘  │  │
│  └──────────────────────────────────────────────────────────────┘  │
│                                                                     │
│  Resize 事件流:                                                     │
│  ┌──────────┐    ┌──────────────┐    ┌──────────────────┐          │
│  │ 用户拖拽  │───▶│ mainSplitter │───▶│ Panel2 尺寸变化  │          │
│  │ Splitter  │    │ SplitterMoved│    │                  │          │
│  └──────────┘    └──────────────┘    └────────┬─────────┘          │
│                                               ▼                    │
│  ┌────────────────────────────────────────────────────────────┐    │
│  │ sideContainer.Resize 事件触发                                │    │
│  │   → TrackResizeStage 回调                                    │    │
│  │     → RenderPipeline.Relayout(sideContainer)                 │    │
│  │       → GetActiveSession(sideContainer)                      │    │
│  │         → _relayoutPipeline.ExecuteFrom(0)                   │    │
│  │           → CacheConfigStage                                 │    │
│  │           → ExecuteStrategyStage  ← 这里可能有问题           │    │
│  │           → ProcessNestedStage                               │    │
│  │           → ApplySplitterStage                               │    │
│  └────────────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────────────┘
```

---

## 七、关键文件索引

| 文件 | 用途 |
|------|------|
| `WdUI/Layout/RenderPipeline.cs` | 渲染管线主入口，会话管理 |
| `WdUI/Layout/Pipeline/Stages/TrackResizeStage.cs` | Resize 事件订阅 |
| `WdUI/Layout/Pipeline/Stages/ExecuteStrategyStage.cs` | 执行布局策略 |
| `WdUI/Layout/Pipeline/Stages/ProcessNestedStage.cs` | 递归处理嵌套控件 |
| `WdUI/Layout/Pipeline/Stages/ApplySplitterStage.cs` | 应用 Splitter 距离 |
| `WdUI/UI/ConfigContainer.cs` | 可嵌套的 JSON 配置 UI 容器 |
| `FileDriven/FileDriven.UI/Core/MainForm.cs` | FileDriven 主窗体 |
| `FileDriven/DrivenConfig/JsonConfig/MainLayout.json` | 主布局配置 |
| `BasicMethod/Pipeline/` | 通用 Pipeline 框架 |
| `BasicMethod/UniversalJsonMiddleware/Infrastructure.cs` | JsonPipelineBuilder 使用处（构建失败） |
