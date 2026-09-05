# UniversalJsonMiddleware - 通用 JSON 中间件框架

## 概述

**UniversalJsonMiddleware** 是一个全新的通用 JSON 中间件框架，设计目标是将 `Workflow_` 项目中 **6 处分散的 JSON 处理层** 统一为单一、可组合、可测试的管线化框架。基于此框架重构现有项目中的所有 JSON 处理逻辑。

### 设计理念

```
统一抽象 → 管线编排 → 适配器桥接 → 逐步替换
```

### 解决的核心问题

| 问题 | 当前状态 | 解决后 |
|------|---------|--------|
| JSON 处理分散 | 6 处独立实现，各有不同的错误处理和配置 | 统一管线，单一入口 |
| 序列化器不统一 | System.Text.Json + Newtonsoft.Json 混用 | 统一为 System.Text.Json + 兼容层 |
| 配置重复定义 | LayoutConfig、StyleConfig 等重复出现 | 模型转换器统一映射 |
| 错误处理不一致 | try-catch 分散在各处，日志格式各异 | 管线级统一错误处理 |
| 不可测试 | JSON 逻辑与业务逻辑耦合 | 独立管线，可单独测试 |
| 学习成本高 | 开发者需要了解多种模式 | 统一的 Pipeline + Stage + Middleware 概念 |

---

## 框架架构（单文件结构）

整个框架实现在一个文件中，通过 **9 个命名空间** 组织：

```
UniversalJsonMiddleware.cs
│
├── Abstractions      # 核心抽象：接口 + 基础模型
├── Core              # 管线引擎：Pipeline + Builder + Stage
├── Serialization     # 序列化层：序列化器 + 转换器 + 美化工具
├── Validator         # 验证层：语法/结构/模型验证器
├── Transformation    # 转换层：字段映射 + 模型合并 + 节点转换
├── Navigation        # 导航层：JSON Path 引擎
├── Middleware        # 中间件层：命令管线 + 解析器
├── Extensions        # 扩展层：Json扩展方法 + DI注册 + 管线辅助
├── Adapters          # 适配层：桥接现有项目的适配器
└── Diagnostics       # 诊断层：性能分析 + 大小分析
```

---

## 命名空间详解

### 1. `Abstractions` — 核心抽象

这是整个框架的"宪法"，定义了所有核心接口。

#### 管线接口

```csharp
// 管线：串联多个处理阶段
public interface IJsonPipeline<TInput, TOutput>
{
    string Name { get; }
    Task<TOutput> ProcessAsync(TInput input, CancellationToken ct = default);
}

// 阶段：管线中的单一处理步骤
public interface IJsonStage<TInput, TOutput>
{
    string Name { get; }
    Task<TOutput> ProcessAsync(TInput input, JsonPipelineContext context);
}
```

#### 中间件模式

```csharp
// 中间件委托 — 类似 ASP.NET Core 的 next()
public delegate Task<JsonCommandResult> JsonMiddlewareDelegate(JsonCommand command);
public delegate Task<JsonCommandResult> JsonMiddlewareFunc(JsonCommand command, JsonMiddlewareDelegate next);
```

#### 核心模型

```csharp
public class JsonPipelineConfig { ... }   // 管线配置
public class JsonValidationResult { ... } // 验证结果
public class JsonCommand { ... }          // 命令模型（来自 MetaCommand）
public class JsonCommandResult { ... }    // 命令结果
public enum JsonVerb { Do, Get, Look, Wait, Assert }
public enum JsonErrorCode { ... }         // 统一错误码
```

---

### 2. `Core` — 管线引擎

核心编排器，负责 Stage 的串联和执行。

#### 管线上下文 `JsonPipelineContext`

```csharp
public class JsonPipelineContext
{
    public ConcurrentDictionary<string, object?> Items { get; }
    public CancellationToken CancellationToken { get; set; }
    public IServiceProvider? Services { get; set; }
    public JsonPipelineConfig Config { get; set; }

    public T? GetItem<T>(string key) where T : class;
    public void SetItem<T>(string key, T value) where T : class;
}
```

#### Fluent Builder

```csharp
var pipeline = new JsonPipelineBuilder<string, UIConfiguration>("WdUIConfig")
    .AddStage<JsonFileLoadStage>()              // 1. 读取文件
    .AddStage<JsonParseStage>()                  // 2. 解析 JSON
    .AddStage<JsonValidateStage>()               // 3. 验证结构
    .AddStage(new JsonDeserializeStage<UIConfiguration>()) // 4. 反序列化
    .Configure(c => { c.ThrowOnError = true; })
    .Build();

var config = await pipeline.ProcessAsync("config.json");
```

#### 内置 Stage

| Stage | 功能 | 输入 | 输出 |
|-------|------|------|------|
| `JsonFileLoadStage` | 读取文件 | 路径 string | JSON string |
| `JsonParseStage` | 解析 JSON | string | JsonNode? |
| `JsonValidateStage` | 验证非空 | JsonNode? | JsonNode? |
| `JsonDeserializeStage<T>` | 泛型反序列化 | JsonNode? | T? |
| `JsonFileSaveStage` | 写入文件 | (Path, Content) | bool |
| `NullStage<TIn,TOut>` | 占位/调试 | TInput | TOutput |

---

### 3. `Serialization` — 序列化层

整合了项目中所有序列化模式。

#### `UnifiedJsonSerializer`（核心类）

三种预设配置：

```csharp
public class UnifiedJsonSerializer : IJsonSerializer
{
    // 默认（带缩进、camelCase、忽略null）
    public static readonly JsonSerializerOptions DefaultOptions = ...;

    // 紧凑（无缩进，用于存储）
    public static readonly JsonSerializerOptions CompactOptions = ...;

    // 安全（忽略只读属性、过滤不可序列化类型）
    public static readonly JsonSerializerOptions SafeOptions = ...;
}
```

核心方法：

```csharp
// 基础序列化
string Serialize<T>(T obj, JsonSerializerOptions? options = null);
T? Deserialize<T>(string json, JsonSerializerOptions? options = null);

// 安全序列化 — 自动清理委托、Stream 等不可序列化类型
string SafeSerialize<T>(T obj, JsonSerializerOptions? options = null);

// 三步反序列化 — 解析 → 验证 → 反序列化
T? DeserializeWithValidation<T>(string json, Action<JsonValidationResult>? onValidate = null);

// 文件操作
Task SerializeToFileAsync<T>(string path, T obj, ...);
Task<T?> DeserializeFromFileAsync<T>(string path, ...);
```

#### 内置转换器

| 转换器 | 功能 |
|--------|------|
| `DynamicObjectConverter` | 支持 Dictionary、匿名类型、object 的动态序列化 |
| `SafeObjectConverter` | 安全序列化（过滤不可序列化属性） |

#### 辅助工具

```csharp
public static class JsonPrettyPrinter
{
    static string PrettyPrint(string json);  // 格式化 JSON
    static string Minify(string json);       // 压缩 JSON
}

public class StorageSerializer : IStorageSerializer
{
    // 用于持久化存储的轻量序列化器
    string Serialize<T>(T obj);
    T Deserialize<T>(string data);
    object Deserialize(string data, Type type);
}
```

---

### 4. `Validator` — 验证层

三层验证体系：

```csharp
// 1. 语法验证 — 检查 JSON 是否合法
public class JsonSyntaxValidator : IJsonValidator
{
    JsonValidationResult ValidateSyntax(string json);
    JsonValidationResult ValidateStructure<T>(string json);
    JsonValidationResult ValidateModel<T>(T model);
}

// 2. Schema 验证 — 注册并校验 schema
public class JsonSchemaValidator : IJsonValidator
{
    void RegisterSchema<T>(string schemaName);
    JsonValidationResult ValidateAgainstSchema(string json, string schemaName);
}

// 3. 模型验证 — 利用 DataAnnotations 反射验证
public class JsonModelValidator : IJsonValidator
{
    JsonValidationResult ValidateModel<T>(T model);  // 检查 Required/StringLength/Range
}
```

---

### 5. `Transformation` — 转换层

#### 字段映射器

```csharp
var mapper = new FieldMapper<ControlConfig, ElementDefinition>()
    .AddMapping("Name", "ControlName")
    .AddMapping("Width", "SizeConstraints.MinWidth")
    .AddCustomMapping((src, tgt) => {
        tgt.Position = ConvertPosition(src.Layout);
        return tgt;
    });

var element = mapper.Map(controlConfig);
```

#### JSON 合并器（替代 ConfigurationLoader 的 MergeConfigurations）

```csharp
public static class JsonMerger
{
    // JSON Object 层级合并
    static JsonObject Merge(JsonObject baseObj, JsonObject overrideObj, MergeOptions options);

    // 模型级合并（序列化 → 合并 → 反序列化）
    static T MergeModels<T>(T baseModel, T overrideModel) where T : class, new();
}

public enum MergeOptions { ReplaceArray, AppendArray }
```

#### 模型转换 Stage

```csharp
public class ModelTransformStage<TSource, TTarget> : IJsonStage<TSource, TTarget>
{
    // 构造函数注入转换函数
    public ModelTransformStage(Func<TSource, TTarget> transformFunc);
}
```

---

### 6. `Navigation` — JSON Path 引擎

整合并增强 MetaCommand 中的 `JsonPathUtil`：

```csharp
public class JsonPathEngine : IJsonPathEngine
{
    // 基本导航
    JsonNode? Navigate(JsonNode root, string path);             // 如 "views[0].controls[1].name"
    bool TryNavigate(JsonNode root, string path, out JsonNode? result);
    bool TryGetValue<T>(JsonNode root, string path, out T? value);

    // 修改
    JsonNode? SetValue(JsonNode root, string path, JsonNode? value);

    // 高级查询
    IEnumerable<string> GetPaths(JsonNode root, string? filterPattern = null);

    // 异步查询
    Task<JsonNode?> QueryAsync(JsonNode root, string pathExpression);
}
```

路径语法：

```
user.name                  → 对象属性访问
users[0]                   → 数组索引访问
data.items[*].id           → 通配符（所有元素）
store.book[0].title        → 复合路径
```

---

### 7. `Middleware` — 中间件层

源自 MetaCommand 的中间件模式，但升级为完整的中间件管线。

#### MiddlewarePipeline

```csharp
var pipeline = new MiddlewarePipeline();

// 注册中间件（按注册顺序执行）
pipeline.Use(LoggingMiddleware.Create());
pipeline.Use(ValidationMiddleware.Create());
pipeline.Use(RetryMiddleware.Create(maxRetries: 3));

// 设置核心处理器
pipeline.SetCoreHandler(async cmd => {
    // 实际命令执行逻辑
    return JsonCommandResult.Ok($"Executed: {cmd.Verb} {cmd.Target}");
});

// 执行
var result = await pipeline.ExecuteTextAsync("Do create path=/data value=hello");
```

#### 内置中间件

| 中间件 | 功能 |
|--------|------|
| `LoggingMiddleware.Create()` | 命令执行前后日志 |
| `ValidationMiddleware.Create()` | 执行前验证命令格式 |
| `RetryMiddleware.Create(n, ms)` | 失败自动重试 |

#### JsonCommandParser

替代 `JsonCommandFramework.JsonCommand.Parse()`：

```csharp
var cmd = JsonCommandParser.Parse("Do create path=/data value=hello");
// → JsonCommand { Verb = Do, Action = "create", Parameters = { ["path"] = "/data", ["value"] = "hello" } }
```

---

### 8. `Extensions` — 扩展方法

#### 字符串扩展

```csharp
// JSON 字符串的直接反序列化
var config = jsonString.FromJson<UIConfiguration>();

// JSON 格式检查与美化
bool valid = jsonString.IsValidJson();
string pretty = jsonString.PrettyPrintJson();
string minified = jsonString.MinifyJson();
```

#### 对象扩展

```csharp
// 对象序列化
string json = myObj.ToJson(writeIndented: true);
string safeJson = myObj.ToSafeJson();

// 对象 → JsonNode
var node = myObj.ToJsonNode();

// 深度克隆（通过 JSON 序列化/反序列化）
var clone = myObj.DeepCloneViaJson();
```

#### 管线扩展

```csharp
// 快速构建"从 JSON 到模型"的管线
var pipeline = new JsonPipelineBuilder<string, MyModel>()
    .BuildFromJson<MyModel>()  // Parse → Validate → Deserialize
    .Build();

// 从文件直接处理
var result = await pipeline.ProcessJsonFileAsync<MyModel>("data.json");
```

#### DI 注册

```csharp
// 注册全局服务
ServiceCollectionExtensions.AddSingleton<IJsonSerializer>(new UnifiedJsonSerializer());
ServiceCollectionExtensions.AddSingleton<IJsonPathEngine>(new JsonPathEngine());

// 获取服务
var serializer = ServiceCollectionExtensions.GetSerializer();
var pathEngine = ServiceCollectionExtensions.GetPathEngine();
```

---

### 9. `Adapters` — 适配层

这是连接新框架与现有项目的桥梁。

#### ConfigLoaderAdapter — 替代 WdUI.ConfigurationLoader

```csharp
var adapter = new ConfigLoaderAdapter();

// 基础加载
var config = await adapter.LoadConfigAsync<UIConfiguration>("config.json");

// 带继承合并的加载（替代 MergeConfigurations）
var config = await adapter.LoadConfigWithExtendsAsync<UIConfiguration>(
    "child.json",
    c => c.Extends,                              // 获取 extends 字段
    (baseC, derivedC) => JsonMerger.MergeModels(baseC, derivedC)  // 合并函数
);
```

#### StorageAdapter — 替代 DataManage.Storage.Helpers.Serializer

```csharp
var storage = new StorageAdapter();
string json = storage.Serialize(myData);
var data = storage.Deserialize<MyType>(json);
```

#### CommandAdapter — 替代 MetaCommand.JsonCommandFramework

```csharp
var cmdAdapter = new CommandAdapter();

// 自动包含日志和验证中间件
cmdAdapter.Pipeline.Use(RetryMiddleware.Create(maxRetries: 3));
cmdAdapter.Pipeline.SetCoreHandler(async cmd => { ... });

var result = await cmdAdapter.ExecuteAsync("Do update path=/user name=张三");
```

#### WdUIAdapter — WdUI 配置管线专用

```csharp
// 构建 WdUI 配置加载管线
var pipeline = WdUIAdapter.BuildConfigPipeline<UIConfiguration>();
var config = await pipeline.ProcessAsync("ui_config.json");
```

---

### 10. `Diagnostics` — 诊断层

```csharp
// 管线性能测量
var diag = await JsonDiagnostics.MeasureAsync(pipeline, input);
Console.WriteLine($"耗时: {diag.DurationMs}ms, 内存变化: {diag.MemoryDeltaFormat}");

// JSON 大小分析
var sizeInfo = JsonDiagnostics.AnalyzeJsonSize(json);
Console.WriteLine($"大小: {sizeInfo.EstimatedKb}KB, 元素数: {sizeInfo.ElementCount}");
```

---

## 重构方案：从现有项目迁移

### 阶段一：WdUI 重构

#### 当前架构

```
WdUI.Utilities.JsonHelper                   ─── 基础序列化
WdUI.Configuration.ConfigurationLoader       ─── 配置加载+缓存+继承合并
WdUI.Layout.Transformation.LayoutTransformer ─── ControlConfig ↔ LayoutDefinition 转换
WdUI.UI.Framework.IConfigurationLoader       ─── 配置加载接口
```

#### 重构后架构

```csharp
// ─── 1. 替换 JsonHelper ───
// 之前：
var json = JsonHelper.Serialize(obj);
var obj = JsonHelper.Deserialize<T>(json);

// 之后：
using UniversalJsonMiddleware.Extensions;
var json = obj.ToJson();
var obj = json.FromJson<T>();

// ─── 2. 替换 ConfigurationLoader ───
// 之前：
var loader = ConfigurationLoader.Instance;
var config = await loader.LoadConfigAsync(path);

// 之后：
using UniversalJsonMiddleware.Adapters;
var adapter = new ConfigLoaderAdapter();
var config = await adapter.LoadConfigWithExtendsAsync<UIConfiguration>(
    path,
    c => c.Extends,
    (b, d) => JsonMerger.MergeModels(b, d)
);

// ─── 3. 用管线替代 LayoutTransformer ───
using UniversalJsonMiddleware.Core;
using UniversalJsonMiddleware.Serialization;

// 构建 ControlConfig → LayoutDefinition 转换管线
var transformPipeline = new JsonPipelineBuilder<ControlConfig, LayoutDefinition>("LayoutTransform")
    .AddStage(new ModelTransformStage<ControlConfig, LayoutDefinition>(config => {
        var mapper = new FieldMapper<ControlConfig, LayoutDefinition>()
            .AddMapping("Name", "Name")
            .AddCustomMapping((src, tgt) => {
                tgt.Elements.Add(new ElementDefinition {
                    ControlName = src.Name,
                    Position = ConvertPosition(src.Layout)
                });
                return tgt;
            });
        return mapper.Map(config);
    }))
    .Build();

var layoutDef = await transformPipeline.ProcessAsync(controlConfig);
```

#### WdUI 代码变更清单

| 文件 | 变更内容 |
|------|---------|
| `WdUI.Utilities.cs` | 删除 `JsonHelper`，改为引用 `UniversalJsonMiddleware.Extensions` |
| `WdUI.Configuration.cs` | `ConfigurationLoader` 内部改用 `ConfigLoaderAdapter` + `JsonMerger` |
| `WdUI.Framework.cs` | `IConfigurationLoader` 保留接口，实现委托给适配器 |
| `WdUIComposer.cs` | `ToJson/FromJson/LoadFromFile/SaveToFile` 委托给新框架扩展方法 |
| `LayoutApplier.cs` | `LayoutTransformer` 内部使用 `FieldMapper` + `ModelTransformStage` |

---

### 阶段二：BasicMethod 重构

#### 当前架构

```
SystemJsonFramework.Serialization          ─── 序列化 + SafeJsonSerializer
SystemJsonFramework.Deserialization        ─── 三步反序列化
SystemJsonFramework.JsonHelper             ─── 静态 JsonHelper
NewtonSoftJsonFramework.Deserialization    ─── Newtonsoft 版（计划废弃）
```

#### 重构后

```csharp
// ─── 统一使用 UnifiedJsonSerializer ───
using UniversalJsonMiddleware.Serialization;

// 基础序列化
var json = UnifiedJsonSerializer.Default.Serialize(obj);

// 安全序列化（替代 SafeJsonSerializer）
var safeJson = UnifiedJsonSerializer.Default.SafeSerialize(obj);

// 三步反序列化（替代 Deserialization.Deserialize<T>）
var result = UnifiedJsonSerializer.Default.DeserializeWithValidation<T>(json,
    validation => {
        if (!validation.IsValid)
            Console.WriteLine($"Validation errors: {string.Join(", ", validation.Errors)}");
    });

// 静态 JsonHelper 替换
using UniversalJsonMiddleware.Extensions;
var obj = json.FromJson<T>();
var json = obj.ToJson();
```

#### 废弃 Newtonsoft 依赖

`NewtonSoftJsonFramework.cs` 完全废弃，所有功能由 `UnifiedJsonSerializer` 覆盖。

---

### 阶段三：MetaCommand 重构

#### 当前架构

```
JsonCommandFramework
├── JsonCommand.Parse()       ─── 命令解析
├── JsonCommandResult         ─── 命令结果
├── MiddlewarePipeline        ─── 中间件管线
└── JsonPathUtil              ─── JSON Path 导航
```

#### 重构后

```csharp
// ─── 命令解析 ───
using UniversalJsonMiddleware.Middleware;
var cmd = JsonCommandParser.Parse("Do create path=/data");

// ─── 中间件管线 ───
var mwPipeline = new MiddlewarePipeline()
    .Use(LoggingMiddleware.Create())
    .Use(ValidationMiddleware.Create())
    .Use(RetryMiddleware.Create(maxRetries: 3));
mwPipeline.SetCoreHandler(async cmd => {
    // 业务逻辑
    return JsonCommandResult.Ok("done");
});
var result = await mwPipeline.ExecuteAsync(cmd);

// ─── JSON Path 导航 ───
using UniversalJsonMiddleware.Navigation;
var engine = new JsonPathEngine();
var node = engine.Navigate(root, "data.items[0].name");
engine.TryGetValue<int>(root, "data.count", out var count);
```

---

### 阶段四：DataManage 重构

#### 当前架构

```
Storage.Helpers.Serializer (ISerializer)  ─── 用于 LiteDB/SQLite 存储
```

#### 重构后

```csharp
using UniversalJsonMiddleware.Adapters;

// 替换 ISerializer 实现
public class MyStorage
{
    private readonly IStorageSerializer _serializer = new StorageAdapter();

    public void Save<T>(T data)
    {
        var json = _serializer.Serialize(data);
        // 写入存储...
    }

    public T Load<T>(string raw)
    {
        return _serializer.Deserialize<T>(raw);
    }
}
```

---

## 快速开始示例

### 示例 1：JSON 文件 → 模型（完整管线）

```csharp
using UniversalJsonMiddleware.Core;
using UniversalJsonMiddleware.Abstractions;
using UniversalJsonMiddleware.Serialization;

// 定义模型
public class AppConfig
{
    public string? AppName { get; set; }
    public int Version { get; set; }
    public List<string>? Modules { get; set; }
}

// 构建管线
var pipeline = new JsonPipelineBuilder<string, AppConfig>("ConfigLoader")
    .AddStage<JsonFileLoadStage>()           // 文件读取
    .AddStage<JsonParseStage>()               // JSON 解析
    .AddStage<JsonValidateStage>()            // 验证
    .AddStage(new JsonDeserializeStage<AppConfig>()) // 反序列化
    .Configure(c => { c.ThrowOnError = true; })
    .Build();

// 执行
var config = await pipeline.ProcessAsync("appsettings.json");
Console.WriteLine(config.AppName);
```

### 示例 2：中间件命令模式

```csharp
using UniversalJsonMiddleware.Middleware;
using UniversalJsonMiddleware.Abstractions;

var pipeline = new MiddlewarePipeline();
pipeline.Use(LoggingMiddleware.Create(msg => File.AppendAllText("log.txt", msg)));
pipeline.Use(ValidationMiddleware.Create());
pipeline.SetCoreHandler(async cmd => {
    // 模拟命令处理
    await Task.Delay(10);
    return JsonCommandResult.Ok($"Processed: {cmd.Verb} {cmd.Target}");
});

var result = await pipeline.ExecuteTextAsync("Do update user name=John");
```

### 示例 3：JSON Path 导航 + 序列化

```csharp
using UniversalJsonMiddleware.Navigation;
using UniversalJsonMiddleware.Serialization;

var json = @"{""store"":{""book"":[{""title"":""Book1"",""price"":10}]}}";
var root = JsonNode.Parse(json)!;

var engine = new JsonPathEngine();
var title = engine.Navigate(root, "store.book[0].title");
Console.WriteLine(title!.GetValue<string>());  // Book1

// 修改值
engine.SetValue(root, "store.book[0].price", JsonValue.Create(25));

// 序列化回字符串
var output = UnifiedJsonSerializer.Default.Serialize(root);
```

### 示例 4：验证管线

```csharp
using UniversalJsonMiddleware.Validator;

var json = @"{""name"":""Test"",""value"":42}";

var syntaxValidator = new JsonSyntaxValidator();
var syntaxResult = syntaxValidator.ValidateSyntax(json);
Console.WriteLine($"Syntax valid: {syntaxResult.IsValid}");

var schemaValidator = new JsonSchemaValidator();
schemaValidator.RegisterSchema<MyModel>("MyModel");
var schemaResult = schemaValidator.ValidateAgainstSchema(json, "MyModel");

var modelValidator = new JsonModelValidator();
var model = JsonSerializer.Deserialize<MyModel>(json)!;
var modelResult = modelValidator.ValidateModel(model);
```

---

## 与现有项目的对比

### 功能覆盖矩阵

| 功能 | WdUI | BasicMethod | MetaCommand | 新框架 |
|------|------|-------------|-------------|--------|
| 基本序列化 | ✅ | ✅ | ❌ | ✅ (统一) |
| 安全序列化 | ❌ | ✅ | ❌ | ✅ |
| 三步反序列化 | ❌ | ✅ | ❌ | ✅ |
| 配置继承合并 | ✅ | ❌ | ❌ | ✅ (增强) |
| JSON Path 导航 | ❌ | ❌ | ✅ | ✅ (增强) |
| 中间件管线 | ❌ | ❌ | ✅ | ✅ (增强) |
| 命令解析 | ❌ | ❌ | ✅ | ✅ |
| 模型验证 | ❌ | ❌ | ❌ | ✅ (新增) |
| 字段映射 | ❌ | ❌ | ❌ | ✅ (新增) |
| 性能诊断 | ❌ | ❌ | ❌ | ✅ (新增) |
| 扩展方法 | ✅ | ❌ | ❌ | ✅ |
| DI 集成 | ❌ | ❌ | ❌ | ✅ (新增) |

### 代码量对比（估算）

| 项目 | 当前行数 | 重构后行数 | 减少 |
|------|---------|-----------|------|
| WdUI JSON 相关 | ~800 | ~200 | 75% |
| BasicMethod JSON 相关 | ~600 | ~50 | 92% |
| MetaCommand JSON 相关 | ~500 | ~50 | 90% |
| DataManage 序列化 | ~60 | ~10 | 83% |
| **新框架本身** | **0** | **~900** | **(新增)** |
| **总量** | **~1960** | **~1210** | **~38%** |

---

## 迁移路线图

```
Phase 1: 创建框架                        Phase 2: WdUI 迁移
┌──────────────────────┐                 ┌──────────────────────┐
│ UniversalJsonMiddleware.cs │    ──►    │ WdUI.Utilities       │
│ (单文件 ~900行)        │                 │ WdUI.Configuration   │
└──────────────────────┘                 │ WdUIComposer         │
                                         └──────────────────────┘
         │                                        │
         ▼                                        ▼
Phase 3: BasicMethod 迁移                Phase 4: MetaCommand + DataManage
┌──────────────────────┐                 ┌──────────────────────┐
│ SystemJsonFramework  │    ──►    │ JsonCommandFramework  │
│ NewtonSoftJsonFrame¬ │                 │ Storage.Serializer   │
│ JsonTransation       │                 └──────────────────────┘
└──────────────────────┘
```

### 推荐步骤

1. **第 1 天**：创建 `UniversalJsonMiddleware.cs` 单文件项目，通过单元测试
2. **第 2 天**：WdUI 引用新框架，替换 `JsonHelper` + `ConfigurationLoader` 内部实现
3. **第 3 天**：BasicMethod 移除 `SystemJsonFramework` 和 `NewtonSoftJsonFramework`，全部委托给新框架
4. **第 4 天**：MetaCommand 的 `JsonCommandFramework` 继承新框架的 `MiddlewarePipeline` + `JsonPathEngine`
5. **第 5 天**：DataManage 的 `Serializer` 替换为 `StorageAdapter`
6. **第 6-7 天**：全面测试 + 性能对比 + 文档完善

---

## 总结

`UniversalJsonMiddleware` 通过 **1 个文件、9 个命名空间、10 个核心接口**，统一了 `Workflow_` 项目中所有 JSON 处理逻辑。其核心优势在于：

- **管线化架构**：将 JSON 处理从"硬编码流程"转变为"可组合的管线 Stage"
- **中间件模式**：源自 MetaCommand 但大幅增强，支持日志/验证/重试等横切关注点
- **适配器桥接**：现有项目无需重写，通过适配器逐步迁移
- **单文件部署**：整个框架仅一个 `.cs` 文件，复制即用，零外部依赖
- **类型安全**：全泛型设计，编译时类型检查
- **可测试性**：每个 Stage 可独立测试，管线可 mock
