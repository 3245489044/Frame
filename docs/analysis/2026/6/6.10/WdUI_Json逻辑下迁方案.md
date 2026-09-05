# WdUI JSON 逻辑下迁方案

## 1. 背景与目标

### 1.1 当前问题

WdUI 作为 UI 渲染引擎，职责边界不清晰，混杂了大量 JSON 处理逻辑：

- JSON 文件加载/保存/缓存
- JSON 格式校验（契约校验）
- JSON 模板展开（style/template/extends）
- JSON 配置路径管理

这些逻辑应该属于 `FileDriven.JsonRefer`（JSON 引用处理层），而非 WdUI。

### 1.2 目标

将 WdUI 中的 JSON 相关逻辑全部下迁到 `FileDriven.JsonRefer`，使：

- **WdUI** 只接收已解析好的 `UIConfiguration` 对象，执行：创建控件 → 应用布局 → 绑定事件
- **FileDriven.JsonRefer** 负责所有 JSON 处理：加载/校验/模板展开/缓存/路径管理

---

## 2. 现状分析

### 2.1 WdUI 中需要迁移的 JSON 逻辑

| # | 文件 | 当前位置 | 需迁移的内容 | 迁移目标 |
|---|------|---------|-------------|---------|
| 1 | `WdUIJson.cs` | `Operation/WdUI/Serialization/` | `LoadConfigAsync`、`SaveConfigAsync`、`DeserializeConfig`、`SerializeConfig`、`ValidateConfigContent`、`ExtractJsonErrorContext` | `FileDriven.JsonRefer` |
| 2 | `ConfigurationLoader.cs` | `Operation/WdUI/UI/Configuration/` | `LoadConfigAsync`（文件加载+缓存）、`SaveConfigAsync`、`ValidateConfigContent`、`ReloadConfigAsync`、缓存管理、变更通知 | `FileDriven.JsonRefer` |
| 3 | `ConfigPathManager.cs` | `Operation/WdUI/UI/Utils/` | `ConfigRoot`、`GlobalConfigPath`、`GetConfigPath`、`EnsureConfigDirectories` | `FileDriven.JsonRefer` |
| 4 | `WdUIUsage.cs` 中的 JSON 方法 | `Operation/WdUI/` | `LoadConfigAsync`、`SaveConfigAsync`、`ValidateConfigAsync`、`RenderFromJsonAsync`、`DeserializeConfig`、`SerializeConfig`、`ValidateJson`、`ExtractJsonError`、`GetCachedConfig`、`ReloadConfigAsync`、`RegisterConfigChangeHandler`、`LoadConfigToCache`、`ClearConfigCache` | 改为委托到 `FileDriven.JsonRefer` |
| 5 | `WdUI.RenderPipeline.cs` 中的 `RenderFromJsonAsync` | `Operation/WdUI/` | 反序列化+渲染的快捷方法 | 改为接收 `UIConfiguration` 对象 |

### 2.2 已正确归属的逻辑

以下逻辑已在 `FileDriven.JsonRefer` 中，**无需迁移**：

| 文件 | 位置 | 说明 |
|------|------|------|
| `LayoutJsonService.cs` | `FileDriven.JsonRefer/LayoutJson/` | 加载/校验/查询的统一入口 |
| `ContractBuilder.cs` | `FileDriven.JsonRefer/LayoutJson/` | 契约构建 |
| `ContractRegistry.cs` | `FileDriven.JsonRefer/LayoutJson/` | 契约注册 |
| `ContractValidator.cs` | `FileDriven.JsonRefer/LayoutJson/` | 契约校验 |
| `ControlConfigContract.cs` | `FileDriven.JsonRefer/LayoutJson/` | 契约定义 |
| `ControlTreeWalker.cs` | `FileDriven.JsonRefer/LayoutJson/` | 控件树遍历 |
| `JsonProcessingService.cs` | `FileDriven.JsonRefer/JsonProcessing/` | JSON 预处理 |
| `JsonTemplateProcessor.cs` | `FileDriven.JsonRefer/JsonProcessing/` | 模板展开 |
| `JsonService.cs` | `FileDriven.UI/` | 委托层（已指向 `LayoutJsonService`） |

### 2.3 调用链现状

```
FileDriven.UI (MainForm / FileDrivenWdUIBridge)
  │
  ├── JsonService.LoadAndValidateAsync()          ← 已委托到 FileDriven.JsonRefer ✅
  │
  └── container.LoadConfigAsync(config)           ← 进入 WdUI
        │
        └── ConfigContainer.RenderConfigAsync()
              │
              ├── _orchestrator.RenderFullAsync()  ← 渲染管线（保留在 WdUI ✅）
              │
              └── WdUIJson.LoadConfigAsync()       ← 双重加载问题 ❌
```

**关键问题**：`FileDrivenWdUIBridge.LoadConfigAsync` 先通过 `JsonService.LoadAndValidateAsync` 加载校验，再传入 `container.LoadConfigAsync(config)`。但 `ConfigContainer.RenderConfigAsync` 内部不再重新加载文件，所以没有双重加载问题。但 `WdUIJson.LoadConfigAsync` 仍然被 `ConfigurationLoader` 和 `WdUIUsage` 直接调用，这些调用路径应该改为通过 `FileDriven.JsonRefer`。

---

## 3. 迁移方案

### 3.1 总体架构

```
迁移前：
  FileDriven.UI → WdUI (WdUIJson / ConfigurationLoader) → JSON 文件
                    ↓
                  WdUI (RenderPipeline) → WinForms 控件

迁移后：
  FileDriven.UI → FileDriven.JsonRefer (JsonService / LayoutJsonService) → JSON 文件
                    ↓ (返回 UIConfiguration)
                  WdUI (RenderPipeline) → WinForms 控件
```

### 3.2 迁移步骤

#### 步骤 1：在 FileDriven.JsonRefer 中创建 JsonFileService

在 `FileDriven.JsonRefer` 中创建新的 `JsonFileService`，整合 WdUI 中的 JSON 文件操作逻辑：

```csharp
// FileDriven.JsonRefer/Services/JsonFileService.cs
namespace FileDriven.JsonRefer.Services;

/// <summary>
/// JSON 文件服务 — 统一管理 JSON 配置文件的加载/保存/缓存/校验
/// 从 WdUI.Serialization.WdUIJson 和 WdUI.UI.Configuration.ConfigurationLoader 迁移而来
/// </summary>
public class JsonFileService
{
    // 单例
    public static JsonFileService Instance { get; } = new();

    // 配置缓存
    private readonly MemoryCache<string, UIConfiguration> _configCache = new();
    private readonly Dictionary<string, Action<string>> _changeHandlers = new();

    // 从文件加载配置（含预处理）
    public async Task<UIConfiguration> LoadConfigAsync(string configPath) { ... }

    // 保存配置到文件
    public async Task SaveConfigAsync(string configPath, UIConfiguration config) { ... }

    // 从 JSON 字符串反序列化
    public UIConfiguration? DeserializeConfig(string json) { ... }

    // 序列化到 JSON 字符串
    public string SerializeConfig(UIConfiguration config) { ... }

    // 校验配置内容
    public JsonValidationResult? ValidateConfigContent(string jsonContent) { ... }

    // 提取 JSON 错误上下文
    public string? ExtractJsonErrorContext(string configPath, JsonException ex) { ... }

    // 缓存管理
    public UIConfiguration GetCachedConfig(string configPath) { ... }
    public void LoadConfigToCache(UIConfiguration config) { ... }
    public void ClearCache() { ... }
    public void RegisterChangeHandler(string configPath, Action<string> handler) { ... }
    public void UnregisterChangeHandler(string configPath) { ... }
}
```

#### 步骤 2：WdUIJson 改为委托层

将 `WdUIJson.cs` 改为纯委托层，所有方法委托到 `FileDriven.JsonRefer.Services.JsonFileService`：

```csharp
// Operation/WdUI/Serialization/WdUIJson.cs — 改为委托层
public static class WdUIJson
{
    private static readonly JsonFileService _service = JsonFileService.Instance;

    public static Task<UIConfiguration> LoadConfigAsync(string configPath)
        => _service.LoadConfigAsync(configPath);

    public static Task SaveConfigAsync(string configPath, UIConfiguration config)
        => _service.SaveConfigAsync(configPath, config);

    // ... 其他方法同理
}
```

#### 步骤 3：ConfigurationLoader 改为委托层

将 `ConfigurationLoader.cs` 改为委托到 `JsonFileService`：

```csharp
// Operation/WdUI/UI/Configuration/ConfigurationLoader.cs — 改为委托层
public class ConfigurationLoader : IWduiConfigurationLoader
{
    private static readonly JsonFileService _service = JsonFileService.Instance;

    public async Task<UIConfiguration> LoadConfigAsync(string configPath)
        => await _service.LoadConfigAsync(configPath);

    // ... 其他方法同理
}
```

#### 步骤 4：WdUIUsage 中的 JSON 方法改为委托

`WdUIUsage.cs` 中所有 JSON 相关方法改为委托到 `FileDriven.JsonRefer`：

```csharp
// WdUIUsage.cs — JSON 方法改为委托
public static Task<UIConfiguration> LoadConfigAsync(string configPath)
    => FileDriven.JsonRefer.Services.JsonFileService.Instance.LoadConfigAsync(configPath);
```

#### 步骤 5：ConfigPathManager 迁移

将 `ConfigPathManager` 迁移到 `FileDriven.JsonRefer`，WdUI 中保留委托：

```csharp
// FileDriven.JsonRefer/Services/JsonConfigPathManager.cs
public static class JsonConfigPathManager
{
    public static string ConfigRoot => ...;
    public static string GlobalConfigPath => ...;
    public static string GetConfigPath(string relativePath) => ...;
    public static void EnsureConfigDirectories() => ...;
}
```

#### 步骤 6：清理 WdUI 项目引用

迁移完成后，WdUI 项目不再需要直接引用 JSON 处理相关的 NuGet 包（如 `System.Text.Json` 的某些配置），但需要保留对 `BasicMethod.ConfigurationModels` 的引用（因为 `UIConfiguration` 模型仍在使用）。

---

## 4. 接口定义

### 4.1 JsonFileService 接口

```csharp
// FileDriven.JsonRefer/Services/IJsonFileService.cs
public interface IJsonFileService
{
    // 加载
    Task<UIConfiguration> LoadConfigAsync(string configPath);
    Task<UIConfiguration?> LoadConfigOptionalAsync(string configPath);

    // 保存
    Task SaveConfigAsync(string configPath, UIConfiguration config);

    // 序列化/反序列化
    UIConfiguration? DeserializeConfig(string json);
    string SerializeConfig(UIConfiguration config);

    // 校验
    JsonValidationResult? ValidateConfigContent(string jsonContent);
    string? ExtractJsonErrorContext(string configPath, JsonException ex);

    // 缓存
    UIConfiguration GetCachedConfig(string configPath);
    void LoadConfigToCache(UIConfiguration config);
    void ClearCache();
    void RegisterChangeHandler(string configPath, Action<string> handler);
    void UnregisterChangeHandler(string configPath);
}
```

### 4.2 渲染管线接口（保持不变）

WdUI 的渲染管线接口保持不变，只接收 `UIConfiguration` 对象：

```csharp
// WdUI 渲染管线 — 不关心 JSON 来源
public class WinFormsRenderPipeline
{
    public void Render(Control target, UIConfiguration config);
    public void ApplyLayout(Control target);
    public Task BindEventsAsync(Control target);
}
```

---

## 5. 影响范围

### 5.1 需要修改的文件

| 文件 | 修改类型 | 说明 |
|------|---------|------|
| `Operation/WdUI/Serialization/WdUIJson.cs` | 重构 | 改为委托层 |
| `Operation/WdUI/UI/Configuration/ConfigurationLoader.cs` | 重构 | 改为委托层 |
| `Operation/WdUI/UI/Utils/ConfigPathManager.cs` | 重构 | 改为委托层 |
| `Operation/WdUI/WdUIUsage.cs` | 重构 | JSON 方法改为委托 |
| `FileDriven.JsonRefer/FileDriven.JsonRefer.csproj` | 修改 | 添加新文件引用 |
| `FileDriven.JsonRefer/Services/JsonFileService.cs` | **新建** | 核心迁移目标 |
| `FileDriven.JsonRefer/Services/IJsonFileService.cs` | **新建** | 接口定义 |
| `FileDriven.JsonRefer/Services/JsonConfigPathManager.cs` | **新建** | 路径管理迁移 |

### 5.2 不需要修改的文件

| 文件 | 说明 |
|------|------|
| `FileDriven.UI/JsonService.cs` | 已正确委托到 `LayoutJsonService` |
| `FileDriven.UI/Abstractions/FileDrivenWdUIBridge.cs` | 调用链不变 |
| `FileDriven.UI/Core/MainForm.cs` | 调用链不变 |
| `Operation/WdUI/Layout/Skeletons/CreateLayoutPanelSkeleton.cs` | 纯 UI 逻辑，保留 |
| `Operation/WdUI/Layout/Skeletons/ApplyLayoutSkeleton.cs` | 纯 UI 逻辑，保留 |
| `Operation/WdUI/UI/WdUI.Container.cs` | 渲染编排逻辑，保留 |
| `Operation/WdUI/WdUI.RenderPipeline.cs` | 渲染管线，保留 |

### 5.3 外部调用方影响

| 调用方 | 当前调用 | 迁移后调用 | 影响 |
|--------|---------|-----------|------|
| `FileDrivenWdUIBridge.LoadConfigAsync` | `JsonService.LoadAndValidateAsync` + `container.LoadConfigAsync(config)` | 不变 | 无影响 |
| `WdUIUsage.LoadConfigAsync` | `WdUIJson.LoadConfigAsync` | `JsonFileService.Instance.LoadConfigAsync` | 需修改引用 |
| `WdUIUsage.RenderFromJsonAsync` | `WdUIJson.RenderFromJsonAsync` | 不变（内部委托） | 无影响 |
| `ConfigurationLoader` 外部使用者 | 直接使用 | 不变（内部委托） | 无影响 |

---

## 6. 迁移优先级

### 第一阶段（高优先级 — 核心功能迁移）

1. 创建 `JsonFileService`（整合 `WdUIJson` 和 `ConfigurationLoader` 的核心逻辑）
2. 创建 `IJsonFileService` 接口
3. 将 `WdUIJson` 改为委托层
4. 将 `ConfigurationLoader` 改为委托层

### 第二阶段（中优先级 — 路径管理迁移）

5. 创建 `JsonConfigPathManager`
6. 将 `ConfigPathManager` 改为委托层

### 第三阶段（低优先级 — 清理与优化）

7. 更新 `WdUIUsage` 中的 JSON 方法引用
8. 移除 WdUI 中不再需要的 JSON 相关依赖
9. 全面测试

---

## 7. 风险与注意事项

### 7.1 循环依赖风险

- `FileDriven.JsonRefer` 引用了 `WdUI`（通过 `WdUI.csproj` 第 43 行）
- 如果 `JsonFileService` 需要引用 WdUI 类型，会造成循环依赖
- **解决方案**：`JsonFileService` 只依赖 `BasicMethod.ConfigurationModels`（模型层），不依赖 WdUI

### 7.2 缓存一致性

- 迁移后 `JsonFileService` 管理缓存，WdUI 不再维护独立缓存
- 需要确保 `ConfigurationLoader` 的缓存与 `JsonFileService` 的缓存一致
- **解决方案**：`ConfigurationLoader` 完全委托到 `JsonFileService`，不维护独立缓存

### 7.3 线程安全

- `JsonFileService` 需要保持 `ConfigurationLoader` 现有的线程安全设计
- 使用 `ConcurrentDictionary` 和 `SemaphoreSlim` 确保并发安全

### 7.4 向后兼容

- WdUI 中的旧方法标记为 `[Obsolete]`，指向新方法
- 确保所有现有调用代码在迁移后无需修改

---

## 8. 验收标准

1. ✅ `FileDriven.JsonRefer` 中存在 `JsonFileService`，包含所有 JSON 文件操作
2. ✅ `WdUIJson.cs` 中所有方法改为委托到 `JsonFileService`
3. ✅ `ConfigurationLoader.cs` 中所有方法改为委托到 `JsonFileService`
4. ✅ `ConfigPathManager.cs` 中所有方法改为委托到 `JsonConfigPathManager`
5. ✅ 所有现有功能正常运行（加载/保存/校验/缓存/渲染）
6. ✅ 无循环依赖
7. ✅ 缓存一致性保持
