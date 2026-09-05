# JsonMethod 集中迁移方案

> 目标：将所有 JSON 处理逻辑集中到 `Plate/JsonMethod`，构建基础通用框架，消除各层的重复实现。

## 架构总览

```
Plate/JsonMethod/                          ← 统一 JSON 处理框架
├── JsonMethod.csproj
├── Abstractions/                          ← 抽象接口
│   ├── IJsonProcessingOptions.cs
│   ├── IJsonProcessingResult.cs
│   └── IJsonProcessor.cs
├── Serialization/                         ← 序列化核心
│   ├── JsonSerializerFactory.cs           ← 集中管理所有序列化选项
│   ├── JsonSerializationService.cs        ← 统一序列化/反序列化入口
│   └── DynamicObjectConverter.cs          ← 动态对象转换器
├── Utilities/                             ← 工具类
│   ├── JsonPreprocessor.cs                ← JSON 预处理管线
│   ├── JsonMergeService.cs                ← JSON 深度合并
│   ├── JsonPathHelper.cs                  ← JSON 路径辅助
│   └── TemplateFormatter.cs               ← 模板格式化
├── Validation/                            ← 验证服务
│   └── JsonValidationService.cs           ← JSON 语法/结构验证
├── Diagnostics/                           ← 诊断工具
│   ├── JsonErrorDiagnostics.cs            ← JSON 错误信息增强
│   └── JsonSizeAnalyzer.cs                ← JSON 大小分析（待新增）
└── Extensions/                            ← 扩展方法（待新增）
    └── JsonExtensions.cs                  ← FromJson/ToJson/IsValidJson 等
```

## 第一阶段：核心迁移（已完成）

### 迁移内容

| 源 | 目标 | 状态 |
|----|------|------|
| `BasicMethod/JsonProcess/Serialization/` | `Plate/JsonMethod/Serialization/` | ✅ 已完成 |
| `BasicMethod/JsonProcess/Utilities/` | `Plate/JsonMethod/Utilities/` | ✅ 已完成 |
| `BasicMethod/JsonProcess/Validation/` | `Plate/JsonMethod/Validation/` | ✅ 已完成 |
| `BasicMethod/JsonProcess/Diagnostics/` | `Plate/JsonMethod/Diagnostics/` | ✅ 已完成 |
| `BasicMethod/JsonProcess/Abstractions/` | `Plate/JsonMethod/Abstractions/` | ✅ 已完成 |

### 删除的旧副本

| 文件 | 原因 |
|------|------|
| `BasicMethod/JsonProcess/*.cs` | 核心代码已迁移到 Plate/JsonMethod |
| `Share/UniversalMiddleware/Serialization/JsonSerializerFactory.cs` | 旧副本，已委托到 Plate/JsonMethod |
| `Share/UniversalMiddleware/Serialization/JsonPreprocessor.cs` | 旧副本，已委托到 Plate/JsonMethod |

### 更新的引用方

| 文件 | 更新内容 |
|------|---------|
| `BasicMethod/BasicMethod.csproj` | 移除 JsonProcess 引用，改为引用 Plate/JsonMethod |
| `Plate/Plate.csproj` | 添加 JsonMethod 子项目引用 |
| `Operation/BackEndDriven/Diagnostics/LogBase.cs` | `Modules.JsonProcess.Serialization` → `Plate.JsonMethod.Serialization` |
| `Share/UniversalMiddleware/Serialization.cs` | 通过 `JP = Plate.JsonMethod` 别名委托 |
| `Share/UniversalMiddleware/Infrastructure.cs` | 同上 |
| `Share/UniversalMiddleware/Serialization/JsonConverterRegistrar.cs` | 同上 |
| `Share/EventBindingOrchestrator/Contexts/EventBindingDiagnosticContext.cs` | 同上 |

## 第二阶段：扩展与简化（待实施）

### 2.1 在 Plate/JsonMethod 中新增通用能力

#### 2.1.1 Extensions/JsonExtensions.cs

从 `Share/UniversalMiddleware/Infrastructure.cs` 提取 JSON 扩展方法：

```csharp
namespace Plate.JsonMethod.Extensions
{
    public static class JsonExtensions
    {
        // 字符串扩展
        public static T? FromJson<T>(this string json, ...);
        public static bool IsValidJson(this string json);
        public static string PrettyPrintJson(this string json);
        public static string MinifyJson(this string json);

        // 对象扩展
        public static string ToJson<T>(this T obj, bool writeIndented = true);
        public static string ToSafeJson<T>(this T obj);
        public static JsonNode? ToJsonNode<T>(this T obj);
        public static T? DeepCloneViaJson<T>(this T obj);
    }
}
```

#### 2.1.2 Diagnostics/JsonSizeAnalyzer.cs

从 `Share/UniversalMiddleware/Infrastructure.cs` 提取 JSON 大小分析工具：

```csharp
namespace Plate.JsonMethod.Diagnostics
{
    public static class JsonSizeAnalyzer
    {
        public static JsonSizeInfo AnalyzeJsonSize(string json);
        // 包含 JsonSizeInfo、DiagnosticResult 等类型
    }
}
```

### 2.2 删除旧副本

| 文件 | 行数 | 原因 |
|------|------|------|
| `Share/PlaceholderFramework/Core/JsonService.cs` | 1098 行 | 完整旧版副本，无外部引用（`using Common.PlaceholderFramework.Core.Json` 搜索结果为 0） |

### 2.3 简化外部引用方

#### 2.3.1 Share/UniversalMiddleware/Serialization.cs

当前状态：已通过 `JP = Plate.JsonMethod` 别名委托，但仍有大量本地代码。

简化方案：
- **保留**：`ModelValidator`（依赖 FluentValidation + DataAnnotation，无法迁移）
- **保留**：`UnifiedJsonSerializer` 作为适配器（实现 `IJsonSerializer` 接口）
- **简化**：`JsonPathEngine` — 去掉本地回退逻辑，完全委托到 `JP.Utilities.JsonPathHelper`
- **简化**：`JsonMerger` — 去掉包装类，直接委托到 `JP.Utilities.JsonMergeService`
- **简化**：`JsonPrettyPrinter` — 去掉本地 `Minify` 实现，完全委托到 CommonAssembly

#### 2.3.2 Share/UniversalMiddleware/Infrastructure.cs

- **移除**：`StringJsonExtensions` 中的 JSON 方法 → 改为委托到 `Plate.JsonMethod.Extensions`
- **移除**：`ObjectJsonExtensions` 中的 JSON 方法 → 改为委托到 `Plate.JsonMethod.Extensions`
- **移除**：`JsonDiagnostics.AnalyzeJsonSize` → 改为委托到 `Plate.JsonMethod.Diagnostics.JsonSizeAnalyzer`

### 2.4 接口定义放在 TConfig

接口（`IJsonSerializer`、`IJsonPathEngine` 等）放在 `Plate/TConfig` 中，`Plate/JsonMethod` 实现这些接口。

## 影响范围

### 第一阶段已影响

| 项目 | 影响 |
|------|------|
| `BasicMethod` | 移除 JsonProcess 引用，新增 JsonMethod 引用 |
| `Plate` | 新增 JsonMethod 子项目 |
| `Operation/BackEndDriven` | 更新命名空间引用 |
| `Share/UniversalMiddleware` | 更新命名空间引用 |
| `Share/EventBindingOrchestrator` | 更新命名空间引用 |

### 第二阶段将影响

| 项目 | 影响 |
|------|------|
| `Share/PlaceholderFramework` | 删除 JsonService.cs（无外部引用） |
| `Share/UniversalMiddleware` | 简化 Serialization.cs 和 Infrastructure.cs |
| `Plate/JsonMethod` | 新增 Extensions 和 Diagnostics 模块 |
| `Plate/TConfig` | 新增 JSON 相关接口定义 |

## 验证方法

```bash
# 构建验证
dotnet build CsharpPrj/MainProj.sln

# 运行时验证 DiagnosticSnapshot 日志格式
# 观察换行符不再被转义
```
