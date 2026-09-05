# WdUI 布局管线 Pipeline 阶段化重构方案

## 一、背景与动机

### 1.1 当前架构问题

`RenderPipeline.Render` 方法目前将所有步骤硬编码在一个方法中：

```csharp
public void Render(Control target, UIConfiguration config)
{
    TrackResize(target);                    // 步骤1: 订阅 Resize
    // ... 创建 LayoutPanel                // 步骤2: 创建布局面板
    // ... 缓存配置到 State                // 步骤3: 缓存配置
    // ... 执行布局策略                    // 步骤4: 执行策略
    // ... 递归处理嵌套控件                // 步骤5: 递归处理
    // ... 应用 Splitter 距离              // 步骤6: 应用分割器
}
```

**问题：**
1. **不可扩展**：新增步骤必须修改 `Render` 方法本身
2. **不可复用**：`Relayout` 需要跳过"创建 LayoutPanel"步骤，但无法复用中间逻辑
3. **不可测试**：无法单独测试某个阶段
4. **不可替换**：无法为特定场景替换某个阶段

### 1.2 参考 SkeletonFlow 的设计模式

`SkeletonFlow` 提供了成熟的 Pipeline 编排模式：

| SkeletonFlow 模式 | 说明 | 可借鉴点 |
|------------------|------|---------|
| `ISkeleton<TInput, TOutput>` | 可组合的执行单元 | `IPipelineStage` |
| `Then()` 链式组合 | 顺序组合多个骨架 | 阶段链式组合 |
| `SkeletonContext` | 上下文数据共享 | `PipelineContext` |
| `SequentialSkeleton` | 自动接口匹配 | 阶段自动编排 |

---

## 二、目标

1. **阶段化**：将 `Render` 拆分为独立的 Pipeline 阶段
2. **可组合**：阶段之间通过链式 API 组合
3. **可复用**：`Render` 和 `Relayout` 共享中间阶段
4. **可扩展**：新增阶段无需修改现有代码
5. **可测试**：每个阶段可独立单元测试

---

## 三、架构设计

### 3.1 核心接口

```csharp
/// <summary>
/// Pipeline 阶段接口
/// 每个阶段是一个独立的执行单元
/// </summary>
public interface IPipelineStage
{
    /// <summary>阶段名称（用于日志和调试）</summary>
    string Name { get; }

    /// <summary>
    /// 执行阶段
    /// </summary>
    /// <param name="target">目标控件</param>
    /// <param name="config">UI 配置</param>
    /// <param name="context">Pipeline 上下文（共享数据）</param>
    void Execute(Control target, UIConfiguration config, PipelineContext context);
}

/// <summary>
/// Pipeline 上下文
/// 在阶段之间共享数据，类似 SkeletonContext
/// </summary>
public class PipelineContext
{
    /// <summary>运行时数据存储（线程安全）</summary>
    public ConcurrentDictionary<string, object> Items { get; } = new();

    /// <summary>布局上下文（来自 RenderPipeline）</summary>
    public ILayoutContext LayoutContext { get; }

    /// <summary>当前阶段索引（用于调试）</summary>
    public int CurrentStageIndex { get; set; }

    /// <summary>取消令牌</summary>
    public CancellationToken CancellationToken { get; set; }

    public PipelineContext(ILayoutContext layoutContext)
    {
        LayoutContext = layoutContext;
    }
}
```

### 3.2 Pipeline 构建器

```csharp
/// <summary>
/// Pipeline 构建器
/// 支持链式组合阶段，类似 Flow.Then()
/// </summary>
public class PipelineBuilder
{
    private readonly List<IPipelineStage> _stages = new();

    public PipelineBuilder Then(IPipelineStage stage)
    {
        _stages.Add(stage);
        return this;
    }

    public Pipeline Build() => new Pipeline(_stages.AsReadOnly());
}

/// <summary>
/// Pipeline 执行器
/// 按顺序执行所有阶段
/// </summary>
public class Pipeline
{
    private readonly IReadOnlyList<IPipelineStage> _stages;

    public Pipeline(IReadOnlyList<IPipelineStage> stages)
    {
        _stages = stages;
    }

    public void Execute(Control target, UIConfiguration config, PipelineContext context)
    {
        for (int i = 0; i < _stages.Count; i++)
        {
            context.CurrentStageIndex = i;
            _stages[i].Execute(target, config, context);
        }
    }

    /// <summary>
    /// 从指定索引开始执行（用于 Relayout 跳过前面的阶段）
    /// </summary>
    public void ExecuteFrom(Control target, UIConfiguration config, PipelineContext context, int startIndex)
    {
        for (int i = startIndex; i < _stages.Count; i++)
        {
            context.CurrentStageIndex = i;
            _stages[i].Execute(target, config, context);
        }
    }
}
```

### 3.3 阶段定义

```csharp
// ===== 阶段1: 订阅 Resize =====
public class TrackResizeStage : IPipelineStage
{
    public string Name => "TrackResize";

    public void Execute(Control target, UIConfiguration config, PipelineContext context)
    {
        var pipeline = context.Items["__pipeline"] as Pipeline
            ?? throw new InvalidOperationException("Pipeline not found in context");

        // 使用 ConditionalWeakTable 防重复订阅
        if (!_resizeTracked.TryGetValue(target, out _))
        {
            _resizeTracked.Add(target, null);
            target.Resize += (s, e) =>
            {
                if (s is Control c)
                {
                    // Resize 时从阶段2开始执行（跳过 TrackResize）
                    pipeline.ExecuteFrom(c, config, context, startIndex: 1);
                }
            };
        }
    }

    private static readonly ConditionalWeakTable<Control, object> _resizeTracked = new();
}

// ===== 阶段2: 创建 LayoutPanel =====
public class CreateLayoutPanelStage : IPipelineStage
{
    public string Name => "CreateLayoutPanel";

    public void Execute(Control target, UIConfiguration config, PipelineContext context)
    {
        var pending = new List<(Panel panel, ViewConfig view)>();

        target.SuspendLayout();
        try
        {
            foreach (var view in config.Views)
            {
                var panel = new Panel
                {
                    Name = view.Name,
                    Dock = DockStyle.Fill
                };
                target.Controls.Add(panel);
                pending.Add((panel, view));
            }
        }
        finally
        {
            target.ResumeLayout(true);
        }

        context.Items["__pendingPanels"] = pending;
    }
}

// ===== 阶段3: 缓存配置到 State =====
public class CacheConfigStage : IPipelineStage
{
    public string Name => "CacheConfig";

    public void Execute(Control target, UIConfiguration config, PipelineContext context)
    {
        var pending = context.Items["__pendingPanels"] as List<(Panel panel, ViewConfig view)>
            ?? throw new InvalidOperationException("No pending panels found");

        foreach (var (panel, view) in pending)
        {
            var wrapperConfig = new ControlConfig
            {
                Type = "panel",
                Name = view.Name,
                Controls = view.Controls
            };

            context.LayoutContext.State.GetOrCreateValue(panel).Config = wrapperConfig;
        }
    }
}

// ===== 阶段4: 执行布局策略 =====
public class ExecuteStrategyStage : IPipelineStage
{
    public string Name => "ExecuteStrategy";

    public void Execute(Control target, UIConfiguration config, PipelineContext context)
    {
        var pending = context.Items["__pendingPanels"] as List<(Panel panel, ViewConfig view)>
            ?? throw new InvalidOperationException("No pending panels found");

        foreach (var (panel, view) in pending)
        {
            var wrapperConfig = new ControlConfig
            {
                Type = "panel",
                Name = view.Name,
                Controls = view.Controls
            };

            context.LayoutContext.TryExecuteStrategy(panel, wrapperConfig);
        }
    }
}

// ===== 阶段5: 递归处理嵌套控件 =====
public class ProcessNestedStage : IPipelineStage
{
    public string Name => "ProcessNested";

    public void Execute(Control target, UIConfiguration config, PipelineContext context)
    {
        foreach (var view in config.Views)
        {
            ProcessNestedControls(target, view.Controls, context);
        }
    }

    private void ProcessNestedControls(Control parent, List<ControlConfig> controls, PipelineContext context)
    {
        foreach (var controlConfig in controls)
        {
            var child = FindChildControlByName(parent, controlConfig.Name);
            if (child == null) continue;

            // ★ 缓存子控件的配置到 State（修复 Resize 时 CollectLayoutConfigs 收集不到的问题）
            var state = context.LayoutContext.State.GetOrCreateValue(child);
            state.Config = controlConfig;

            if (!context.LayoutContext.TryExecuteStrategy(child, controlConfig))
            {
                if (controlConfig.Controls != null && controlConfig.Controls.Count > 0)
                {
                    ProcessNestedControls(child, controlConfig.Controls, context);
                }
            }
        }
    }

    private static Control? FindChildControlByName(Control parent, string name)
    {
        if (parent == null || string.IsNullOrEmpty(name)) return null;
        if (parent.Controls.ContainsKey(name))
            return parent.Controls[name];
        foreach (Control child in parent.Controls)
        {
            var found = FindChildControlByName(child, name);
            if (found != null) return found;
        }
        return null;
    }
}

// ===== 阶段6: 应用 Splitter 距离 =====
public class ApplySplitterStage : IPipelineStage
{
    public string Name => "ApplySplitter";

    public void Execute(Control target, UIConfiguration config, PipelineContext context)
    {
        ApplySplitterDistances(target, context);
    }

    private void ApplySplitterDistances(Control parent, PipelineContext context)
    {
        foreach (Control control in parent.Controls)
        {
            if (control is SplitContainer splitter)
            {
                var state = context.LayoutContext.State.GetOrCreateValue(splitter);
                if (state.Config?.SplitterDistance > 0 && state.Config.SplitterDistance <= 1)
                {
                    int splitterWidth = splitter.Orientation == Orientation.Vertical
                        ? splitter.Width : splitter.Height;
                    splitter.SplitterDistance = (int)(splitterWidth * state.Config.SplitterDistance);
                }

                ApplySplitterDistances(splitter.Panel1, context);
                ApplySplitterDistances(splitter.Panel2, context);
            }
            else if (control.HasChildren)
            {
                ApplySplitterDistances(control, context);
            }
        }
    }
}
```

---

## 四、RenderPipeline 改造

### 4.1 改造后的 RenderPipeline

```csharp
public class RenderPipeline
{
    private readonly LayoutStrategyRegistry _registry;
    private readonly ILayoutContext _context;
    private readonly Pipeline _renderPipeline;
    private readonly Pipeline _relayoutPipeline;

    public RenderPipeline(LayoutStrategyRegistry registry, ILayoutContext context)
    {
        _registry = registry;
        _context = context;

        // ★ 构建 Render 管线（6个阶段）
        _renderPipeline = new PipelineBuilder()
            .Then(new TrackResizeStage())
            .Then(new CreateLayoutPanelStage())
            .Then(new CacheConfigStage())
            .Then(new ExecuteStrategyStage())
            .Then(new ProcessNestedStage())
            .Then(new ApplySplitterStage())
            .Build();

        // ★ 构建 Relayout 管线（跳过 CreateLayoutPanel，从阶段3开始）
        _relayoutPipeline = new PipelineBuilder()
            .Then(new CacheConfigStage())       // 重新缓存配置
            .Then(new ExecuteStrategyStage())   // 重新执行策略
            .Then(new ProcessNestedStage())     // 重新处理嵌套
            .Then(new ApplySplitterStage())     // 重新应用分割器
            .Build();
    }

    public void Render(Control target, UIConfiguration config)
    {
        var context = new PipelineContext(_context)
        {
            CancellationToken = CancellationToken.None
        };
        context.Items["__pipeline"] = _renderPipeline;

        _renderPipeline.Execute(target, config, context);
    }

    public void Relayout(Control target)
    {
        // 从 State 中恢复配置
        var state = _context.State.GetOrCreateValue(target);
        if (state.Config == null) return;

        // 重建 UIConfiguration
        var config = RebuildConfig(target);

        var context = new PipelineContext(_context)
        {
            CancellationToken = CancellationToken.None
        };

        _relayoutPipeline.Execute(target, config, context);
    }

    private UIConfiguration RebuildConfig(Control target)
    {
        // 从 State 中收集所有配置，重建 UIConfiguration
        var config = new UIConfiguration();
        var viewConfig = new ViewConfig
        {
            Name = "MainView",
            Type = "Panel",
            Controls = new List<ControlConfig>()
        };

        CollectConfigsRecursive(target, viewConfig.Controls);
        config.Views.Add(viewConfig);
        return config;
    }

    private void CollectConfigsRecursive(Control parent, List<ControlConfig> configs)
    {
        foreach (Control control in parent.Controls)
        {
            var state = _context.State.GetOrCreateValue(control);
            if (state.Config != null)
            {
                configs.Add(state.Config);
            }
            if (control.HasChildren)
            {
                CollectConfigsRecursive(control, configs);
            }
        }
    }
}
```

### 4.2 关键改进

| 改进点 | 改造前 | 改造后 |
|--------|-------|-------|
| **阶段拆分** | 6个步骤硬编码在 Render 中 | 6个独立阶段，可单独测试 |
| **Relayout 复用** | 独立的 Relayout 方法，逻辑重复 | 共享阶段，跳过 CreateLayoutPanel |
| **子控件配置缓存** | 未缓存到 State（导致 Resize 失效） | ProcessNestedStage 中缓存 ✅ |
| **可扩展性** | 新增步骤需修改 Render | 新增阶段只需添加 Then() |
| **上下文共享** | 无 | PipelineContext.Items 支持阶段间数据共享 |

---

## 五、实施步骤

### Step 1: 创建 Pipeline 基础设施
- 创建 `WdUI/Layout/Pipeline/IPipelineStage.cs` — 阶段接口
- 创建 `WdUI/Layout/Pipeline/PipelineContext.cs` — 上下文
- 创建 `WdUI/Layout/Pipeline/PipelineBuilder.cs` — 构建器
- 创建 `WdUI/Layout/Pipeline/Pipeline.cs` — 执行器

### Step 2: 创建各阶段实现
- 创建 `WdUI/Layout/Pipeline/Stages/TrackResizeStage.cs`
- 创建 `WdUI/Layout/Pipeline/Stages/CreateLayoutPanelStage.cs`
- 创建 `WdUI/Layout/Pipeline/Stages/CacheConfigStage.cs`
- 创建 `WdUI/Layout/Pipeline/Stages/ExecuteStrategyStage.cs`
- 创建 `WdUI/Layout/Pipeline/Stages/ProcessNestedStage.cs`
- 创建 `WdUI/Layout/Pipeline/Stages/ApplySplitterStage.cs`

### Step 3: 改造 RenderPipeline
- 使用 PipelineBuilder 构建 Render 和 Relayout 管线
- 删除旧的 Render 和 Relayout 方法中的硬编码逻辑
- 保留 TrackResize 方法（外部调用入口）

### Step 4: 修复子控件配置缓存
- 在 `ProcessNestedStage` 中缓存子控件的 ControlConfig 到 State
- 确保 `CollectLayoutConfigs` 能收集到所有配置

### Step 5: 构建验证
- 编译验证 0 Error(s)
- 运行测试验证布局渲染和 Resize 自适应

---

## 六、与 SkeletonFlow 的对比

| 概念 | SkeletonFlow | 改造后的 RenderPipeline |
|------|-------------|----------------------|
| 执行单元 | `ISkeleton<TInput, TOutput>` | `IPipelineStage` |
| 组合方式 | `Then()` 链式 | `PipelineBuilder.Then()` 链式 |
| 上下文 | `SkeletonContext`（Items + CTS + DI） | `PipelineContext`（Items + CT） |
| 顺序组合 | `SequentialSkeleton` | `Pipeline`（顺序执行） |
| 条件分支 | `Branch()` | 暂不支持（可后续扩展） |
| 循环 | `Loop()` | 暂不支持（可后续扩展） |
| 并行 | `Parallel()` | 暂不支持（可后续扩展） |
| JSON 驱动 | `SkeletonConfig` + `Registry` | 暂不支持（可后续扩展） |

---

## 七、后续扩展

### 7.1 条件阶段（Branch）
```csharp
pipeline
    .Then(new TrackResizeStage())
    .Branch(
        ctx => ctx.Items.ContainsKey("__hasSplitter"),
        new PipelineBuilder().Then(new ApplySplitterStage()).Build(),
        new PipelineBuilder().Build()
    );
```

### 7.2 循环阶段（Loop）
```csharp
pipeline
    .Then(new TrackResizeStage())
    .Loop(
        ctx => ctx.Items.ContainsKey("__needsRetry"),
        new PipelineBuilder().Then(new RetryStage()).Build()
    );
```

### 7.3 JSON 驱动的阶段配置
```json
{
  "pipeline": [
    { "stage": "TrackResize" },
    { "stage": "CreateLayoutPanel" },
    { "stage": "CacheConfig" },
    { "stage": "ExecuteStrategy" },
    { "stage": "ProcessNested" },
    { "stage": "ApplySplitter" }
  ]
}
```

---

## 八、总结

本次重构的核心思路是 **"Pipeline 阶段化 + 链式组合"**，借鉴 SkeletonFlow 的编排模式：

1. **阶段化**：将 Render 的 6 个步骤拆分为独立阶段
2. **链式组合**：通过 `PipelineBuilder.Then()` 组合阶段
3. **上下文共享**：通过 `PipelineContext.Items` 在阶段间传递数据
4. **Relayout 复用**：共享阶段，跳过 CreateLayoutPanel
5. **修复 Resize 问题**：在 ProcessNestedStage 中缓存子控件配置到 State

改造后，RenderPipeline 的 Render 和 Relayout 方法将变得简洁、可扩展、可测试。
