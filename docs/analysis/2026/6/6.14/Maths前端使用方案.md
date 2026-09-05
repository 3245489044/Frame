# 通用数学函数计算前端使用方案

> 基于反射动态注册的 Maths.cs 前端使用方案
>
> 专注于数学函数计算领域，但能通用适配任何数学函数库

---

## 目录

1. [概述](#一概述)
2. [总体架构](#二总体架构)
3. [数学函数注册器](#三数学函数注册器)
4. [数学函数浏览器（核心 UI）](#四数学函数浏览器核心-ui)
5. [数学工作流](#五数学工作流)
6. [数学脚本](#六数学脚本)
7. [实施步骤](#七实施步骤)
8. [扩展指南](#八扩展指南)
9. [附录：Maths.cs 函数注册清单](#九附录mathscs-函数注册清单)

---

## 一、概述

### 1.1 背景

`MathematicalAlgorithms.Maths` 是一个静态门面类（Facade），包含 **16 个嵌套静态类**，约 **80+ 个公开静态方法**，覆盖激活函数、距离计算、聚类算法、统计分析、张量工厂、向量运算、矩阵运算等 20 个功能域。

当前 `MathLibrary` 只手动注册了 4 个基础运算（Add/Subtract/Multiply/Divide），与 Maths.cs 的丰富功能完全不匹配。

### 1.2 目标

构建一个**专注于数学函数计算领域**的通用前端使用方案，满足：

- **通用性**：任何数学函数库只需在 JSON 配置中添加条目即可自动注册
- **自动化**：利用反射自动提取方法元数据，无需手动编写注册代码
- **可视化**：提供完整的 UI 交互，支持函数浏览、参数输入、结果展示
- **可编排**：支持多步骤数学工作流，步骤间变量传递
- **可脚本化**：支持批量测试、对比实验、自动化演示

### 1.3 核心抽象

所有数学函数共享相同的交互模式：

```
f(x) → 输入参数 → 数学运算 → 输出结果
```

即：**选择函数 → 输入参数 → 执行 → 查看结果**

---

## 二、总体架构

```
┌─────────────────────────────────────────────────────────────────────┐
│                    通用数学函数计算前端方案                            │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  ┌─────────────────────────────────────────────────────────────┐    │
│  │ ① 数学函数注册器（MathFunctionRegistrar）                    │    │
│  │    ┌─────────────────────────────────────────────────────┐  │    │
│  │    │ 通过 JSON 配置描述"哪个数学类的哪些方法要注册"         │  │    │
│  │    │ math-register-config.json → AutoRegisterService      │  │    │
│  │    │ → 反射提取 → 命名 → 注册到 FunctionStorageRegistry   │  │    │
│  │    │ 命名规则统一：Domain_MethodName                       │  │    │
│  │    └─────────────────────────────────────────────────────┘  │    │
│  └─────────────────────────────────────────────────────────────┘    │
│                           ↓                                         │
│  ┌─────────────────────────────────────────────────────────────┐    │
│  │ ② 数学函数浏览器（MathFunctionBrowser）                      │    │
│  │    ┌─────────────────────────────────────────────────────┐  │    │
│  │    │ 一个通用的数学函数 UI，自动展示所有已注册的数学函数     │  │    │
│  │    │ 按数学域分组（激活/距离/聚类/统计/张量...）            │  │    │
│  │    │ 自动读取函数元数据生成参数输入                        │  │    │
│  │    │ 支持：数值输入、数组输入、矩阵输入                    │  │    │
│  │    └─────────────────────────────────────────────────────┘  │    │
│  └─────────────────────────────────────────────────────────────┘    │
│                           ↓                                         │
│  ┌─────────────────────────────────────────────────────────────┐    │
│  │ ③ 数学工作流（MathWorkflows）                                │    │
│  │    ┌─────────────────────────────────────────────────────┐  │    │
│  │    │ 多步骤数学计算流水线                                  │  │    │
│  │    │ 步骤间变量传递、复合运算                              │  │    │
│  │    │ 示例：归一化→Softmax→采样 流水线                     │  │    │
│  │    └─────────────────────────────────────────────────────┘  │    │
│  └─────────────────────────────────────────────────────────────┘    │
│                           ↓                                         │
│  ┌─────────────────────────────────────────────────────────────┐    │
│  │ ④ 数学脚本（MathScripts）                                    │    │
│  │    ┌─────────────────────────────────────────────────────┐  │    │
│  │    │ 批量测试、对比实验、自动化演示                        │  │    │
│  │    │ 支持：多函数对比、参数扫描、结果汇总                  │  │    │
│  │    └─────────────────────────────────────────────────────┘  │    │
│  └─────────────────────────────────────────────────────────────┘    │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

---

## 三、数学函数注册器

### 3.1 注册配置

**文件位置**：`Operation/FunctionStorage/Registrations/math-register-config.json`

```json
{
  "description": "数学函数注册配置 - 在此添加更多数学类即可自动注册",
  "registrations": [
    {
      "source": { "type": "assembly", "path": "Mathematics.dll" },
      "className": "MathematicalAlgorithms.Maths",
      "naming": {
        "convention": "NestedClass_MethodName",
        "separator": "_"
      },
      "description": { "source": "xmlDoc" },
      "methodFilters": {
        "includeStaticOnly": true,
        "excludeByReturnType": ["Void"],
        "excludeMethodNames": [
          "GetType", "ToString", "Equals", "GetHashCode"
        ]
      }
    }
  ]
}
```

**配置字段说明**：

| 字段 | 说明 | 可选值 |
|------|------|--------|
| `source.type` | 源类型 | `assembly` |
| `source.path` | 程序集路径 | DLL 文件名 |
| `className` | 要注册的完整类名 | 包含命名空间 |
| `naming.convention` | 命名规则 | `NestedClass_MethodName` / `NestedClass.MethodName` / `MethodName` |
| `naming.separator` | 分隔符 | `_` / `.` |
| `description.source` | 描述来源 | `xmlDoc` / `attribute` / `none` |
| `methodFilters.includeStaticOnly` | 仅静态方法 | `true` / `false` |
| `methodFilters.excludeByReturnType` | 排除的返回类型 | `["Void"]` |
| `methodFilters.excludeMethodNames` | 排除的方法名 | 如 `["GetType"]` |

### 3.2 共享工具类

**文件位置**：`Operation/FunctionStorage/Registrations/MathUtils.cs`

`ParseMathParameters` 和 `FormatMathResult` 被 `MathFunctionRegistrar` 和 `MathBrowserFunctions` 共同使用，提取为共享工具类：

```csharp
using System.Text;
using System.Text.Json;
using Share.Registry;

namespace FunctionStorage.Registrations;

/// <summary>
/// 数学函数工具类
/// 提供参数解析和结果格式化等共享功能
/// </summary>
public static class MathUtils
{
    /// <summary>解析参数字符串为对象数组</summary>
    public static object?[] ParseMathParameters(string paramStr,
        IReadOnlyList<IParameterDescriptor>? paramInfos)
    {
        // 尝试 JSON 数组解析
        try { return JsonSerializer.Deserialize<object?[]>(paramStr) ?? []; }
        catch { }

        // 逗号分隔解析
        var parts = paramStr.Split(',', StringSplitOptions.TrimEntries);
        var result = new object?[parts.Length];
        for (int i = 0; i < parts.Length && paramInfos != null && i < paramInfos.Count; i++)
        {
            result[i] = ConvertToType(parts[i], paramInfos[i].Type);
        }
        return result;
    }

    /// <summary>字符串转目标类型</summary>
    public static object? ConvertToType(string value, Type targetType)
    {
        if (targetType == typeof(double)) return double.Parse(value);
        if (targetType == typeof(int)) return int.Parse(value);
        if (targetType == typeof(bool)) return bool.Parse(value);
        if (targetType == typeof(string)) return value;
        if (targetType == typeof(long)) return long.Parse(value);
        if (targetType.IsArray && value.StartsWith('['))
            return JsonSerializer.Deserialize(value, targetType);
        if (targetType.IsEnum)
            return Enum.Parse(targetType, value, ignoreCase: true);
        return Convert.ChangeType(value, targetType);
    }

    /// <summary>格式化数学函数执行结果</summary>
    public static string FormatMathResult(object? value)
    {
        if (value == null) return "(null)";
        if (value is double d) return d.ToString("G6");
        if (value is double[] arr)
            return $"[{string.Join(", ", arr.Select(x => x.ToString("G6")))}]";
        if (value is double[,] mat)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < mat.GetLength(0); i++)
            {
                sb.AppendLine(string.Join("\t",
                    Enumerable.Range(0, mat.GetLength(1)).Select(j => mat[i, j].ToString("G6"))));
            }
            return sb.ToString();
        }
        return value.ToString() ?? "(null)";
    }
}
```

### 3.3 注册器实现

**文件位置**：`Operation/FunctionStorage/Registrations/MathFunctionRegistrar.cs`

```csharp
using System.Reflection;
using System.Text.Json;
using Share.Registry.AutoRegister;
using static FunctionStorage.Registrations.MathUtils;

namespace FunctionStorage.Registrations;

/// <summary>
/// 数学函数注册器
/// 从 math-register-config.json 读取配置，自动注册所有数学函数
/// 任何数学类只需在配置中添加条目即可
/// </summary>
public static class MathFunctionRegistrar
{
    private static bool _registered;

    public static void RegisterAll()
    {
        if (_registered) return;
        _registered = true;

        var configPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Registrations/math-register-config.json");

        if (!File.Exists(configPath))
        {
            Console.WriteLine("[MathFunctionRegistrar] 配置文件不存在，跳过注册");
            return;
        }

        var json = File.ReadAllText(configPath);
        var config = JsonSerializer.Deserialize<AutoRegisterConfig>(json);
        var service = new AutoRegisterService();

        foreach (var entry in config!.Registrations)
        {
            var assembly = Assembly.LoadFrom(entry.Source.Path);
            var result = service.ProcessAssembly(assembly, entry);

            foreach (var item in result.RegisteredItems)
            {
                if (item.Status != RegisteredItemStatus.Success || item.Executor == null)
                    continue;

                // ★ 包装委托：item.Executor 是 Func<object[], Task<object>>，
                //   直接注册会导致描述符参数显示为 (object[] args) 而非实际数学参数。
                //   因此包装为 Func<string, string>，利用 item.Parameters 保留准确参数信息。
                var funcName = item.RegisteredName;
                var funcParams = item.Parameters;
                FunctionStorageRegistry.Register(
                    funcName,
                    (Func<string, string>)(paramStr =>
                    {
                        var parameters = ParseMathParameters(paramStr, funcParams);
                        var result = item.Executor!(parameters).GetAwaiter().GetResult();
                        return FormatMathResult(result);
                    }),
                    item.Description);
            }

            Console.WriteLine($"[MathFunctionRegistrar] 已注册 {result.RegisteredItems.Count} 个数学函数");
        }
    }
}
```

### 3.4 注册命名规则

以 Maths.cs 为例，注册后的函数命名规则为 `{嵌套类名}_{方法名}`：

| 嵌套类 | 方法 | 注册名 | 参数 |
|--------|------|--------|------|
| Activation | ReLU | `Activation_ReLU` | double |
| Activation | GELU | `Activation_GELU` | double |
| Activation | Sigmoid | `Activation_Sigmoid` | double |
| Activation | Swish | `Activation_Swish` | double |
| Activation | Tanh | `Activation_Tanh` | double |
| Activation | Softplus | `Activation_Softplus` | double |
| Activation | PReLU | `Activation_PReLU` | double, double |
| Activation | ELU | `Activation_ELU` | double, double |
| Activation | LeakyReLU | `Activation_LeakyReLU` | double, double |
| Activation | Apply | `Activation_Apply` | double, ActivationType |
| Distance | Euclidean | `Distance_Euclidean` | double[], double[] |
| Distance | Manhattan | `Distance_Manhattan` | double[], double[] |
| Distance | CosineSimilarity | `Distance_CosineSimilarity` | double[], double[] |
| Distance | HammingDistance | `Distance_HammingDistance` | string, string |
| Distance | Chebyshev | `Distance_Chebyshev` | double[], double[] |
| Distance | Minkowski | `Distance_Minkowski` | double[], double[], double |
| Distance | JaccardDistance | `Distance_JaccardDistance` | HashSet\<int\>, HashSet\<int\> |
| Clustering | KMeans | `Clustering_KMeans` | double[][], int |
| Clustering | DBSCAN | `Clustering_DBSCAN` | double[][], double, int |
| Clustering | Hierarchical | `Clustering_Hierarchical` | double[][] |
| Statistics | Entropy | `Statistics_Entropy` | IEnumerable\<double\> |
| Statistics | Variance | `Statistics_Variance` | IEnumerable\<double\> |
| Statistics | SilhouetteScore | `Statistics_SilhouetteScore` | List\<double[]\>, int[], DistanceMetricType |
| Statistics | Mean | `Statistics_Mean` | double[] |
| Statistics | Median | `Statistics_Median` | double[] |
| Statistics | Mode | `Statistics_Mode` | double[] |
| Statistics | StandardDeviation | `Statistics_StandardDeviation` | IEnumerable\<double\>, bool |
| Tensor | Zeros | `Tensor_Zeros` | int, int |
| Tensor | Identity | `Tensor_Identity` | int |
| Tensor | Random | `Tensor_Random` | int, int, Random? |
| Tensor | Ones | `Tensor_Ones` | int, int |
| Tensor | FromArray | `Tensor_FromArray` | double[] |
| Unary | Apply | `Unary_Apply` | double[], Func\<double,double\> |
| Unary | Sqrt | `Unary_Sqrt` | double[] |
| Unary | Abs | `Unary_Abs` | double[] |
| Unary | Negate | `Unary_Negate` | double[] |
| Normalization | LayerNorm | `Normalization_LayerNorm` | double[,], double[], double[], double |
| Normalization | MinMax | `Normalization_MinMax` | double[] |
| Normalization | ZScore | `Normalization_ZScore` | double[] |
| Reduce | Mean | `Reduce_Mean` | double[] |
| Reduce | Sum | `Reduce_Sum` | double[] |
| Reduce | Max | `Reduce_Max` | double[] |
| Reduce | Min | `Reduce_Min` | double[] |
| Sampling | Softmax | `Sampling_Softmax` | double[], double |
| Sampling | SampleToken | `Sampling_SampleToken` | double[], double, int, double, double, Random |
| Softmax | Compute | `Softmax_Compute` | double[] |
| Softmax | StableSoftmax | `Softmax_StableSoftmax` | double[] |
| Broadcast | Add | `Broadcast_Add` | double[,], double[,] |
| Broadcast | Subtract | `Broadcast_Subtract` | double[,], double[,] |
| Broadcast | Multiply | `Broadcast_Multiply` | double[,], double[,] |
| Broadcast | Divide | `Broadcast_Divide` | double[,], double[,] |
| Vector | DotProduct | `Vector_DotProduct` | double[], double[] |
| Vector | Normalize | `Vector_Normalize` | double[] |
| Vector | CrossProduct | `Vector_CrossProduct` | double[], double[] |
| Vector | AngleBetween | `Vector_AngleBetween` | double[], double[] |
| Vector | EuclideanDistance | `Vector_EuclideanDistance` | double[], double[] |
| Similarity | PearsonCorrelation | `Similarity_PearsonCorrelation` | double[], double[] |
| Similarity | CosineSimilarity | `Similarity_CosineSimilarity` | double[], double[] |
| Similarity | JaccardSimilarity | `Similarity_JaccardSimilarity` | HashSet\<int\>, HashSet\<int\> |
| Matrix | ToMatrix | `Matrix_ToMatrix` | List\<double[]\> |
| Matrix | Transpose | `Matrix_Transpose` | double[,] |
| Matrix | Multiply | `Matrix_Multiply` | double[,], double[,] |
| RotaryPositionEncoding | PrecomputeFrequencies | `RotaryPositionEncoding_PrecomputeFrequencies` | int, int, double |
| Utility | SafeLog | `Utility_SafeLog` | double, double |
| Utility | StandardDeviation | `Utility_StandardDeviation` | IEnumerable\<double\>, bool |
| Memory | HasSufficient | `Memory_HasSufficient` | long, object? |
| Transform | RunAsync | `Transform_RunAsync` | string, TInput, TransformConfig?, ... |
| Dimension | Validate | `Dimension_Validate` | int, int |

---

## 四、数学函数浏览器（核心 UI）

### 4.1 UI 布局

**文件位置**：`FileDriven/DrivenConfig/JsonConfig/MathFunctionBrowser.json`

**布局示意**：

```
┌──────────────────────────────────────────────────────────────────┐
│  🧮 数学函数计算器                                    [状态栏]  │
├──────────────────────────────────────────────────────────────────┤
│  ┌────────────────────────────────────────────────────────────┐  │
│  │  📂 数学域    │  🔢 函数    │  📝 参数输入    │  📊 结果   │  │
│  │               │             │                 │            │  │
│  │  ● 激活函数   │  ReLU       │  x: [________]  │  结果:     │  │
│  │  ○ 距离计算   │  GELU       │                 │  0.5000    │  │
│  │  ○ 相似度     │  Sigmoid    │                 │            │  │
│  │  ○ 聚类算法   │  Swish      │                 │  ───────── │  │
│  │  ○ 统计分析   │  Tanh       │                 │  历史:     │  │
│  │  ○ 张量工厂   │  Softplus   │                 │  ReLU(0.5) │  │
│  │  ○ 一元运算   │  PReLU      │                 │  = 0.5     │  │
│  │  ○ 归一化     │  ELU        │                 │            │  │
│  │  ○ 归约       │  LeakyReLU  │                 │            │  │
│  │  ○ 采样       │             │                 │            │  │
│  │  ○ 向量运算   │             │                 │            │  │
│  │  ○ 矩阵运算   │             │                 │            │  │
│  │  ○ 广播操作   │             │                 │            │  │
│  │               │             │                 │            │  │
│  │  [🔍 搜索...] │  [▶ 执行]  │                 │            │  │
│  └───────────────┴─────────────┴─────────────────┴────────────┘  │
│  共 85 个数学函数已注册                             就绪        │
└──────────────────────────────────────────────────────────────────┘
```

### 4.2 辅助函数

**文件位置**：`Operation/FunctionStorage/Registrations/MathBrowserFunctions.cs`

```csharp
using System.Text;
using Share.Registry;
using static FunctionStorage.Registrations.MathUtils;

namespace FunctionStorage.Registrations;

/// <summary>
/// 数学函数浏览器的辅助函数
/// 所有函数都是通用的，不依赖任何特定数学类
/// </summary>
public static class MathBrowserFunctions
{
    public static void RegisterAll()
    {
        // 1. 获取所有数学域（按 _ 分隔的前缀分组）
        FunctionStorageRegistry.Register("Math_GetDomains",
            (Func<string>)(() =>
            {
                var names = FunctionStorageRegistry.GetRegisteredNames();
                var domains = names
                    .Select(n => n.Contains('_') ? n.Split('_')[0] : "其他")
                    .Distinct().OrderBy(d => d);
                return string.Join(",", domains);
            }), "获取所有数学域列表");

        // 2. 获取指定域下的所有函数
        FunctionStorageRegistry.Register("Math_GetFunctionsInDomain",
            (Func<string, string>)(domain =>
            {
                var names = FunctionStorageRegistry.GetRegisteredNames()
                    .Where(n => n.StartsWith(domain + "_") || n == domain)
                    .OrderBy(n => n);
                return string.Join(",", names);
            }), "获取指定数学域下的所有函数");

        // 3. 获取函数详情（含参数信息）
        FunctionStorageRegistry.Register("Math_GetFunctionDetail",
            (Func<string, string>)(funcName =>
            {
                if (!FunctionStorageRegistry.TryGetDescriptor(funcName, out var desc))
                    return "函数未找到";

                var sb = new StringBuilder();
                sb.AppendLine($"函数: {desc.Name}");
                sb.AppendLine($"描述: {desc.Description}");
                sb.AppendLine($"返回: {desc.ReturnType.Name}");
                sb.AppendLine($"参数 ({desc.Parameters.Count} 个):");
                foreach (var p in desc.Parameters)
                {
                    var opt = p.IsOptional ? $" (可选, 默认={p.DefaultValue})" : "";
                    sb.AppendLine($"  {p.Name}: {p.Type.Name}{opt}");
                }
                return sb.ToString();
            }), "获取数学函数的详细信息");

        // 4. 执行数学函数（自动类型转换）
        FunctionStorageRegistry.Register("Math_Execute",
            (Func<string, string, string>)((funcName, paramStr) =>
            {
                if (!FunctionStorageRegistry.TryGetDescriptor(funcName, out var desc))
                    return $"错误: 函数 '{funcName}' 未找到";

                var parameters = ParseMathParameters(paramStr, desc.Parameters);
                var result = FunctionStorageRegistry.ExecuteAsync(funcName, (object)parameters)
                    .GetAwaiter().GetResult();
                return FormatMathResult(result.Value);
            }), "执行数学函数并返回格式化结果");

        // 5. 搜索数学函数
        FunctionStorageRegistry.Register("Math_Search",
            (Func<string, string>)(keyword =>
            {
                var names = FunctionStorageRegistry.GetRegisteredNames()
                    .Where(n => n.Contains(keyword, StringComparison.OrdinalIgnoreCase));
                return string.Join(",", names);
            }), "按关键字搜索数学函数");

        // 6. 获取统计信息
        FunctionStorageRegistry.Register("Math_GetStats",
            (Func<string>)(() =>
            {
                var names = FunctionStorageRegistry.GetRegisteredNames().ToList();
                var domains = names
                    .Select(n => n.Contains('_') ? n.Split('_')[0] : "其他")
                    .Distinct().Count();
                return $"共 {names.Count} 个数学函数，{domains} 个数学域";
            }), "获取数学函数统计信息");
    }
}
```

### 4.3 UI 配置

**文件位置**：`FileDriven/DrivenConfig/JsonConfig/MathFunctionBrowser.json`

```json
{
  "description": "通用数学函数计算器 - 自动展示所有已注册数学函数",
  "form": {
    "title": "数学函数计算器",
    "width": 1100,
    "height": 750
  },
  "views": [{
    "name": "MainView",
    "type": "Panel",
    "controls": [
      {
        "type": "label", "name": "titleLabel",
        "text": "  数学函数计算器",
        "layout": { "left": 0.02, "top": 0.01, "width": 0.5, "height": 0.04 }
      },
      {
        "type": "panel", "name": "searchPanel",
        "layout": { "left": 0.02, "top": 0.06, "width": 0.96, "height": 0.05 },
        "controls": [
          {
            "type": "label", "name": "searchLabel", "text": "搜索函数:",
            "layout": { "left": 0.01, "top": 0.15, "width": 0.1, "height": 0.6 }
          },
          {
            "type": "textbox", "name": "searchTextBox",
            "placeholder": "输入函数名搜索（如 ReLU、Euclidean、KMeans）...",
            "events": { "textChanged": "Math_Search" },
            "layout": { "left": 0.12, "top": 0.1, "width": 0.4, "height": 0.8 }
          },
          {
            "type": "label", "name": "statsLabel", "text": "加载中...",
            "foreColor": "#6B7280",
            "layout": { "left": 0.55, "top": 0.15, "width": 0.4, "height": 0.6 }
          }
        ]
      },
      {
        "type": "panel", "name": "mainPanel",
        "layout": { "left": 0.02, "top": 0.13, "width": 0.96, "height": 0.83 },
        "controls": [
          {
            "type": "groupbox", "name": "domainGroup", "text": "数学域",
            "layout": { "left": 0.0, "top": 0.0, "width": 0.18, "height": 0.85 },
            "controls": [
              {
                "type": "listview", "name": "domainListView",
                "view": "List", "fullRowSelect": true,
                "events": { "selectedIndexChanged": "Math_GetFunctionsInDomain" },
                "layout": { "left": 0.05, "top": 0.05, "width": 0.9, "height": 0.9 }
              }
            ]
          },
          {
            "type": "groupbox", "name": "funcGroup", "text": "函数",
            "layout": { "left": 0.19, "top": 0.0, "width": 0.22, "height": 0.85 },
            "controls": [
              {
                "type": "listview", "name": "funcListView",
                "view": "Details", "fullRowSelect": true,
                "columns": [
                  { "text": "函数名", "width": 120 },
                  { "text": "返回", "width": 60 }
                ],
                "events": { "selectedIndexChanged": "Math_GetFunctionDetail" },
                "layout": { "left": 0.05, "top": 0.05, "width": 0.9, "height": 0.9 }
              }
            ]
          },
          {
            "type": "groupbox", "name": "detailGroup", "text": "详情与参数",
            "layout": { "left": 0.42, "top": 0.0, "width": 0.33, "height": 0.85 },
            "controls": [
              {
                "type": "textbox", "name": "detailTextBox",
                "multiline": true, "readOnly": true,
                "font": { "name": "Consolas", "size": 9 },
                "layout": { "left": 0.05, "top": 0.03, "width": 0.9, "height": 0.45 }
              },
              {
                "type": "label", "name": "paramLabel", "text": "参数输入:",
                "layout": { "left": 0.05, "top": 0.50, "width": 0.3, "height": 0.05 }
              },
              {
                "type": "textbox", "name": "paramInput",
                "multiline": true,
                "placeholder": "输入参数值，多个参数用逗号分隔",
                "font": { "name": "Consolas", "size": 10 },
                "layout": { "left": 0.05, "top": 0.56, "width": 0.9, "height": 0.4 }
              }
            ]
          },
          {
            "type": "groupbox", "name": "resultGroup", "text": "结果",
            "layout": { "left": 0.76, "top": 0.0, "width": 0.24, "height": 0.85 },
            "controls": [
              {
                "type": "textbox", "name": "resultTextBox",
                "multiline": true, "readOnly": true,
                "scrollBars": "Both",
                "font": { "name": "Consolas", "size": 11, "bold": true },
                "layout": { "left": 0.05, "top": 0.03, "width": 0.9, "height": 0.55 }
              },
              {
                "type": "label", "name": "historyLabel", "text": "历史:",
                "layout": { "left": 0.05, "top": 0.60, "width": 0.3, "height": 0.05 }
              },
              {
                "type": "listview", "name": "historyListView",
                "view": "Details", "fullRowSelect": true,
                "columns": [
                  { "text": "函数", "width": 80 },
                  { "text": "结果", "width": 100 }
                ],
                "layout": { "left": 0.05, "top": 0.66, "width": 0.9, "height": 0.3 }
              }
            ]
          },
          {
            "type": "panel", "name": "actionBar",
            "layout": { "left": 0.0, "top": 0.87, "width": 1.0, "height": 0.12 },
            "controls": [
              {
                "type": "button", "name": "execButton",
                "text": "执行计算",
                "events": { "click": "Math_Execute" },
                "layout": { "left": 0.42, "top": 0.1, "width": 0.16, "height": 0.7 }
              },
              {
                "type": "button", "name": "clearButton",
                "text": "清空",
                "events": { "click": "ClearOutput" },
                "layout": { "left": 0.60, "top": 0.1, "width": 0.1, "height": 0.7 }
              },
              {
                "type": "button", "name": "copyButton",
                "text": "复制",
                "events": { "click": "CopyToClipboard" },
                "layout": { "left": 0.72, "top": 0.1, "width": 0.1, "height": 0.7 }
              }
            ]
          }
        ]
      },
      {
        "type": "label", "name": "statusLabel",
        "text": "就绪 - 选择数学域和函数，输入参数后点击执行",
        "layout": { "left": 0.02, "top": 0.97, "width": 0.96, "height": 0.03 }
      }
    ]
  }]
}
```

### 4.4 用户交互流程

```
1. 启动 → dotnet run --project FileDriven/FileDriven.Launcher -- --config MathFunctionBrowser.json --show

2. 左侧点击数学域（如 "Activation"）
   ↓
3. 中间函数列表自动更新为该域下的所有函数
   ↓
4. 点击某个函数（如 Activation_ReLU）
   ↓
5. 详情面板显示函数签名、描述、参数信息
   ↓
6. 在参数输入框输入参数值（如 "0.5"）
   ↓
7. 点击 "执行计算" 按钮
   ↓
8. 结果面板显示执行结果
   ↓
9. 历史记录自动保存本次执行
```

---

## 五、数学工作流

### 5.1 工作流列表

**文件位置**：`FileDriven/DrivenConfig/WorkFlow/MathWorkflows/`

| 工作流文件 | 工作流名 | 步骤 | 说明 |
|-----------|---------|------|------|
| `ActivationCompare.json` | `ActivationCompareWorkflow` | ReLU → Sigmoid → GELU → Tanh | 激活函数对比 |
| `DistanceCompare.json` | `DistanceCompareWorkflow` | Euclidean → Manhattan → Cosine | 距离度量对比 |
| `ClusteringPipeline.json` | `ClusteringPipelineWorkflow` | KMeans → SilhouetteScore | 聚类+评估 |
| `FeaturePipeline.json` | `FeaturePipelineWorkflow` | LayerNorm → Softmax → SampleToken | 特征工程流水线 |
| `TensorPipeline.json` | `TensorPipelineWorkflow` | Zeros → Broadcast_Add → Reduce_Mean | 张量运算流水线 |
| `StatisticsPipeline.json` | `StatisticsPipelineWorkflow` | Entropy → Variance → StdDev | 统计分析流水线 |

### 5.2 工作流示例

**聚类分析流水线** (`ClusteringPipeline.json`)：

```json
{
  "workflowName": "ClusteringPipelineWorkflow",
  "displayName": "聚类分析流水线",
  "defaultInput": {
    "data": "[[1,2],[1.5,1.8],[5,8],[8,8],[1,0.6],[9,11]]",
    "k": 2,
    "epsilon": 1.5,
    "minPoints": 2
  },
  "steps": [
    { "stepName": "K-Means聚类", "funcName": "Clustering_KMeans",
      "inputKeys": ["data","k"], "outputKey": "kmeansResult",
      "parameters": ["${data}", "${k}"] },
    { "stepName": "DBSCAN聚类", "funcName": "Clustering_DBSCAN",
      "inputKeys": ["data","epsilon","minPoints"], "outputKey": "dbscanResult",
      "parameters": ["${data}", "${epsilon}", "${minPoints}"] },
    { "stepName": "层次聚类", "funcName": "Clustering_Hierarchical",
      "inputKeys": ["data"], "outputKey": "hierarchicalResult",
      "parameters": ["${data}"] }
  ],
  "outputMapping": {
    "kmeansResult": "kmeansResult",
    "dbscanResult": "dbscanResult",
    "hierarchicalResult": "hierarchicalResult"
  }
}
```

**特征工程流水线** (`FeaturePipeline.json`)：

```json
{
  "workflowName": "FeaturePipelineWorkflow",
  "displayName": "特征工程流水线",
  "defaultInput": {
    "data": "[[1,2,3],[4,5,6],[7,8,9]]",
    "gamma": "[1,1,1]",
    "beta": "[0,0,0]",
    "temperature": 1.0
  },
  "steps": [
    { "stepName": "LayerNorm", "funcName": "Normalization_LayerNorm",
      "inputKeys": ["data","gamma","beta"], "outputKey": "normResult",
      "parameters": ["${data}", "${gamma}", "${beta}"] },
    { "stepName": "Softmax", "funcName": "Softmax_Compute",
      "inputKeys": ["normResult"], "outputKey": "softmaxResult",
      "parameters": ["${normResult}"] },
    { "stepName": "采样", "funcName": "Sampling_SampleToken",
      "inputKeys": ["softmaxResult","temperature"], "outputKey": "sampleResult",
      "parameters": ["${softmaxResult}", "${temperature}", "1", "0.0", "1.0", null] }
  ],
  "outputMapping": {
    "normResult": "normResult",
    "softmaxResult": "softmaxResult",
    "sampleResult": "sampleResult"
  }
}
```

---

## 六、数学脚本

### 6.1 脚本列表

**文件位置**：`FileDriven/DrivenConfig/Scripts/MathScripts/`

| 脚本文件 | 说明 |
|---------|------|
| `activation_compare_test.json` | 激活函数对比测试 |
| `distance_compare_test.json` | 距离计算对比测试 |
| `clustering_demo.json` | 聚类算法演示 |
| `math_quick_test.txt` | 快速测试命令集 |

### 6.2 脚本示例

**激活函数对比测试** (`activation_compare_test.json`)：

```json
{
  "description": "激活函数对比测试",
  "script": "activation_compare_test",
  "commands": [
    { "template": "echo-section", "title": "激活函数对比测试" },
    { "template": "load-config-show", "configPath": "MathFunctionBrowser.json" },
    { "template": "echo-section", "title": "ReLU(0.5)" },
    { "action": "set", "target": "domainListView", "value": "Activation" },
    { "action": "set", "target": "funcListView", "value": "Activation_ReLU" },
    { "action": "set", "target": "paramInput", "value": "0.5" },
    { "action": "click", "target": "execButton" },
    { "action": "sleep", "target": "200" },
    { "template": "echo-section", "title": "Sigmoid(0.5)" },
    { "action": "set", "target": "funcListView", "value": "Activation_Sigmoid" },
    { "action": "set", "target": "paramInput", "value": "0.5" },
    { "action": "click", "target": "execButton" },
    { "action": "sleep", "target": "200" },
    { "template": "echo-section", "title": "GELU(0.5)" },
    { "action": "set", "target": "funcListView", "value": "Activation_GELU" },
    { "action": "set", "target": "paramInput", "value": "0.5" },
    { "action": "click", "target": "execButton" },
    { "action": "sleep", "target": "200" },
    { "action": "close", "target": "数学函数计算器" },
    { "action": "exit" }
  ]
}
```

---

## 七、实施步骤

| 步骤 | 内容 | 文件 | 优先级 |
|------|------|------|--------|
| **1** | 创建 `math-register-config.json` — 数学函数注册配置 | `Operation/FunctionStorage/Registrations/math-register-config.json` | P0 |
| **2** | 创建 `MathUtils.cs` — 共享工具类 | `Operation/FunctionStorage/Registrations/MathUtils.cs` | P0 |
| **3** | 创建 `MathFunctionRegistrar.cs` — 数学函数注册器 | `Operation/FunctionStorage/Registrations/MathFunctionRegistrar.cs` | P0 |
| **4** | 创建 `MathBrowserFunctions.cs` — 数学浏览器辅助函数 | `Operation/FunctionStorage/Registrations/MathBrowserFunctions.cs` | P0 |
| **5** | 创建 `MathFunctionBrowser.json` — 四栏数学函数浏览器 UI | `DrivenConfig/JsonConfig/MathFunctionBrowser.json` | P0 |
| **6** | 在初始化入口注册所有数学组件 | 修改初始化代码 | P0 |
| **7** | 创建 6 个数学工作流文件 | `DrivenConfig/WorkFlow/MathWorkflows/` | P1 |
| **8** | 创建测试脚本 | `DrivenConfig/Scripts/MathScripts/` | P1 |

### 7.1 初始化入口修改

在框架初始化时调用：

```csharp
// 1. 注册所有数学函数
MathFunctionRegistrar.RegisterAll();

// 2. 注册数学浏览器辅助函数
MathBrowserFunctions.RegisterAll();
```

### 7.2 启动命令

```bash
# 启动数学函数浏览器
dotnet run --project FileDriven/FileDriven.Launcher -- --config MathFunctionBrowser.json --show

# 执行测试脚本
dotnet run --project FileDriven/FileDriven.Launcher -- --file Scripts/MathScripts/activation_compare_test.json
```

---

## 八、扩展指南

### 8.1 添加新的数学函数库

假设要添加一个新的数学库 `AdvancedMath.dll` 中的 `LinearAlgebra` 类：

**步骤 1**：在 `math-register-config.json` 中添加条目：

```json
{
  "source": { "type": "assembly", "path": "AdvancedMath.dll" },
  "className": "AdvancedMath.LinearAlgebra",
  "naming": { "convention": "NestedClass_MethodName" },
  "description": { "source": "xmlDoc" },
  "methodFilters": { "includeStaticOnly": true }
}
```

**步骤 2**：重新启动应用，新函数自动出现在数学函数浏览器中。

### 8.2 自定义命名规则

如果不想使用 `Domain_MethodName` 格式，可以修改命名规则：

```json
{
  "naming": {
    "convention": "NestedClass.MethodName",
    "separator": "."
  }
}
```

这样注册名会变成 `Activation.ReLU`、`Distance.Euclidean` 等。

### 8.3 添加自定义过滤

```json
{
  "methodFilters": {
    "includeStaticOnly": true,
    "excludeByReturnType": ["Void"],
    "excludeMethodNames": ["GetType", "ToString"],
    "includeNestedClasses": ["Activation", "Distance", "Clustering"],
    "maxParams": 5
  }
}
```

---

## 九、附录：Maths.cs 函数注册清单

### 9.1 Activation（激活函数）

| 注册名 | 方法签名 | 说明 |
|--------|---------|------|
| `Activation_ReLU` | `ReLU(double x)` | ReLU: max(0, x) |
| `Activation_GELU` | `GELU(double x)` | GELU 激活函数 |
| `Activation_Sigmoid` | `Sigmoid(double x)` | Sigmoid 激活函数 |
| `Activation_Swish` | `Swish(double x)` | Swish: x * sigmoid(x) |
| `Activation_Tanh` | `Tanh(double x)` | Tanh 激活函数 |
| `Activation_Softplus` | `Softplus(double x)` | Softplus: log(1 + e^x) |
| `Activation_PReLU` | `PReLU(double x, double alpha)` | 参数化 ReLU |
| `Activation_ELU` | `ELU(double x, double alpha)` | ELU 激活函数 |
| `Activation_LeakyReLU` | `LeakyReLU(double x, double alpha)` | Leaky ReLU |
| `Activation_Apply` | `Apply(double x, ActivationType type)` | 按类型应用激活函数 |

### 9.2 Distance（距离计算）

| 注册名 | 方法签名 | 说明 |
|--------|---------|------|
| `Distance_Euclidean` | `Euclidean(double[] a, double[] b)` | 欧几里得距离 |
| `Distance_Manhattan` | `Manhattan(double[] a, double[] b)` | 曼哈顿距离 |
| `Distance_CosineSimilarity` | `CosineSimilarity(double[] a, double[] b)` | 余弦相似度 |
| `Distance_HammingDistance` | `HammingDistance(string a, string b)` | 汉明距离 |
| `Distance_Chebyshev` | `Chebyshev(double[] a, double[] b)` | 切比雪夫距离 |
| `Distance_Minkowski` | `Minkowski(double[] a, double[] b, double p)` | 闵可夫斯基距离 |
| `Distance_JaccardDistance` | `JaccardDistance(HashSet<int> a, HashSet<int> b)` | 杰卡德距离 |

### 9.3 Clustering（聚类算法）

| 注册名 | 方法签名 | 说明 |
|--------|---------|------|
| `Clustering_KMeans` | `KMeans(double[][] data, int k)` | K-Means 聚类 |
| `Clustering_DBSCAN` | `DBSCAN(double[][] data, double epsilon, int minPoints)` | DBSCAN 聚类 |
| `Clustering_Hierarchical` | `Hierarchical(double[][] data)` | 层次聚类 |

### 9.4 Statistics（统计分析）

| 注册名 | 方法签名 | 说明 |
|--------|---------|------|
| `Statistics_Entropy` | `Entropy(IEnumerable<double> probabilities)` | 熵计算 |
| `Statistics_Variance` | `Variance(IEnumerable<double> values)` | 方差 |
| `Statistics_SilhouetteScore` | `SilhouetteScore(List<double[]> data, int[] labels, DistanceMetricType metric)` | 轮廓系数 |
| `Statistics_Mean` | `Mean(double[] values)` | 均值 |
| `Statistics_Median` | `Median(double[] values)` | 中位数 |
| `Statistics_Mode` | `Mode(double[] values)` | 众数 |
| `Statistics_StandardDeviation` | `StandardDeviation(IEnumerable<double> values, bool isSample)` | 标准差 |

### 9.5 Tensor（张量工厂）

| 注册名 | 方法签名 | 说明 |
|--------|---------|------|
| `Tensor_Zeros` | `Zeros(int rows, int cols)` | 零矩阵 |
| `Tensor_Identity` | `Identity(int size)` | 单位矩阵 |
| `Tensor_Random` | `Random(int rows, int cols, Random? rng)` | 随机矩阵 |
| `Tensor_Ones` | `Ones(int rows, int cols)` | 全 1 矩阵 |
| `Tensor_FromArray` | `FromArray(double[] data)` | 从数组创建 |

### 9.6 其他功能域

| 域 | 函数数 | 说明 |
|----|--------|------|
| Unary | 4 | 一元运算（Apply/Sqrt/Abs/Negate） |
| Normalization | 3 | 归一化（LayerNorm/MinMax/ZScore） |
| Reduce | 4 | 归约（Mean/Sum/Max/Min） |
| Sampling | 2 | 采样（Softmax/SampleToken） |
| Softmax | 2 | Softmax 计算 |
| Broadcast | 4 | 广播运算（Add/Subtract/Multiply/Divide） |
| Vector | 5 | 向量运算（DotProduct/Normalize/CrossProduct/AngleBetween/EuclideanDistance） |
| Similarity | 3 | 相似度（PearsonCorrelation/CosineSimilarity/JaccardSimilarity） |
| Matrix | 3 | 矩阵运算（ToMatrix/Transpose/Multiply） |
| RotaryPositionEncoding | 1 | RoPE 频率预计算 |
| Utility | 2 | 工具函数（SafeLog/StandardDeviation） |
| Memory | 1 | 内存检查 |
| Transform | 1 | 异步变换 |
| Dimension | 1 | 维度验证 |

---

> **文档版本**：v1.1  
> **最后更新**：2026-06-14  
> **关联文件**：`Mathematics/Maths.cs`、`Share/Registry/AutoRegister/`、`FileDriven/`
