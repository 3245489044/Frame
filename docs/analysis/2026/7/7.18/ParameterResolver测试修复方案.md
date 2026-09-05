# ParameterResolver 测试修复方案

> 创建日期：2026-07-18
> 状态：待执行
> 关联：`CentralTest/CSharpTest/Modules.Tests/ParameterResolver.Tests/`

---

## 一、当前状态

### 已完成
- ✅ `ParameterResolver.Tests.csproj` 项目文件已创建
- ✅ 28 个测试文件已创建（覆盖所有模块）
- ❌ 构建失败：571 个错误

### 构建错误分类

| 类别 | 数量 | 原因 |
|------|------|------|
| MSTest 属性找不到 | ~500 | csproj 缺少 MSTest NuGet 包引用 |
| 类型/命名空间不存在 | ~50 | 测试引用的类型与 ParameterResolver 实际 API 不匹配 |
| Mock 接口实现不完整 | ~10 | Mock 类未实现接口全部成员 |

---

## 二、修复原则

### 2.1 不改动生产代码

**静态类保持不动**，测试适配静态 API。涉及：

| 生产类 | 性质 | 测试写法 |
|--------|------|----------|
| `TypeConverter` | `public static class` | 直接调用 `TypeConverter.Convert(value, type)` |
| `ValueFormatter` | `public static class` | 直接调用 `ValueFormatter.Format(value)` |
| `JsonPathResolver` | 待确认 | 按实际 API 调用 |

理由：
- 静态类设计合理（无状态工具类）
- 测试静态类完全可行
- 修改生产代码引入回归风险

### 2.2 测试文件匹配真实 API

所有测试文件中的类型引用必须与 `ParameterResolver` 项目的实际类型完全一致。

---

## 三、真实 API 映射表

### 3.1 Converter 模块

| 测试中引用的类型 | 实际存在的类型 | 说明 |
|-----------------|---------------|------|
| `TypeConverter`（实例化） | `TypeConverter`（静态类） | 不能 `new`，直接静态调用 |
| `ValueConverterRegistry` | `ConverterRegistry` | 类名不同 |
| `IValueConverter<string, int>` | `IValueConverter`（单泛型） | 接口签名不同 |
| `CanConvert<string, int>()` | `CanConvert(Type, Type)` | 参数不同 |

**TypeConverter 真实 API：**
```csharp
public static class TypeConverter
{
    public static (object? converted, string? error) Convert(object? value, Type targetType);
    public static object?[] ConvertArguments(object?[] arguments, Type[] targetTypes);
    public static void RegisterParser<T>(Func<string?, (bool, object?)> parser);
    public static bool CanConvert(Type sourceType, Type targetType);
}
```

**ConverterRegistry 真实 API：**
```csharp
public class ConverterRegistry : IConverterRegistry
{
    public void Register(Type targetType, IValueConverter converter);
    public void Register<TTarget>(IValueConverter converter);
    public IValueConverter? GetConverter(Type targetType);
    public bool HasConverter(Type targetType);
    public void RegisterProvider(IConverterProvider provider);
    public IEnumerable<KeyValuePair<Type, IValueConverter>> GetAllConverters();
}
```

### 3.2 Formatting 模块

| 测试中引用的类型 | 实际存在的类型 | 说明 |
|-----------------|---------------|------|
| `DiagnosticFormatter` | `ValueFormatter`（静态类） | 类名不同，且为静态 |

**ValueFormatter 真实 API：**
```csharp
public static class ValueFormatter
{
    public static string Format(object? value);
    public static string Format(object? value, string? format);
}
```

### 3.3 KeyMapping 模块

| 测试中引用的类型 | 实际存在的类型 | 说明 |
|-----------------|---------------|------|
| `KeyMappingEntry` | `MappingRule` | 类名不同 |
| `KeyMappingConfigTests` | 保留 | `KeyMappingConfig` 存在 |
| `PatternMatcher` | `KeyMappingMatcher` / `SingleValueMatcher` | 类名不同 |
| `WildcardMatcher` | `DictionaryMatcher` / `DefaultValueMatcher` | 类名不同 |

**KeyMapping 真实类型：**
- `KeyMappingConfig` — 映射配置模型
- `MappingRule` — 映射规则（替代 `KeyMappingEntry`）
- `MappingRuleType` — 枚举：`Exact`、`Pattern`、`MultiLevel`
- `LevelPattern` — 层级模式
- `KeyMatchResult` — 匹配结果
- `KeyMapperBase<TKey>` — 抽象基类（模板方法模式）
- `PathProcessor` — 路径处理工具

**Matchers 真实类型：**
- `KeyMappingMatcher` — 键映射匹配器
- `ParameterMatcherBase` — 参数匹配器基类
- `SingleValueMatcher` — 单值匹配器
- `DictionaryMatcher` — 字典匹配器
- `DefaultValueMatcher` — 默认值匹配器
- `PositionalMatcher` — 位置匹配器

### 3.4 Parameters 模块

| 测试中引用的类型 | 实际存在的类型 | 说明 |
|-----------------|---------------|------|
| `ParameterDefinition` | `ParameterHelper` / `DefaultParameterProcessor` | 类名不同 |

**Parameters 真实类型：**
- `DefaultParameterProcessor` — 默认参数处理器
- `FallbackTypeConverter` — 回退类型转换器
- `ParameterHelper` — 参数辅助工具
- `ParameterTransformer` — 参数转换器

### 3.5 Sources 模块

| 测试中引用的类型 | 实际存在的类型 | 说明 |
|-----------------|---------------|------|
| `ParameterSource` | `VirtualStoreSource` | 类名不同 |

**Sources 真实类型：**
- `VirtualStoreSource` — 虚拟存储源
- `WinForms` 子目录 — WinForms 相关源

### 3.6 Strategies 模块

| 测试中引用的类型 | 实际存在的类型 | 说明 |
|-----------------|---------------|------|
| `DirectValueStrategy` | `StrategyResolver` | 类名不同 |
| `ReferenceStrategy` | `StrategyResolver` | 类名不同 |
| `DefaultValueStrategy` | `StrategyResolver` | 类名不同 |
| `ChainStrategy` | `StrategyResolver` | 类名不同 |
| `IParameterResolutionStrategy` | 不存在 | 接口不同 |

**Strategies 真实类型：**
- `StrategyResolver` — 策略解析器
- `Abstractions` 子目录 — 策略抽象

### 3.7 Validation 模块（大部分匹配）

| 测试中引用的类型 | 实际存在的类型 | 说明 |
|-----------------|---------------|------|
| `ControlRefRule` | ✅ 存在 | 匹配 |
| `FunctionRefRule` | ✅ 存在 | 匹配 |
| `TemplateRefRule` | ✅ 存在 | 匹配 |
| `OutputTargetRule` | ✅ 存在 | 匹配 |
| `EventBindingFormatRule` | ✅ 存在 | 匹配 |
| `ParameterMappingFormatRule` | ✅ 存在 | 匹配 |
| `ControlExistenceRule` | ✅ 存在 | 匹配 |
| `RequiredFieldRule` | ✅ 存在 | 匹配 |
| `EnumValueRule` | ✅ 存在 | 匹配 |
| `EventValueTypeRule` | ✅ 存在 | 匹配 |
| `LayoutFormatRule` | ✅ 存在 | 匹配 |
| `ConfigValidationContext` | ✅ 存在 | 匹配 |
| `ConfigValidatorFactory` | ✅ 存在 | 匹配 |
| `ParameterMappingValidator` | ✅ 存在 | 匹配 |
| `ValidationDiagnostic` | ✅ 存在 | 匹配 |
| `SourceLocation` | ✅ 存在 | 匹配 |
| `Severity` | ✅ 存在 | 匹配 |
| `ValidationReport` | ✅ 存在 | 匹配 |

### 3.8 Validation Providers 接口（Mock 需修正）

**IControlTreeProvider 真实接口：**
```csharp
public interface IControlTreeProvider
{
    string Name { get; }
    IReadOnlySet<string> GetControlNames();
    bool HasControl(string controlName);
    string? GetControlType(string controlName);
    IReadOnlyList<string>? GetSupportedEvents(string controlName);
}
```

**IFunctionRegistryProvider 真实接口：**
```csharp
public interface IFunctionRegistryProvider
{
    string Name { get; }
    IReadOnlySet<string> GetFunctionNames();
    bool HasFunction(string functionName);
    IReadOnlyList<string>? GetFunctionParameterTypes(string functionName);
}
```

**ITemplateRegistryProvider 真实接口：**
```csharp
public interface ITemplateRegistryProvider
{
    string Name { get; }
    IReadOnlySet<string> GetTemplateNames();
    bool HasTemplate(string templateName);
    string? GetTemplateDescription(string templateName);
}
```

---

## 四、修复步骤

### Step 1：修复 csproj — 添加 MSTest 包引用

在 `ParameterResolver.Tests.csproj` 中添加：
```xml
<PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.9.0" />
<PackageReference Include="MSTest.TestAdapter" Version="3.3.1" />
<PackageReference Include="MSTest.TestFramework" Version="3.3.1" />
```

### Step 2：重写不匹配的测试文件

需要重写的文件（约 10 个）：

| 文件 | 操作 |
|------|------|
| `Converter/TypeConverterTests.cs` | 重写：静态调用，`Convert(object, Type)` 返回元组 |
| `Converter/ValueConverterRegistryTests.cs` | 重写：改为 `ConverterRegistry`，适配 `IValueConverter` 单泛型 |
| `Formatting/DiagnosticFormatterTests.cs` | 重写：改为 `ValueFormatter` 静态调用 |
| `KeyMapping/KeyMappingEntryTests.cs` | 重写：改为 `MappingRule` |
| `KeyMapping/Matchers/PatternMatcherTests.cs` | 重写：改为 `KeyMappingMatcher`/`SingleValueMatcher` |
| `KeyMapping/Matchers/WildcardMatcherTests.cs` | 重写：改为 `DictionaryMatcher`/`DefaultValueMatcher` |
| `Parameters/ParameterDefinitionTests.cs` | 重写：改为 `ParameterHelper`/`DefaultParameterProcessor` |
| `Sources/ParameterSourceTests.cs` | 重写：改为 `VirtualStoreSource` |
| `Strategies/ParameterResolutionStrategyTests.cs` | 重写：改为 `StrategyResolver` |

### Step 3：修复 Mock 接口实现

需要修复的文件（3 个）：

| 文件 | 修复内容 |
|------|----------|
| `Validation/Rules/OutputTargetRuleTests.cs` | Mock 实现 `IControlTreeProvider` 全部 5 个成员 |
| `Validation/Rules/TemplateRefRuleTests.cs` | Mock 实现 `ITemplateRegistryProvider` 全部 4 个成员 |
| `Validation/Rules/FunctionRefRuleTests.cs` | Mock 实现 `IFunctionRegistryProvider` 全部 4 个成员 |

### Step 4：构建验证

```bash
dotnet build "d:\Project\Program\WorkFlowPrj\CentralTest\CSharpTest\Modules.Tests\ParameterResolver.Tests\ParameterResolver.Tests.csproj"
```

---

## 五、保留的测试文件（无需修改）

以下 15 个文件与真实 API 匹配，只需 MSTest 包引用修复后即可编译：

- `Validation/Rules/ControlRefRuleTests.cs`
- `Validation/Rules/FunctionRefRuleTests.cs`（仅 Mock 修复）
- `Validation/Rules/TemplateRefRuleTests.cs`（仅 Mock 修复）
- `Validation/Rules/OutputTargetRuleTests.cs`（仅 Mock 修复）
- `Validation/Rules/EventBindingFormatRuleTests.cs`
- `Validation/Rules/ParameterMappingFormatRuleTests.cs`
- `Validation/Rules/ControlExistenceRuleTests.cs`
- `Validation/Rules/RequiredFieldRuleTests.cs`
- `Validation/Rules/EnumValueRuleTests.cs`
- `Validation/Rules/EventValueTypeRuleTests.cs`
- `Validation/Rules/LayoutFormatRuleTests.cs`
- `Validation/Diagnostics/ValidationDiagnosticTests.cs`
- `Validation/Diagnostics/SourceLocationTests.cs`
- `Validation/Diagnostics/SeverityTests.cs`
- `Validation/Diagnostics/ValidationReportTests.cs`
- `Validation/ConfigValidationContextTests.cs`
- `Validation/ConfigValidatorFactoryTests.cs`
- `Validation/ParameterMappingValidatorTests.cs`
- `KeyMapping/KeyMappingConfigTests.cs`
- `Utilities/JsonPathResolverTests.cs`
- `Utilities/JsonElementExtensionsTests.cs`

---

## 六、测试文件清单总表

| # | 文件路径 | 状态 | 操作 |
|---|---------|------|------|
| 1 | `ParameterResolver.Tests.csproj` | ❌ | 添加 MSTest 包引用 |
| 2 | `Converter/TypeConverterTests.cs` | ❌ | 重写（静态 API） |
| 3 | `Converter/ValueConverterRegistryTests.cs` | ❌ | 重写（ConverterRegistry） |
| 4 | `Formatting/DiagnosticFormatterTests.cs` | ❌ | 重写（ValueFormatter） |
| 5 | `KeyMapping/KeyMappingEntryTests.cs` | ❌ | 重写（MappingRule） |
| 6 | `KeyMapping/Matchers/PatternMatcherTests.cs` | ❌ | 重写（真实 Matchers） |
| 7 | `KeyMapping/Matchers/WildcardMatcherTests.cs` | ❌ | 重写（真实 Matchers） |
| 8 | `Parameters/ParameterDefinitionTests.cs` | ❌ | 重写（真实 Parameters） |
| 9 | `Sources/ParameterSourceTests.cs` | ❌ | 重写（VirtualStoreSource） |
| 10 | `Strategies/ParameterResolutionStrategyTests.cs` | ❌ | 重写（StrategyResolver） |
| 11 | `Validation/Rules/OutputTargetRuleTests.cs` | ❌ | 修复 Mock |
| 12 | `Validation/Rules/TemplateRefRuleTests.cs` | ❌ | 修复 Mock |
| 13 | `Validation/Rules/FunctionRefRuleTests.cs` | ❌ | 修复 Mock |
| 14-28 | 其余 15 个文件 | ✅ | 无需修改 |
