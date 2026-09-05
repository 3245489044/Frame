# WdUI JSON 逻辑迁移到 FileDriven 方案

## 一、背景与目标

### 1.1 背景

WdUI 被定义为"纯 UI 编排层"，职责是 WinForms 控件的**创建、布局、事件绑定**。然而，当前 WdUI 中仍残留大量 JSON 相关逻辑：

- **直接 JSON 操作**：`ConfigurationLoader` 委托到 `JsonFileService` 进行 JSON 文件加载/保存/校验
- **JSON 管线编排**：`ConfigLoadSkeleton`、`TemplatePreprocessSkeleton`、`ConfigPipelineBuilder` 编排 JSON 加载管线
- **JSON 路径管理**：`ConfigPathManager` 委托到 `JsonConfigPathManager` 进行路径解析
- **JSON 工具方法**：`WdUIUsage` 中暴露 `DeserializeConfig`、`SerializeConfig`、`ValidateJson` 等方法
- **JSON 管线构建**：`WdUIAdapter` 构建 JSON 处理管线

这些逻辑应全部迁移到 **FileDriven** 层，由 FileDriven 完成 JSON 的读取、解析、校验、预处理后，将已反序列化的 `UIConfiguration` 实例传入 WdUI 进行纯 UI 渲染。

### 1.2 目标

```
┌──────────────────────────────────────────────────────────────┐
│  FileDriven (JSON 处理层)                                     │
│  1. 读取 JSON 文件 → JsonFileService                          │
│  2. 反序列化 → UIConfiguration 对象                            │
│  3. 契约校验 → ContractValidator                              │
│  4. 模板预处理 → JsonTemplateProcessor                        │
│  5. 传入已反序列化的 UIConfiguration 到 WdUI                   │
└──────────────────────┬───────────────────────────────────────┘
                       │ 传入已反序列化的 UIConfiguration
                       ▼
┌──────────────────────────────────────────────────────────────┐
│  WdUI (纯 UI 编排层)                                          │
│  - ConfigContainer.LoadConfigAsync(UIConfiguration)          │
│  - WinFormsRenderPipeline.Render(target, config)             │
│  - LayoutMediator.ConvertFromConfig(config)                  │
│  - ControlConfigApplier.ApplyConfig(control, config)         │
│  - WdUILayout.ApplyProportional(control, ...)                │
│  ★ 不再有任何 JSON 操作 ★                                    │
└──────────────────────────────────────────────────────────────┘
```

### 1.3 原则

1. **WdUI 不接触任何 JSON 字符串或文件路径** — 只接收已反序列化的 POCO 对象
2. **FileDriven 负责所有 JSON 处理** — 读取、解析、校验、预处理、缓存、变更通知
3. **UniversalMiddleware 提供框架抽象** — `IConfigLoader` 等接口定义在共享层
4. **先完善 FileDriven，再清理 WdUI** — 确保 FileDriven 具备完整的 JSON 处理能力后，再移除 WdUI 中的重复逻辑

---

## 二、WdUI 中 JSON 逻辑全景图

### 2.1 残留 JSON 逻辑清单

| 类别 | 文件 | 具体内容 | 性质 |
|------|------|---------|------|
| **项目引用** | `WdUI.csproj` (L41) | 引用 `FileDriven.Utilities.csproj` | 依赖 FileDriven |
| **配置加载器** | `WdUI.Configuration.cs` | `ConfigurationLoader` 类（纯委托层） | 纯委托 |
| **路径管理器** | `WdUI.Management.cs` (L33-67) | `ConfigPathManager` 类（委托到 `JsonConfigPathManager`） | 纯委托 |
| **JSON 管线适配器** | `WdUI.Adapters.cs` | `WdUIAdapter` 类（构建 JSON 管线） | 纯委托 |
| **配置加载骨架** | `Layout/Skeletons/ConfigLoadSkeleton.cs` | 委托到 `JsonConfigLoadStage` | 骨架层 |
| **模板预处理骨架** | `Layout/Skeletons/TemplatePreprocessSkeleton.cs` | 委托到 `TemplatePreprocessStage` | 骨架层 |
| **配置管道构建器** | `Layout/Skeletons/ConfigPipelineBuilder.cs` | 编排 JSON 加载管线 | 骨架层 |
| **统一入口 JSON 方法** | `WdUIUsage.cs` (L139-211, L809-839, L1002-1011) | 12+ 个 JSON 操作方法 | 入口层 |
| **容器 JSON 加载** | `WdUI.Container.cs` (L204-261) | `LoadConfigAsync(string)` 从文件路径加载 | 容器层 |

### 2.2 已迁移（无需处理）的逻辑

| 类别 | 文件 | 说明 |
|------|------|------|
| **布局引擎** | `WdUI.LayoutEngine.cs` | `ConvertLayoutPosition` 已委托到 `LayoutPositionConverter`，这是 POCO→POCO 转换，非 JSON 操作 |
| **布局管理** | `WdUI.Layout.cs` | `ApplyProportional`、`DetectOverlaps` 等是纯 WinForms 布局计算 |
| **控件工厂** | `ControlFactoryFramework.cs` | `ControlConfigApplier` 将 POCO 属性应用到控件，非 JSON 操作 |
| **容器渲染阶段** | `WduiContainerRenderStage.cs` | 将 `UIConfiguration` 渲染到容器，这是 UI 编排逻辑，**保留** |

---

## 三、改造方案

### 3.1 总体策略：先完善 FileDriven，再清理 WdUI

整个改造分为两个阶段：

```
第一阶段：完善 FileDriven（新增能力）
  Step 1: FileDriven 新增 JSON 加载→渲染的完整管线
  Step 2: FileDriven 新增配置加载骨架（从 WdUI 迁移）
  Step 3: FileDriven 新增模板预处理骨架（从 WdUI 迁移）
  Step 4: FileDriven 新增配置管道构建器（从 WdUI 迁移）
  Step 5: FileDrivenWdUIBridge 增强（直接加载 JSON 再传入对象）

第二阶段：清理 WdUI（移除重复逻辑）
  Step 6: 改造 ConfigContainer — 移除文件路径加载
  Step 7: 移除 ConfigurationLoader（纯委托层）
  Step 8: 清理 WdUIUsage 中的 JSON 方法
  Step 9: 移除 ConfigPathManager（纯委托层）
  Step 10: 移除 WdUIAdapter（JSON 管线构建）
  Step 11: 移除项目引用
```

---

### 3.2 第一阶段：完善 FileDriven

#### Step 1: FileDriven 新增 JSON 加载→渲染的完整管线

**目标**: 在 FileDriven 中建立从 JSON 文件到 UI 渲染的完整流程，使 FileDriven 具备独立的 JSON 处理能力。

**文件**: `FileDriven/FileDriven.UI/Services/FileDrivenConfigService.cs`（新建）

```csharp
namespace FileDriven.UI.Services;

/// <summary>
/// FileDriven 配置服务 — JSON 加载→渲染的完整管线
/// <para>★ 集中管理 JSON 的加载、反序列化、校验、预处理、渲染调用 ★</para>
/// </summary>
public static class FileDrivenConfigService
{
    /// <summary>
    /// 从 JSON 文件加载配置并渲染到容器（完整管线）
    /// </summary>
    public static async Task LoadAndRenderAsync(ConfigContainer container, string configPath, bool strictMode = true)
    {
        // 1. 加载 JSON 文件
        var config = await JsonService.LoadAndValidateAsync(configPath, strictMode);
        if (config == null) return;
        
        // 2. 传入已反序列化的对象到 WdUI
        await container.LoadConfigAsync(config);
    }
    
    /// <summary>
    /// 从 JSON 字符串加载配置并渲染到容器
    /// </summary>
    public static async Task LoadAndRenderFromJsonAsync(ConfigContainer container, string json)
    {
        var config = JsonService.Deserialize(json);
        if (config != null)
        {
            await container.LoadConfigAsync(config);
        }
    }
    
    /// <summary>
    /// 预处理 JSON 文件后加载并渲染
    /// </summary>
    public static async Task PreprocessAndRenderAsync(ConfigContainer container, string configPath)
    {
        // 1. 预处理 JSON（展开 style/template 引用）
        var processedJson = LayoutJsonService.PreprocessConfig(configPath);
        if (processedJson == null) return;
        
        // 2. 反序列化
        var config = JsonService.Deserialize(processedJson);
        if (config == null) return;
        
        // 3. 传入已反序列化的对象到 WdUI
        await container.LoadConfigAsync(config);
    }
}
```

#### Step 2: FileDriven 新增配置加载骨架（从 WdUI 迁移）

**源文件**: `Operation/WdUI/Layout/Skeletons/ConfigLoadSkeleton.cs`
**目标文件**: `FileDriven/FileDriven.UI/Layout/Skeletons/ConfigLoadSkeleton.cs`

**操作**: 将文件从 WdUI 复制到 FileDriven，更新命名空间为 `FileDriven.UI.Layout.Skeletons`

```csharp
// 迁移后：FileDriven/FileDriven.UI/Layout/Skeletons/ConfigLoadSkeleton.cs
namespace FileDriven.UI.Layout.Skeletons;

/// <summary>
/// 配置加载骨架 — 从 JSON 文件加载 UIConfiguration
/// <para>★ 从 WdUI 迁移至 FileDriven ★</para>
/// </summary>
public class ConfigLoadSkeleton : SkeletonBase<string, UIConfiguration>
{
    private readonly JsonConfigLoadStage _stage = new();
    
    protected override async Task<UIConfiguration> ExecuteCoreAsync(string input, SkeletonContext ctx)
    {
        var session = new RenderSession(input);
        await _stage.ExecuteAsync(session);
        return session.GetResult<UIConfiguration>();
    }
}
```

#### Step 3: FileDriven 新增模板预处理骨架（从 WdUI 迁移）

**源文件**: `Operation/WdUI/Layout/Skeletons/TemplatePreprocessSkeleton.cs`
**目标文件**: `FileDriven/FileDriven.UI/Layout/Skeletons/TemplatePreprocessSkeleton.cs`

**操作**: 将文件从 WdUI 复制到 FileDriven，更新命名空间

```csharp
// 迁移后：FileDriven/FileDriven.UI/Layout/Skeletons/TemplatePreprocessSkeleton.cs
namespace FileDriven.UI.Layout.Skeletons;

/// <summary>
/// 模板预处理骨架 — 在 JSON 加载前进行模板展开
/// <para>★ 从 WdUI 迁移至 FileDriven ★</para>
/// </summary>
public class TemplatePreprocessSkeleton : SkeletonBase<string, string>
{
    private readonly TemplatePreprocessStage _stage = new();
    
    protected override async Task<string> ExecuteCoreAsync(string input, SkeletonContext ctx)
    {
        var session = new RenderSession(input);
        await _stage.ExecuteAsync(session);
        return session.GetResult<string>();
    }
}
```

#### Step 4: FileDriven 新增配置管道构建器（从 WdUI 迁移）

**源文件**: `Operation/WdUI/Layout/Skeletons/ConfigPipelineBuilder.cs`
**目标文件**: `FileDriven/FileDriven.UI/Layout/Skeletons/ConfigPipelineBuilder.cs`

**操作**: 将文件从 WdUI 复制到 FileDriven，更新命名空间

```csharp
// 迁移后：FileDriven/FileDriven.UI/Layout/Skeletons/ConfigPipelineBuilder.cs
namespace FileDriven.UI.Layout.Skeletons;

/// <summary>
/// 配置管道构建器 — 编排 JSON 加载管线
/// <para>★ 从 WdUI 迁移至 FileDriven ★</para>
/// </summary>
public static class ConfigPipelineBuilder
{
    private static readonly ConfigRenderPipeline<ConfigContainer> _pipeline = new();
    
    public static Flow<string, ConfigContainer> CreateDefaultPipeline(bool hasTemplateProcessor = false)
        => _pipeline.CreateDefaultPipeline(hasTemplateProcessor);
    
    public static async Task<ConfigContainer> ExecuteAsync(
        string configPath, ConfigContainer container, ...)
        => await _pipeline.ExecuteAsync(configPath, container, ...);
}
```

#### Step 5: FileDrivenWdUIBridge 增强

**文件**: `FileDriven/FileDriven.UI/Abstractions/FileDrivenWdUIBridge.cs`

**操作**: 增强桥接方法，使其在 FileDriven 侧完成 JSON 加载后再传入 WdUI

```csharp
// 增强后：FileDrivenWdUIBridge 中的方法改为先加载 JSON 再传入对象

/// <summary>
/// 从文件加载配置到容器（FileDriven 侧完成 JSON 加载）
/// </summary>
public static async Task LoadConfigAsync(ConfigContainer container, string configPath, bool strictMode = true)
{
    // ★ 在 FileDriven 侧完成 JSON 加载和校验
    var config = await JsonService.LoadAndValidateAsync(configPath, strictMode);
    if (config != null)
    {
        // ★ 传入已反序列化的对象到 WdUI
        await container.LoadConfigAsync(config);
    }
}

/// <summary>
/// 加载侧边容器（FileDriven 侧完成 JSON 加载）
/// </summary>
public static async Task<UIConfiguration?> LoadSideContainerAsync(
    ConfigContainer sideContainer, string configPath, SemaphoreSlim loadLock, Action<string> logAction)
{
    await loadLock.WaitAsync(CancellationToken.None);
    try
    {
        // ★ 在 FileDriven 侧完成 JSON 加载
        var config = await JsonService.LoadAndValidateAsync(configPath, strictMode: false);
        if (config == null)
        {
            logAction($"[ERROR] Failed to load config: {configPath}");
            return null;
        }
        
        // ★ 传入已反序列化的对象到 WdUI
        await sideContainer.LoadConfigAsync(config);
        return config;
    }
    finally
    {
        loadLock.Release();
    }
}
```

---

### 3.3 第二阶段：清理 WdUI

> ⚠️ 第二阶段必须在第一阶段完成后执行，确保 FileDriven 已具备完整的 JSON 处理能力。

#### Step 6: 改造 ConfigContainer — 移除文件路径加载

**文件**: `Operation/WdUI/UI/WdUI.Container.cs`

**操作**:
- 移除 `LoadConfigAsync(string configPath)` 方法（第204-261行）
- 保留 `LoadConfigAsync(UIConfiguration config)` 方法（第270-295行）
- 移除 `OnHandleCreated` 中的自动加载逻辑（第169-194行），因为自动加载需要文件路径

**变更后接口**:
```csharp
public class ConfigContainer : Panel, IContainer
{
    // 只保留对象加载
    public async Task LoadConfigAsync(UIConfiguration config)
    {
        // 纯 UI 渲染逻辑，不涉及任何 JSON 操作
        await RenderConfigAsync(config);
    }
    
    // 移除: public async Task LoadConfigAsync(string configPath)
}
```

#### Step 7: 移除 ConfigurationLoader（纯委托层）

**文件**: `Operation/WdUI/UI/WdUI.Configuration.cs`

**操作**:
- 删除整个 `ConfigurationLoader` 类
- 删除 `IWduiConfigurationLoader` 接口
- 保留 `NavigationBarPosition`、`RefreshMode`、`ComponentType` 枚举（WdUI 特有）
- 保留 `ConfigurationModelExtensions` 扩展方法类（WdUI 特有枚举转换）
- 保留 `WinFormsControlConfigExtensions` 类（WinForms 类型转换）

**影响**:
- `WdUIComposer.Config` 改为返回 `UniversalMiddleware.Abstractions.IConfigLoader`（由 Middleware 提供）
- `WdUIUsage.ConfigurationLoader` 属性移除
- `WdUIUsage.Config` 改为直接返回 `Middleware.ConfigLoader`

#### Step 8: 清理 WdUIUsage 中的 JSON 方法

**文件**: `Operation/WdUI/WdUIUsage.cs`

**操作**: 移除以下方法：

| 方法 | 行号 | 替代方式 |
|------|------|---------|
| `LoadConfigAsync(string)` | L139-140 | 使用 `FileDrivenConfigService.LoadAndRenderAsync` |
| `SaveConfigAsync(UIConfiguration, string)` | L145-146 | 使用 `JsonFileService.SaveConfigAsync` |
| `ValidateConfigAsync(string)` | L152-157 | 使用 `JsonService.LoadAndValidateAsync` |
| `RenderFromJsonAsync(Control, string)` | L163-173 | 使用 `FileDrivenConfigService.LoadAndRenderFromJsonAsync` |
| `RenderFromFileAsync(Control, string)` | L233-237 | 使用 `FileDrivenConfigService.LoadAndRenderAsync` |
| `ConfigurationLoader` | L176-177 | 移除 |
| `GetCachedConfig(string)` | L182-183 | 使用 `JsonFileService.GetCachedConfig` |
| `ReloadConfigAsync(string)` | L187-191 | 使用 `JsonFileService.ClearCache` + `LoadAndRenderAsync` |
| `RegisterConfigChangeHandler` | L196-197 | 使用 `JsonFileService.RegisterChangeHandler` |
| `UnregisterConfigChangeHandler` | L201-202 | 使用 `JsonFileService.UnregisterChangeHandler` |
| `LoadConfigToCache` | L206-207 | 使用 `JsonFileService.LoadConfigToCache` |
| `ClearConfigCache` | L210-211 | 使用 `JsonFileService.ClearCache` |
| `DeserializeConfig(string)` | L812-813 | 使用 `JsonService.Deserialize` |
| `SerializeConfig(UIConfiguration)` | L818-819 | 使用 `JsonFileService.SerializeConfig` |
| `ValidateJson(string)` | L824-832 | 使用 `JsonFileService.ValidateConfigContent` |
| `ExtractJsonError(string, JsonException)` | L838-839 | 使用 `JsonFileService.ExtractJsonErrorContext` |
| `BuildConfigPipeline<T>()` | L1002-1004 | 使用 `ConfigPipelineBuilder`（FileDriven 侧） |
| `BuildExtendedConfigPipeline<T>()` | L1009-1011 | 使用 `ConfigPipelineBuilder`（FileDriven 侧） |

**保留的方法**:
- `RenderAsync(Control, UIConfiguration)` — 纯 UI 操作
- `RenderCreate(Control, UIConfiguration)` — 纯 UI 操作
- `RenderApplyLayout(Control)` — 纯 UI 操作
- `RenderBindEventsAsync(Control)` — 纯 UI 操作
- `Relayout(Control)` — 纯 UI 操作
- `CreateContainer(string)` — 纯 UI 操作
- `CreateContainerAsync(string, UIConfiguration)` — 改为只接受对象
- `CreateContainerWithConfigAsync(string, UIConfiguration)` — 保留
- `CreateSubContainerAsync(ConfigContainer, string, UIConfiguration)` — 改为只接受对象

#### Step 9: 移除 ConfigPathManager（纯委托层）

**文件**: `Operation/WdUI/UI/WdUI.Management.cs` (L33-67)

**操作**:
- 删除 `ConfigPathManager` 类

**影响**:
- 调用方改为直接使用 `FileDriven.Utilities.Services.JsonConfigPathManager`

#### Step 10: 移除 WdUIAdapter（JSON 管线构建）

**文件**: `Operation/WdUI/UI/WdUI.Adapters.cs`

**操作**:
- 删除整个文件

**影响**:
- `WdUIUsage.BuildConfigPipeline<T>()` 和 `BuildExtendedConfigPipeline<T>()` 已在上一步移除

#### Step 11: 移除项目引用

**文件**: `Operation/WdUI/WdUI.csproj` (L41)

**操作**: 删除以下行：
```xml
<!-- FileDriven.Utilities（JsonFileService — JSON 文件操作统一入口） -->
<ProjectReference Include="..\..\FileDriven\FileDriven.Utilities\FileDriven.Utilities.csproj" />
```

---

## 四、改造后架构

### 4.1 依赖关系变化

```
改造前：
  WdUI ──依赖──> FileDriven.Utilities (JsonFileService)
    │
    └──依赖──> UniversalMiddleware (IConfigLoader, LayoutPositionConverter)
    │
    └──依赖──> BasicMethod.ConfigurationModels (UIConfiguration)

改造后：
  WdUI ──依赖──> UniversalMiddleware (IConfigLoader, LayoutPositionConverter)
    │
    └──依赖──> BasicMethod.ConfigurationModels (UIConfiguration)
    │
    └──不再依赖 FileDriven 任何项目

  FileDriven ──依赖──> FileDriven.Utilities (JsonFileService)
    │
    └──依赖──> WdUI (ConfigContainer, WinFormsRenderPipeline)
```

### 4.2 数据流变化

```
改造前：
  FileDriven → WdUI.LoadConfigAsync(string path)
    → WdUI.ConfigurationLoader.LoadConfigAsync(path)
      → FileDriven.Utilities.JsonFileService.LoadConfigAsync(path)
        → 读取 JSON 文件
        → 反序列化
        → 返回 UIConfiguration
    → ConfigContainer.RenderConfigAsync(config)
    → WinFormsRenderPipeline.Render(target, config)

改造后：
  FileDriven → FileDrivenConfigService.LoadAndRenderAsync(container, path)
    → JsonService.LoadAndValidateAsync(path)
      → FileDriven.Utilities.JsonFileService.LoadConfigAsync(path)
        → 读取 JSON 文件
        → 反序列化
        → 返回 UIConfiguration
    → FileDriven.JsonRefer.LayoutJson.ContractValidator.Validate(config)
    → WdUI.ConfigContainer.LoadConfigAsync(config)    ← 传入已反序列化的对象
      → ConfigContainer.RenderConfigAsync(config)
      → WinFormsRenderPipeline.Render(target, config)
```

### 4.3 WdUI 最终职责

改造完成后，WdUI 只负责以下纯 UI 编排逻辑：

| 职责 | 说明 |
|------|------|
| **控件创建** | 根据 `ControlConfig.Type` 创建 WinForms 控件实例 |
| **属性应用** | 将 `ControlConfig` 的属性映射到 WinForms 控件 |
| **布局计算** | 比例布局、堆叠布局、重叠检测、自动优化 |
| **事件绑定** | 将 JSON 配置中的事件绑定到 WinForms 事件 |
| **容器管理** | ConfigContainer 的创建、注册、查找、清空 |
| **渲染管线** | 三阶段渲染（创建→布局→事件绑定） |
| **视图导航** | Form/View 的注册、显示、隐藏、关闭 |

### 4.4 FileDriven 新增职责

| 职责 | 说明 |
|------|------|
| **JSON 文件读取** | 通过 `JsonFileService` 读取 JSON 文件 |
| **JSON 反序列化** | 将 JSON 字符串反序列化为 `UIConfiguration` 对象 |
| **契约校验** | 通过 `ContractValidator` 校验配置合法性 |
| **模板预处理** | 通过 `JsonTemplateProcessor` 展开 style/template 引用 |
| **配置缓存** | 管理已加载配置的缓存 |
| **变更通知** | 配置文件变更时通知订阅者 |
| **配置管线编排** | 通过 `ConfigPipelineBuilder` 编排 JSON 加载管线 |
| **统一入口** | 通过 `FileDrivenConfigService` 提供 JSON→渲染的完整管线 |

---

## 五、实施计划

### 5.1 执行顺序

```
第一阶段：完善 FileDriven
  Step 1 ──→ Step 2 ──→ Step 3 ──→ Step 4 ──→ Step 5
  (新建服务)   (迁移骨架)   (迁移骨架)   (迁移构建器)   (增强桥接)

第二阶段：清理 WdUI（依赖第一阶段完成）
  Step 6 ──→ Step 7 ──→ Step 8 ──→ Step 9 ──→ Step 10 ──→ Step 11
  (改造容器)   (移除加载器) (清理入口) (移除路径) (移除适配器) (移除引用)
```

### 5.2 详细步骤（实施状态）

#### 第一阶段：完善 FileDriven ✅ 已完成

| 步骤 | 内容 | 文件 | 操作 | 风险 | 状态 |
|------|------|------|------|------|------|
| **1** | 新建 `FileDrivenConfigService` | `FileDriven/FileDriven.UI/Services/FileDrivenConfigService.cs` | 新建文件 | 🟢 低 | ✅ 完成 |
| **2** | 迁移 `ConfigLoadSkeleton` | WdUI → FileDriven | 复制 + 改命名空间 | 🟢 低 | ✅ 完成 |
| **3** | 迁移 `TemplatePreprocessSkeleton` | WdUI → FileDriven | 复制 + 改命名空间 | 🟢 低 | ✅ 完成 |
| **4** | 迁移 `ConfigPipelineBuilder` + `ContainerRenderSkeleton` | WdUI → FileDriven | 复制 + 改命名空间 | 🟢 低 | ✅ 完成 |
| **5** | 增强 `FileDrivenWdUIBridge` | `FileDrivenWdUIBridge.cs` | 修改方法内部逻辑 | 🟡 中 | ✅ 完成 |

#### 第二阶段：清理 WdUI ✅ 已完成

| 步骤 | 内容 | 文件 | 操作 | 风险 | 依赖 | 状态 |
|------|------|------|------|------|------|------|
| **6** | 改造 `ConfigContainer` | `WdUI.Container.cs` | 移除 `OnHandleCreated` 自动加载 | 🔴 高 | Step 5 | ✅ 完成 |
| **7** | 移除 `ConfigurationLoader` | `WdUI.Configuration.cs` | 删除类 | 🟡 中 | Step 6 | ✅ 完成 |
| **8** | 清理 `WdUIUsage` | `WdUIUsage.cs` | 标记 JSON 方法为 `[Obsolete]` | 🟡 中 | Step 7 | ✅ 完成 |
| **9** | 移除 `ConfigPathManager` | `WdUI.Management.cs` | 删除类 | 🟢 低 | 无 | ✅ 完成 |
| **10** | 移除 `WdUIAdapter` | `WdUI.Adapters.cs` | 删除文件 | 🟢 低 | Step 8 | ✅ 完成 |
| **11** | 移除项目引用 | `WdUI.csproj` | 删除引用行 | 🟢 低 | Step 7, 8, 9, 10 | ✅ 完成 |

### 5.3 验证方式

| 阶段 | 验证方式 |
|------|---------|
| 第一阶段每个 Step 完成后 | `dotnet build` 确保编译通过 |
| Step 5 完成后 | 运行 FileDriven 集成测试，确保 JSON 加载→渲染流程正常 |
| 第二阶段 Step 6 完成后 | 运行 WdUI 集成测试，确保容器加载功能正常 |
| 第二阶段全部完成后 | 运行完整测试套件，确保无回归 |
| 最终 | 确认 WdUI 不再依赖 `FileDriven.Utilities` |

---

## 六、附录

### 6.1 相关文件路径

| 文件 | 路径 |
|------|------|
| WdUI 项目文件 | `Operation/WdUI/WdUI.csproj` |
| WdUI 统一入口 | `Operation/WdUI/WdUIUsage.cs` |
| WdUI 容器 | `Operation/WdUI/UI/WdUI.Container.cs` |
| WdUI 配置加载器 | `Operation/WdUI/UI/WdUI.Configuration.cs` |
| WdUI 路径管理器 | `Operation/WdUI/UI/WdUI.Management.cs` |
| WdUI 适配器 | `Operation/WdUI/UI/WdUI.Adapters.cs` |
| WdUI 配置加载骨架 | `Operation/WdUI/Layout/Skeletons/ConfigLoadSkeleton.cs` |
| WdUI 模板预处理骨架 | `Operation/WdUI/Layout/Skeletons/TemplatePreprocessSkeleton.cs` |
| WdUI 配置管道构建器 | `Operation/WdUI/Layout/Skeletons/ConfigPipelineBuilder.cs` |
| WdUI 容器渲染阶段 | `Operation/WdUI/Layout/Skeletons/WduiContainerRenderStage.cs` |
| WdUI 布局引擎 | `Operation/WdUI/UI/WdUI.LayoutEngine.cs` |
| WdUI 布局管理 | `Operation/WdUI/UI/WdUI.Layout.cs` |
| FileDriven 桥接 | `FileDriven/FileDriven.UI/Abstractions/FileDrivenWdUIBridge.cs` |
| FileDriven JSON 服务 | `FileDriven/FileDriven.UI/JsonService.cs` |
| FileDriven JSON 引用 | `FileDriven/FileDriven.JsonRefer/LayoutJson/LayoutJsonService.cs` |
| FileDriven 工具 JSON 服务 | `FileDriven/FileDriven.Utilities/Services/JsonFileService.cs` |
| FileDriven 工具路径管理 | `FileDriven/FileDriven.Utilities/Services/JsonConfigPathManager.cs` |
| UniversalMiddleware 入口 | `Share/UniversalMiddleware/UniversalMiddleware.cs` |
| UniversalMiddleware 配置加载器接口 | `Share/UniversalMiddleware/Abstractions/IConfigLoader.cs` |
| UniversalMiddleware 布局转换器 | `Share/UniversalMiddleware/Layout/LayoutPositionConverter.cs` |
| UniversalMiddleware JSON 加载阶段 | `Share/UniversalMiddleware/Rendering/JsonConfigLoadStage.cs` |
| UniversalMiddleware 模板预处理阶段 | `Share/UniversalMiddleware/Rendering/TemplatePreprocessStage.cs` |
| UniversalMiddleware 配置渲染管线 | `Share/UniversalMiddleware/Rendering/ConfigRenderPipeline.cs` |

### 6.2 现有文档

- `docs/analysis/WdUI_Json逻辑迁移到UniversalMiddleware方案.md` — 旧方案（迁移到 UniversalMiddleware）
- `docs/analysis/WdUI_Json逻辑下迁方案.md` — 更早的下迁方案
