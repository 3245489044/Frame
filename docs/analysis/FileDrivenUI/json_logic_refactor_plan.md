# FileDriven.UI JSON 相关逻辑集中整理方案

> 文档版本：v1.0
> 创建日期：2026-06-09
> 方案类型：方案 C（混合方案）

---

## 一、背景与目标

### 1.1 背景

FileDriven.UI 是 FileDriven 框架的 WinForms UI 层，其中包含大量 JSON 相关处理逻辑。这些逻辑散落在项目的多个目录和文件中，缺乏统一的组织和清晰的职责划分，导致：

- **可发现性差**：JSON 相关逻辑分布在 `根目录`、`Skeletons/`、`Event/`、`Abstractions/`、`Core/`、`UIInspector/` 等多个位置
- **职责模糊**：纯 JSON 处理逻辑（与 UI 无关）与 JSON→UI 桥接逻辑混在一起
- **维护成本高**：新增 JSON 功能时难以确定归属位置

### 1.2 目标

1. **物理集中**：将所有 JSON 相关逻辑集中到 `FileDriven.UI/Json/` 文件夹
2. **逻辑分层**：在 `Json/` 内部按职责划分子目录（Processing / Models / Loading / Export）
3. **渐进迁移**：纯 JSON 处理逻辑逐步迁移到 `FileDriven.JsonRefer`，UI 层保留委托
4. **向后兼容**：原位置保留委托方法，不影响现有调用代码

---

## 二、现状分析

### 2.1 FileDriven.UI 中 JSON 相关逻辑分布

| # | 文件路径 | 行数 | 类别 | 说明 |
|---|---------|------|------|------|
| 1 | `JsonService.cs` | 100 | 委托层 | 所有 JSON 操作的统一入口，核心逻辑已迁移至 `FileDriven.JsonRefer.LayoutJson.LayoutJsonService` |
| 2 | `Skeletons/UISkeletons.cs` — `PreprocessJsonConfig()` | ~30 | 纯 JSON 处理 | JSON 模板预处理（展开 style/template/extends 引用） |
| 3 | `Skeletons/UISkeletons.cs` — `FormatEventBindings()` | ~15 | 纯 JSON 处理 | 格式化事件绑定信息 |
| 4 | `Skeletons/Models/SkeletonInputs.cs` | ~60 | JSON 模型 DTO | `ConfigLoadInput` / `ConfigLoadOutput` |
| 5 | `Event/EventCore.cs` — `EventBindingConfig` | ~40 | JSON 模型 DTO | 事件绑定配置 |
| 6 | `Abstractions/FileDrivenWdUIBridge.cs` | 464 | JSON→UI 桥接 | 加载 JSON 配置到 UI 容器 |
| 7 | `Core/ConfigSelectorController.cs` | 155 | JSON→UI 桥接 | 扫描 JSON 配置文件列表 |
| 8 | `Core/MainForm.cs` | 567 | JSON 驱动 UI | 使用 JSON 路径加载布局 |
| 9 | `Layout/Skeletons/` (4 个文件) | ~416 | JSON 驱动 UI | JSON 配置加载管线 |
| 10 | `Event/EventTriggerService.cs` | 338 | JSON 驱动 UI | JSON 事件绑定触发 |
| 11 | `UIInspector/UIInspectorCore.cs` — JSON 导出 | ~30 | JSON 导出 | JSON 格式布局报告 |

### 2.2 FileDriven.JsonRefer 现有结构

```
FileDriven.JsonRefer/
├── Abstractions/                    — JSON 处理抽象接口（IJsonProcessor 等）
├── Event/
│   └── EventHelper.cs               — 事件逻辑辅助（常量、构建、校验）
├── GraphKeyFrame/
│   ├── Builders/                    — 图键框架构建器
│   └── Templates/                   — 模板引擎
├── JsonProcessing/
│   ├── JsonProcessingOptions.cs      — JSON 处理选项
│   ├── JsonProcessingService.cs      — JSON 处理服务
│   ├── JsonTemplateProcessor.cs      — JSON 模板处理器（核心）
│   └── JsonUIBeautifierAdapter.cs    — JSON 美化适配器
├── LayoutJson/
│   ├── ContractBuilder.cs            — 契约构建器
│   ├── ContractRegistry.cs           — 契约注册中心
│   ├── ContractValidator.cs          — 契约校验器
│   ├── ControlConfigContract.cs      — 控件配置契约
│   ├── ControlTreeWalker.cs          — 控件树遍历
│   └── LayoutJsonService.cs          — JSON 布局服务（核心）
└── ReferenceProcessing/
    ├── README.md
    └── REFACTOR_PLAN.md
```

---

## 三、目标结构

### 3.1 FileDriven.UI/Json/ 目录结构

```
FileDriven.UI/
└── Json/                                    ← ★ 新建：JSON 相关逻辑集中目录
    │
    ├── JsonService.cs                        ← [移动] 从根目录移入
    │   所有 JSON 操作的统一入口（委托层）
    │
    ├── README.md                             ← [新建] 目录说明
    │
    ├── Processing/                           ← ★ 纯 JSON 处理逻辑
    │   ├── JsonPreprocessor.cs               ← [提取] 从 UISkeletons.PreprocessJsonConfig()
    │   │   JSON 模板预处理：展开 style/template/extends 引用
    │   │   ★ 核心逻辑委托给 FileDriven.JsonRefer.JsonProcessing.JsonTemplateProcessor
    │   │
    │   └── EventBindingFormatter.cs          ← [提取] 从 UISkeletons.FormatEventBindings()
    │       事件绑定格式化：将 EventConfig 格式化为可读字符串
    │
    ├── Models/                               ← ★ JSON 相关 DTO
    │   ├── ConfigLoadInput.cs                ← [迁移] 从 Skeletons/Models/SkeletonInputs.cs
    │   │   配置加载操作的输入 DTO
    │   │
    │   ├── ConfigLoadOutput.cs               ← [迁移] 从 Skeletons/Models/SkeletonInputs.cs
    │   │   配置加载操作的输出 DTO
    │   │
    │   └── EventBindingConfig.cs             ← [迁移] 从 Event/EventCore.cs
    │       事件绑定配置 DTO
    │
    ├── Loading/                              ← ★ JSON 加载到 UI 的逻辑
    │   ├── ConfigLoader.cs                   ← [提取] 从 FileDrivenWdUIBridge
    │   │   JSON 配置加载器：加载 JSON 到 ConfigContainer
    │   │   包含：路径解析、预处理、反序列化、校验、渲染
    │   │
    │   └── ConfigSelectorScanner.cs          ← [提取] 从 ConfigSelectorController
    │       配置选择器扫描器：扫描 JSON 配置文件目录
    │
    └── Export/                               ← ★ JSON 导出逻辑
        └── LayoutJsonExporter.cs             ← [提取] 从 UIInspectorCore
            JSON 布局导出器：将控件元数据导出为 JSON 格式
```

### 3.2 整理后的完整文件映射

```
整理前：                                     整理后：
FileDriven.UI/                              FileDriven.UI/
│                                           │
├── JsonService.cs              ──────→     ├── Json/
│   (委托层)                                 │   ├── JsonService.cs
│                                           │   ├── README.md
├── Skeletons/                              │   ├── Processing/
│   ├── UISkeletons.cs                      │   │   ├── JsonPreprocessor.cs
│   │   ├── PreprocessJsonConfig()  ──→     │   │   └── EventBindingFormatter.cs
│   │   └── FormatEventBindings()  ──→      │   ├── Models/
│   └── Models/                             │   │   ├── ConfigLoadInput.cs
│       └── SkeletonInputs.cs               │   │   ├── ConfigLoadOutput.cs
│           ├── ConfigLoadInput    ──→      │   │   └── EventBindingConfig.cs
│           └── ConfigLoadOutput   ──→      │   ├── Loading/
│                                           │   │   ├── ConfigLoader.cs
├── Event/                                  │   │   └── ConfigSelectorScanner.cs
│   └── EventCore.cs                        │   └── Export/
│       └── EventBindingConfig     ──→      │       └── LayoutJsonExporter.cs
│                                           │
├── Abstractions/                           ├── Skeletons/
│   └── FileDrivenWdUIBridge.cs             │   ├── UISkeletons.cs ← 保留委托
│       ├── LoadConfigAsync()      ──→      │   └── Models/
│       ├── LoadConfigSelectorAsync() ──→   │       └── SkeletonInputs.cs ← 保留委托
│       └── LoadSideContainerAsync() ──→    │
│                                           ├── Event/
├── Core/                                   │   └── EventCore.cs ← 保留委托
│   └── ConfigSelectorController.cs         │
│       ├── RefreshConfigList()    ──→      ├── Abstractions/
│       └── LoadSelectedConfig()  ──→       │   └── FileDrivenWdUIBridge.cs ← 保留委托
│                                           │
├── UIInspector/                            ├── Core/
│   └── UIInspectorCore.cs                  │   └── ConfigSelectorController.cs ← 保留委托
│       └── GenerateJsonReport()   ──→      │
│                                           └── UIInspector/
│                                               └── UIInspectorCore.cs ← 保留委托
```

---

## 四、实施步骤

### 第 1 步：创建目录结构

创建 `FileDriven.UI/Json/` 及其子目录：

```
FileDriven.UI/Json/
├── Processing/
├── Models/
├── Loading/
└── Export/
```

### 第 2 步：移动 JsonService.cs

| 操作 | 说明 |
|------|------|
| **移动** | `JsonService.cs` → `FileDriven.UI/Json/JsonService.cs` |
| **命名空间** | 保持 `FileDriven.UI` 不变（避免破坏现有引用） |
| **影响** | 无，仅文件路径变化 |

### 第 3 步：提取纯 JSON 处理逻辑

#### 3.1 创建 `Json/Processing/JsonPreprocessor.cs`

```csharp
// ============================================================
// JsonPreprocessor.cs — JSON 模板预处理器
//
// ★ 从 UISkeletons.PreprocessJsonConfig() 提取
// ★ 核心逻辑委托给 FileDriven.JsonRefer.JsonProcessing.JsonTemplateProcessor
// ★ UISkeletons.cs 中保留委托方法，保持向后兼容
// ============================================================

namespace FileDriven.UI.Json.Processing;

/// <summary>
/// JSON 模板预处理器
/// <para>展开 JSON 配置文件中的 style/template/extends 引用</para>
/// </summary>
public static class JsonPreprocessor
{
    /// <summary>
    /// 预处理 JSON 配置文件
    /// <para>委托给 FileDriven.JsonRefer.JsonProcessing.JsonTemplateProcessor.Process</para>
    /// </summary>
    public static string? Preprocess(string configPath, SkeletonContext? ctx = null)
    {
        // ... 实现代码
    }
}
```

#### 3.2 创建 `Json/Processing/EventBindingFormatter.cs`

```csharp
// ============================================================
// EventBindingFormatter.cs — 事件绑定格式化器
//
// ★ 从 UISkeletons.FormatEventBindings() 提取
// ★ UISkeletons.cs 中保留委托方法，保持向后兼容
// ============================================================

namespace FileDriven.UI.Json.Processing;

/// <summary>
/// 事件绑定格式化器
/// <para>将 EventConfig 中的事件绑定信息格式化为可读字符串</para>
/// </summary>
public static class EventBindingFormatter
{
    /// <summary>格式化控件事件绑定为可读字符串列表</summary>
    public static List<string> Format(EventConfig evt)
    {
        // ... 实现代码
    }
}
```

### 第 4 步：迁移 JSON 模型 DTO

#### 4.1 创建 `Json/Models/ConfigLoadInput.cs`

从 `Skeletons/Models/SkeletonInputs.cs` 迁移 `ConfigLoadInput` 类。

#### 4.2 创建 `Json/Models/ConfigLoadOutput.cs`

从 `Skeletons/Models/SkeletonInputs.cs` 迁移 `ConfigLoadOutput` 类。

#### 4.3 创建 `Json/Models/EventBindingConfig.cs`

从 `Event/EventCore.cs` 迁移 `EventBindingConfig` 类。

### 第 5 步：提取 JSON→UI 桥接逻辑

#### 5.1 创建 `Json/Loading/ConfigLoader.cs`

从 `FileDrivenWdUIBridge.cs` 提取 JSON 配置加载逻辑：

| 方法 | 来源 | 说明 |
|------|------|------|
| `LoadConfigAsync(container, configPath)` | `FileDrivenWdUIBridge.LoadConfigAsync` | 从文件加载 JSON 到容器 |
| `LoadConfigAsync(container, config)` | `FileDrivenWdUIBridge.LoadConfigAsync` | 从对象加载 JSON 到容器 |
| `LoadConfigSelectorAsync()` | `FileDrivenWdUIBridge.LoadConfigSelectorAsync` | 加载 ConfigSelector.json |
| `LoadSideContainerAsync()` | `FileDrivenWdUIBridge.LoadSideContainerAsync` | 加载 JSON 到侧边容器 |

#### 5.2 创建 `Json/Loading/ConfigSelectorScanner.cs`

从 `ConfigSelectorController.cs` 提取 JSON 文件扫描逻辑：

| 方法 | 来源 | 说明 |
|------|------|------|
| `ScanConfigFiles(searchPattern)` | `ConfigSelectorController.RefreshConfigList` | 扫描 JSON 配置文件 |
| `GetConfigStatus()` | `ConfigSelectorController.UpdateConfigStatus` | 获取配置状态信息 |

### 第 6 步：提取 JSON 导出逻辑

#### 6.1 创建 `Json/Export/LayoutJsonExporter.cs`

从 `UIInspectorCore.cs` 提取 JSON 布局导出逻辑：

| 方法 | 来源 | 说明 |
|------|------|------|
| `ExportLayoutAsJson(controls)` | `WinFormsUIInspector.GenerateJsonReport` | 导出控件元数据为 JSON |

### 第 7 步：在原位置保留委托方法

所有被提取逻辑的源文件，在原位置保留委托方法，并添加 `[Obsolete]` 标记：

```csharp
// UISkeletons.cs — 保留委托
[Obsolete("请使用 FileDriven.UI.Json.Processing.JsonPreprocessor.Preprocess")]
private static string? PreprocessJsonConfig(string configPath, SkeletonContext? ctx = null)
    => JsonPreprocessor.Preprocess(configPath, ctx);

[Obsolete("请使用 FileDriven.UI.Json.Processing.EventBindingFormatter.Format")]
private static List<string> FormatEventBindings(EventConfig evt)
    => EventBindingFormatter.Format(evt);
```

### 第 8 步：更新引用

| 原引用 | 新引用 |
|--------|--------|
| `using FileDriven.UI.Skeletons.Models` | `using FileDriven.UI.Json.Models` |
| `using FileDriven.UI.Event` (仅 EventBindingConfig) | `using FileDriven.UI.Json.Models` |
| `UISkeletons.PreprocessJsonConfig()` | `JsonPreprocessor.Preprocess()` |
| `UISkeletons.FormatEventBindings()` | `EventBindingFormatter.Format()` |

---

## 五、向后兼容策略

### 5.1 委托层保留

所有被提取逻辑的源文件保留委托方法，确保：

- **编译兼容**：现有代码无需修改即可编译通过
- **运行时兼容**：委托方法内部调用新位置的方法，行为完全一致
- **渐进迁移**：调用方可以逐步迁移到新位置

### 5.2 废弃标记

委托方法添加 `[Obsolete]` 标记，附带迁移提示信息：

```csharp
[Obsolete("此方法已废弃，请使用 FileDriven.UI.Json.Processing.JsonPreprocessor.Preprocess()")]
```

### 5.3 迁移周期

| 阶段 | 时间 | 操作 |
|------|------|------|
| 阶段一 | 立即 | 创建 `Json/` 目录，移动/提取逻辑，保留委托 |
| 阶段二 | 1-2 周 | 逐步更新内部调用方使用新位置 |
| 阶段三 | 1 个月 | 删除废弃的委托方法 |

---

## 六、预期效果

### 6.1 目录结构变化

```
整理前：                             整理后：
FileDriven.UI/                      FileDriven.UI/
│                                   │
├── JsonService.cs                  ├── Json/                          ← ★ 集中目录
├── Skeletons/                      │   ├── JsonService.cs             ← 委托层
│   ├── UISkeletons.cs              │   ├── README.md
│   └── Models/                     │   ├── Processing/
│       └── SkeletonInputs.cs       │   │   ├── JsonPreprocessor.cs    ← 模板预处理
├── Event/                          │   │   └── EventBindingFormatter.cs ← 事件格式化
│   └── EventCore.cs                │   ├── Models/
├── Abstractions/                   │   │   ├── ConfigLoadInput.cs     ← 配置加载输入
│   └── FileDrivenWdUIBridge.cs     │   │   ├── ConfigLoadOutput.cs    ← 配置加载输出
├── Core/                           │   │   └── EventBindingConfig.cs  ← 事件绑定配置
│   └── ConfigSelectorController.cs │   ├── Loading/
├── UIInspector/                    │   │   ├── ConfigLoader.cs        ← JSON 加载器
│   └── UIInspectorCore.cs          │   │   └── ConfigSelectorScanner.cs ← 配置扫描器
                                    │   └── Export/
                                    │       └── LayoutJsonExporter.cs  ← JSON 导出器
                                    │
                                    ├── Skeletons/                     ← 保留委托
                                    ├── Event/                         ← 保留委托
                                    ├── Abstractions/                  ← 保留委托
                                    ├── Core/                          ← 保留委托
                                    └── UIInspector/                   ← 保留委托
```

### 6.2 核心收益

| 维度 | 改善前 | 改善后 |
|------|--------|--------|
| **可发现性** | JSON 逻辑散落在 7+ 个目录 | 全部集中在 `Json/` 一个目录 |
| **可维护性** | 新增 JSON 功能难以定位归属 | 按职责明确分层（Processing/Models/Loading/Export） |
| **职责清晰度** | 纯 JSON 处理与 UI 桥接混在一起 | 纯 JSON 逻辑在 `Processing/`，桥接逻辑在 `Loading/` |
| **迁移路径** | 无明确迁移计划 | 纯 JSON 逻辑可逐步迁移到 `FileDriven.JsonRefer` |
| **向后兼容** | — | 原位置保留委托，零破坏性变更 |

---

## 七、附录

### 7.1 术语说明

| 术语 | 说明 |
|------|------|
| **纯 JSON 处理** | 与 WinForms UI 无关的 JSON 操作，如模板展开、格式化、序列化/反序列化 |
| **JSON→UI 桥接** | 将 JSON 配置加载到 WinForms UI 容器的逻辑，涉及 ConfigContainer 等 UI 组件 |
| **JSON 驱动 UI** | 使用 JSON 配置来驱动 UI 行为的逻辑，如事件绑定触发、布局渲染管线 |
| **JSON 导出** | 将 UI 控件状态导出为 JSON 格式的逻辑 |
| **委托层** | 仅转发调用到其他项目（如 FileDriven.JsonRefer），自身不包含业务逻辑 |

### 7.2 参考文档

- [FileDriven.UI JSON 相关逻辑综合分析报告](./json_logic_analysis_report.md)
- [FileDriven.JsonRefer 综合分析报告](../../FileDriven.JsonRefer/docs/analysis_report.md)
