# FileDriven 模板展开与 PlaceholderFramework 通用框架集成分析

## 概述

本文档分析 FileDriven 项目中模板展开逻辑如何基于 Share/PlaceholderFramework 中的通用框架（TemplateExpansion）实现。核心思路是将 FileDriven 的静态管线（`ReferencePipeline`）迁移到 PlaceholderFramework 的 `JsonPipelineRegistry`，实现模板处理的统一化、可配置化和可测试化。

---

## 一、现状分析

### 1.1 FileDriven 模板展开架构（旧系统）

FileDriven 的模板展开逻辑位于 `FileDriven.JsonRefer/ReferenceProcessing/` 目录，核心组件：

| 组件 | 文件 | 职责 |
|------|------|------|
| `TemplateResolver` | `Resolvers.cs` | 解析 `@template.xxx` 引用，从静态管线加载模板定义并展开 |
| `StyleResolver` | `Resolvers.cs` | 解析 `@style.xxx` 引用，从静态管线加载样式定义并应用 |
| `ExtendsResolver` | `Resolvers.cs` | 解析 `@extends.xxx` 引用，实现模板继承 |
| `ReferencePipeline` | `Pipeline.cs` | 静态管线，管理模板/样式的注册和查询 |
| `ReferenceProcessingInitializer` | `Infrastructure.cs` | 初始化静态管线，加载 styles.json 和 composites.json |
| `FileDrivenPipelineFactory` | `Pipeline.cs` | 创建处理管线，串联多个 Resolver |

**关键问题：**
- 静态管线（`ReferencePipeline`）是全局单例，难以测试和扩展
- 模板加载逻辑分散在多个 Resolver 中，存在重复代码
- 错误处理不统一，每个 Resolver 各自处理错误
- 不支持管线的动态配置（如添加/移除处理步骤）

### 1.2 PlaceholderFramework 模板展开架构（新系统）

PlaceholderFramework 的模板展开逻辑位于 `Share/PlaceholderFramework/TemplateExpansion/` 目录，核心组件：

| 组件 | 文件 | 职责 |
|------|------|------|
| `IJsonPipeline` | `Core/JsonPipeline.cs` | 管线接口，定义 `Execute()` 方法 |
| `JsonPipeline` | `Core/JsonPipeline.cs` | 管线实现，按顺序执行多个 IJsonConverter |
| `JsonPipelineBuilder` | `Core/JsonPipelineBuilder.cs` | Builder 模式构建管线 |
| `JsonPipelineRegistry` | `Core/JsonPipelineRegistry.cs` | 全局管线注册中心，管理默认和命名管线 |
| `ITemplateStorage` | `Abstractions/ITemplateStorage.cs` | 模板存储接口，统一管理 styles/composites |
| `JsonFileTemplateStorage` | `Storage/JsonFileTemplateStorage.cs` | 基于 JSON 文件的模板存储实现 |
| `ExtendsConverter` | `Converters/ExtendsConverter.cs` | 处理 `@extends.xxx` 引用 |
| `StyleApplyConverter` | `Converters/StyleApplyConverter.cs` | 处理 `@style.xxx` 引用 |
| `TemplateExpandConverter` | `Converters/TemplateExpandConverter.cs` | 处理 `@template.xxx` 引用 |
| `DefaultsApplyConverter` | `Converters/DefaultsApplyConverter.cs` | 应用模板默认值 |
| `ParameterResolveConverter` | `Converters/ParameterResolveConverter.cs` | 解析参数引用 |
| `ConfigMergeConverter` | `Converters/ConfigMergeConverter.cs` | 合并配置 |
| `ConfigValidateConverter` | `Converters/ConfigValidateConverter.cs` | 验证配置 |
| `JsonMerger` | `Converters/JsonMerger.cs` | JSON 合并工具 |
| `JsonPipelineHelper` | `Converters/JsonPipelineHelper.cs` | 管线辅助方法 |
| `TemplateExpansionReferencePrefix` | `Abstractions/TemplateExpansionReferencePrefix.cs` | 引用前缀常量定义 |
| `FileDrivenAdapter` | `Adapters/FileDrivenAdapter.cs` | FileDriven 适配器，桥接新旧系统 |

---

## 二、映射关系：FileDriven 旧系统 → PlaceholderFramework 新系统

### 2.1 核心功能映射

| FileDriven 旧系统 | PlaceholderFramework 新系统 | 说明 |
|---|---|---|
| `TemplateResolver.Resolve()` | `TemplateExpandConverter` | 展开 `@template.xxx` 引用 |
| `StyleResolver.Resolve()` | `StyleApplyConverter` | 应用 `@style.xxx` 引用 |
| `ExtendsResolver.Resolve()` | `ExtendsConverter` | 处理 `@extends.xxx` 继承 |
| `ReferencePipeline` | `ITemplateStorage` + `JsonPipelineRegistry` | 模板存储和管线管理 |
| `ReferenceProcessingInitializer` | `JsonPipelineRegistry.Configure()` | 初始化配置 |
| `FileDrivenPipelineFactory` | `JsonPipelineBuilder` | 构建处理管线 |
| 无（旧系统无此功能） | `DefaultsApplyConverter` | 应用模板默认值 |
| 无（旧系统无此功能） | `ParameterResolveConverter` | 解析参数引用 |
| 无（旧系统无此功能） | `ConfigValidateConverter` | 验证配置 |

### 2.2 引用前缀映射

| 引用类型 | FileDriven 旧系统 | PlaceholderFramework 新系统 |
|---|---|---|
| 模板引用 | `@template.xxx` | `@template.xxx`（兼容） |
| 样式引用 | `@style.xxx` | `@style.xxx`（兼容） |
| 继承引用 | `@extends.xxx` | `@extends.xxx`（兼容） |

### 2.3 管线处理顺序

**FileDriven 旧系统：**
1. `ExtendsResolver` — 处理继承
2. `StyleResolver` — 应用样式
3. `TemplateResolver` — 展开模板

**PlaceholderFramework 新系统（默认配置）：**
1. `ExtendsConverter` — 配置继承
2. `StyleApplyConverter` — 样式应用
3. `TemplateExpandConverter` — 模板展开
4. `DefaultsApplyConverter` — 默认值应用
5. `ParameterResolveConverter` — 参数解析
6. `ConfigValidateConverter` — 验证

**差异说明：** 新系统增加了默认值应用、参数解析和验证步骤，提供了更完整的处理流程。

---

## 三、集成方案

### 3.1 架构设计

```
┌─────────────────────────────────────────────────────────────┐
│                    FileDriven 应用层                          │
│  ┌──────────────────────────────────────────────────────┐   │
│  │  FrameworkInitializer.InitializeTemplateEngine()      │   │
│  │  ┌────────────────────────────────────────────────┐  │   │
│  │  │ 1. JsonPipelineRegistry.Configure()            │  │   │
│  │  │ 2. FileDrivenAdapter.MigrationCallback = ...   │  │   │
│  │  │ 3. FileDrivenAdapter.MigrateFromFileDriven()   │  │   │
│  │  │ 4. TemplateProcessor = path => {               │  │   │
│  │  │      FileDrivenAdapter.ConvertBareNamesToPrefixed │  │   │
│  │  │      JsonPipelineRegistry.Process()            │  │   │
│  │  │    }                                           │  │   │
│  │  └────────────────────────────────────────────────┘  │   │
│  └──────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────┐
│              PlaceholderFramework 通用框架                     │
│  ┌──────────────────────────────────────────────────────┐   │
│  │  JsonPipelineRegistry (全局注册中心)                   │   │
│  │  ┌──────────────┐  ┌─────────────────────────────┐  │   │
│  │  │ ITemplateStorage│  │ IJsonPipeline (管线)       │  │   │
│  │  │ - GetStyle()  │  │ - ExtendsConverter         │  │   │
│  │  │ - GetComposite│  │ - StyleApplyConverter      │  │   │
│  │  │ - RegisterStyle│  │ - TemplateExpandConverter  │  │   │
│  │  │ - RegisterComp.│  │ - DefaultsApplyConverter   │  │   │
│  │  └──────────────┘  │ - ParameterResolveConverter │  │   │
│  │                     │ - ConfigValidateConverter   │  │   │
│  │                     └─────────────────────────────┘  │   │
│  └──────────────────────────────────────────────────────┘   │
│  ┌──────────────────────────────────────────────────────┐   │
│  │  FileDrivenAdapter (桥接层)                           │   │
│  │  - ConvertBareNamesToPrefixed()                     │   │
│  │  - MigrationCallback (委托)                          │   │
│  │  - MigrateFromFileDriven()                          │   │
│  └──────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
```

### 3.2 关键集成点

#### 3.2.1 裸名引用转换

FileDriven 的 JSON 配置中使用裸名引用（如 `"template": "text-input"`），而 PlaceholderFramework 使用 `@` 前缀格式（如 `"template": "@template.text-input"`）。

`FileDrivenAdapter.ConvertBareNamesToPrefixed()` 负责在管线处理前将裸名转换为带前缀的格式：

```csharp
// 转换前
{ "template": "text-input", "style": "primary-button" }

// 转换后
{ "template": "@template.text-input", "style": "@style.primary-button" }
```

#### 3.2.2 数据迁移

FileDriven 的静态管线（`ReferencePipeline`）在初始化时从 styles.json 和 composites.json 加载模板数据。迁移到新系统后，这些数据需要导入到 `ITemplateStorage` 中。

`FileDrivenMigrationHelper.Migrate()` 负责执行迁移：

```csharp
// 将 FileDriven 静态管线中的模板导入 ITemplateStorage
foreach (var kv in TemplateResolver.Pipeline.All)
{
    if (kv.Value.Definition != null)
        storage.RegisterComposite(name, kv.Value.Definition);
}
```

#### 3.2.3 回退机制

为确保平滑迁移，新系统保留了旧系统的回退机制。当新管线处理失败时，自动回退到旧系统：

```csharp
var result = JsonPipelineRegistry.Process(json, path);
if (!result.IsSuccess)
{
    // 回退到旧系统
    return ReferenceProcessingInitializer.PreprocessFile(path);
}
```

### 3.3 文件变更

#### 新增文件

| 文件 | 说明 |
|------|------|
| `FileDriven/FileDriven.JsonRefer/ReferenceProcessing/FileDrivenMigrationHelper.cs` | 迁移辅助类，将 FileDriven 静态管线数据导入 ITemplateStorage |

#### 修改文件

| 文件 | 修改内容 |
|------|----------|
| `Share/PlaceholderFramework/TemplateExpansion/Abstractions/ITemplateStorage.cs` | 添加 `RegisterComposite()`、`RegisterStyle()`、`UnregisterComposite()`、`UnregisterStyle()` 方法 |
| `Share/PlaceholderFramework/TemplateExpansion/Storage/JsonFileTemplateStorage.cs` | 实现新增的注册/注销方法 |
| `Share/PlaceholderFramework/TemplateExpansion/Adapters/FileDrivenAdapter.cs` | 添加 `MigrationCallback` 委托属性，修改 `MigrateFromFileDriven()` 使用委托 |
| `FileDriven/FileDriven.Launcher/FrameworkInitializer.cs` | 修改 `InitializeTemplateEngine()` 使用新系统 |

---

## 四、使用示例

### 4.1 初始化流程

```csharp
// 1. 配置 JsonPipelineRegistry
JsonPipelineRegistry.Configure(
    jsonConfigStyles,
    jsonConfigComposites,
    configurePipeline: builder =>
    {
        builder.UseExtends(JsonPipelineRegistry.Storage)
               .UseStyleApply(JsonPipelineRegistry.Storage)
               .UseTemplateExpand(JsonPipelineRegistry.Storage)
               .UseDefaultsApply(JsonPipelineRegistry.Storage)
               .UseParameterResolve()
               .UseValidation(false);
    });

// 2. 设置迁移回调
FileDrivenAdapter.MigrationCallback = FileDrivenMigrationHelper.Migrate;

// 3. 执行迁移
FileDrivenAdapter.MigrateFromFileDriven();
```

### 4.2 处理 JSON 配置

```csharp
// 1. 读取 JSON 文件
var json = File.ReadAllText(path);
var doc = JsonNode.Parse(json)?.AsObject();

// 2. 裸名 → @前缀 转换
FileDrivenAdapter.ConvertBareNamesToPrefixed(doc);

// 3. 执行管线处理
var result = JsonPipelineRegistry.Process(
    doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
    path);

// 4. 检查结果
if (result.IsSuccess)
{
    var processedJson = (result.Data as JsonNode)?.ToJsonString();
}
```

### 4.3 运行时注册模板

```csharp
// 运行时注册新模板
var storage = JsonPipelineRegistry.Storage;
storage.RegisterComposite("my-template", new JsonObject
{
    ["type"] = "container",
    ["controls"] = new JsonArray { ... }
});

// 运行时注销模板
storage.UnregisterComposite("old-template");
```

---

## 五、优势总结

| 方面 | 旧系统（FileDriven） | 新系统（PlaceholderFramework） |
|------|---------------------|-------------------------------|
| **可配置性** | 固定管线，不可配置 | Builder 模式，可自定义管线步骤 |
| **可测试性** | 静态管线，难以 Mock | 接口注入，易于单元测试 |
| **错误处理** | 分散在各 Resolver | 统一错误收集和报告 |
| **扩展性** | 添加新功能需修改核心代码 | 添加新 Converter 即可扩展 |
| **模板管理** | 仅文件加载 | 文件加载 + 运行时注册/注销 |
| **功能完整性** | 仅继承+样式+模板展开 | 额外支持默认值、参数解析、验证 |
| **代码复用** | FileDriven 独有 | 通用框架，可被其他项目复用 |

---

## 六、注意事项

1. **向后兼容**：旧系统（`ReferenceProcessingInitializer` + `FileDrivenPipelineFactory`）仍保留，作为新系统处理失败时的回退方案
2. **线程安全**：`JsonFileTemplateStorage` 使用 `lock` 保证线程安全，`JsonPipelineRegistry` 也是线程安全的
3. **文件变更监听**：`JsonFileTemplateStorage` 使用 `FileSystemWatcher` 自动检测文件变更，无需手动调用 `RefreshIfNeeded()`
4. **迁移时机**：`FileDrivenAdapter.MigrationCallback` 必须在 `JsonPipelineRegistry.Configure()` 之后设置，因为迁移需要访问已初始化的 `ITemplateStorage`
