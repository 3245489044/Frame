# 序列化框架重构方案：JsonConverter 自动注册机制

## 1. 问题概述

### 1.1 错误现象

```
[ERROR] Failed to load MainLayout: [ConfigContainer] JSON 反序列化失败: 
JSON 反序列化失败: 目标类型=BasicMethod.ConfigurationModels.UIConfiguration
路径: $.views[0].controls[0].style
行号: 15, 列位置: 34
实际 JSON 值: "toolbar-panel"
期望类型: StyleConfig { FontFamily, FontSize, FontStyle, ForeColor, BackColor, BorderColor, BorderWidth, BorderStyle }
```

### 1.2 根因

`StyleReferenceConverter` 虽然已经存在于代码中，但**没有被注册到 `UnifiedJsonSerializer.DefaultOptions.Converters` 集合中**，导致 `System.Text.Json` 在遇到字符串 `"toolbar-panel"` 时无法将其转换为 `StyleConfig` 对象。

### 1.3 框架设计缺陷

| # | 缺陷 | 严重程度 | 说明 |
|---|------|---------|------|
| 1 | Converter 注册采用"手动散装"模式 | **致命** | 新增 Converter 需手动修改 `DefaultOptions`，容易遗漏 |
| 2 | 存在两套平行的序列化配置体系 | **严重** | `UnifiedJsonSerializer` 与 `FileDriven.Utilities.JsonSerializerFactory` 各自维护一套配置 |
| 3 | `StyleReferenceConverter.CanConvert` 过于宽泛 | **中等** | `typeToConvert.Name.Contains("Style")` 可能与其他 Converter 冲突 |
| 4 | 无 Converter 注册校验机制 | **中等** | 运行时才能发现缺少 Converter，无编译期或启动期检查 |

---

## 2. 重构目标

1. **自动注册**：JsonConverter 通过特性或接口自动注册到全局序列化配置
2. **统一入口**：消除两套序列化配置体系，统一到一个注册入口
3. **启动期校验**：在框架初始化时校验所有必要的 Converter 是否已注册
4. **向后兼容**：现有代码无需修改或仅需最小修改

---

## 3. 重构方案

### 方案一：特性驱动自动注册（推荐）

#### 3.1.1 设计思路

定义 `[JsonConverterRegistration]` 特性，标记在 Converter 类上，框架启动时通过反射自动扫描并注册。

#### 3.1.2 核心代码

```csharp
// ============================================================
// JsonConverterRegistrationAttribute.cs
// ============================================================
namespace UniversalJsonMiddleware.Serialization;

/// <summary>
/// 标记 JsonConverter 需要自动注册到全局序列化配置
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class JsonConverterRegistrationAttribute : Attribute
{
    /// <summary>
    /// 注册优先级（数值越小优先级越高）
    /// </summary>
    public int Priority { get; set; } = 100;

    /// <summary>
    /// 适用的序列化上下文（Default = 0, Compact = 1, Safe = 2, All = 3）
    /// </summary>
    public SerializerContext TargetContexts { get; set; } = SerializerContext.Default;
}

/// <summary>
/// 序列化上下文标记（Flags 枚举，支持组合）
/// </summary>
[Flags]
public enum SerializerContext
{
    Default = 1,
    Compact = 2,
    Safe = 4,
    All = Default | Compact | Safe
}
```

```csharp
// ============================================================
// 使用示例：在 StyleReferenceConverter 上添加特性
// ============================================================
using UniversalJsonMiddleware.Serialization;

namespace UniversalJsonMiddleware.Serialization.Converters;

/// <summary>
/// Style 引用转换器 — 将 style 字符串引用展开为完整的 StyleConfig 对象
/// </summary>
[JsonConverterRegistration(Priority = 10, TargetContexts = SerializerContext.Default | SerializerContext.Safe)]
public class StyleReferenceConverter : JsonConverter<object>
{
    // ... 现有代码保持不变 ...
}
```

```csharp
// ============================================================
// JsonConverterRegistry.cs — 自动注册器
// ============================================================
namespace UniversalJsonMiddleware.Serialization;

/// <summary>
/// JsonConverter 自动注册器
/// 在框架初始化时扫描所有标记了 [JsonConverterRegistration] 的 Converter，
/// 并将其自动注册到 UnifiedJsonSerializer 的全局选项中。
/// </summary>
public static class JsonConverterRegistry
{
    private static readonly object _lock = new();
    private static bool _initialized;

    /// <summary>
    /// 初始化注册器，扫描并注册所有标记的 Converter
    /// </summary>
    /// <param name="assembliesToScan">要扫描的程序集列表（null 表示扫描所有已加载程序集）</param>
    public static void Initialize(IEnumerable<System.Reflection.Assembly>? assembliesToScan = null)
    {
        lock (_lock)
        {
            if (_initialized) return;

            var assemblies = assembliesToScan?.ToList()
                ?? AppDomain.CurrentDomain.GetAssemblies().ToList();

            var converterTypes = new List<(int Priority, Type Type, SerializerContext Contexts)>();

            foreach (var assembly in assemblies)
            {
                try
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        var attr = type.GetCustomAttribute<JsonConverterRegistrationAttribute>();
                        if (attr == null) continue;

                        if (!typeof(JsonConverter).IsAssignableFrom(type))
                        {
                            Logger?.Invoke($"[JsonConverterRegistry] 警告: {type.FullName} 标记了 " +
                                $"[JsonConverterRegistration] 但不是 JsonConverter，已跳过");
                            continue;
                        }

                        converterTypes.Add((attr.Priority, type, attr.TargetContexts));
                    }
                }
                catch (System.Reflection.ReflectionTypeLoadException)
                {
                    // 跳过无法加载的程序集
                }
            }

            // 按优先级排序
            converterTypes.Sort((a, b) => a.Priority.CompareTo(b.Priority));

            foreach (var (_, type, contexts) in converterTypes)
            {
                var converter = (JsonConverter)Activator.CreateInstance(type)!;

                if (contexts.HasFlag(SerializerContext.Default))
                    UnifiedJsonSerializer.DefaultOptions.Converters.Add(converter);
                if (contexts.HasFlag(SerializerContext.Compact))
                    UnifiedJsonSerializer.CompactOptions.Converters.Add(converter);
                if (contexts.HasFlag(SerializerContext.Safe))
                    UnifiedJsonSerializer.SafeOptions.Converters.Add(converter);

                Logger?.Invoke($"[JsonConverterRegistry] 已注册 Converter: {type.Name} " +
                    $"(优先级={_}, 上下文={contexts})");
            }

            _initialized = true;
        }
    }

    public static Action<string>? Logger { get; set; }
}
```

#### 3.1.3 初始化时机

在 `FrameworkInitializer` 或 `WdUIComposer` 的初始化入口调用：

```csharp
// 在 FrameworkInitializer.InitializeAsync() 或 WdUIComposer 的 Lazy 初始化中
JsonConverterRegistry.Initialize();
```

---

### 方案二：集中式注册表（轻量级替代方案）

如果不想引入反射扫描，可以采用集中式注册表模式：

```csharp
// ============================================================
// JsonConverterRegistrar.cs — 集中式注册表
// ============================================================
namespace UniversalJsonMiddleware.Serialization;

/// <summary>
/// JsonConverter 集中注册表
/// 所有 Converter 在此处显式注册，统一管理
/// </summary>
public static class JsonConverterRegistrar
{
    private static bool _registered;

    /// <summary>
    /// 注册所有内置 Converter 到 UnifiedJsonSerializer 的全局选项
    /// </summary>
    public static void RegisterAll()
    {
        if (_registered) return;

        // ============================================================
        // ★ 所有 Converter 在此集中注册 ★
        // 新增 Converter 时必须在此添加注册代码
        // ============================================================

        // DefaultOptions
        RegisterToDefault(new JsonStringEnumConverter());
        RegisterToDefault(new DynamicObjectConverter());
        RegisterToDefault(new StyleReferenceConverter());  // ← 之前遗漏的就是这一行

        // SafeOptions
        RegisterToSafe(new DynamicObjectConverter());
        RegisterToSafe(new StyleReferenceConverter());

        // CompactOptions
        RegisterToCompact(new JsonStringEnumConverter());

        _registered = true;
    }

    public static void RegisterToDefault(JsonConverter converter)
        => UnifiedJsonSerializer.DefaultOptions.Converters.Add(converter);

    public static void RegisterToCompact(JsonConverter converter)
        => UnifiedJsonSerializer.CompactOptions.Converters.Add(converter);

    public static void RegisterToSafe(JsonConverter converter)
        => UnifiedJsonSerializer.SafeOptions.Converters.Add(converter);

    /// <summary>
    /// 启动期校验：检查必要的 Converter 是否已注册
    /// </summary>
    public static List<string> Validate()
    {
        var errors = new List<string>();

        var defaultTypes = UnifiedJsonSerializer.DefaultOptions.Converters
            .Select(c => c.GetType()).ToHashSet();

        // 检查必须注册的 Converter
        var required = new[] {
            typeof(StyleReferenceConverter),
            typeof(DynamicObjectConverter)
        };

        foreach (var type in required)
        {
            if (!defaultTypes.Contains(type))
                errors.Add($"缺少必要的 Converter: {type.Name}");
        }

        return errors;
    }
}
```

---

### 方案三：统一序列化入口（消除两套体系）

无论选择方案一还是方案二，都需要解决"两套序列化体系"的问题。

```csharp
// ============================================================
// 统一序列化入口 — 消除 FileDriven.Utilities 中的平行体系
// ============================================================

// 在 FileDriven.Utilities.Json.JsonSerializerFactory 中：
public static class JsonSerializerFactory
{
    /// <summary>
    /// 获取全局统一的 JsonSerializerOptions
    /// 委托到 UnifiedJsonSerializer.DefaultOptions，消除两套配置
    /// </summary>
    public static JsonSerializerOptions DefaultOptions 
        => UnifiedJsonSerializer.DefaultOptions;

    /// <summary>
    /// 获取紧凑序列化选项
    /// </summary>
    public static JsonSerializerOptions CompactOptions 
        => UnifiedJsonSerializer.CompactOptions;
}
```

---

## 4. 推荐方案对比

| 维度 | 方案一：特性驱动 | 方案二：集中注册表 |
|------|----------------|------------------|
| 实现复杂度 | 中等（需反射扫描） | 低（显式调用） |
| 可维护性 | 高（新增 Converter 只需加特性） | 中（需手动添加注册代码） |
| 性能影响 | 仅启动期一次反射 | 无反射 |
| 可发现性 | 低（需扫描才知道有哪些 Converter） | 高（注册表一目了然） |
| 第三方扩展 | 支持（第三方程序集加特性即可） | 不支持 |

**推荐方案二（集中注册表）**，理由：
1. 当前项目 Converter 数量有限（3-5 个），集中管理更清晰
2. 无反射性能开销
3. 代码可读性更好，所有 Converter 一目了然
4. 配合 `Validate()` 方法可在启动期检查遗漏

---

## 5. 实施步骤

### Step 1：创建 `JsonConverterRegistrar` 类

在 `BasicMethod/UniversalJsonMiddleware/Serialization/` 目录下创建 `JsonConverterRegistrar.cs`，实现集中注册表。

### Step 2：修改 `UnifiedJsonSerializer.DefaultOptions`

移除 `DefaultOptions` 中现有的 `Converters` 初始化，改为由 `JsonConverterRegistrar` 统一管理：

```csharp
public static readonly JsonSerializerOptions DefaultOptions = new()
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    Converters = { }  // ★ 由 JsonConverterRegistrar 填充
};
```

### Step 3：在框架初始化入口调用注册

在 `FrameworkInitializer.InitializeAsync()` 或 `WdUIComposer` 的 Lazy 初始化中添加：

```csharp
// 在 WdUIComposer 的 Lazy<WdUIHost> 初始化中
JsonConverterRegistrar.RegisterAll();

// 可选：启动期校验
var errors = JsonConverterRegistrar.Validate();
if (errors.Any())
{
    // 记录警告日志
    foreach (var error in errors)
        WdUILogPlaceholderHandler.Log("JsonConverter_Validation_Warning", 
            new { message = error });
}
```

### Step 4：统一 `FileDriven.Utilities` 中的序列化配置

将 `FileDriven.Utilities.Json.JsonSerializerFactory` 改为委托到 `UnifiedJsonSerializer`。

### Step 5：添加启动期校验测试

```csharp
// 单元测试示例
[Test]
public void DefaultOptions_Should_Contain_StyleReferenceConverter()
{
    JsonConverterRegistrar.RegisterAll();
    var errors = JsonConverterRegistrar.Validate();
    Assert.IsEmpty(errors);
}
```

---

## 6. 预期效果

| 指标 | 修复前 | 修复后 |
|------|--------|--------|
| `StyleReferenceConverter` 注册状态 | ❌ 未注册 | ✅ 自动注册 |
| 新增 Converter 的流程 | 手动修改多处 | 集中注册表 + 启动期校验 |
| 两套序列化体系 | 存在冲突风险 | 统一入口 |
| 启动期发现遗漏 | ❌ 运行时崩溃 | ✅ 启动期日志警告 |

---

## 7. 附录：当前涉及的 Converter 清单

| Converter | 位置 | 当前注册状态 | 应注册到 |
|-----------|------|-------------|---------|
| `JsonStringEnumConverter` | System.Text.Json 内置 | ✅ DefaultOptions | Default, Compact |
| `DynamicObjectConverter` | `Serialization.cs` | ✅ DefaultOptions, SafeOptions | Default, Safe |
| `StyleReferenceConverter` | `Converters/StyleReferenceConverter.cs` | ❌ **未注册** | Default, Safe |
| `EventConfigConverter` | `ConfigurationModels/Models/EventConfig.cs` | ❌ 需确认 | Default |
