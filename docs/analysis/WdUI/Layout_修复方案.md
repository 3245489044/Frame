# WdUI 布局修复方案

## 一、问题根因分析

### 1.1 核心缺陷：LayoutPipeline 不递归 SplitContainer 的子控件

`LayoutPipeline.ApplyLayoutViaWalker` 只递归 `config.Controls`，但 `SplitContainer` 的子控件在 `config.Panel1Controls` 和 `config.Panel2Controls` 中。这导致：

```
MainView.Controls
  ├── toolbarPanel (controls ✓)
  └── mainSplitter (controls ✓)
        ├── Panel1Controls: mainArea (✗ 不递归)
        │     └── controls: contentSplitter (✗ 不递归)
        │           ├── Panel1Controls: contentContainer (✗ 不递归)
        │           └── Panel2Controls: terminalPanel (✗ 不递归)
        └── Panel2Controls: sideContainer (✗ 不递归)
```

**结果**：所有嵌套在 `SplitContainer` 内部的控件，其 `layout` 配置（包括 `SplitterDistance`）从未被应用。`contentSplitter` 的 `SplitterDistance` 保持默认值 50%，但 `SplitterPanel` 的默认最小尺寸是 33×23，导致 `contentContainer` 和 `sideContainer` 的父容器尺寸为 33×23。

### 1.2 次级缺陷：布局与创建分离导致时序脆弱

框架设计将"创建控件"和"应用布局"分离到两个阶段：
- **创建阶段**（`CreateLayoutPanelSkeleton`）：创建控件树，设置 `DockStyle`
- **布局阶段**（`LayoutPipeline`）：应用比例布局，设置 `SplitterDistance`

这种分离导致：
1. 创建阶段在 `SuspendLayout` 状态下执行，`DockStyle.Fill` 不生效
2. 布局阶段依赖 `ApplyLayout` 在 `ResumeLayout` 后被调用
3. 动态容器（`contentContainer`、`sideContainer`）的加载时序与父容器的布局时序耦合

### 1.3 次级缺陷：会话管理复杂且脆弱

`WinFormsRenderPipeline` 使用 `_sessions` 字典管理会话，key 是 `Control` 实例。但：
1. `GetActiveSession(splitContainer.Parent)` 返回 null（key 不匹配）
2. 会话的创建和清理分散在多个地方
3. 没有统一的会话生命周期管理

---

## 二、综合修复方案

### 方案 A：修复 LayoutPipeline 递归 SplitContainer 子控件（必须）

**目标**：让 `LayoutPipeline.ApplyLayoutViaWalker` 能递归处理 `SplitContainer` 的 `Panel1Controls`/`Panel2Controls`。

**修改文件**：`Operation/WdUI/Layout/LayoutPipeline.cs`

**修改内容**：在 `ApplyLayoutViaWalker` 的第一遍和第二遍遍历中，当控件是 `SplitContainer` 时，递归处理 `config.Panel1Controls` 和 `config.Panel2Controls`。

```csharp
// 第一遍：先序遍历
foreach (var config in configs)
{
    var control = context.FindControlInParent(parent, config.Name);
    if (control == null) continue;

    // ... 现有逻辑 ...

    // ★ 修复：递归处理 SplitContainer 的 Panel1Controls/Panel2Controls
    if (control is SplitContainer)
    {
        if (config.Panel1Controls != null && config.Panel1Controls.Count > 0)
            ApplyLayoutViaWalker(control, config.Panel1Controls, context);
        if (config.Panel2Controls != null && config.Panel2Controls.Count > 0)
            ApplyLayoutViaWalker(control, config.Panel2Controls, context);
    }

    // 递归处理子控件
    if (config.Controls != null && config.Controls.Count > 0)
        ApplyLayoutViaWalker(control, config.Controls, context);
}

// 第二遍：后序遍历（同样处理 Panel1Controls/Panel2Controls）
```

**影响范围**：仅 `LayoutPipeline.cs`，无外部 API 变更。

---

### 方案 B：在 CreateLayoutPanelSkeleton 中立即设置 SplitterDistance（增强）

**目标**：在创建 `SplitContainer` 时立即设置 `SplitterDistance`，消除对 `ApplyLayout` 时序的依赖。

**修改文件**：`Operation/WdUI/Layout/Skeletons/CreateLayoutPanelSkeleton.cs`

**修改内容**：在 `CreateControlWithChildren` 中创建 `SplitContainer` 后，立即根据配置设置 `SplitterDistance`。

```csharp
// 处理 SplitContainer 的 Panel1Controls/Panel2Controls
if (control is SplitContainer splitter)
{
    // ★ 修复：立即设置 SplitterDistance，不依赖后续的 ApplyLayout
    if (config.SplitterDistance.HasValue)
    {
        int dimension = splitter.Orientation == Orientation.Vertical
            ? splitter.Width
            : splitter.Height;
        // 如果当前尺寸为 0（SuspendLayout 状态下），订阅 Resize 事件延迟设置
        if (dimension > 0)
        {
            splitter.SplitterDistance = (int)(config.SplitterDistance.Value * dimension);
        }
        else
        {
            // 延迟到第一次 Resize 时设置
            EventHandler handler = null!;
            handler = (s, e) =>
            {
                int d = splitter.Orientation == Orientation.Vertical
                    ? splitter.Width
                    : splitter.Height;
                if (d > 0)
                {
                    splitter.SplitterDistance = (int)(config.SplitterDistance.Value * d);
                    splitter.Resize -= handler;
                }
            };
            splitter.Resize += handler;
        }
    }

    // ... 现有 Panel1Controls/Panel2Controls 处理 ...
}
```

**影响范围**：仅 `CreateLayoutPanelSkeleton.cs`，无外部 API 变更。

---

### 方案 C：简化 ConfigContainer.RenderConfigAsync 中的布局后处理（清理）

**目标**：移除 `ConfigContainer.RenderConfigAsync` 中针对 `SplitterPanel` 的特殊处理逻辑，因为方案 A 和方案 B 已经从根本上解决了问题。

**修改文件**：`Operation/WdUI/UI/WdUI.Container.cs`

**修改内容**：移除 `RenderConfigAsync` 中 `ResumeLayout` 后的 `SplitterPanel` 检测和 `SplitterDistance` 设置代码，只保留 `pipeline.ApplyLayout(this)` 调用。

```csharp
// ★ 简化：方案 A 和方案 B 已解决 SplitterDistance 问题，不再需要特殊处理
// ★ 只需对自身执行布局即可
bool layoutApplied = pipeline.ApplyLayout(this);
```

**影响范围**：仅 `WdUI.Container.cs`，移除冗余代码。

---

## 三、实施步骤

### 第 1 步：实施方案 A（修复 LayoutPipeline 递归）

修改 `LayoutPipeline.ApplyLayoutViaWalker`，在遍历 `configs` 时，对 `SplitContainer` 类型的控件递归处理 `Panel1Controls`/`Panel2Controls`。

### 第 2 步：实施方案 B（创建时设置 SplitterDistance）

修改 `CreateLayoutPanelSkeleton.CreateControlWithChildren`，在创建 `SplitContainer` 后立即根据配置设置 `SplitterDistance`。

### 第 3 步：实施方案 C（清理冗余代码）

修改 `ConfigContainer.RenderConfigAsync`，移除 `SplitterPanel` 特殊处理代码。

### 第 4 步：验证

1. 构建 WdUI 项目：`dotnet build Operation/WdUI/WdUI.csproj`
2. 构建 FileDriven.UI 项目：`dotnet build FileDriven/FileDriven.UI/FileDriven.UI.csproj`
3. 运行程序验证布局是否正确

---

## 四、预期效果

1. **`contentSplitter` 和 `mainSplitter` 的 `SplitterDistance` 正确设置**：方案 A 确保 `LayoutPipeline` 能递归处理 `SplitContainer` 的子控件；方案 B 确保在创建时立即设置
2. **`contentContainer` 和 `sideContainer` 获得正确的父容器尺寸**：`SplitterDistance` 设置后，`SplitterPanel` 的尺寸正确，比例布局按正确尺寸计算
3. **代码更简洁**：方案 C 移除冗余的特殊处理逻辑
4. **时序更健壮**：方案 B 消除了对 `ApplyLayout` 调用时序的依赖

---

## 五、风险与注意事项

1. **方案 A 的风险**：`ApplyLayoutViaWalker` 递归 `Panel1Controls`/`Panel2Controls` 时，`context.FindControlInParent(parent, config.Name)` 的 `parent` 参数是 `SplitContainer` 本身，而不是 `SplitterPanel`。需要确保 `ControlFinder.FindRecursive` 能在 `SplitContainer` 的子控件中正确查找（`SplitContainer` 的子控件是 `SplitterPanel`，而 `SplitterPanel` 的子控件才是目标控件）。

2. **方案 B 的风险**：在 `SuspendLayout` 状态下，`splitter.Width`/`splitter.Height` 可能为 0，导致 `SplitterDistance` 无法计算。需要使用 Resize 事件延迟设置。

3. **方案 C 的风险**：移除 `SplitterPanel` 特殊处理后，如果方案 A 或方案 B 有 Bug，布局可能仍然不生效。建议先实施方案 A 和方案 B，验证通过后再实施方案 C。
