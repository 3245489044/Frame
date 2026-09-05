# WdUI 布局管线 Pipeline 阶段化重构 — 总结与规划

## 一、已完成工作

### 1.1 Pipeline 阶段化重构（6 阶段管线）

将 RenderPipeline 从"一次性执行"重构为"阶段化 Pipeline"，分为 6 个阶段：

| 阶段 | 名称 | 职责 |
|------|------|------|
| 1 | `TrackResizeStage` | 订阅 Resize 事件（`ConditionalWeakTable` 防重复） |
| 2 | `CreateLayoutPanelStage` | 为每个 ViewConfig 创建 LayoutPanel |
| 3 | `CacheConfigStage` | 缓存 ViewConfig 到 LayoutContext.State |
| 4 | `ExecuteStrategyStage` | 执行注册的布局策略（PanelStrategy 等） |
| 5 | `ProcessNestedStage` | 递归处理嵌套控件，缓存子控件配置 |
| 6 | `ApplySplitterStage` | 应用 SplitContainer 初始分割位置 |

**两条管线**：
- **Render 管线**（6 阶段）：完整渲染流程
- **Relayout 管线**（4 阶段）：跳过 CreateLayoutPanel，用于 Resize 时重新布局

### 1.2 基础设施

| 组件 | 职责 |
|------|------|
| `IPipelineStage` | 阶段接口 |
| `PipelineContext` | 阶段间共享数据（`ConcurrentDictionary`） |
| `PipelineBuilder` | Fluent API 构建管线 |
| `Pipeline` | 按顺序执行阶段，支持 `ExecuteFrom(startIndex)` |

### 1.3 布局策略

| 策略 | 类型 | 职责 |
|------|------|------|
| `SplitContainerStrategy` | splitcontainer | 设置 SplitterDistance、布局 Panel1/Panel2 子控件、订阅 SplitterMoved |
| `PanelStrategy` | panel | 比例布局内部 Controls、递归处理嵌套容器 |
| `GroupBoxStrategy` | groupbox | 同 PanelStrategy |

### 1.4 已修复的问题

| 问题 | 修复 |
|------|------|
| Resize 时 CollectLayoutConfigs 收集不到子控件配置 | `ProcessNestedStage` 中缓存子控件 `ControlConfig` 到 State |
| Bottom/Right 处理逻辑重复 | 抽取 `LayoutPositionHelper.ConvertBottomRightToNegative` 共享方法 |
| sideContainer 加载配置后无法自适应拉伸 | `MainForm.ConvertToProportionalLayout` 将绝对像素值转为比例值 |
| contentContainer 和 terminalPanel 之间添加 Splitter | `MainLayout.json` 嵌套第二个 SplitContainer（`contentSplitter`） |

---

## 二、当前存在的问题

### 2.1 核心问题：FileDriven 加载的 JsonUI 无法拉伸

**问题表现**：通过 `LoadSideContainer` 加载的 JsonUI（如 ConfigSelector.json），在容器 Resize 时不会自适应拉伸。

**根因分析**：

#### 根因 1：`TrackResizeStage` 的 Resize 回调引用旧配置

```csharp
// TrackResizeStage.cs 第 25-36 行
target.Resize += (s, e) =>
{
    // 这里捕获的是第一次 Render 时的 config 和 context
    pipeline.ExecuteFrom(c, config, context, startIndex: 1);
    //                          ^^^^^^  ^^^^^^^
    //                          旧配置！旧上下文！
};
```

- `_resizeTracked` 是 `static ConditionalWeakTable`，一旦标记为"已订阅"就不会重新订阅
- 当 `ConfigContainer.RenderConfigAsync` 清空 Controls 并重新 Render 时，Resize 回调仍然引用旧的 `config` 和 `context`
- 导致 Resize 时使用错误的配置计算布局

#### 根因 2：`TrackResize` 方法使用了错误的 Pipeline

```csharp
// RenderPipeline.cs 第 89-102 行
public void TrackResize(Control target)
{
    var stage = new TrackResizeStage();
    var context = new PipelineContext(_context);
    context.Items["__pipeline"] = _renderPipeline;  // ← 使用了 6 阶段管线！
    stage.Execute(target, dummyConfig, context);
}
```

- `TrackResize` 设置 `__pipeline` 为 `_renderPipeline`（6 阶段）
- 但 Resize 时应该使用 `_relayoutPipeline`（4 阶段，跳过 CreateLayoutPanel）
- 虽然 `TrackResizeStage` 会检查 `_resizeTracked` 并跳过自身，但后续阶段 2 `CreateLayoutPanelStage` 会尝试创建新的 LayoutPanel

#### 根因 3：`ConvertToProportionalLayout` 修改原始配置对象

```csharp
// MainForm.cs 第 454-466 行
ConvertToProportionalLayout(config, _sideContainer);
// 直接修改了 config 对象的 Layout 值（像素→比例）
// 如果多次加载同一个文件，第二次加载时 > 1 判断会失效
```

### 2.2 架构缺陷

| 缺陷 | 说明 |
|------|------|
| **无会话管理** | Pipeline 是"无状态的一次性执行器"，无法感知控件内容更新 |
| **上下文不隔离** | 不同 Render 调用共享同一个 `PipelineContext` |
| **生命周期不明确** | 控件销毁或内容更新时，没有清理旧会话的机制 |
| **配置所有权模糊** | `ConvertToProportionalLayout` 直接修改原始配置，违反数据所有权原则 |

---

## 三、通用框架设计：生命周期感知的 Pipeline

### 3.1 核心概念：RenderSession（渲染会话）

每个 `Render` 调用创建一个会话，会话持有自己的配置、上下文和生命周期状态：

```
RenderSession
├── SessionId (Guid)
├── Config (UIConfiguration)          // 当前会话的配置
├── Context (PipelineContext)         // 当前会话的上下文
├── Pipeline (Pipeline)               // 当前会话的管线
├── Status (SessionStatus)            // Active / Disposed
└── Controls (List<Control>)          // 会话管理的控件列表
```

### 3.2 生命周期管理

```
RenderPipeline (改造后)
├── _sessions (Dictionary<Control, RenderSession>)
│
├── Render(target, config)
│   ├── DisposeSession(target)          // 清理旧会话
│   ├── session = new RenderSession(...)
│   ├── _sessions[target] = session
│   └── session.Pipeline.Execute(...)
│
├── Relayout(target)
│   ├── session = _sessions[target]
│   ├── if session == null → return
│   └── session.Pipeline.ExecuteFrom(startIndex: 1)
│
├── GetActiveSession(target)
│   └── return _sessions.TryGetValue(target, out var s) && s.Status == Active ? s : null
│
└── DisposeSession(target)
    ├── session = _sessions[target]
    ├── session.Dispose()               // 取消 Resize 订阅、清理缓存
    ├── _sessions.Remove(target)
    └── // 从 _resizeTracked 中移除（如果有）
```

### 3.3 关键改进

#### 改进 1：Resize 回调通过会话查找获取最新配置

```csharp
// TrackResizeStage 改造后
target.Resize += (s, e) =>
{
    if (s is Control c)
    {
        var session = pipeline.GetActiveSession(c);
        if (session != null)
        {
            session.Pipeline.ExecuteFrom(c, session.Config, session.Context, startIndex: 1);
        }
    }
};
```

#### 改进 2：重新 Render 时自动清理旧会话

```csharp
// ConfigContainer.RenderConfigAsync 改造后
Controls.Clear();
pipeline.DisposeSession(this);  // 清理旧会话
pipeline.Render(this, config);  // 创建新会话
```

#### 改进 3：配置克隆（数据隔离）

```csharp
// MainForm.LoadSideContainer 改造后
var clonedConfig = config.DeepClone();
ConvertToProportionalLayout(clonedConfig, _sideContainer);
_sideContainer.LoadConfig(clonedConfig);
```

### 3.4 与 ComponentLifecycle 的复用关系

| ComponentLifecycle 模式 | RenderPipeline 复用方式 |
|------------------------|----------------------|
| `Dispose()` 统一清理 | `RenderSession.Dispose()` 取消 Resize 订阅、清理缓存 |
| `RegisterHandler/UnregisterHandler` | `RenderPipeline.RegisterStage/UnregisterStage` |
| `ConditionalWeakTable` 自动清理 | 保留现有 `_resizeTracked`，但改为非静态（每个 Pipeline 实例独立） |
| `IExecutionStrategy` 执行策略 | 不适用（WinForms 布局必须在 UI 线程执行） |

**不直接复用 ComponentLifecycle 类**（与 ECS Entity 强耦合），而是**复用其设计模式**。

---

## 四、下一步实施计划

### 阶段 1：创建 RenderSession 模型

| 任务 | 文件 | 说明 |
|------|------|------|
| 创建 `RenderSession` 类 | `WdUI/Layout/RenderSession.cs` | 会话模型，实现 `IDisposable` |
| 创建 `SessionStatus` 枚举 | 同上 | `Active / Disposed` |

### 阶段 2：改造 RenderPipeline

| 任务 | 文件 | 说明 |
|------|------|------|
| 添加 `_sessions` 字典 | `WdUI/Layout/RenderPipeline.cs` | `Dictionary<Control, RenderSession>` |
| 改造 `Render` 方法 | 同上 | 创建/替换会话 |
| 改造 `Relayout` 方法 | 同上 | 通过会话获取配置 |
| 添加 `GetActiveSession` | 同上 | 查找活跃会话 |
| 添加 `DisposeSession` | 同上 | 清理会话 |
| 改造 `TrackResize` | 同上 | 使用 `_relayoutPipeline` |

### 阶段 3：改造 TrackResizeStage

| 任务 | 文件 | 说明 |
|------|------|------|
| 改造 Resize 回调 | `WdUI/Layout/Pipeline/Stages/TrackResizeStage.cs` | 通过 `RenderPipeline.GetActiveSession` 获取最新配置 |
| 添加 `_pipeline` 引用 | 同上 | 持有 `RenderPipeline` 引用而非 `Pipeline` |

### 阶段 4：改造 ConfigContainer

| 任务 | 文件 | 说明 |
|------|------|------|
| 在 `RenderConfigAsync` 中调用 `DisposeSession` | `WdUI/UI/ConfigContainer.cs` | 清空 Controls 前清理旧会话 |

### 阶段 5：改造 MainForm

| 任务 | 文件 | 说明 |
|------|------|------|
| `ConvertToProportionalLayout` 改为操作克隆 | `FileDriven/FileDriven.UI/Core/MainForm.cs` | 不修改原始配置对象 |

### 阶段 6：构建验证

| 任务 | 说明 |
|------|------|
| `dotnet build WdUI` | 验证 WdUI 项目编译通过 |
| `dotnet build FileDriven.UI` | 验证 FileDriven.UI 项目编译通过 |
| 运行测试 | 验证 sideContainer 加载后 Resize 能自适应拉伸 |

---

## 五、架构图

```
┌─────────────────────────────────────────────────────────┐
│                    RenderPipeline                        │
│  ┌──────────────────────────────────────────────────┐   │
│  │              _sessions (Dictionary)               │   │
│  │  ┌──────────────┐  ┌──────────────┐              │   │
│  │  │ Control A    │  │ Control B    │              │   │
│  │  │ └─Session A  │  │ └─Session B  │              │   │
│  │  └──────────────┘  └──────────────┘              │   │
│  └──────────────────────────────────────────────────┘   │
│                                                         │
│  Render(target, config)                                  │
│  ├─ DisposeSession(target)  // 清理旧会话               │
│  ├─ CreateSession(config)   // 创建新会话               │
│  └─ session.Pipeline.Execute(...)                       │
│                                                         │
│  Relayout(target)                                        │
│  ├─ session = GetActiveSession(target)                  │
│  └─ session.Pipeline.ExecuteFrom(1)                     │
│                                                         │
│  DisposeSession(target)                                  │
│  ├─ session.Dispose()                                   │
│  └─ _sessions.Remove(target)                            │
└─────────────────────────────────────────────────────────┘
         │
         │ TrackResizeStage 通过 GetActiveSession 获取最新配置
         ▼
┌─────────────────────────────────────────────────────────┐
│                    TrackResizeStage                      │
│                                                         │
│  target.Resize += (s, e) =>                             │
│  {                                                      │
│      var session = pipeline.GetActiveSession(c);        │
│      session?.Pipeline.ExecuteFrom(1);                  │
│  }                                                      │
└─────────────────────────────────────────────────────────┘
         │
         │ 使用会话中的最新配置
         ▼
┌─────────────────────────────────────────────────────────┐
│                    Relayout Pipeline                     │
│  1. CacheConfigStage    // 重新缓存配置                 │
│  2. ExecuteStrategyStage // 重新执行策略                │
│  3. ProcessNestedStage   // 重新处理嵌套                │
│  4. ApplySplitterStage   // 重新应用分割器              │
└─────────────────────────────────────────────────────────┘
