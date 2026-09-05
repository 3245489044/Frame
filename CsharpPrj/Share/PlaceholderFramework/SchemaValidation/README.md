# SchemaValidation — 通用 JSON Schema 校验框架

## 概述

通用 JSON Schema 校验框架，提供声明式的 Schema 定义和校验能力。任何 JSON 对象只需实现 `IJsonSchemaTarget` 适配器接口即可被校验。

## 核心类型

| 类型 | 说明 |
|------|------|
| `JsonSchema` | Schema 定义模型（必填/可选/条件字段 + 值约束 + 嵌套 Schema） |
| `JsonSchemaBuilder` | 流式 Schema 构建器 |
| `SchemaRegistry<TKey>` | 泛型 Schema 注册中心（线程安全） |
| `SchemaValidator` | 通用 Schema 校验器 |
| `IJsonSchemaTarget` | 校验目标适配器接口 |
| `ValidationResult` | 校验结果 |
| `ValidationError` | 校验错误 |
| `ValidationSeverity` | 校验严重级别（Warning/Error） |

## 使用示例

### 1. 定义 Schema

```csharp
using Common.PlaceholderFramework.SchemaValidation;

// 使用流式 Builder 定义 Schema
var buttonSchema = new JsonSchemaBuilder()
    .WithRequired("type", "name")
    .WithOptional("text", "events", "dock", "style")
    .WithAllowedValues("dock", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "none", "top", "bottom", "left", "right", "fill" })
    .Build();
```

### 2. 实现适配器

```csharp
// 将领域模型适配为 IJsonSchemaTarget
public class MyConfigTarget : IJsonSchemaTarget
{
    private readonly MyConfig _config;

    public MyConfigTarget(MyConfig config) => _config = config;

    public string TargetName => _config.Name ?? "(unnamed)";
    public string TargetType => _config.Type ?? "(unknown)";

    public bool HasField(string fieldName)
    {
        // 使用反射或硬编码检查字段是否存在
        return fieldName.ToLowerInvariant() switch
        {
            "type" => !string.IsNullOrEmpty(_config.Type),
            "name" => !string.IsNullOrEmpty(_config.Name),
            "text" => _config.Text != null,
            _ => false
        };
    }

    public string? GetFieldValue(string fieldName)
    {
        return fieldName.ToLowerInvariant() switch
        {
            "type" => _config.Type,
            "name" => _config.Name,
            "dock" => _config.Dock,
            _ => null
        };
    }

    public IEnumerable<IJsonSchemaTarget> GetChildren()
    {
        // 返回子对象（用于递归校验）
        return _config.Children?.Select(c => new MyConfigTarget(c))
               ?? Enumerable.Empty<IJsonSchemaTarget>();
    }
}
```

### 3. 注册 Schema 并校验

```csharp
// 创建注册中心
var registry = new SchemaRegistry<string>("__default__");
registry.Register("__default__", new JsonSchemaBuilder()
    .WithRequired("type", "name")
    .Build());
registry.Register("button", buttonSchema);

// 创建校验器
var validator = new SchemaValidator();

// 校验单个目标
var target = new MyConfigTarget(myButtonConfig);
var result = validator.Validate(target, registry, "button");

if (!result.IsValid)
{
    Console.WriteLine(result.GetFormattedErrors());
}

// 校验目标集合
var targets = configs.Select(c => new MyConfigTarget(c));
var batchResult = validator.ValidateAll(
    targets,
    registry,
    target => target.TargetType);  // 使用 TargetType 作为 Schema 键
```

### 4. 宽松模式

```csharp
// 宽松模式下，校验失败仅记录警告，不中断处理
var result = validator.Validate(target, schema, strictMode: false);
foreach (var warning in result.Warnings)
{
    Console.WriteLine($"[WARNING] {warning}");
}
```

## 设计原则

1. **适配器模式**：通过 `IJsonSchemaTarget` 接口解耦领域模型和校验逻辑
2. **声明式定义**：Schema 定义与校验逻辑分离，新增类型只需定义 Schema
3. **线程安全**：`SchemaRegistry<TKey>` 使用 `ConcurrentDictionary`，支持运行时注册
4. **递归校验**：支持嵌套对象的递归校验
5. **无领域依赖**：不绑定任何特定领域模型
