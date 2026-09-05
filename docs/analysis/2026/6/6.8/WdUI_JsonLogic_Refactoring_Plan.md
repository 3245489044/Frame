# WdUI JSON 逻辑重构方案

## 一、问题现状

### 1.1 错误现象

```
[ERROR] Failed to load side config: 类型名称不能为空
```

错误发生在 `DefaultControlCreator.CreateControl(String typeName)` 第 93 行，原因是 `typeName` 参数为 null 或空字符串。

### 1.2 JSON 反序列化入口散落分布（重构前）

> **当前状态：** 以下 5 处入口已全部集中到 `WdUIJson` 静态类（`Operation/WdUI/Serialization/WdUIJson.cs`），详见下方「已集中到 WdUIJson」标注。

| # | 位置 | 文件 | 行 | 反序列化方式 | 问题 | 当前状态 |
|---|------|------|----|-------------|------|---------|
| ① | `ConfigContainer.LoadConfigAsync` | `WdUI.Container.cs` | 200 | `System.Text.Json.JsonSerializer.Deserialize(DefaultOptions)` | **绕过** `UnifiedJsonSerializer`，缺少 `ConvertBareNamesToPrefixed` | ✅ 已集中到 `WdUIJson.FromJson` |
| ② | `ConfigContainer.LoadConfigFromJsonAsync` | `WdUI.Container.cs` | 278 | `WdUIComposer.FromJson<UIConfiguration>` | 委托到 `UnifiedJsonSerializer`，正确 | ✅ 已集中到 `WdUIJson.FromJson` |
| ③ | `ConfigurationLoader.LoadConfigAsync` | `WdUI.Configuration.cs` | 57 | `_serializer.Deserialize<UIConfiguration>` | 使用 `UnifiedJsonSerializer`，但**手动预处理导致双重预处理** | ✅ 已集中到 `WdUIJson.LoadConfigAsync` |
| ④ | `WdUIComposer.RenderFromJson` | `WdUIComposer.cs` | 260 | `FromJson<UIConfiguration>` | 委托到 `UnifiedJsonSerializer`，正确 | ✅ 已集中到 `WdUIJson.RenderFromJson` |
| ⑤ | `ConfigLoadSkeleton` | `ConfigLoadSkeleton.cs` | 48 | `WdUIComposer.LoadConfigAsync`（已标记 Obsolete） | 委托到 `ConfigurationLoader` | ✅ 已标记 Obsolete |

### 1.3 预处理逻辑重复（重构前）

> **当前状态：** 以下 3 处预处理已全部集中到 `WdUIJson.PreprocessJson`，消除重复。

| # | 位置 | 文件 | 行 | 预处理方式 | 当前状态 |
|---|------|------|----|-----------|---------|
| ① | `ConfigContainer.LoadConfigAsync` | `WdUI.Container.cs` | 178 | 手动调用 `UnifiedJsonSerializer.Preprocessor.Preprocess(rawJson)` | ✅ 已集中到 `WdUIJson.PreprocessJson` |
| ② | `ConfigurationLoader.LoadConfigAsync` | `WdUI.Configuration.cs` | 55 | 手动调用 `UnifiedJsonSerializer.Preprocessor.Preprocess(rawJson)` | ✅ 已集中到 `WdUIJson.LoadConfigAsync` |
| ③ | `TemplatePreprocessSkeleton` | `TemplatePreprocessSkeleton.cs` | 43 | 通过 `SkeletonContext` 获取 `TemplateProcessor` 委托（**已废弃**） | ✅ 已标记 Obsolete |

### 1.4 核心问题链

```
JSON 文件
    │
    ├─→ ConfigContainer.LoadConfigAsync (入口①)
    │      ├─ Preprocessor.Preprocess()     ← 手动预处理（正确）
    │      └─ JsonSerializer.Deserialize()  ← 跳过 ConvertBareNamesToPrefixed（错误！）
    │
    ├─→ ConfigurationLoader.LoadConfigAsync (入口③)
    │      ├─ Preprocessor.Preprocess()     ← 手动预处理（冗余）
    │      └─ _serializer.Deserialize()     ← 带 ConvertBareNamesToPrefixed
    │                                        ← 双重预处理！
    │
    └─→ TemplatePreprocessSkeleton (入口⑤)
           └─ TemplateProcessor 委托        ← 已废弃，永远不执行
```

**"类型名称不能为空"的根本原因：**
1. JSON 中某个控件的 `type` 字段确实为空（配置错误）
2. 经过 `Preprocessor` 预处理后，`type` 字段被错误地置空（预处理 bug）
3. `ConvertBareNamesToPrefixed` 转换器在某个路径上被应用了两次，导致类型名被错误转换

---

## 二、目标架构

### 2.1 统一 JSON 处理管线（实际架构）

> **实际实现：** 采用 `WdUIJson` 静态类作为统一入口，而非原先计划的 `ConfigContainerBase` 基类方案。

所有 JSON 反序列化统一经过 `WdUIJson` 静态类，消除散落入口。

```
┌─────────────────────────────────────────────────────────────┐
│  WdUI 层                                                    │
│  ┌──────────────────────────────────────────────────────┐   │
│  │              WdUIJson (统一入口)                      │   │
│  │                                                       │   │
│  │  - ToJson<T>(obj)             序列化                  │   │
│  │  - FromJson<T>(json)          反序列化                │   │
│  │  - LoadFromFile<T>(path)      文件加载                │   │
│  │  - SaveToFile<T>(path, obj)   文件保存                │   │
│  │  - RenderFromJson(json, ctrl) JSON→UI 渲染            │   │
│  │  - PreprocessJson(rawJson)    预处理                  │   │
│  │  - LoadConfigAsync(path)      配置加载（含预处理）     │   │
│  │  - SaveConfigAsync(path, cfg) 配置保存                │   │
│  │  - ValidateConfigContent(json) 配置验证               │   │
│  │  - ExtractJsonErrorContext     错误诊断               │   │
│  │  - ToDockStyle / ToAnchorStyles / ... 枚举转换         │   │
│  │  - ConvertBottomRightToNegative / ... 布局转换         │   │
│  │                                                       │   │
│  └──────────────────────────────────────────────────────┘   │
│                              │ 委托                          │
│                              ▼                               │
│  ┌──────────────────────────────────────────────────────┐   │
│  │         UnifiedJsonSerializer (Share 层)              │   │
│  │                                                       │   │
│  │  Deserialize<T>(json)                                 │   │
│  │      │                                                │   │
│  │      ├─ 1. Preprocessor.Preprocess(json)              │   │
│  │      │      ├─ Step1: 模板展开 (ReferenceExpansion)   │   │
│  │      │      ├─ Step2: 样式解析 (StyleResolution)      │   │
│  │      │      └─ Step3: 变量替换 (VariableResolve)      │   │
│  │      │                                                │   │
│  │      └─ 2. JsonSerializer.Deserialize<T>(json)        │   │
│  │             ├─ ConvertBareNamesToPrefixed             │   │
│  │             ├─ StyleReferenceConverter                │   │
│  │             └─ DynamicObjectConverter                 │   │
│  │                                                       │   │
│  └──────────────────────────────────────────────────────┘   │
│                                                             │
│  ┌──────────────────────────────────────────────────────┐   │
│  │  ConfigContainer (WinForms 实现层)                    │   │
│  │                                                       │   │
│  │  - LoadConfigAsync(path) → WdUIJson.FromJson          │   │
│  │  - LoadConfigFromJsonAsync(json) → WdUIJson.FromJson  │   │
│  │  - RenderConfigAsync(config)  // WinForms 具体实现     │   │
│  │  - 子容器管理 / 控件查找                              │   │
│  │                                                       │   │
│  └──────────────────────────────────────────────────────┘   │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

### 2.2 组件职责（实际架构）

| 层 | 组件 | 职责 |
|----|------|------|
| **Share** | `UnifiedJsonSerializer` | JSON 序列化/反序列化核心，内置预处理管线 |
| **Share** | `JsonPreprocessor` | 预处理步骤注册和执行 |
| **Share** | `JsonConverterRegistrar` | 所有 JsonConverter 集中注册 |
| **WdUI** | `WdUIJson` | **JSON 处理统一入口**（静态类），集中管理序列化/反序列化/预处理/验证/错误诊断/枚举转换/布局转换 |
| **WdUI** | `ConfigContainer` | WinForms 具体实现，委托到 `WdUIJson` 进行 JSON 处理 |

---

## 三、重构步骤（已完成）

> **实际完成情况：** 采用 `WdUIJson` 静态类作为统一入口的方案，而非原先计划的 `ConfigContainerBase` 基类方案。

### 第一步：WdUIJson 统一入口（已完成 ✅）

#### 3.1.1 新增 `WdUIJson` 静态类

**文件路径：** `Operation/WdUI/Serialization/WdUIJson.cs`

**实际实现：**
- 采用静态类而非基类，避免引入继承耦合
- 集中管理 7 大功能模块：序列化/反序列化、预处理、配置加载、验证、错误诊断、WinForms 枚举转换、布局位置转换
- 内部委托到 `UnifiedJsonSerializer`（Share 层）

**关键设计：**
```csharp
public static class WdUIJson
{
    private static readonly UnifiedJsonSerializer _serializer = new();

    // 序列化/反序列化
    public static string ToJson<T>(T obj, bool writeIndented = true) { ... }
    public static T? FromJson<T>(string json) { ... }

    // 预处理
    public static string PreprocessJson(string rawJson) { ... }

    // 配置加载（含预处理 + 反序列化）
    public static async Task<UIConfiguration> LoadConfigAsync(string configPath) { ... }

    // 验证
    public static JsonValidationResult? ValidateConfigContent(string jsonContent) { ... }

    // 错误诊断
    public static string? ExtractJsonErrorContext(string configPath, JsonException ex) { ... }

    // WinForms 枚举转换
    public static DockStyle ToDockStyle(string? dock) { ... }

    // 布局位置转换
    public static (double Left, double Top) ConvertBottomRightToNegative(...) { ... }
}
```

#### 3.1.2 迁移 `ConvertBareNamesToPrefixed` 到 UniversalMiddleware

**文件路径：** `Share/UniversalMiddleware/Serialization/Converters/ConvertBareNamesToPrefixed.cs`

**说明：** 从 WdUI 的 `ControlFactoryFramework.cs` 中提取 `ConvertBareNamesToPrefixed` 逻辑，作为 `JsonConverter` 注册到 `JsonConverterRegistrar`。

#### 3.1.3 增强 `UnifiedJsonSerializer.Deserialize` 的错误诊断

在 `Deserialize` 方法中，当反序列化失败时，提供更详细的错误信息：
- 包含 JSON 路径
- 包含目标类型的属性定义
- 包含实际 JSON 值

### 第二步：WdUI 引用通用版本（已完成 ✅）

#### 3.2.1 `ConfigContainer` 委托到 `WdUIJson`

```csharp
// ConfigContainer.LoadConfigAsync 实际实现：
public async Task LoadConfigAsync(string configPath)
{
    // ★ 统一：委托到 WdUIJson.FromJson（内部使用 UnifiedJsonSerializer）
    var config = WdUI.Serialization.WdUIJson.FromJson<CfgModels.UIConfiguration>(jsonToDeserialize)
        ?? throw new InvalidOperationException("Failed to deserialize configuration");
    await RenderConfigAsync(config);
}
```

#### 3.2.2 `ConfigurationLoader` 委托到 `WdUIJson`

```csharp
// ConfigurationLoader.LoadConfigAsync 实际实现：
public async Task<UIConfiguration> LoadConfigAsync(string configPath)
{
    // ★ 集中：委托到 WdUIJson.LoadConfigAsync（内部含预处理 + 反序列化）
    var config = await WdUI.Serialization.WdUIJson.LoadConfigAsync(configPath);
    _configCache.GetOrAdd(configPath, config);
    return config;
}
```

#### 3.2.3 `WdUIComposer` JSON 方法委托到 `WdUIJson`

```csharp
// WdUIComposer 中的 JSON 方法全部委托到 WdUIJson：
public static string ToJson<T>(T obj, bool writeIndented = true)
    => WdUI.Serialization.WdUIJson.ToJson(obj, writeIndented);
public static T? FromJson<T>(string json)
    => WdUI.Serialization.WdUIJson.FromJson<T>(json);
public static void RenderFromJson(string json, Control target)
    => WdUI.Serialization.WdUIJson.RenderFromJson(json, target);
```

#### 3.2.4 标记/移除废弃代码

- `TemplatePreprocessSkeleton.cs` → 标记为 `[Obsolete]`
- `WdUIComposer.LoadConfigAsync` → 保持 `[Obsolete]` 标记

### 第三步：统一错误处理（已完成 ✅）

#### 3.3.1 在 `WdUIJson.ExtractJsonErrorContext` 中增强错误诊断

当 `type` 字段为空时，在异常信息中包含：
- 控件名称
- 父控件名称
- JSON 文件路径
- JSON 中该控件附近的上下文内容

#### 3.3.2 在 `CreateLayoutPanelSkeleton` 中保持防御性校验

保留现有的 `string.IsNullOrWhiteSpace(config.Type)` 检查，但将错误信息统一格式。

---

## 四、文件变更清单（实际变更）

### 新增文件

| 文件路径 | 说明 | 状态 |
|---------|------|:----:|
| `Operation/WdUI/Serialization/WdUIJson.cs` | **WdUI JSON 处理统一入口**（静态类），集中管理序列化/反序列化/预处理/验证/错误诊断/枚举转换/布局转换 | ✅ 已创建 |
| `Share/UniversalMiddleware/Serialization/Converters/ConvertBareNamesToPrefixed.cs` | 裸名称转完整类型名转换器 | ✅ 已创建 |

### 修改文件

| 文件路径 | 修改内容 | 状态 |
|---------|---------|:----:|
| `Share/UniversalMiddleware/Serialization.cs` | 增强 `UnifiedJsonSerializer.Deserialize` 的错误诊断（`JsonErrorEnricher`） | ✅ 已完成 |
| `Share/UniversalMiddleware/Serialization/JsonConverterRegistrar.cs` | 注册 `ConvertBareNamesToPrefixed`，增强 `Validate()` 校验 | ✅ 已完成 |
| `Share/UniversalMiddleware/Serialization/JsonPreprocessor.cs` | 防双重预处理 — 添加 `PreprocessedMarker` 标记机制 | ✅ 已完成 |
| `Operation/WdUI/UI/WdUI.Container.cs` | `ConfigContainer` 委托到 `WdUIJson.FromJson`，消除手动预处理 | ✅ 已完成 |
| `Operation/WdUI/UI/WdUI.Configuration.cs` | `ConfigurationLoader.LoadConfigAsync` 委托到 `WdUIJson.LoadConfigAsync` | ✅ 已完成 |
| `Operation/WdUI/UI/WdUIComposer.cs` | JSON 方法全部委托到 `WdUIJson` | ✅ 已完成 |
| `Operation/WdUI/Layout/Skeletons/TemplatePreprocessSkeleton.cs` | 标记 `[Obsolete]` | ⬜ 待确认 |

### 无需修改的文件

| 文件路径 | 原因 |
|---------|------|
| `Operation/WdUI/Framework/ControlFactoryFramework.cs` | `DefaultControlCreator.CreateControl` 的校验逻辑保留 |
| `Operation/WdUI/Layout/Skeletons/CreateLayoutPanelSkeleton.cs` | 防御性校验保留 |

---

## 五、风险与注意事项

### 5.1 向后兼容性

- `ConfigContainer` 的公共 API 签名不变（`LoadConfigAsync(string)`、`LoadConfigFromJsonAsync(string)`）
- 内部实现改为委托到基类，行为一致
- `ConfigurationLoader` 的公共 API 签名不变

### 5.2 双重预处理风险

当前 `ConfigContainer.LoadConfigAsync` 手动调用 `Preprocessor.Preprocess` 后，再使用 `JsonSerializer.Deserialize`（不带 `ConvertBareNamesToPrefixed`）。改为 `UnifiedJsonSerializer.Deserialize` 后，内部会再次调用 `Preprocessor.Preprocess`。

**解决方案：** `UnifiedJsonSerializer.Deserialize` 内部调用 `Preprocessor.Preprocess` 时，应检查 JSON 是否已被预处理过（可通过添加标记或检查 JSON 内容特征）。

### 5.3 测试策略

1. **单元测试（UniversalMiddleware）：**
   - `UnifiedJsonSerializer.Deserialize` 正确处理裸名称
   - `UnifiedJsonSerializer.Deserialize` 正确处理已预处理的 JSON（不双重处理）
   - `ConfigContainerBase.LoadConfigAsync` 正确反序列化

2. **集成测试（WdUI）：**
   - `ConfigContainer.LoadConfigAsync` 加载 `MathFunctions.json` 成功
   - 嵌套容器正确创建
   - 事件绑定正确执行

---

## 六、执行计划（实际进度）

| 阶段 | 步骤 | 预计工作量 | 状态 | 说明 |
|------|------|-----------|:----:|------|
| **Phase 0** | 增强 `UniversalReference.ResolverRegistry` 通用性以支持 JsonLogic 场景 | 0.5 天 | ✅ 已完成 | |
| **Phase 1** | 新增 `ConvertBareNamesToPrefixed` 到 UniversalMiddleware | 0.5 天 | ✅ 已完成 | 已创建 `ConvertBareNamesToPrefixed.cs` 并注册到 `JsonConverterRegistrar` |
| **Phase 2** | 新增 `WdUIJson` 统一入口（替代原计划 `ConfigContainerBase`） | 0.5 天 | ✅ 已完成 | 实际创建 `WdUIJson.cs` |
| **Phase 3** | 增强 `UnifiedJsonSerializer.Deserialize` 错误诊断 | 0.5 天 | ✅ 已完成 | `JsonErrorEnricher` 已实现，提供路径/行号/实际值/期望类型诊断 |
| **Phase 3.5** | 防双重预处理机制 | 0.3 天 | ✅ 已完成 | `JsonPreprocessor` 添加 `PreprocessedMarker` 标记机制 |
| **Phase 4** | WdUI 引用通用版本，消除散落入口 | 1 天 | ✅ 已完成 | `ConfigContainer`/`ConfigurationLoader`/`WdUIComposer` 已委托到 `WdUIJson` |
| **Phase 5** | 标记废弃代码，清理冗余逻辑 | 0.5 天 | ⬜ 待确认 | 需确认 `TemplatePreprocessSkeleton` 是否已标记 |
| **Phase 6** | 测试验证 | 1 天 | ⬜ | |
| **合计** | | **5.3 天** | **~75%** | |

### Phase 0 完成内容

| # | 操作 | 文件 | 说明 |
|---|------|------|------|
| 1 | 合并重复注册表 | `Core/ResolverRegistry.cs` | 合并 `ReferenceStore<TDefinition>` 功能（Find、GetKeys、GetValues、TryGetValue） |
| 2 | 废弃 ReferenceStore | `Core/Resolution/ReferenceStore.cs` | 添加 `[Obsolete]`，委托到 `ResolverRegistry` |
| 3 | 合并 ResolutionContext | `Core/Resolution/ResolutionContext.cs` | 吸收 `ReferenceContext` 的属性（CurrentObject、TargetType、Path） |
| 4 | 废弃 ReferenceContext | `Frame/ReferenceResolution.cs` | 添加 `[Obsolete]`，委托到 `ResolutionContext` |
| 5 | 删除空壳 | `Core/UnifiedReferenceServices.cs` | 删除 329 行空壳实现 |
| 6 | 清理废弃枚举 | `EventDriven/Events.cs` | `ReferenceChangeType` 改用统一枚举 |
| 7 | 清理废弃类 | `Core/CircularDependencyDetector.cs` | 删除废弃的 `CircularDependencyResult` |
| 8 | 统一接口 | `Reference/Resolvers/` | 废弃旧接口，指向 `Core/Resolution` 泛型接口 |
| 9 | 增强通用性 | `Core/ResolverRegistry.cs` | 新增 MatchByPrefix/Suffix/Contains、GetOrAdd、TryUpdate、ToFrozen、AsServiceProvider |

#### ResolverRegistry 新增 API 说明

```csharp
// 按前缀匹配（适用于 JsonLogic 中按引用名称前缀查找解析器）
registry.MatchByPrefix("@template.", StringComparison.OrdinalIgnoreCase);

// 按后缀匹配
registry.MatchBySuffix(".config", StringComparison.OrdinalIgnoreCase);

// 按包含匹配
registry.MatchByContains("style", StringComparison.OrdinalIgnoreCase);

// 延迟初始化（适用于按需创建解析器）
registry.GetOrAdd("myKey", key => new MyResolver());

// 条件更新（仅当现有值匹配时才更新）
registry.TryUpdate("myKey", newValue, comparisonValue);

// 创建不可变快照（启动期注册完成后冻结）
var frozen = registry.ToFrozen();

// IServiceProvider 适配（支持依赖注入集成）
var sp = registry.AsServiceProvider();
```

---

## 七、附录：当前 JSON 处理流程图

```
┌─────────────────────────────────────────────────────────────────────┐
│                        JSON 文件加载流程                              │
│                                                                     │
│  MainForm.LoadSideContainerAsync                                    │
│      │                                                              │
│      ▼                                                              │
│  ConfigContainer.LoadConfigAsync(path)                              │
│      │                                                              │
│      ├─ 1. File.ReadAllTextAsync(path)                              │
│      │                                                              │
│      ├─ 2. Preprocessor.Preprocess(rawJson)     ← 手动预处理        │
│      │      │                                                       │
│      │      └─ ReferenceExpansion 步骤                              │
│      │           ├─ ConvertBareNamesToPrefixed                      │
│      │           └─ JsonPipelineRegistry.Process                    │
│      │                                                              │
│      ├─ 3. JsonSerializer.Deserialize(DefaultOptions)  ← 直接反序列化│
│      │      │                                                       │
│      │      └─ DefaultOptions.Converters:                           │
│      │           ├─ JsonStringEnumConverter                         │
│      │           ├─ DynamicObjectConverter                          │
│      │           └─ StyleReferenceConverter                         │
│      │                                                              │
│      └─ 4. RenderConfigAsync(config)                                │
│             │                                                       │
│             ├─ RenderPipeline.Render(this, config)                  │
│             │    ├─ TrackResizeSkeleton                             │
│             │    └─ CreateLayoutPanelSkeleton                       │
│             │         └─ CreateControlWithChildren                  │
│             │              ├─ config.Type == null → 抛出异常 ★      │
│             │              └─ WdUIComposer.CreateControl(config)    │
│             │                   └─ ControlFactory.CreateControl      │
│             │                        └─ DefaultControlCreator        │
│             │                             └─ CreateControl(typeName)│
│             │                                  └─ typeName为空→异常★│
│             │                                                       │
│             ├─ RenderPipeline.ApplyLayout(this)                     │
│             └─ RenderPipeline.BindEvents(this)                      │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

---

## 八、附录：重构后 JSON 处理流程图（实际架构）

```
┌─────────────────────────────────────────────────────────────────────┐
│                        JSON 文件加载流程（重构后）                     │
│                                                                     │
│  MainForm.LoadSideContainerAsync                                    │
│      │                                                              │
│      ▼                                                              │
│  ConfigContainer.LoadConfigAsync(path)                              │
│      │                                                              │
│      ├─ 1. File.ReadAllTextAsync(path)                              │
│      │                                                              │
│      └─ 2. WdUIJson.FromJson<UIConfiguration>(json)                 │
│             │                                                       │
│             ├─ 2a. WdUIJson.PreprocessJson(rawJson)                 │
│             │      │                                                │
│             │      └─ UnifiedJsonSerializer.Preprocessor.Preprocess │
│             │             │                                         │
│             │             └─ ReferenceExpansion 步骤                │
│             │                  ├─ ConvertBareNamesToPrefixed        │
│             │                  └─ JsonPipelineRegistry.Process      │
│             │                                                       │
│             └─ 2b. JsonSerializer.Deserialize<T>(json)              │
│                    │                                                │
│                    └─ UnifiedJsonSerializer.DefaultOptions:          │
│                         ├─ JsonStringEnumConverter                  │
│                         ├─ DynamicObjectConverter                   │
│                         ├─ StyleReferenceConverter                  │
│                         └─ ConvertBareNamesToPrefixed               │
│                                                                     │
│  ★ 关键变化：所有 JSON 反序列化统一经过 WdUIJson 静态类             │
│  ★ 消除手动预处理 + 直接反序列化的散落模式                          │
│  ★ ConvertBareNamesToPrefixed 作为 JsonConverter 注册，而非预处理步骤│
│  ★ WdUIJson 同时集中管理验证/错误诊断/枚举转换/布局转换等辅助功能   │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```
