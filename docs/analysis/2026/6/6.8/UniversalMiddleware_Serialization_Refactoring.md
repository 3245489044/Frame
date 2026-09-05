# UniversalMiddleware 序列化层重构方案

> 日期：2026-06-07
> 背景：`StyleConfigConverter` 未被 `System.Text.Json` 正确识别，导致 `style: "groupbox-default"` 反序列化失败

---

## 一、问题根因分析

### 1.1 直接原因

`StyleConfig` 类上的 `[JsonConverter(typeof(StyleConfigConverter))]` 注解没有被 `System.Text.Json` 正确识别，导致 `style: "groupbox-default"` 字符串值无法被反序列化为 `StyleConfig` 对象。

### 1.2 根本原因

**`DynamicObjectConverter`（`JsonConverter<object>`）干扰了 `[JsonConverter]` 注解的解析。**

`System.Text.Json` 的 converter 解析顺序：
1. `JsonSerializerOptions.Converters` 中的 converter（按注册顺序）
2. 类型上的 `[JsonConverter]` 注解
3. 内置 converter

当 `JsonSerializerOptions.Converters` 中包含 `JsonConverter<object>` 时，在某些 `System.Text.Json` 版本中，它会**拦截**所有类型的反序列化，导致类型上的 `[JsonConverter]` 注解被跳过。

### 1.3 架构缺陷

| # | 缺陷 | 描述 |
|---|------|------|
| 1 | **多上下文设计不合理** | `JsonSerializerFactory.Contexts` 按"用途"划分（UIConfig/PrettyPrint/Compact），但实际差异仅 `WriteIndented` 一个属性，不应作为上下文划分依据 |
| 2 | **Converter 无差别注册** | `DynamicObjectConverter` 这种有副作用的 converter 被注册到所有 3 个上下文 |
| 3 | **`StyleConfigConverter` 未显式注册** | 仅依赖 `[JsonConverter]` 注解，未在 `JsonSerializerOptions.Converters` 中注册 |
| 4 | **双重预处理** | `WdUIJson.LoadConfigAsync` 手动调用 `PreprocessJson` + `_serializer.Deserialize` 内部再次预处理 |
| 5 | **`DefaultOptions` 语义混淆** | 指向 `PrettyPrint` 上下文，但 UI 配置加载需要 `UIConfig` 的特性 |

---

## 二、重构目标

1. **消除多上下文设计**：合并为单一核心上下文，JSON 美化/压缩等功能通过插件式预处理管线实现
2. **Converter 统一注册**：所有 converter 注册到单一上下文，`DynamicObjectConverter` 不再干扰 `[JsonConverter]` 注解
3. **消除双重预处理**：统一预处理入口
4. **增强启动期校验**：自动发现未注册的 converter

---

## 三、重构方案

### 3.1 架构总览（重构后）

```
┌─────────────────────────────────────────────────────────────┐
│                  UnifiedJsonSerializer                       │
├─────────────────────────────────────────────────────────────┤
│  SerializerOptions (单一核心上下文)                           │
│  ├─ JsonStringEnumConverter                                  │
│  ├─ DynamicObjectConverter                                   │
│  ├─ StyleConfigConverter                                     │
│  └─ (其他 Converter)                                         │
├─────────────────────────────────────────────────────────────┤
│  Preprocessor (插件式预处理管线)                              │
│  ├─ Step 1: TemplateExpandStep (模板展开)                    │
│  ├─ Step 2: StyleResolveStep (样式解析)                      │
│  ├─ Step 3: PrettyPrintStep (美化输出, 可选注册)             │
│  └─ Step N: CustomStep (自定义步骤)                          │
├─────────────────────────────────────────────────────────────┤
│  Deserialize<T>(json)                                        │
│  → Preprocessor.Preprocess(json)                             │
│  → JsonSerializer.Deserialize<T>(json, SerializerOptions)    │
└─────────────────────────────────────────────────────────────┘
```

### 3.2 修改清单

#### 文件 1：`JsonSerializerFactory.cs` — 合并为单一上下文

**修改前：** 3 个上下文（UIConfig / PrettyPrint / Compact），差异仅 `WriteIndented`

**修改后：** 单一核心上下文，`WriteIndented` 通过参数控制

```csharp
public static class JsonSerializerFactory
{
    /// <summary>
    /// 核心序列化选项 — 所有序列化/反序列化共用
    /// Converter 通过 JsonConverterRegistrar 统一注册
    /// </summary>
    public static JsonSerializerOptions DefaultOptions { get; } = CreateDefaultOptions();
    
    private static JsonSerializerOptions CreateDefaultOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
    }
    
    /// <summary>
    /// 创建带 WriteIndented 的选项副本（用于序列化时控制缩进）
    /// </summary>
    public static JsonSerializerOptions CreateIndentedOptions()
    {
        var options = new JsonSerializerOptions(DefaultOptions);
        options.WriteIndented = true;
        return options;
    }
}
```

#### 文件 2：`JsonConverterRegistrar.cs` — 只注册到单一上下文

**修改前：** 注册到所有 3 个上下文

**修改后：** 只注册到 `JsonSerializerFactory.DefaultOptions`

```csharp
public static void RegisterAll()
{
    lock (_lock)
    {
        if (_registered) return;
        
        var options = JP.Serialization.JsonSerializerFactory.DefaultOptions;
        
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new JP.Serialization.DynamicObjectConverter());
        options.Converters.Add(new BasicMethod.ConfigurationModels.StyleConfigConverter());
        
        _registered = true;
    }
}
```

**`Validate()` 增强：**

```csharp
public static List<string> Validate()
{
    var errors = new List<string>();
    var options = UnifiedJsonSerializer.DefaultOptions;
    var registeredTypes = options.Converters.Select(c => c.GetType()).ToHashSet();
    
    // 1. 检查必要 converter
    var required = new[]
    {
        typeof(JsonStringEnumConverter),
        typeof(JP.Serialization.DynamicObjectConverter),
        typeof(BasicMethod.ConfigurationModels.StyleConfigConverter),
    };
    
    foreach (var type in required)
    {
        if (!registeredTypes.Contains(type))
            errors.Add($"缺少必要的 Converter: {type.Name}");
    }
    
    // 2. 检查重复注册
    var duplicates = options.Converters
        .GroupBy(c => c.GetType())
        .Where(g => g.Count() > 1)
        .Select(g => $"{g.Key.Name} (重复 {g.Count()} 次)")
        .ToList();
    
    if (duplicates.Any())
        errors.AddRange(duplicates);
    
    return errors;
}
```

#### 文件 3：`Serialization.cs` — 简化 UnifiedJsonSerializer

**修改前：** 3 个 Options 属性（DefaultOptions / CompactOptions / SafeOptions），其中两个指向同一个上下文

**修改后：** 2 个 Options 属性（DefaultOptions / IndentedOptions），语义清晰

```csharp
public class UnifiedJsonSerializer : IJsonSerializer
{
    public static JsonPreprocessor Preprocessor { get; } = new();
    
    /// <summary>默认序列化选项（核心上下文）</summary>
    public static JsonSerializerOptions DefaultOptions =>
        JP.Serialization.JsonSerializerFactory.DefaultOptions;
    
    /// <summary>美化输出选项（用于日志导出、调试）</summary>
    public static JsonSerializerOptions IndentedOptions =>
        JP.Serialization.JsonSerializerFactory.CreateIndentedOptions();
    
    public string Serialize<T>(T obj, bool writeIndented = false)
    {
        var options = writeIndented ? IndentedOptions : DefaultOptions;
        return JsonSerializer.Serialize(obj, options);
    }
    
    public T? Deserialize<T>(string json, JsonSerializerOptions? options = null)
    {
        options ??= DefaultOptions;
        json = Preprocessor.Preprocess(json);
        return JsonSerializer.Deserialize<T>(json, options);
    }
    
    // 移除：CompactOptions / SafeOptions / SerializeCompact / DeserializeCompact
    // 调用方直接使用 Serialize/Deserialize 并传入 options 参数
}
```

#### 文件 4：`WdUIJson.cs` — 消除双重预处理

**修改前：** 手动 `PreprocessJson` + `_serializer.Deserialize` 内部再次预处理

**修改后：** 直接使用 `_serializer.DeserializeFromFileAsync`，内部已集成预处理管线

```csharp
public static async Task<UIConfiguration> LoadConfigAsync(string configPath)
{
    if (!File.Exists(configPath))
        throw new FileNotFoundException("Configuration file not found", configPath);
    
    // 直接使用 DeserializeFromFileAsync，内部已集成预处理管线
    var config = await _serializer.DeserializeFromFileAsync<UIConfiguration>(configPath)
        ?? throw new InvalidOperationException("Failed to deserialize configuration");
    
    // ... 数据快照日志保持不变 ...
    
    return config;
}
```

**`PreprocessJson` 方法标记为 `[Obsolete]`：**

```csharp
[Obsolete("直接使用 _serializer.Deserialize 系列方法，内部已集成预处理管线")]
public static string PreprocessJson(string rawJson)
{
    if (string.IsNullOrEmpty(rawJson)) return rawJson;
    return UnifiedJsonSerializer.Preprocessor.Preprocess(rawJson) ?? rawJson;
}
```

---

## 四、影响范围分析

### 4.1 需要同步修改的调用方

| 调用方 | 当前使用 | 修改后 |
|--------|----------|--------|
| `WdUIJson.ToJson<T>(obj, true)` | `UnifiedJsonSerializer.DefaultOptions` | `UnifiedJsonSerializer.IndentedOptions` |
| `WdUIJson.ToJson<T>(obj, false)` | `UnifiedJsonSerializer.CompactOptions` | `UnifiedJsonSerializer.DefaultOptions` |
| `WdUIJson.FromJson<T>(json)` | `UnifiedJsonSerializer.DefaultOptions` | 不变 |
| `WdUIJson.LoadConfigAsync` | 手动 `PreprocessJson` | 直接 `DeserializeFromFileAsync` |
| `JsonValidator.ValidateStructure<T>` | `UnifiedJsonSerializer.DefaultOptions` | 不变 |
| `JsonValidator.ValidateAgainstSchema<T>` | `UnifiedJsonSerializer.DefaultOptions` | 不变 |
| `JsonNodeConverter.FromObject` | `UnifiedJsonSerializer.DefaultOptions` | 不变 |
| `JsonNodeConverter.ToObject` | `UnifiedJsonSerializer.DefaultOptions` | 不变 |

### 4.2 风险点

1. **`DefaultOptions` 的 `WriteIndented` 值变化**：重构前 `DefaultOptions` 指向 `PrettyPrint`（`WriteIndented = true`），重构后 `DefaultOptions` 的 `WriteIndented = false`。所有依赖 `DefaultOptions` 进行美化输出的地方需要改为 `IndentedOptions`。
2. **`CompactOptions` 移除**：所有使用 `CompactOptions` 的地方需要改为 `DefaultOptions`（两者在重构前配置相同，除 `Encoder` 外）。

---

## 五、实施步骤

| 步骤 | 文件 | 内容 | 风险 |
|------|------|------|------|
| 1 | `JsonSerializerFactory.cs` | 合并为单一上下文 | 低 |
| 2 | `JsonConverterRegistrar.cs` | 只注册到单一上下文 + Validate 增强 | 低 |
| 3 | `Serialization.cs` | 简化 UnifiedJsonSerializer | 中 |
| 4 | `WdUIJson.cs` | 消除双重预处理 | 低 |
| 5 | 全局搜索 `CompactOptions` | 替换为 `DefaultOptions` | 中 |
| 6 | 全局搜索 `DefaultOptions` 用于序列化 | 确认是否需要 `IndentedOptions` | 中 |
| 7 | 构建验证 | 确认 0 错误 | 低 |
| 8 | 集成测试 | 覆盖两条启动路径 | 高 |

---

## 六、预期效果

1. **`StyleConfigConverter` 被正确使用**：显式注册到 `DefaultOptions.Converters`，优先级高于 `DynamicObjectConverter`
2. **`DynamicObjectConverter` 不干扰 `[JsonConverter]` 注解**：两者共存于同一上下文，`System.Text.Json` 按注册顺序优先匹配具体类型
3. **预处理管线只执行一次**：消除双重预处理
4. **语义清晰**：`DefaultOptions` = 核心上下文，`IndentedOptions` = 美化输出
5. **启动期自动校验**：发现未注册的 converter，避免运行时崩溃
6. **插件式预处理**：美化输出作为预处理步骤注册，与业务逻辑解耦

---

## 七、回滚方案

如果重构后出现问题，可以：

1. **保留 `JsonSerializerFactory.Contexts` 类**（标记为 `[Obsolete]`），确保旧代码仍可编译
2. **保留 `CompactOptions` 和 `SafeOptions` 属性**（标记为 `[Obsolete]`），指向 `DefaultOptions`
3. **保留 `PreprocessJson` 方法**（标记为 `[Obsolete]`），确保旧调用方不受影响
