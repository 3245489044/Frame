# MathFunctionBrowser 修复方案

> 修复 `MathFunctionBrowser.json` 中函数列表为空的问题
>
> 根因：`Maths.cs` 的 80+ 个数学函数未被注册到 `FunctionStorageRegistry`

---

## 一、现状分析

### 1.1 已完成的组件

| 组件 | 文件 | 状态 |
|------|------|------|
| **UI 布局配置** | `FileDriven/DrivenConfig/JsonConfig/MathFunctionBrowser.json` | ✅ 已完成 |
| **浏览器辅助函数** | `Operation/FunctionStorage/Business/Registrations/MathBrowserFunctions.cs` | ✅ 已完成 |
| **参数解析工具** | `Operation/FunctionStorage/Business/Registrations/MathUtils.cs` | ✅ 已完成 |
| **基础数学库** | `Operation/FunctionStorage/Business/Registrations/MathLibrary.cs` | ✅ 已完成（仅 4 个基础运算） |
| **自动注册服务** | `Share/Registry/AutoRegister/AutoRegisterService.cs` | ✅ 已完成 |
| **初始化入口** | `Operation/FunctionStorage/Framework/FunctionStorageRegistry.cs` (FunctionStorageLifecycle) | ✅ 已完成 |

### 1.2 缺失的组件

| 组件 | 文件 | 状态 |
|------|------|------|
| **注册配置文件** | `Operation/FunctionStorage/Registrations/math-register-config.json` | ❌ 未创建 |
| **自动注册器** | `Operation/FunctionStorage/Registrations/MathFunctionRegistrar.cs` | ❌ 未创建 |
| **初始化调用** | `FunctionStorageLifecycle.Initialize()` 中调用 `MathFunctionRegistrar.RegisterAll()` | ❌ 未添加 |

### 1.3 数据流

```
Maths.cs (80+ 函数)
    ↓ 反射提取
AutoRegisterService.ProcessAssembly()
    ↓ 过滤、命名
MathFunctionRegistrar.RegisterAll()  ← ❌ 缺失
    ↓ 注册到
FunctionStorageRegistry
    ↓ 查询
MathBrowserFunctions (Math_GetDomains, Math_GetFunctionsInDomain 等)
    ↓ UI 绑定
MathFunctionBrowser.json (UI 布局)
```

---

## 二、修复步骤

### 步骤 1：创建注册配置文件

**文件**：`Operation/FunctionStorage/Registrations/math-register-config.json`

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

**配置说明**：

| 字段 | 值 | 说明 |
|------|-----|------|
| `source.type` | `assembly` | 从已编译的程序集反射提取 |
| `source.path` | `Mathematics.dll` | 程序集文件名 |
| `className` | `MathematicalAlgorithms.Maths` | 完整类名（含命名空间） |
| `naming.convention` | `NestedClass_MethodName` | 嵌套类名_方法名，如 `Activation_ReLU` |
| `naming.separator` | `_` | 分隔符 |
| `description.source` | `xmlDoc` | 从 XML 文档注释提取描述 |
| `includeStaticOnly` | `true` | 只注册静态方法 |
| `excludeByReturnType` | `["Void"]` | 排除无返回值的方法 |
| `excludeMethodNames` | `["GetType", ...]` | 排除 Object 继承方法 |

### 步骤 2：创建自动注册器

**文件**：`Operation/FunctionStorage/Registrations/MathFunctionRegistrar.cs`

```csharp
using System.Reflection;
using System.Text.Json;
using Share.Registry;
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

    /// <summary>
    /// 注册所有数学函数（无参数重载，保持向后兼容）
    /// </summary>
    public static void RegisterAll()
    {
        RegisterAll(FunctionStorageRegistry.Registry);
    }

    /// <summary>
    /// 注册所有数学函数到指定注册表
    /// </summary>
    public static void RegisterAll(IFunctionRegistry registry)
    {
        if (_registered) return;
        _registered = true;

        // 1. 查找配置文件
        var configPath = FindConfigPath();
        if (configPath == null)
        {
            System.Diagnostics.Debug.WriteLine(
                "[MathFunctionRegistrar] 配置文件不存在，跳过注册");
            return;
        }

        // 2. 读取并解析配置
        var json = File.ReadAllText(configPath);
        var config = JsonSerializer.Deserialize<AutoRegisterConfig>(json);
        if (config?.Registrations == null || config.Registrations.Count == 0)
        {
            System.Diagnostics.Debug.WriteLine(
                "[MathFunctionRegistrar] 配置为空，跳过注册");
            return;
        }

        // 3. 创建自动注册服务
        var service = new AutoRegisterService();

        // 4. 处理每个注册条目
        foreach (var entry in config.Registrations)
        {
            try
            {
                ProcessEntry(service, registry, entry);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[MathFunctionRegistrar] 注册条目 '{entry.ClassName}' 失败: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 查找配置文件路径
    /// 搜索顺序：BaseDirectory → 项目目录 → 当前目录
    /// </summary>
    private static string? FindConfigPath()
    {
        // 候选文件名
        var candidates = new[]
        {
            "Registrations/math-register-config.json",
            "math-register-config.json",
            "Operation/FunctionStorage/Registrations/math-register-config.json",
        };

        foreach (var relativePath in candidates)
        {
            // 1. 从 BaseDirectory 搜索
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativePath);
            if (File.Exists(path)) return path;

            // 2. 从当前目录搜索
            path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
            if (File.Exists(path)) return path;
        }

        return null;
    }

    /// <summary>
    /// 处理单个注册条目
    /// </summary>
    private static void ProcessEntry(
        AutoRegisterService service,
        IFunctionRegistry registry,
        RegistrationEntry entry)
    {
        // 1. 加载程序集
        var assemblyPath = ResolveAssemblyPath(entry.Source.Path);
        if (assemblyPath == null || !File.Exists(assemblyPath))
        {
            System.Diagnostics.Debug.WriteLine(
                $"[MathFunctionRegistrar] 程序集 '{entry.Source.Path}' 未找到");
            return;
        }

        var assembly = Assembly.LoadFrom(assemblyPath);

        // 2. 使用 AutoRegisterService 提取方法
        var result = service.ProcessAssembly(assembly, entry);

        // 3. 注册到 FunctionStorageRegistry
        int successCount = 0;
        foreach (var item in result.RegisteredItems)
        {
            if (item.Status != RegisteredItemStatus.Success || item.Executor == null)
                continue;

            // ★ 包装委托：item.Executor 是 Func<object[], Task<object>>，
            //   直接注册会导致描述符参数显示为 (object[] args) 而非实际数学参数。
            //   因此包装为 Func<string, string>，利用 item.Parameters 保留准确参数信息。
            var funcName = item.RegisteredName;
            var funcParams = item.Parameters;

            registry.Register(
                funcName,
                (Func<string, string>)(paramStr =>
                {
                    try
                    {
                        var parameters = ParseMathParameters(paramStr, funcParams);
                        var task = item.Executor!(parameters);
                        var resultValue = task.GetAwaiter().GetResult();
                        return FormatMathResult(resultValue);
                    }
                    catch (Exception ex)
                    {
                        return $"错误: {ex.Message}";
                    }
                }),
                item.Description);

            successCount++;
        }

        System.Diagnostics.Debug.WriteLine(
            $"[MathFunctionRegistrar] 已注册 {successCount}/{result.TotalCount} 个数学函数 " +
            $"(来自 {entry.ClassName})");
    }

    /// <summary>
    /// 解析程序集路径
    /// </summary>
    private static string? ResolveAssemblyPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        // 如果是绝对路径，直接返回
        if (Path.IsPathRooted(path))
            return path;

        // 从 BaseDirectory 搜索
        var basePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);
        if (File.Exists(basePath))
            return basePath;

        // 从项目输出目录搜索
        var projectPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "..", "..", "..", "..",
            "Mathematics", "bin", "Debug", "net8.0", path);
        projectPath = Path.GetFullPath(projectPath);
        if (File.Exists(projectPath))
            return projectPath;

        // 从当前目录搜索
        var cwdPath = Path.Combine(Directory.GetCurrentDirectory(), path);
        if (File.Exists(cwdPath))
            return cwdPath;

        return null;
    }
}
```

### 步骤 3：修改初始化入口

**文件**：`Operation/FunctionStorage/Framework/FunctionStorageRegistry.cs`

在 `FunctionStorageLifecycle.Initialize()` 方法中，在注册 `MathBrowserFunctions` 之前添加 `MathFunctionRegistrar.RegisterAll(_registry)` 调用：

```csharp
public void Initialize()
{
    if (_initialized) return;

    // 1. 同步到静态门面（保持向后兼容）
    FunctionStorageRegistry.Initialize(_registry, _executor);

    // 2. 注册所有内置函数（按依赖顺序）
    MathLibrary.RegisterAll(_registry);                    // 基础数学运算
    Libraries.BuiltInUnitsLibrary.RegisterAll(_registry);  // 类型转换
    Registrations.MathFunctionRegistrar.RegisterAll(_registry);  // ★ 新增：自动注册 Maths.cs 的 80+ 函数
    Registrations.MathBrowserFunctions.RegisterAll(_registry);   // 浏览器辅助函数（依赖已注册的数学函数）
    UIToolbox.RegisterAll(_registry);                      // UI 操作
    WorkflowLibrary.RegisterAll(_registry);                // 工作流

    _initialized = true;

    System.Diagnostics.Debug.WriteLine(
        $"[FunctionStorageLifecycle] Initialize 完成: " +
        $"registeredCount={_registry.Count}");
}
```

**注意**：`MathFunctionRegistrar.RegisterAll()` 必须在 `MathBrowserFunctions.RegisterAll()` **之前**调用，因为 `MathBrowserFunctions` 中的 `Math_GetDomains`、`Math_GetFunctionsInDomain` 等函数需要查询已注册的数学函数。

---

## 三、注册后的效果

### 3.1 注册的函数数量

| 来源 | 函数数 | 示例 |
|------|--------|------|
| MathLibrary | 6 | Add, Plus, Subtract, Minus, Multiply, Divide |
| MathFunctionRegistrar | ~80+ | Activation_ReLU, Distance_Euclidean, Clustering_KMeans ... |
| MathBrowserFunctions | 7 | Math_GetDomains, Math_GetFunctionDetail, Math_Execute ... |
| BuiltInUnitsLibrary | 5 | StringToInt, StringToDouble ... |
| UIToolbox | 7 | ClearOutput, ExecuteWorkflow ... |
| WorkflowLibrary | 动态 | 取决于工作流文件 |
| **合计** | **~110+** | |

### 3.2 注册命名示例

| 嵌套类 | 方法 | 注册名 |
|--------|------|--------|
| Activation | ReLU | `Activation_ReLU` |
| Activation | GELU | `Activation_GELU` |
| Distance | Euclidean | `Distance_Euclidean` |
| Distance | Manhattan | `Distance_Manhattan` |
| Clustering | KMeans | `Clustering_KMeans` |
| Clustering | DBSCAN | `Clustering_DBSCAN` |
| Statistics | Entropy | `Statistics_Entropy` |
| Statistics | Variance | `Statistics_Variance` |
| Tensor | Zeros | `Tensor_Zeros` |
| Tensor | Identity | `Tensor_Identity` |
| Normalization | LayerNorm | `Normalization_LayerNorm` |
| Sampling | Softmax | `Sampling_Softmax` |
| Vector | DotProduct | `Vector_DotProduct` |
| Similarity | CosineSimilarity | `Similarity_CosineSimilarity` |

### 3.3 UI 交互流程

```
1. 启动 → 左侧"数学域"列表自动显示所有域
   (Activation, Distance, Clustering, Statistics, Tensor ...)

2. 点击域（如 "Activation"）→ 中间函数列表显示该域下所有函数
   (Activation_ReLU, Activation_GELU, Activation_Sigmoid ...)

3. 点击函数（如 "Activation_ReLU"）→ 详情面板显示：
   函数: Activation_ReLU
   描述: ReLU: max(0, x)
   返回: Double
   参数 (1 个):
     x: Double

4. 输入参数 "0.5" → 点击"执行计算" → 结果面板显示 "0.5"

5. 搜索 "KMeans" → 显示匹配的 Clustering_KMeans
```

---

## 四、验证方法

### 4.1 启动测试

```bash
# 启动数学函数浏览器
dotnet run --project FileDriven/FileDriven.Launcher -- --config MathFunctionBrowser.json --show
```

### 4.2 脚本测试

```bash
# 执行批量测试脚本
dotnet run --project FileDriven/FileDriven.Launcher -- --file DrivenConfig/Scripts/Math/math_browser_simple_test.json
```

### 4.3 预期输出

测试脚本中的关键步骤预期结果：

| 测试 | 预期输出 |
|------|---------|
| `Math_GetDomains` | `Activation,Broadcast,Clustering,Distance,Dimension,Matrix,Memory,Normalization,Reduce,RotaryPositionEncoding,Sampling,Similarity,Softmax,Statistics,Tensor,Transform,Unary,Utility,Vector` |
| `Math_GetStats` | `共 85 个数学函数，19 个数学域` |
| `Math_Search("ReLU")` | `Activation_ReLU` |
| `Math_GetFunctionDetail("Activation_ReLU")` | 显示函数签名、参数信息 |
| `Math_Execute("Activation_ReLU|-1.0")` | `0` |

---

## 五、扩展指南

### 5.1 添加新的数学函数库

假设要添加 `AdvancedMath.dll` 中的 `LinearAlgebra` 类：

在 `math-register-config.json` 中添加条目：

```json
{
  "source": { "type": "assembly", "path": "AdvancedMath.dll" },
  "className": "AdvancedMath.LinearAlgebra",
  "naming": { "convention": "NestedClass_MethodName" },
  "description": { "source": "xmlDoc" },
  "methodFilters": { "includeStaticOnly": true }
}
```

重新启动应用，新函数自动出现在数学函数浏览器中。

### 5.2 自定义命名规则

```json
{
  "naming": {
    "convention": "NestedClass.MethodName",
    "separator": "."
  }
}
```

注册名变为 `Activation.ReLU`、`Distance.Euclidean` 等。

### 5.3 自定义过滤

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

## 六、实施清单

| # | 操作 | 文件 | 预计工时 |
|---|------|------|---------|
| 1 | 创建注册配置文件 | `Operation/FunctionStorage/Registrations/math-register-config.json` | 5 分钟 |
| 2 | 创建自动注册器 | `Operation/FunctionStorage/Registrations/MathFunctionRegistrar.cs` | 30 分钟 |
| 3 | 修改初始化入口 | `Operation/FunctionStorage/Framework/FunctionStorageRegistry.cs` | 5 分钟 |
| 4 | 编译验证 | 项目编译 | 2 分钟 |
| 5 | 启动测试 | 运行浏览器验证函数列表 | 5 分钟 |
| | **合计** | | **~47 分钟** |

---

> **文档版本**：v1.0  
> **最后更新**：2026-06-14  
> **关联文件**：`Mathematics/Maths.cs`、`Share/Registry/AutoRegister/`、`Operation/FunctionStorage/`
