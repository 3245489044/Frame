# TemplateExpansion 重构方案

## 一、现状分析

### 当前架构（两套体系并行）

```
体系 A：IJsonConverter 管线（新设计的底层转换器）
├── Abstractions/     — IJsonConverter, IJsonPipeline, ITemplateStorage
├── Converters/       — 6 个转换器（StyleApply, TemplateExpand, DefaultsApply, ParameterResolve, ConfigMerge, ConfigValidate）
├── Core/             — JsonPipeline, JsonPipelineBuilder, JsonPipelineRegistry
└── Storage/          — JsonFileTemplateStorage

体系 B：IJsonProcessingMiddleware 管线（旧的上层业务管线）
├── Abstractions/     — IJsonProcessingMiddleware, ProcessingContext
├── Core/             — JsonProcessingPipeline, TemplateExpansionMiddleware, ConfigMergeMiddleware, ConfigValidationMiddleware
└── Serialization/    — StyleConfigJsonConverter
```

### 核心问题

1. **两套体系功能重叠**：体系 A 和体系 B 都在做同样的事——样式展开、模板展开、配置合并、验证
2. **体系 B 有 Bug**：`ProcessControls` 和 `ProcessItems` 中 template 展开后缺少 `i--`，导致展开结果中的后续元素被跳过
3. **体系 B 有重复逻辑**：`ProcessControls` 与 `ProcessItems` 高度重复，`ApplyParameters` 与 `ApplyParametersWithPlaceholders` 高度相似
4. **体系 B 过度包装**：`TemplateEngineManager` → `SimpleTemplateEngineWrapper` → `TemplatePipelineProcessor` → `JsonProcessingPipeline` → `TemplateExpansionMiddleware`，5 层包装
5. **体系 A 的 `ITemplateStorage` 每次查询都检查文件变更**，性能较差

## 二、重构目标

1. **消除体系 B**：移除所有旧中间件代码，只保留体系 A
2. **基于 GraphKey 重写体系 A 的转换器**：复用 `PlaceholderParser` 和 `ChainTemplate`
3. **统一占位符语法**：JSON 配置中的 `${paramName}` 在内部统一转为 `@input.paramName`
4. **保持 API 兼容**：`JsonPipelineRegistry.Configure()` 和 `JsonPipelineRegistry.Process()` 的对外接口不变

## 三、重构后的架构

```
┌─────────────────────────────────────────────────────────────────┐
│                    JSON 转换管线（重构后）                        │
│  Share/PlaceholderFramework/TemplateExpansion/                   │
│                                                                  │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │  抽象层 (Abstractions/)                                    │  │
│  │  ┌──────────────┐  ┌──────────────┐  ┌────────────────┐  │  │
│  │  │ IJsonConverter│  │ IJsonPipeline │  │ITemplateStorage│  │  │
│  │  └──────┬───────┘  └──────┬───────┘  └───────┬────────┘  │  │
│  └─────────┼─────────────────┼───────────────────┼──────────┘  │
│            │                 │                   │             │
│  ┌─────────┼─────────────────┼───────────────────┼──────────┐  │
│  │  核心层 (Core/)           │                   │          │  │
│  │  ┌──────────┐  ┌──────────────┐  ┌────────────────────┐ │  │
│  │  │JsonPipe- │  │JsonPipe-     │  │JsonPipelineRegistry│ │  │
│  │  │line      │  │lineBuilder   │  │(全局注册中心)       │ │  │
│  │  └──────────┘  └──────────────┘  └────────────────────┘ │  │
│  └──────────────────────────────────────────────────────────┘  │
│            │                                                    │
│  ┌─────────┼───────────────────────────────────────────────┐   │
│  │  转换器层 (Converters/) — 基于 GraphKey 重写              │   │
│  │  ┌────────────────────────────────────────────────────┐ │   │
│  │  │ JsonConverterBase (基类)                            │ │   │
│  │  ├────────────────────────────────────────────────────┤ │   │
│  │  │ StyleApplyConverter      (Order=100) ← 复用 PlaceholderParser │   │
│  │  │ TemplateExpandConverter  (Order=200) ← 复用 ChainTemplate    │   │
│  │  │ DefaultsApplyConverter   (Order=300) ← 复用 ParameterDefinition │   │
│  │  │ ParameterResolveConverter(Order=400) ← 复用 PlaceholderParser │   │
│  │  │ ConfigMergeConverter     (Order=500) ← 复用 DataFlowGraph.Merge │   │
│  │  │ ConfigValidateConverter  (Order=900) ← 独立验证逻辑   │   │
│  │  └────────────────────────────────────────────────────┘ │   │
│  └──────────────────────────────────────────────────────────┘   │
│            │                                                    │
│  ┌─────────┼───────────────────────────────────────────────┐   │
│  │  存储层 (Storage/) — 基于 GraphKey 重写                  │   │
│  │  ┌────────────────────────────────────────────────────┐ │   │
│  │  │ JsonFileTemplateStorage (文件模板存储实现)          │ │   │
│  │  │ • styles.json → 保持 Dictionary<string, JsonObject> │   │
│  │  │ • composites.json → 转为 ChainTemplate 注册         │   │
│  │  │ • 使用 FileSystemWatcher 替代轮询                   │ │   │
│  │  └────────────────────────────────────────────────────┘ │   │
│  └──────────────────────────────────────────────────────────┘   │
└──────────────────────────────────────────────────────────────────┘
```

## 四、GraphKey 复用对照表

| JSON 模板框架的需求 | GraphKey 中已有的能力 | 复用方式 |
|-------------------|---------------------|---------|
| 解析 `${paramName}` | `PlaceholderParser.ParseToParam()` 解析 `@input.paramName` | 先转为 `@input.xxx` 再解析 |
| 判断是否为占位符 | `PlaceholderParser.IsInputReference()` | 直接调用 |
| 模板定义（composites.json） | `ChainTemplate<TNode>` | 将 JSON 模板定义转为 `ChainTemplate<JsonNode>` |
| 模板注册 | `ChainTemplateRegistry<TNode>` | 使用 `ChainTemplateRegistry<JsonNode>` |
| 模板实例化（参数绑定） | `TemplateInstantiationContext.ResolveParameter()` | 直接使用 |
| 命名空间隔离 | `TemplateInstantiationContext.GetNamespacePrefix()` | 直接使用 |
| 样式定义（styles.json） | 无对应能力 | 保持简单的 `Dictionary<string, JsonObject>` 查找 |
| 配置合并（extends） | `DataFlowGraphExtensions.Merge()` | 直接使用 |
| 默认值 | `ParameterDefinition.DefaultValue` | 直接使用 |

## 五、具体重构步骤

### 步骤 1：重写 `JsonFileTemplateStorage`

**变化**：
- `composites.json` 加载后转为 `ChainTemplate<JsonNode>` 注册到 `ChainTemplateRegistry`
- `styles.json` 保持 `Dictionary<string, JsonObject>` 查找
- 使用 `FileSystemWatcher` 替代每次查询都检查文件变更
- 新增 `GetTemplateRegistry()` 方法返回 `ChainTemplateRegistry<JsonNode>`

### 步骤 2：重写 `StyleApplyConverter`

**变化**：
- 内部使用 `PlaceholderParser.IsInputReference()` 判断 `@style.xxx` 引用
- 使用 `PlaceholderParser.ExtractInputKey()` 提取样式名称
- 从 `ITemplateStorage.GetStyle()` 获取样式定义
- 保持 `applyTo` 检查逻辑不变

### 步骤 3：重写 `TemplateExpandConverter`

**变化**：
- 内部使用 `ChainTemplateRegistry<JsonNode>` 获取模板
- 使用 `TemplateInstantiationContext` 实例化模板
- 使用 `PlaceholderParser` 解析参数引用
- 支持 `@template.xxx` 语法

### 步骤 4：重写 `DefaultsApplyConverter`

**变化**：
- 使用 `ParameterDefinition.DefaultValue` 获取默认值
- 从 `ITemplateStorage` 获取类型默认值

### 步骤 5：重写 `ParameterResolveConverter`

**变化**：
- 使用 `PlaceholderParser.ParseToParam()` 解析 `${paramName}`（先转为 `@input.paramName`）
- 使用 `PlaceholderResolver.Resolve()` 解析占位符

### 步骤 6：重写 `ConfigMergeConverter`

**变化**：
- 使用 `DataFlowGraphExtensions.Merge()` 合并配置
- 保持递归合并逻辑不变

### 步骤 7：标记并移除旧中间件

- 将体系 B 的所有文件移到 `_deprecated` 目录
- 更新 `.csproj` 排除这些文件

## 六、占位符语法统一

### 当前状态（两套语法混用）

```
JSON 模板框架：${paramName}  — 参数引用
GraphKey：      @input.xxx   — 输入引用
                @prev        — 前一个节点引用
                @node.xxx    — 节点引用
```

### 重构后（统一为 GraphKey 语法）

```
JSON 配置内部使用：@input.paramName  — 参数引用
                  @style.xxx        — 样式引用
                  @template.xxx     — 复合组件引用
                  @default.xxx      — 默认值引用

JSON 配置文件（外部）仍可使用：${paramName} — 在加载时自动转为 @input.paramName
```

## 七、文件变更清单

### 新增文件
无（不创建中间层）

### 修改文件
| 文件 | 修改内容 |
|------|---------|
| `Storage/JsonFileTemplateStorage.cs` | 重写：支持 `ChainTemplateRegistry`，使用 `FileSystemWatcher` |
| `Converters/StyleApplyConverter.cs` | 重写：基于 `PlaceholderParser` |
| `Converters/TemplateExpandConverter.cs` | 重写：基于 `ChainTemplate` |
| `Converters/DefaultsApplyConverter.cs` | 重写：基于 `ParameterDefinition` |
| `Converters/ParameterResolveConverter.cs` | 重写：基于 `PlaceholderParser` |
| `Converters/ConfigMergeConverter.cs` | 重写：基于 `DataFlowGraph.Merge` |
| `Abstractions/ITemplateStorage.cs` | 扩展：新增 `GetTemplateRegistry()` 方法 |

### 废弃文件（移到 `_deprecated`）
| 文件 | 替代 |
|------|------|
| `Abstractions/IJsonProcessingMiddleware.cs` | 无，不再需要 |
| `Abstractions/ProcessingContext.cs` | 无，不再需要 |
| `Abstractions/ITemplateEngine.cs` | 无，不再需要 |
| `Core/JsonProcessingPipeline.cs` | `JsonPipeline` |
| `Core/JsonProcessingPipelineBuilder.cs` | `JsonPipelineBuilder` |
| `Core/TemplateEngineManager.cs` | 无，不再需要 |
| `Core/TemplatePipelineProcessor.cs` | 无，不再需要 |
| `Core/TemplateExpansionMiddleware.cs` | 6 个独立转换器 |
| `Core/ConfigMergeMiddleware.cs` | `ConfigMergeConverter` |
| `Core/ConfigValidationMiddleware.cs` | `ConfigValidateConverter` |
| `Core/TemplateSchema.cs` | 无，不再需要 |
| `Serialization/StyleConfigJsonConverter.cs` | `StyleApplyConverter` |

## 八、API 兼容性

### 保持不变的 API

```csharp
// 启动时配置（不变）
JsonPipelineRegistry.Configure(
    stylesPath: "DrivenConfig/JsonConfig/styles.json",
    compositesPath: "DrivenConfig/JsonConfig/composites.json"
);

// 处理 JSON（不变）
var result = JsonPipelineRegistry.Process(jsonString);
var result = await JsonPipelineRegistry.ProcessFileAsync(filePath);

// 获取管线（不变）
var pipeline = JsonPipelineRegistry.GetDefaultPipeline();
```

### 新增的 API

```csharp
// 获取 ChainTemplate 注册表（新增）
var registry = JsonPipelineRegistry.GetTemplateRegistry<JsonNode>();
var template = registry.Get("labeled-textbox");
```

### 移除的 API

```csharp
// 以下 API 不再可用
JsonProcessingPipelineBuilder
TemplateEngineManager.Create()
TemplatePipelineProcessor.Process()
```
