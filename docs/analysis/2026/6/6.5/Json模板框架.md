# JSON 模板框架分析与重构方案

## 一、背景：当前 JSON 模板处理的 5 条"岔路"

在重构前，JSON 模板处理逻辑分散在 4 个不同的地方，各有独立的实现、路径体系和错误处理策略：

### 岔路 1：WdUI UI 加载（ConfigContainer）
- **文件**：`Share/WdUI/UI/WdUI.Container.cs`
- **方式**：通过 `TemplatePreprocessor` 委托（`Func<string, string>?`）
- **路径**：委托由 `GuiModeRunner` 注入（`FrameworkInitializer.TemplateProcessor`）
- **问题**：委托签名是 `string → string`，与 `ReferenceProcessingInitializer.PreprocessFile` 的 `string → string?` 不完全匹配

### 岔路 2：WdUI 管线骨架（TemplatePreprocessSkeleton）
- **文件**：`Share/WdUI/Layout/Skeletons/TemplatePreprocessSkeleton.cs`
- **方式**：通过 `SkeletonContext` 获取 `TemplateProcessor` 委托
- **路径**：从 `ConfigPipelineBuilder` 传入
- **问题**：与岔路 1 本质相同，但走的是不同的注入路径

### 岔路 3：ScriptConverter（JSON 脚本 → 命令文件）
- **文件**：`FileDriven/FileDriven.Utilities/CLI/ScriptConverter.cs`
- **方式**：内部使用 `SimpleTemplateEngine` + `ReferenceProcessingInitializer`
- **路径**：使用 `PathConstants.JsonConfigStyles` / `JsonConfigComposites`
- **问题**：路径注册逻辑与 UI 加载路径不同

### 岔路 4：ScriptRunner（脚本执行引擎）
- **文件**：`FileDriven/FileDriven.Utilities/ScriptRunner/Core/ScriptRunner.cs`
- **方式**：内部直接加载 `styles.json` / `composites.json` 并注册到 `StyleResolver` / `TemplateResolver`
- **路径**：使用 `PathConstants.JsonConfigStyles` / `JsonConfigComposites`
- **问题**：与 ScriptConverter 的注册逻辑重复

### 岔路 5：JsonPreprocessor 管线（UnifiedJsonSerializer）
- **文件**：`FileDriven/FileDriven.Launcher/FrameworkInitializer.cs`
- **方式**：注册预处理步骤到 `UnifiedJsonSerializer.Preprocessor`
- **路径**：使用 `FileDrivenPipelineFactory.Process(json)`
- **问题**：第四条路径，与上述三条完全独立

---

## 二、核心问题

1. **路径注册重复**：5 处都独立加载 `styles.json` / `composites.json`，没有统一的注册入口
2. **委托签名不一致**：`Func<string, string>` vs `Func<string, string?>` vs `Func<string, string?>?`
3. **错误处理策略不统一**：一处失败不影响另一处，但用户看到的是"部分生效"的诡异现象
4. **模板文件有两套**：`JsonConfig/styles.json`（UI 用）和 `Scripts/styles.json`（脚本用），但实际代码中两者混用

---

## 三、解决方案：通用 JSON 转换管线框架

### 3.1 架构设计

```
┌──────────────────────────────────────────────────────────────────┐
│                    JSON 转换管线框架                              │
│  Share/PlaceholderFramework/TemplateExpansion/                   │
│                                                                  │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │  抽象层 (Abstractions/)                                   │   │
│  │  ┌──────────────┐  ┌──────────────┐  ┌────────────────┐ │   │
│  │  │ IJsonConverter│  │ IJsonPipeline │  │ITemplateStorage│ │   │
│  │  └──────┬───────┘  └──────┬───────┘  └───────┬────────┘ │   │
│  └─────────┼─────────────────┼───────────────────┼──────────┘   │
│            │                 │                   │              │
│  ┌─────────┼─────────────────┼───────────────────┼──────────┐   │
│  │  核心层 (Core/)           │                   │          │   │
│  │  ┌──────────┐  ┌──────────────┐  ┌────────────────────┐ │   │
│  │  │JsonPipe- │  │JsonPipe-     │  │JsonPipelineRegistry│ │   │
│  │  │line      │  │lineBuilder   │  │(全局注册中心)       │ │   │
│  │  └──────────┘  └──────────────┘  └────────────────────┘ │   │
│  └──────────────────────────────────────────────────────────┘   │
│            │                                                    │
│  ┌─────────┼───────────────────────────────────────────────┐   │
│  │  转换器层 (Converters/)                                  │   │
│  │  ┌────────────────────────────────────────────────────┐ │   │
│  │  │ JsonConverterBase (基类)                            │ │   │
│  │  ├────────────────────────────────────────────────────┤ │   │
│  │  │ StyleApplyConverter      (Order=100) 样式应用      │ │   │
│  │  │ TemplateExpandConverter  (Order=200) 模板展开      │ │   │
│  │  │ DefaultsApplyConverter   (Order=300) 默认值应用    │ │   │
│  │  │ ParameterResolveConverter(Order=400) 参数解析      │ │   │
│  │  │ ConfigMergeConverter     (Order=500) 配置合并      │ │   │
│  │  │ ConfigValidateConverter  (Order=900) 配置验证      │ │   │
│  │  └────────────────────────────────────────────────────┘ │   │
│  └──────────────────────────────────────────────────────────┘   │
│            │                                                    │
│  ┌─────────┼───────────────────────────────────────────────┐   │
│  │  存储层 (Storage/)                                       │   │
│  │  ┌────────────────────────────────────────────────────┐ │   │
│  │  │ JsonFileTemplateStorage (文件模板存储实现)          │ │   │
│  │  │ • 支持 styles.json / composites.json 加载           │ │   │
│  │  │ • 缓存机制 + 变更检测                               │ │   │
│  │  │ • 单例模式，只初始化一次                            │ │   │
│  │  └────────────────────────────────────────────────────┘ │   │
│  └──────────────────────────────────────────────────────────┘   │
└──────────────────────────────────────────────────────────────────┘
```

### 3.2 核心接口

#### IJsonConverter（转换器接口）
```csharp
public interface IJsonConverter : IPipelineStage
{
    string Name { get; }           // 转换器名称
    int Order { get; }             // 执行顺序（越小越先执行）
    bool IsFatal { get; }          // 是否致命（false = 非致命，失败不中断管线）
    JsonObject? Convert(JsonObject json, ErrorCollector errors, PipelineContext context);
}
```

#### IJsonPipeline（管线接口）
```csharp
public interface IJsonPipeline
{
    string Name { get; }
    IReadOnlyList<IJsonConverter> Converters { get; }
    IJsonPipeline AddConverter(IJsonConverter converter);
    IJsonPipeline AddConverters(IEnumerable<IJsonConverter> converters);
    IJsonPipeline RemoveConverter<T>() where T : IJsonConverter;
    void Clear();
    PipelineResult Execute(string json, string? sourcePath = null);
    Task<PipelineResult> ExecuteAsync(string filePath);
}
```

#### ITemplateStorage（模板存储接口）
```csharp
public interface ITemplateStorage
{
    void Initialize(string stylesPath, string compositesPath);
    JsonObject? GetStyle(string styleName);
    JsonObject? GetComposite(string compositeName);
    bool HasStyle(string styleName);
    bool HasComposite(string compositeName);
    IEnumerable<string> GetAllStyleNames();
    IEnumerable<string> GetAllCompositeNames();
    void Reload();
}
```

### 3.3 转换器执行顺序

| Order | 转换器 | 职责 | 是否致命 |
|-------|--------|------|----------|
| 100 | StyleApplyConverter | 展开 `{style: "xxx"}` 引用为完整样式属性 | 否 |
| 200 | TemplateExpandConverter | 展开 `{template: "xxx"}` 引用为完整模板 | 否 |
| 300 | DefaultsApplyConverter | 应用 JSON 中的默认值定义 | 否 |
| 400 | ParameterResolveConverter | 解析 `${paramName}` 参数引用 | 否 |
| 500 | ConfigMergeConverter | 合并 `baseConfig` / `overrideConfig` 覆盖 | 否 |
| 900 | ConfigValidateConverter | 验证配置完整性和正确性 | 否 |

### 3.4 全局注册中心（JsonPipelineRegistry）

```csharp
// 一次性初始化（在应用启动时调用）
JsonPipelineRegistry.Configure(
    stylesPath: "DrivenConfig/JsonConfig/styles.json",
    compositesPath: "DrivenConfig/JsonConfig/composites.json"
);

// 后续所有代码统一通过注册中心处理 JSON
var result = JsonPipelineRegistry.Process(jsonString);
var result = await JsonPipelineRegistry.ProcessFileAsync(filePath);
```

### 3.5 与旧系统的关系

```
旧系统（5 条岔路）                   新系统（统一入口）
─────────────────                   ─────────────────
ConfigContainer.TemplatePreprocessor  → JsonPipelineRegistry.Process()
TemplatePreprocessSkeleton            → JsonPipelineRegistry.Process()
ScriptConverter 内部初始化             → JsonPipelineRegistry.Configure()
ScriptRunner 内部注册                  → JsonPipelineRegistry.Configure()
FrameworkInitializer 预处理步骤        → JsonPipelineRegistry.Configure()
```

---

## 四、与 GraphKey 和 GraphKeyFrame 的关系

### 4.1 三个框架的职责对比

| 维度 | GraphKey | GraphKeyFrame | JSON 转换管线 |
|------|----------|---------------|---------------|
| **位置** | `BasicMethod/FrameMethod/GraphKey.cs` | `FileDriven/FileDriven.JsonRefer/GraphKeyFrame/` | `Share/PlaceholderFramework/TemplateExpansion/` |
| **定位** | 工作流图引擎 | JSON 构建器 | JSON 后处理器 |
| **输入** | 图结构/模板定义 | 代码 API 调用 | JSON 字符串 |
| **输出** | 工作流节点列表 | JSON 字符串 | 转换后的 JSON |
| **处理对象** | 工作流节点间的数据流 | UI 配置/工作流/脚本 | 任意 JSON 配置 |
| **核心能力** | 占位符解析、拓扑排序、模板实例化 | 流畅 API 构建 JSON | 样式/模板展开、参数解析、验证 |

### 4.2 上下游关系

```
GraphKey (工作流图引擎)
    │
    │ 提供 GraphBuildContext、占位符系统
    ▼
GraphKeyFrame (JSON 构建器)
    │
    │ 生成 JSON 字符串
    ▼
JSON 转换管线 (JSON 后处理器)
    │
    │ 展开 style/template 引用、解析参数、验证
    ▼
最终可用的 JSON 配置
```

### 4.3 是否功能重叠？

**没有重叠，三者是互补关系：**

- **GraphKey** 处理的是**工作流节点间的数据流依赖**（谁依赖谁的输出），它不关心 JSON 的样式展开
- **GraphKeyFrame** 是**代码生成 JSON** 的工具，它不负责处理 JSON 中的引用
- **JSON 转换管线** 处理的是**JSON 内容本身的转换**（style/template 引用展开），它不关心工作流逻辑

### 4.4 为什么不直接用 GraphKey 优化 JSON 转换管线？

1. **抽象层级不同**：GraphKey 是工作流图引擎，JSON 转换管线是 JSON 后处理器
2. **依赖关系**：JSON 转换管线是 GraphKeyFrame 的下游，如果 JSON 转换管线再依赖 GraphKey，会形成循环依赖
3. **职责分离**：JSON 转换管线应该保持轻量和通用，只关注 JSON 的转换逻辑

---

## 五、文件清单

### 新建文件

| 文件路径 | 说明 |
|----------|------|
| `Share/PlaceholderFramework/TemplateExpansion/Abstractions/IJsonConverter.cs` | 转换器接口 |
| `Share/PlaceholderFramework/TemplateExpansion/Abstractions/IJsonPipeline.cs` | 管线接口 |
| `Share/PlaceholderFramework/TemplateExpansion/Abstractions/ITemplateStorage.cs` | 模板存储接口 |
| `Share/PlaceholderFramework/TemplateExpansion/Core/JsonPipeline.cs` | 管线实现 |
| `Share/PlaceholderFramework/TemplateExpansion/Core/JsonPipelineBuilder.cs` | 管线构建器 |
| `Share/PlaceholderFramework/TemplateExpansion/Core/JsonPipelineRegistry.cs` | 全局注册中心 |
| `Share/PlaceholderFramework/TemplateExpansion/Converters/JsonConverterBase.cs` | 转换器基类 |
| `Share/PlaceholderFramework/TemplateExpansion/Converters/StyleApplyConverter.cs` | 样式应用转换器 |
| `Share/PlaceholderFramework/TemplateExpansion/Converters/TemplateExpandConverter.cs` | 模板展开转换器 |
| `Share/PlaceholderFramework/TemplateExpansion/Converters/DefaultsApplyConverter.cs` | 默认值应用转换器 |
| `Share/PlaceholderFramework/TemplateExpansion/Converters/ParameterResolveConverter.cs` | 参数解析转换器 |
| `Share/PlaceholderFramework/TemplateExpansion/Converters/ConfigMergeConverter.cs` | 配置合并转换器 |
| `Share/PlaceholderFramework/TemplateExpansion/Converters/ConfigValidateConverter.cs` | 配置验证转换器 |
| `Share/PlaceholderFramework/TemplateExpansion/Storage/JsonFileTemplateStorage.cs` | 文件模板存储实现 |

### 已有文件（未改动）

| 文件路径 | 说明 |
|----------|------|
| `Share/PlaceholderFramework/TemplateExpansion/Abstractions/IJsonProcessingMiddleware.cs` | 旧中间件接口（保留兼容） |
| `Share/PlaceholderFramework/TemplateExpansion/Abstractions/ITemplateEngine.cs` | 旧模板引擎接口（保留兼容） |
| `Share/PlaceholderFramework/TemplateExpansion/Abstractions/ProcessingContext.cs` | 旧处理上下文（保留兼容） |
| `Share/PlaceholderFramework/TemplateExpansion/Core/JsonProcessingPipeline.cs` | 旧管线实现（保留兼容） |
| `Share/PlaceholderFramework/TemplateExpansion/Core/JsonProcessingPipelineBuilder.cs` | 旧管线构建器（保留兼容） |
| `Share/PlaceholderFramework/TemplateExpansion/Core/TemplateEngineManager.cs` | 旧模板引擎管理器（保留兼容） |
| `Share/PlaceholderFramework/TemplateExpansion/Core/TemplatePipelineProcessor.cs` | 旧管线处理器（保留兼容） |
| `Share/PlaceholderFramework/TemplateExpansion/Core/TemplateExpansionMiddleware.cs` | 旧模板展开中间件（保留兼容） |
| `Share/PlaceholderFramework/TemplateExpansion/Core/ConfigMergeMiddleware.cs` | 旧配置合并中间件（保留兼容） |
| `Share/PlaceholderFramework/TemplateExpansion/Core/ConfigValidationMiddleware.cs` | 旧配置验证中间件（保留兼容） |
| `Share/PlaceholderFramework/TemplateExpansion/Serialization/StyleConfigJsonConverter.cs` | 旧序列化转换器（保留兼容） |

---

## 六、后续迁移计划

### 阶段一：框架就绪（已完成）
- [x] 构建抽象层接口
- [x] 构建核心实现层
- [x] 构建转换器层
- [x] 构建存储层
- [x] 编译验证通过

### 阶段二：统一入口（待完成）
- [ ] 将 `JsonPipelineRegistry.Configure()` 集成到应用启动流程
- [ ] 改造 `ConfigContainer.TemplatePreprocessor` 使用 `JsonPipelineRegistry`
- [ ] 改造 `TemplatePreprocessSkeleton` 使用 `JsonPipelineRegistry`
- [ ] 改造 `ScriptConverter` 移除内部初始化逻辑
- [ ] 改造 `ScriptRunner` 移除内部注册逻辑
- [ ] 改造 `FrameworkInitializer` 统一初始化

### 阶段三：清理旧代码（待完成）
- [ ] 确认旧中间件不再被引用后，移除 `IJsonProcessingMiddleware` 等旧接口
- [ ] 移除 `TemplateExpansionMiddleware`、`ConfigMergeMiddleware`、`ConfigValidationMiddleware`
- [ ] 移除 `JsonProcessingPipeline`、`JsonProcessingPipelineBuilder`
- [ ] 移除 `TemplateEngineManager`、`TemplatePipelineProcessor`

## 三个框架的职责对比分析

### 1. 当前 JSON 转换管线（我刚刚构建的）

**位置：** `Share/PlaceholderFramework/TemplateExpansion/`

**职责：** JSON 字符串的**后处理/转换管线**
- 输入：JSON 字符串（如 UI 配置 JSON）
- 处理：样式展开 → 模板展开 → 默认值应用 → 参数解析 → 配置合并 → 验证
- 输出：转换后的 JSON 字符串 + 管线执行结果（含错误收集）
- 核心接口：`IJsonConverter`（转换器）、`IJsonPipeline`（管线编排）、`ITemplateStorage`（模板存储）

**典型场景：** 加载一个 `JsonConfig` 文件后，将其中的 `"style": "primaryButton"` 字符串引用展开为完整的样式属性对象。

---

### 2. GraphKeyFrame（FileDriven/FileDriven.JsonRefer/GraphKeyFrame）

**位置：** `FileDriven/FileDriven.JsonRefer/GraphKeyFrame/`

**职责：** JSON 的**构建器模式（Builder Pattern）**，用于**程序化生成** JSON 配置
- 三大构建器：`JsonConfigBuilder`（UI 配置）、`WorkflowBuilder`（工作流）、`ScriptBuilder`（测试脚本）
- 通过流畅 API 在代码中构建 JSON，而非手写 JSON 文件
- 依赖 `GraphBuildContext` 管理构建上下文和 ID 生成
- 依赖 `ReferenceProcessingInitializer` 进行引用处理（style/template 展开）

**典型场景：** 在 C# 代码中通过 `new JsonConfigBuilder("MyForm").AddView(...).AddControl(...)` 构建 UI 配置 JSON。

**与 JSON 转换管线的关联：**
- GraphKeyFrame 的构建器生成 JSON 字符串后，**需要** JSON 转换管线来处理其中的 style/template 引用
- `GraphKeyFrameUsings.InitializeReferenceSystem()` 初始化引用处理系统，这实际上就是调用转换管线
- 两者是**上下游关系**：构建器生成 JSON → 转换管线处理 JSON

---

### 3. GraphKey（BasicMethod/FrameMethod/GraphKey.cs）

**位置：** `BasicMethod/FrameMethod/GraphKey.cs`（2111 行的大文件）

**职责：** **工作流图构建引擎**，包含三个核心子系统：

#### a) `Graphkey.Core` 命名空间
- **占位符系统**：`Placeholder`（Input/Prev/NodeReference/NodeIndex 四种类型）
- **占位符解析器**：`PlaceholderResolver` — 将 `@input.key`、`@prev`、`@node.id` 等占位符解析为实际键名
- **工作流验证器**：`WorkflowValidator` — 验证参数键和输出键的完整性和唯一性
- **动态工作流构建器**：`DynamicWorkflowBuilder` + `DynamicChainBuilder` — 链式构建工作流节点

#### b) `ChainGraph.Core` 命名空间
- **链模板系统**：`ChainTemplate<TNode>`、`ChainTemplateRegistry<TNode>` — 模板注册和实例化
- **模板实例化**：`TemplateInstantiationContext<TNode>` — 参数绑定、命名空间前缀、引用解析
- **占位符解析器**：`PlaceholderParser` — 解析 `@input.xxx`、`@node.xxx`、`@prev` 语法
- **链展开器**：`ChainTemplateExpander<TNode>` — 将模板实例展开为工作流节点列表

#### c) `GraphBuild.FromGraph` 命名空间
- **图构建**：`DataFlowGraph`、`GraphNode`、`GraphEdge` — 图数据结构
- **拓扑排序**：`GraphWorkflowBuilder.BuildFromGraph()` — 从图生成工作流节点列表

#### d) `GraphConstruction` 命名空间
- **图构建器**：`GraphBuilder` — 添加节点、边、模板实例
- **模板构建器**：`TemplateBuilder` — 构建可复用的链模板

#### e) `GraphBuildingFramework` 命名空间
- **`GraphBuildContext`** — 图构建上下文，封装节点列表、占位符解析、ID 生成
- **`INodeBuilder`** — 节点构建器接口
- **`SingleNodeBuilder`**、`SequentialNodeBuilder` — 内置节点构建器实现

---

## 三个框架的关系总结

```
┌─────────────────────────────────────────────────────────────┐
│                    GraphKey (GraphKey.cs)                    │
│  ┌──────────────────────────────────────────────────────┐   │
│  │  工作流图构建引擎                                      │   │
│  │  • Placeholder 占位符系统 (@input/@prev/@node)        │   │
│  │  • DynamicWorkflowBuilder 链式构建工作流              │   │
│  │  • ChainTemplate 模板系统 + 实例化                    │   │
│  │  • GraphBuilder 图构建 + 拓扑排序                     │   │
│  │  • GraphBuildContext 构建上下文                       │   │
│  └──────────────────────────────────────────────────────┘   │
│                          ↓ 使用                              │
│  ┌──────────────────────────────────────────────────────┐   │
│  │  GraphKeyFrame (构建器层)                             │   │
│  │  • JsonConfigBuilder → 生成 UI 配置 JSON              │   │
│  │  • WorkflowBuilder → 生成工作流定义 JSON              │   │
│  │  • ScriptBuilder → 生成测试脚本                       │   │
│  │  • 依赖 GraphBuildContext 管理上下文                  │   │
│  └──────────────────────────────────────────────────────┘   │
│                          ↓ 输出 JSON 后需要处理              │
│  ┌──────────────────────────────────────────────────────┐   │
│  │  JSON 转换管线 (我刚刚构建的)                         │   │
│  │  • StyleApplyConverter → 展开 style 引用             │   │
│  │  • TemplateExpandConverter → 展开 template 引用      │   │
│  │  • DefaultsApplyConverter → 应用默认值               │   │
│  │  • ParameterResolveConverter → 解析 ${param}         │   │
│  │  • ConfigMergeConverter → 合并配置覆盖               │   │
│  │  • ConfigValidateConverter → 验证配置完整性          │   │
│  └──────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
```

## 是否功能重叠？

**没有重叠，三者是互补关系：**

| 维度 | GraphKey | GraphKeyFrame | JSON 转换管线 |
|------|----------|---------------|---------------|
| **定位** | 工作流图引擎 | JSON 构建器 | JSON 后处理器 |
| **输入** | 图结构/模板定义 | 代码 API 调用 | JSON 字符串 |
| **输出** | 工作流节点列表 | JSON 字符串 | 转换后的 JSON |
| **处理对象** | 工作流节点间的数据流 | UI 配置/工作流/脚本 | 任意 JSON 配置 |
| **核心能力** | 占位符解析、拓扑排序、模板实例化 | 流畅 API 构建 JSON | 样式/模板展开、参数解析、验证 |

**关键区别：**
- **GraphKey** 处理的是**工作流节点间的数据流依赖**（谁依赖谁的输出），它不关心 JSON 的样式展开
- **GraphKeyFrame** 是**代码生成 JSON** 的工具，它不负责处理 JSON 中的引用
- **JSON 转换管线** 处理的是**JSON 内容本身的转换**（style/template 引用展开），它不关心工作流逻辑

**结论：** 构建这个通用 JSON 转换管线框架是**正确且必要的**，它填补了 GraphKey 和 GraphKeyFrame 之间的空白——GraphKeyFrame 生成 JSON 后，需要 JSON 转换管线来处理其中的引用和模板展开，而 GraphKey 本身不提供这个能力。

好问题！让我深入分析一下 GraphKey 中哪些能力可以被 JSON 转换管线复用。

## GraphKey 中与 JSON 转换相关的核心能力

从代码分析，GraphKey 中有几个子系统与 JSON 转换管线**潜在相关**：

### 1. Placeholder 占位符系统（Graphkey.Core）
```csharp
// Placeholder 有四种类型：
public enum PlaceholderType { Input, Prev, NodeReference, NodeIndex }
// 解析 @input.key、@prev、@node.id 等语法
```
- **当前 JSON 转换管线**处理的是 `${paramName}` 和 `{style: "primaryButton"}` 这类引用
- **GraphKey 的 Placeholder** 处理的是 `@input.xxx`、`@prev`、`@node.id` 这类**工作流数据流引用**
- **语法不同、语义不同**：JSON 转换管线处理的是 JSON 配置本身的引用展开，Placeholder 处理的是工作流节点间的数据流依赖

### 2. ChainTemplate 模板系统（ChainGraph.Core）
```csharp
public class ChainTemplate<TNode> {
    public string Name { get; set; }
    public Dictionary<string, ParameterDefinition> InputParameters { get; }
    public List<TNode> Nodes { get; }
    public Dictionary<string, string> Outputs { get; }
}
```
- **当前 JSON 转换管线**的 `TemplateExpandConverter` 处理的是 JSON 中的 `{template: "xxx"}` 引用
- **GraphKey 的 ChainTemplate** 处理的是**工作流节点模板**的实例化（参数绑定、命名空间前缀、节点展开）
- **抽象层级不同**：ChainTemplate 是泛型模板系统（`TNode`），JSON 转换管线是 JSON 专用的模板展开

### 3. GraphBuildContext（GraphBuildingFramework）
```csharp
public class GraphBuildContext {
    public SkeletonContext SkeletonContext { get; }
    public List<object> Nodes { get; }
    public string GenerateNumericId(int length);
    public Placeholder GetPlaceholder(string key);
    public void SetPlaceholder(string key, Placeholder placeholder);
}
```
- 这是**构建上下文**，用于在构建过程中管理节点列表和占位符
- JSON 转换管线使用的是 `PipelineContext`（来自 `BasicMethod.Pipeline`），两者是不同的上下文系统

## 能否用 GraphKey 优化 JSON 转换管线？

### 可以复用的部分

| GraphKey 能力 | 能否用于 JSON 转换管线 | 理由 |
|---|---|---|
| **占位符解析引擎** | ❌ 不能直接复用 | 语法不同（`@input` vs `${}`），语义不同（数据流 vs 配置引用） |
| **ChainTemplate 模板系统** | ⚠️ 部分可借鉴 | 模板注册/实例化的设计模式可以借鉴，但 JSON 模板展开逻辑是 JSON 专用的 |
| **GraphBuildContext** | ❌ 不能直接复用 | 它绑定的是 SkeletonContext 和节点列表，JSON 转换管线需要的是 PipelineContext |
| **ID 生成器** | ✅ 可以复用 | `GenerateNumericId` / `GenerateGuidId` 是通用工具方法 |
| **链式构建器模式** | ✅ 设计模式可借鉴 | `JsonPipelineBuilder` 的流畅 API 设计可以借鉴 `GraphBuilder` 的模式 |

### 更关键的问题：是否应该用 GraphKey 替代 JSON 转换管线？

**不应该。** 原因如下：

1. **抽象层级不同**：GraphKey 是**工作流图引擎**，JSON 转换管线是**JSON 后处理器**。前者处理的是节点间的数据流拓扑，后者处理的是 JSON 字符串内容的转换。

2. **依赖关系**：JSON 转换管线是 GraphKeyFrame 的下游（GraphKeyFrame 生成 JSON → JSON 转换管线处理 JSON），而 GraphKeyFrame 本身依赖 GraphKey 的 `GraphBuildContext`。如果 JSON 转换管线再依赖 GraphKey，会形成**循环依赖**：
   ```
   GraphKey → GraphKeyFrame → JSON 转换管线 → GraphKey (循环!)
   ```

3. **职责分离**：JSON 转换管线应该保持轻量和通用，只关注 JSON 的转换逻辑。引入 GraphKey 的占位符系统和模板系统会使其变得臃肿，且与工作流引擎耦合。

### 正确的优化方向

JSON 转换管线应该**保持独立**，但可以在以下方面借鉴 GraphKey 的设计经验：

1. **模板注册模式**：借鉴 `ChainTemplateRegistry` 的注册/查找模式，优化 `ITemplateStorage` 的实现
2. **错误收集机制**：借鉴 `WorkflowValidator` 的验证模式，增强 `ConfigValidateConverter` 的错误报告
3. **链式 API 设计**：`JsonPipelineBuilder` 的流畅 API 设计已经借鉴了这种模式

## 总结

**JSON 转换管线不应该用 GraphKey 来优化或替代。** 它们是不同抽象层级的组件：

- **GraphKey** = 工作流图引擎（处理节点间的数据流）
- **JSON 转换管线** = JSON 后处理器（处理 JSON 内容的转换）

两者是**上下游协作关系**，而非替代关系。JSON 转换管线保持独立、轻量、通用，才是正确的架构设计。