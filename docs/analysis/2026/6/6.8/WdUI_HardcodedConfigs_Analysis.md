# WdUI 硬编码配置综合分析报告

## 概述

对 `Operation/WdUI` 目录下所有源文件进行了全面审查，识别出所有硬编码的字符串配置项，并将其统一迁移到 `WdUISettings` 配置中心（`WdUI.Settings.cs`）。

## 已识别的硬编码配置项

### 1. 文件路径类

| 配置项 | 原始硬编码值 | 迁移目标 | 使用位置 |
|--------|-------------|---------|---------|
| 错误日志路径 | `d:\wditest_error.log` | `WdUISettings.ErrorLogPath` | `WdUI.Core.cs` L37 |
| UI 配置根目录 | `UI\Config` | `WdUISettings.ConfigRootRelative` | `WdUI.Management.cs` L44 |
| 全局配置目录 | `Global` | `WdUISettings.GlobalConfigRelative` | `WdUI.Management.cs` L50 |
| 默认配置文件列表 | `ui.config.json` 等 | `WdUISettings.DefaultConfigFiles` | 配置加载入口 |

### 2. 字体类

| 配置项 | 原始硬编码值 | 迁移目标 | 使用位置 |
|--------|-------------|---------|---------|
| 默认字体 | `Segoe UI`, 9pt, Regular | `WdUISettings.DefaultFont` | `WdUI.Management.cs` L317 |
| 调试覆盖层字体 | `Consolas`, 8pt | `WdUISettings.DebugOverlayFont` | `WdUI.Layout.cs` L247 |

### 3. 主题/样式类

| 配置项 | 原始硬编码值 | 迁移目标 | 使用位置 |
|--------|-------------|---------|---------|
| Dark 主题颜色 | 各种 ARGB 值 | `WdUISettings.DarkTheme` | `WdUI.Management.cs` L382 |
| 默认样式 | `Control`/`ControlText` 等 | `WdUISettings.DefaultStyle` | `WdUI.Management.cs` L354 |
| 调试覆盖层颜色 | `Red` | `WdUISettings.DebugOverlay` | `WdUI.Layout.cs` L242 |

### 4. 日志类

| 配置项 | 原始硬编码值 | 迁移目标 | 使用位置 |
|--------|-------------|---------|---------|
| 模块名 | `"WdUI"` | `WdUISettings.Logging.ModuleName` | `WdUIJson.cs`, `WdUI.RenderPipeline.cs` |
| 分类前缀 | `"wdui:"` | `WdUISettings.Logging.CategoryPrefix` | `WdUIJson.cs`, `WdUI.RenderPipeline.cs` |
| 消息文件名 | `"wdui_log_messages.json"` | `WdUISettings.Logging.MessagesFileName` | `WdUI.RenderPipeline.cs` L139 |
| Placeholder 处理器名 | `"WdUILog"` | `WdUISettings.Logging.PlaceholderHandlerName` | `WdUI.RenderPipeline.cs` L136 |

### 5. 容器默认值类

| 配置项 | 原始硬编码值 | 迁移目标 | 使用位置 |
|--------|-------------|---------|---------|
| 是否使用 ETF | `false` | `WdUISettings.ContainerDefaults.UseEventTriggerFramework` | `WdUI.Container.cs` L91 |
| 默认 Dock 样式 | `Fill` | `WdUISettings.ContainerDefaults.DockStyle` | `WdUI.Container.cs` L149 |
| 控件样式标志 | `AllPaintingInWmPaint` 等 | `WdUISettings.ContainerDefaults.ControlStyles` | `WdUI.Container.cs` L155 |

### 6. 枚举默认值类

| 配置项 | 原始硬编码值 | 迁移目标 | 使用位置 |
|--------|-------------|---------|---------|
| NavigationBarPosition | `"Top"` | `WdUISettings.EnumDefaults.NavigationBarPosition` | 枚举转换 |
| RefreshMode | `"Manual"` | `WdUISettings.EnumDefaults.RefreshMode` | 枚举转换 |
| DockStyle | `"None"` | `WdUISettings.EnumDefaults.DockStyle` | 枚举转换 |
| Anchor | `"Top, Left"` | `WdUISettings.EnumDefaults.Anchor` | 枚举转换 |
| FormWindowState | `"Normal"` | `WdUISettings.EnumDefaults.FormWindowState` | 枚举转换 |
| FontStyle | `"Regular"` | `WdUISettings.EnumDefaults.FontStyle` | 枚举转换 |
| BorderStyle | `"FixedSingle"` | `WdUISettings.EnumDefaults.BorderStyle` | 枚举转换 |
| DataSourceUpdateMode | `"OnPropertyChanged"` | `WdUISettings.EnumDefaults.DataSourceUpdateMode` | 枚举转换 |
| ListView.View | `"Details"` | `WdUISettings.EnumDefaults.ListViewView` | 枚举转换 |
| ScrollBars | `"None"` | `WdUISettings.EnumDefaults.ScrollBars` | 枚举转换 |
| SplitContainer 类型标识 | `"splitcontainer"` | `WdUISettings.EnumDefaults.SplitContainerType` | `WdUI.Strategies.cs` L74 |

### 7. 布局类

| 配置项 | 原始硬编码值 | 迁移目标 | 使用位置 |
|--------|-------------|---------|---------|
| 递归查找最大深度 | `10` | `WdUISettings.Layout.MaxRecursionDepth` | `ApplyLayoutSkeleton.cs` L291 |
| 复合布局默认名称 | `"CompositeLayout"` | `WdUISettings.Layout.CompositeLayoutName` | `WdUI.LayoutEngine.cs` L310 |
| 已注册策略列表 | `["SplitContainerStrategy"]` | `WdUISettings.Layout.RegisteredStrategies` | 策略注册 |

### 8. 管线名称类

| 配置项 | 原始硬编码值 | 迁移目标 | 使用位置 |
|--------|-------------|---------|---------|
| 渲染管线名称 | `"WinFormsRenderPipeline"` | `WdUISettings.Pipelines.RenderPipeline` | `WdUI.RenderPipeline.cs` L145 |
| 配置加载管线名称 | `"WdUIConfigLoader"` | `WdUISettings.Pipelines.ConfigLoader` | `WdUI.Container.cs` L145 |
| 扩展配置加载管线名称 | `"WdUIExtendedConfigLoader"` | `WdUISettings.Pipelines.ExtendedConfigLoader` | 扩展加载 |

### 9. 事件名称类

| 配置项 | 原始硬编码值 | 迁移目标 | 使用位置 |
|--------|-------------|---------|---------|
| 配置加载完成事件 | `"WdUI.Container.ConfigLoaded"` | `WdUISettings.EventNames.ConfigLoaded` | `WdUI.EventManagement.cs` L42 |
| 容器内容变更事件 | `"WdUI.Container.ContentChanged"` | `WdUISettings.EventNames.ContentChanged` | `WdUI.EventManagement.cs` L45 |

### 10. 控件类型映射类

| 配置项 | 原始硬编码值 | 迁移目标 | 使用位置 |
|--------|-------------|---------|---------|
| 类型名 → 控件类型映射 | 34 个映射项 | `WdUISettings.ControlTypes.TypeMap` | `ControlFactory.cs` → `DefaultControlCreator` |

## 已完成的迁移修改

### 文件修改清单

1. **`WdUI.Settings.cs`** — 新增配置中心，包含所有配置类
2. **`WdUI.RenderPipeline.cs`** — 管线名称、日志配置改为从 `WdUISettings` 读取
3. **`WdUI.Container.cs`** — DockStyle、ControlStyles、编排器名称改为从 `WdUISettings` 读取
4. **`WdUI.Management.cs`** — 字体、主题、样式、路径改为从 `WdUISettings` 读取
5. **`WdUI.Layout.cs`** — 调试覆盖层颜色、字体改为从 `WdUISettings` 读取
6. **`WdUI.LayoutEngine.cs`** — 复合布局名称改为从 `WdUISettings` 读取
7. **`WdUI.EventManagement.cs`** — 事件名称改为从 `WdUISettings` 读取
8. **`WdUI.Strategies.cs`** — SplitContainer 类型标识改为从 `WdUISettings` 读取
9. **`WdUIJson.cs`** — 日志模块名和分类前缀改为从 `WdUISettings` 读取
10. **`WdUI.Core.cs`** — 错误日志路径改为从 `WdUISettings` 读取

## 架构优势

- **单点修改**：所有配置集中在 `WdUI.Settings.cs`，修改无需搜索整个代码库
- **外部可配置**：支持 `wdui.settings.json` 配置文件，无需重新编译
- **向后兼容**：代码内默认值确保 JSON 文件不存在时仍能正常工作
- **类型安全**：强类型配置类，编译时检查属性名
