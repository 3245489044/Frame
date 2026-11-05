# AutoRegister 自动注册模块 — 设计文档

> **目标**：基于 RefectionMethod + RoslynMethod 完善 Share.Registry 的 AutoRegister 模块，实现从程序集（.dll）或源文件（.cs）自动提取方法信息的能力，并通过 JSON 配置驱动整个提取→过滤→命名→委托创建流程。提取结果由调用方自行决定如何消费（如注册到 FunctionStorage、生成文档等）。

---

## 一、背景与现状

### 1.1 Share.Registry 现有能力

Share.Registry 已提供 6 种注册表模式：

| 注册表类型 | 基类 | 用途 |
|-----------|------|------|
| `SetRegistryBase<T>` | `RegistryBase<T>` | 唯一值集合 |
| `MapRegistryBase<TKey, TValue>` | `RegistryBase<TKey>` | 键值映射 |
| `FactoryRegistryBase<TKey, TValue>` | `MapRegistryBase<TKey, TValue>` | 延迟创建工厂 |
| `MetaMapRegistryBase<TKey, TValue, TMeta>` | `MapRegistryBase<TKey, TValue>` | 带元数据的键值映射 |
| `StageRegistryBase<TStage, TValue>` | — | 阶段化注册 |
| `GraphRegistryBase<TNodeId, TNode, TEdge>` | `RegistryBase<TNodeId>` | 有向图注册 |

其中 `FunctionRegistry` 继承自 `MetaMapRegistryBase<string, Delegate, FunctionDescriptor>`，用于注册函数委托及其描述信息。

**当前缺失**：自动从程序集或源文件扫描并注册函数的能力。

### 1.2 Operation.FunctionStorage 现有能力

`FunctionStorage` 是系统中**唯一的注册表中心**，提供统一注册入口：

| 组件 | 说明 |
|------|------|
| `FunctionStorageRegistry` | **静态入口**，所有函数统一注册到此入口 |
| `FunctionStorageRegistry.Register(name, delegate, description)` | 注册委托方式 |
| `FunctionStorageRegistry.Register(IFunctionUnit)` | 注册函数单元方式 |
| `FunctionStorageRegistry.ExecuteAsync(name, params)` | 执行已注册函数 |
| `FunctionStorage.Core.FunctionRegistry` | 实例化版本，实现 `IFunctionRegistry` 接口 |
| `FunctionStorage.Core.FunctionExecutor` | 函数执行器实现 |

**关键关系**：`FunctionStorageRegistry` 内部委托给 `Share.Registry.FunctionRegistry` 实现存储，`FunctionStorage.Core.FunctionRegistry` 同样包装 `Share.Registry.FunctionRegistry`。

已有手动注册示例：
```csharp
FunctionStorageRegistry.Register("Add", (Func<double, double, double>)Add, "Addition: a + b");
```

### 1.3 RefectionMethod 可复用能力

| 模块 | 能力 | 复用方式 |
|------|------|---------|
| `ReflectionService` | 类型元数据提取、方法描述符获取 | `GetMethodDescriptors()` 返回 `List<FunctionDescriptor>` |
| `DynamicMethodExecutor` | 表达式树编译委托 | `CreateInvokerDelegate()` 将 MethodInfo 编译为高性能委托 |
| `DelegateCompiler` | 委托编译为 `Func<object[], Task<object>>` | `Compile(Delegate)` 方法 |
| `MethodResolver` | 方法重载解析、参数类型转换 | 参数匹配逻辑 |
| `TypeExtensions` | 类型友好名称、成员查询 | `GetFriendlyName()`、`GetAllMethods()` |
| `Cache<TKey, TValue>` | 通用缓存 | 缓存策略 |
| `ReflectionDataExtractor` | 字段/属性提取 | 数据提取能力 |

### 1.4 RoslynMethod 可复用能力

| 模块 | 能力 | 复用方式 |
|------|------|---------|
| `DynamicCompilation.CompileAssembly(code)` | 编译源代码为 Assembly | 编译 .cs → Assembly → 反射获取 MethodInfo |
| `CompilationReferenceHelper.GetRequiredReferences(code)` | 自动解析 using 引用 | 为动态编译提供引用 |
| `RoslynCacheManager.GetOrCreateAnalysis(code, refs)` | 编译缓存 | 缓存语法树和编译结果 |
| `CodeExtractor.ExtractFunctionWithClass(code, funcName)` | 提取单个函数及其所在类 | 按需提取特定函数 |
| `SyntaxOptimizer.RewriteMethod(code)` | 语法树重写 | 代码优化（非核心路径） |
| `RefactoringFramework` | 代码重构框架 | 规则驱动的代码变换（非核心路径） |

> **注意**：RoslynMethod 中没有现成的"从 .cs 源文件提取所有方法签名"的类。Roslyn 提取器需要自行使用 `Microsoft.CodeAnalysis.CSharp` 解析语法树，或通过 `DynamicCompilation.CompileAssembly()` 编译后反射获取。

---

## 二、架构设计

### 2.1 整体架构

```
┌─────────────────────────────────────────────────────────────────────────┐
│                          调用方（Caller）                                │
│                                                                          │
│  ┌──────────────────────────────────────────────────────────────────┐   │
│  │  调用方负责：                                                      │   │
│  │  • 解析 JSON 配置（含路径信息）                                    │   │
│  │  • 根据 type 加载输入源：                                          │   │
│  │    - "assembly" → Assembly.LoadFrom(path) 加载程序集               │   │
│  │    - "source"   → File.ReadAllText(path) 读取源代码                │   │
│  │  • 将加载好的对象传递给 AutoRegisterService                       │   │
│  │  • 消费 AutoRegisterResult（如注册到 FunctionStorage）            │   │
│  │  • 按需启用缓存（注入 IAutoRegisterCache 实现）                   │   │
│  └──────────────────────────────────────────────────────────────────┘   │
└────────────────────────────┬─────────────────────────────────────────────┘
                             │
                             ▼ 传入 Assembly / string code
                             │
┌────────────────────────────▼─────────────────────────────────────────────┐
│                        AutoRegisterService                              │
│                                                                          │
│  ┌──────────────────┐    ┌──────────────────┐    ┌────────────────────┐  │
│  │  MethodExtractor  │    │  ExtractedMethod  │    │  AutoRegister     │  │
│  │  (方法提取器)     │───▶│  Info (中间模型)  │───▶│  Result (处理结果) │  │
│  └──────────────────┘    └──────────────────┘    └────────────────────┘  │
│         │                                                                 │
│  ┌──────┴──────┐                                                         │
│  ▼              ▼                                                         │
│ ┌────────────┐ ┌──────────────┐                                          │
│ │ Reflection │ │   Roslyn     │                                          │
│ │ Extractor  │ │  Extractor   │                                          │
│ │ (Assembly) │ │  (string)    │                                          │
│ │ 并行路径 A  │ │  并行路径 B  │                                          │
│ └────────────┘ └──────────────┘                                          │
└──────────────────────────────────────────────────────────────────────────┘
                             │
                             ▼ AutoRegisterResult
                             │
┌────────────────────────────▼─────────────────────────────────────────────┐
│                        调用方消费结果                                     │
│                                                                          │
│  ┌──────────────────────────────────────────────────────────────────┐   │
│  │  调用方可选操作：                                                  │   │
│  │  • 遍历 result.RegisteredItems 注册到 FunctionStorageRegistry    │   │
│  │  • 生成文档/报告                                                 │   │
│  │  • 导出注册表快照                                                │   │
│  │  • 其他自定义消费方式                                            │   │
│  └──────────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────┘
```

### 2.2 两条并行路径对比

| 维度 | 反射路径（Assembly） | Roslyn 路径（Source） |
|------|-------------------|---------------------|
| 输入 | 已编译的 .dll 文件（由调用方加载为 Assembly 对象） | .cs 源文件（由调用方读取为 string） |
| 方法提取 | `Type.GetMethods()` | 语法树解析 → `MethodDeclarationSyntax` 遍历 |
| 委托创建 | `MethodInfo.CreateDelegate` / `DynamicMethodExecutor` | `DynamicCompilation.CompileAssembly()` → 反射 |
| XML 文档注释 | 需要 .xml 文档文件 | 从语法树 `DocumentationCommentTrivia` 直接提取 |
| 性能 | 快（直接反射） | 慢（需要编译） |
| 适用场景 | 有编译好的程序集 | 只有源文件，或需要源码级分析 |

### 2.3 核心流程

```
调用方准备输入
       │
       ├── 解析 JSON 配置 → AutoRegisterConfig 对象
       │
       ├── 遍历每个 RegistrationEntry
       │      │
       │      ├── 根据 source.type 加载输入源（调用方负责）
       │      │      ├── "assembly" → Assembly.LoadFrom(path)
       │      │      └── "source"   → File.ReadAllText(path)
       │      │
      │      ├── 调用 AutoRegisterService.ProcessAssembly(assembly, entry)
      │      │  或 AutoRegisterService.ProcessSource(code, entry)
       │      │
       │      ├── 选择提取器（根据 source.type）
       │      │      ├── "assembly" → ReflectionExtractor
       │      │      │      ├── 通过 ClassName 获取 Type（支持嵌套类 "Outer+Nested"）
       │      │      │      ├── 获取所有公开静态方法 → List<MethodInfo>
       │      │      │      └── 转换为 List<ExtractedMethodInfo>
       │      │      │
       │      │      └── "source" → RoslynExtractor
       │      │             ├── CSharpSyntaxTree.ParseText 解析语法树
       │      │             ├── 遍历 MethodDeclarationSyntax 提取方法签名
       │      │             ├── 从 DocumentationCommentTrivia 提取 XML 文档注释
       │      │             ├── DynamicCompilation.CompileAssembly() 编译
       │      │             ├── 反射获取 MethodInfo → 创建委托
       │      │             └── 转换为 List<ExtractedMethodInfo>
       │      │
       │      ├── 应用 MethodFilters 过滤
       │      │      ├── IncludeNestedClasses
       │      │      ├── ExcludeByReturnType
       │      │      ├── MaxParams
       │      │      ├── IncludeStaticOnly
       │      │      └── ExcludeMethodNames
       │      │
       │      ├── 生成注册名称（按 NamingConvention）
       │      │      ├── NestedClass_MethodName → "Activation_ReLU"
       │      │      ├── NestedClass.MethodName → "Activation.ReLU"
       │      │      └── MethodName → "ReLU"
       │      │
       │      ├── 提取描述（按 DescriptionSource）
       │      │      ├── xmlDoc → 从 XML 文档注释提取
       │      │      ├── attribute → 从 [Description] 特性提取
       │      │      └── none → 空字符串
       │      │
       │      └── 返回 AutoRegisterResult（含 RegisteredName、Executor、Description 等）
       │
       └── 调用方消费 AutoRegisterResult
              ├── 遍历 result.RegisteredItems
              ├── 按需注册到 FunctionStorageRegistry
              │   → FunctionStorageRegistry.Register(item.RegisteredName, item.Executor!, item.Description)
              ├── 或生成文档/报告
              └── 或导出注册表快照
```

---

## 三、文件结构规划

```
Share/Registry/
├── Registry.csproj
├── 01_Interfaces.cs
├── 02_RegistryBase.cs
├── 03_MapBasedRegistries.cs
├── 04_StageGraphRegistries.cs
├── 05_FunctionDescriptors.cs
├── 06_ConcreteRegistries.cs
├── 07_Extensions.cs
├── README.md
│
├── AutoRegister/                          ← 新增：自动注册模块
│   ├── AutoRegisterConfig.cs              ← JSON 配置模型
│   ├── AutoRegisterService.cs             ← 核心服务（入口）
│   ├── AutoRegisterResult.cs              ← 注册结果模型
│   │
│   ├── Models/                            ← 中间模型
│   │   └── ExtractedMethodInfo.cs         ← 统一的方法信息中间模型
│   │
│   ├── Extractors/                        ← 提取器
│   │   ├── IMethodExtractor.cs            ← 提取器接口
│   │   ├── ReflectionExtractor.cs         ← 反射提取器（从 Assembly）
│   │   └── RoslynExtractor.cs             ← Roslyn 提取器（从 string code）
│   │
│   ├── Filters/                           ← 过滤器
│   │   ├── MethodFilter.cs                ← 方法过滤逻辑
│   │   └── MethodFilterConfig.cs          ← 过滤配置模型
│   │
│   ├── Naming/                            ← 命名规则
│   │   ├── INamingConvention.cs           ← 命名规则接口
│   │   └── NamingConventions.cs           ← 内置命名规则实现
│   │
│   ├── Description/                       ← 描述提取
│   │   ├── IDescriptionProvider.cs        ← 描述提供者接口
│   │   └── DescriptionProviders.cs        ← 内置描述提供者实现
│   │
│   ├── Delegate/                          ← 委托创建
│   │   ├── IDelegateFactory.cs            ← 委托工厂接口
│   │   └── DelegateFactory.cs             ← 委托工厂实现
│   │
│   └── Cache/                             ← 缓存模块
│       ├── IAutoRegisterCache.cs          ← 缓存管理器接口
│       ├── FileAutoRegisterCache.cs       ← 文件缓存实现
│       ├── CacheKeyGenerator.cs           ← 缓存键生成器
│       ├── CachedManifest.cs              ← 缓存清单模型
│       └── RegistrySnapshot.cs            ← 注册表快照
│
└── docs/
    └── AutoRegister-设计文档.md           ← 本文档
```

---

## 四、关键接口设计

### 4.1 中间模型：ExtractedMethodInfo

```csharp
/// <summary>
/// 统一的方法信息中间模型
/// 屏蔽反射路径和 Roslyn 路径的差异，为后续注册提供统一数据
/// </summary>
public class ExtractedMethodInfo
{
    /// <summary>方法名称</summary>
    public string MethodName { get; init; } = string.Empty;

    /// <summary>所在类名（如 "Maths"）</summary>
    public string ContainingClassName { get; init; } = string.Empty;

    /// <summary>所在嵌套类名（如 "Activation"，非嵌套类时为空）</summary>
    public string? ContainingNestedClassName { get; init; }

    /// <summary>返回类型</summary>
    public Type ReturnType { get; init; } = typeof(void);

    /// <summary>参数信息</summary>
    public ParameterInfo[] Parameters { get; init; } = Array.Empty<ParameterInfo>();

    /// <summary>XML 文档注释摘要</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>是否静态方法</summary>
    public bool IsStatic { get; init; }

    /// <summary>是否异步方法</summary>
    public bool IsAsync { get; init; }

    /// <summary>可直接调用的执行器委托</summary>
    public Func<object[], Task<object>>? Executor { get; init; }

    /// <summary>参数信息子模型</summary>
    public class ParameterInfo
    {
        public string Name { get; init; } = string.Empty;
        public Type Type { get; init; } = typeof(object);
        public bool IsOptional { get; init; }
        public object? DefaultValue { get; init; }
    }
}
```

### 4.2 提取器接口

```csharp
/// <summary>
/// 方法提取器接口 — 从不同来源提取方法信息
/// </summary>
public interface IMethodExtractor
{
    /// <summary>提取器类型标识（"assembly" 或 "source"）</summary>
    string SourceType { get; }

    /// <summary>
    /// 从指定来源提取方法信息
    /// </summary>
    /// <param name="className">目标类名</param>
    /// <param name="methodFilters">方法过滤配置</param>
    /// <returns>提取到的方法信息列表</returns>
    List<ExtractedMethodInfo> ExtractMethods(string className, MethodFilterConfig? methodFilters);
}
```

### 4.3 反射提取器

```csharp
/// <summary>
/// 反射提取器 — 从已编译的程序集提取方法（并行路径 A）
/// 复用 RefectionMethod 的 ReflectionService 和 TypeExtensions
/// 
/// ★ 路径由调用方负责加载，提取器只接收内存中的 Assembly 对象
/// </summary>
public class ReflectionExtractor : IMethodExtractor
{
    private readonly Assembly _assembly;

    public ReflectionExtractor(Assembly assembly)
    {
        _assembly = assembly ?? throw new ArgumentNullException(nameof(assembly));
    }

    public string SourceType => "assembly";

    public List<ExtractedMethodInfo> ExtractMethods(string className, MethodFilterConfig? methodFilters)
    {
        // 1. 通过 className 获取指定类型（支持嵌套类路径如 "OuterClass+NestedClass"）
        // 2. 获取所有公开静态方法
        // 3. 尝试加载 .xml 文档文件提取注释
        // 4. 使用 DynamicMethodExecutor 创建高性能委托
        // 5. 转换为 List<ExtractedMethodInfo> 返回
    }
}
```

### 4.4 Roslyn 提取器

```csharp
/// <summary>
/// Roslyn 提取器 — 从 .cs 源文件提取方法（并行路径 B）
/// 复用 RoslynMethod 的 DynamicCompilation 和 CompilationReferenceHelper
/// 
/// ★ 路径由调用方负责读取，提取器只接收内存中的源代码字符串
/// </summary>
public class RoslynExtractor : IMethodExtractor
{
    private readonly string _sourceCode;

    public RoslynExtractor(string sourceCode)
    {
        _sourceCode = sourceCode ?? throw new ArgumentNullException(nameof(sourceCode));
    }

    public string SourceType => "source";

    public List<ExtractedMethodInfo> ExtractMethods(string className, MethodFilterConfig? methodFilters)
    {
        // 1. CSharpSyntaxTree.ParseText 解析语法树
        // 2. 遍历 MethodDeclarationSyntax 提取方法签名（名称、参数、返回类型）
        // 3. 从 DocumentationCommentTrivia 提取 XML 文档注释
        // 4. CompilationReferenceHelper.GetRequiredReferences(code) 解析引用
        // 5. DynamicCompilation.CompileAssembly(code) 编译为 Assembly
        // 6. 反射获取 MethodInfo → 创建委托
        // 7. 转换为 List<ExtractedMethodInfo> 返回
    }
}
```

### 4.5 委托工厂

```csharp
/// <summary>
/// 委托工厂 — 为 MethodInfo 创建可调用的 Delegate
/// 复用 RefectionMethod 的 DynamicMethodExecutor 和 DelegateCompiler
/// </summary>
public interface IDelegateFactory
{
    /// <summary>为方法创建委托</summary>
    Delegate CreateDelegate(MethodInfo method);

    /// <summary>编译为可执行器（Func<object[], Task<object>>）</summary>
    Func<object[], Task<object>> CompileExecutor(Delegate del);
}
```

### 4.6 命名规则接口

```csharp
/// <summary>
/// 命名规则接口 — 定义如何为方法生成注册名称
/// </summary>
public interface INamingConvention
{
    /// <summary>规则名称标识</summary>
    string Name { get; }

    /// <summary>
    /// 生成注册名称
    /// </summary>
    /// <param name="className">类名</param>
    /// <param name="nestedClassName">嵌套类名（可能为 null）</param>
    /// <param name="methodName">方法名</param>
    /// <param name="config">命名配置</param>
    /// <returns>生成的注册名称</returns>
    string GenerateName(string className, string? nestedClassName, string methodName, NamingConfig config);
}
```

### 4.7 描述提供者接口

```csharp
/// <summary>
/// 描述提供者接口 — 从不同来源提取方法描述
/// </summary>
public interface IDescriptionProvider
{
    /// <summary>提供者类型标识</summary>
    string SourceType { get; }

    /// <summary>
    /// 从 ExtractedMethodInfo 提取方法描述
    /// （使用 ExtractedMethodInfo 而非 MethodInfo，以兼容 Roslyn 路径）
    /// </summary>
    string GetDescription(ExtractedMethodInfo method);
}
```

---

## 五、JSON 配置格式

### 5.1 完整配置示例

```json
{
  "registrations": [
    {
      "source": {
        "type": "assembly",
        "path": "Mathematics.dll",
        "sourceFile": "Mathematics/Maths.cs"
      },
      "className": "MathematicalAlgorithms.Maths",
      "methodFilters": {
        "includeNestedClasses": ["Activation", "Distance", "Vector", "Statistics"],
        "excludeByReturnType": ["void"],
        "maxParams": 5,
        "includeStaticOnly": true,
        "excludeMethodNames": ["GetHashCode", "Equals", "ToString", "GetType"]
      },
      "naming": {
        "convention": "NestedClass_MethodName",
        "separator": "_",
        "prefix": "",
        "suffix": ""
      },
      "description": {
        "source": "xmlDoc",
        "fallback": "Auto-registered method"
      }
    },
    {
      "source": {
        "type": "source",
        "path": "BasicMethod/FileProcess.cs"
      },
      "className": "FileProcess",
      "methodFilters": {
        "includeStaticOnly": true,
        "maxParams": 5
      },
      "naming": {
        "convention": "MethodName"
      },
      "description": {
        "source": "none"
      }
    }
  ]
}
```

### 5.2 配置模型定义

```csharp
/// <summary>自动注册配置根对象</summary>
public class AutoRegisterConfig
{
    public List<RegistrationEntry> Registrations { get; set; } = new();
}

/// <summary>单个注册条目</summary>
public class RegistrationEntry
{
    public SourceConfig Source { get; set; } = new();
    public string ClassName { get; set; } = string.Empty;
    public MethodFilterConfig? MethodFilters { get; set; }
    public NamingConfig? Naming { get; set; }
    public DescriptionConfig? Description { get; set; }
}

/// <summary>来源配置</summary>
/// <remarks>
/// ★ Path 仅作为调用方的配置信息，框架不负责解析路径。
///    调用方根据 Type 决定如何加载输入源：
///    - "assembly" → Assembly.LoadFrom(Path)
///    - "source"   → File.ReadAllText(Path)
/// </remarks>
public class SourceConfig
{
    public string Type { get; set; } = "assembly";  // "assembly" | "source"
    public string Path { get; set; } = string.Empty;
    public string? SourceFile { get; set; }
}

/// <summary>命名配置</summary>
public class NamingConfig
{
    public string Convention { get; set; } = "MethodName";
    public string Separator { get; set; } = "_";
    public string Prefix { get; set; } = string.Empty;
    public string Suffix { get; set; } = string.Empty;
}

/// <summary>描述配置</summary>
public class DescriptionConfig
{
    public string Source { get; set; } = "none";  // "xmlDoc" | "attribute" | "none"
    public string? Fallback { get; set; }
}
```

---

## 六、使用示例

### 6.1 从程序集注册（Assembly 路径）

```csharp
// 1. 调用方负责加载输入源
var assembly = Assembly.LoadFrom(@"D:\Project\Program\WorkFlowPrj\Mathematics\bin\Debug\net10.0\Mathematics.dll");

// 2. 创建 AutoRegisterService
var service = new AutoRegisterService();

// 3. 注册程序集中的所有方法
var result = service.ProcessAssembly(assembly, new RegistrationEntry
{
    ClassName = "MathematicalAlgorithms.Maths",
    MethodFilters = new MethodFilterConfig
    {
        IncludeNestedClasses = new List<string> { "Activation", "Distance", "Vector", "Statistics" },
        IncludeStaticOnly = true,
        ExcludeMethodNames = new List<string> { "GetHashCode", "Equals", "ToString", "GetType" }
    },
    Naming = new NamingConfig
    {
        Convention = "NestedClass_MethodName",
        Separator = "_"
    },
    Description = new DescriptionConfig
    {
        Source = "xmlDoc",
        Fallback = "Auto-registered method from MathematicalAlgorithms.Maths"
    }
});

// 4. 查看结果
Console.WriteLine($"成功: {result.SuccessCount}, 失败: {result.FailCount}");
foreach (var item in result.RegisteredItems)
{
    Console.WriteLine($"  [{item.Status}] {item.RegisteredName} → {item.MethodName}");
}

// 5. 调用方自行决定如何消费结果（如注册到 FunctionStorageRegistry）
foreach (var item in result.RegisteredItems)
{
    if (item.Status == RegisteredItemStatus.Success && item.Executor != null)
    {
        FunctionStorageRegistry.Register(item.RegisteredName, item.Executor, item.Description);
    }
}

// 6. 通过 FunctionStorageRegistry 调用已注册的函数
var output = await FunctionStorageRegistry.ExecuteAsync("Activation_ReLU", 0.5);
Console.WriteLine($"ReLU(0.5) = {output.Value}");
```

### 6.2 从源文件注册（Roslyn 路径）

```csharp
// 调用方负责读取源文件
var sourceCode = File.ReadAllText(@"D:\Project\Program\WorkFlowPrj\Mathematics\Maths.cs");

var service = new AutoRegisterService();

var result = service.ProcessSource(sourceCode, new RegistrationEntry
{
    ClassName = "MathematicalAlgorithms.Maths",
    MethodFilters = new MethodFilterConfig
    {
        IncludeNestedClasses = new List<string> { "Activation", "Distance" },
        IncludeStaticOnly = true
    },
    Naming = new NamingConfig
    {
        Convention = "NestedClass_MethodName",
        Separator = "_"
    }
});
```

### 6.3 通过 JSON 配置驱动注册

```csharp
// 1. 加载 JSON 配置文件
var configJson = File.ReadAllText("auto-register-config.json");
var config = JsonSerializer.Deserialize<AutoRegisterConfig>(configJson);

// 2. 创建 AutoRegisterService
var service = new AutoRegisterService();

// 3. 遍历每个注册条目
foreach (var entry in config!.Registrations)
{
    // 调用方根据 type 加载输入源
    if (entry.Source.Type == "assembly")
    {
        var assembly = Assembly.LoadFrom(entry.Source.Path);
        var result = service.ProcessAssembly(assembly, entry);
        Console.WriteLine($"注册 {entry.ClassName}: {result.SuccessCount} 成功, {result.FailCount} 失败");
    }
    else if (entry.Source.Type == "source")
    {
        var sourceCode = File.ReadAllText(entry.Source.Path);
        var result = service.ProcessSource(sourceCode, entry);
        Console.WriteLine($"注册 {entry.ClassName}: {result.SuccessCount} 成功, {result.FailCount} 失败");
    }
}
```

### 6.4 构建 JsonUI 进行操作

> **说明**：`JsonUI` 是一个待实现的模块，用于通过 JSON 描述驱动 UI 操作。
> 其核心思路是：将 JSON 命令解析为函数调用，委托给 `FunctionStorageRegistry` 执行。
> 具体实现可参考 `FileDriven/FileDriven.JsonRefer/` 或 `Operation/WdUI/` 中的现有 JSON 处理能力。

```csharp
// 概念示例 — 通过 JSON 命令调用已注册的函数
var jsonCommand = @"
{
  ""action"": ""invoke"",
  ""function"": ""Activation_ReLU"",
  ""args"": [0.5]
}";

// 解析 JSON → 提取 function 和 args → 委托给 FunctionStorageRegistry
var result = await FunctionStorageRegistry.ExecuteAsync("Activation_ReLU", 0.5);
```

---

## 七、与现有代码的复用关系

| 新模块 | 复用的现有代码 | 来源 |
|--------|--------------|------|
| `ReflectionExtractor` | `ReflectionService.GetMethodDescriptors()` | BasicMethod.RefectionMethod |
| `ReflectionExtractor` | `TypeExtensions.GetAllMethods()` | BasicMethod.RefectionMethod |
| `ReflectionExtractor` | `TypeExtensions.GetFriendlyName()` | BasicMethod.RefectionMethod |
| `DelegateFactory` | `DynamicMethodExecutor.CreateInvokerDelegate()` | BasicMethod.RefectionMethod |
| `DelegateFactory` | `DelegateCompiler.Compile()` | BasicMethod.RefectionMethod |
| `RoslynExtractor` | `DynamicCompilation.CompileAssembly()` | BasicMethod.RoslynMethod.Generation |
| `RoslynExtractor` | `CompilationReferenceHelper.GetRequiredReferences()` | BasicMethod.RoslynMethod.Core |
| `RoslynExtractor` | `RoslynCacheManager.GetOrCreateAnalysis()` | BasicMethod.RoslynMethod.Refactoring |
| `AutoRegisterService` | `FunctionRegistry.Register()` | Share.Registry |

---

## 八、实施步骤

> 以下步骤按依赖顺序排列，每个步骤依赖前面的步骤完成后才能开始。

### 阶段一：基础模型层（无外部依赖） ✅

| 步骤 | 内容 | 文件 | 状态 |
|------|------|------|------|
| 1 | 创建中间模型 | `Models/ExtractedMethodInfo.cs` | ✅ 已完成 |
| 2 | 创建配置模型 | `AutoRegisterConfig.cs` | ✅ 已完成 |
| 3 | 创建结果模型 | `AutoRegisterResult.cs` | ✅ 已完成 |

### 阶段二：提取器接口与过滤器（依赖基础模型） ✅

| 步骤 | 内容 | 文件 | 状态 |
|------|------|------|------|
| 4 | 创建提取器接口 | `Extractors/IMethodExtractor.cs` | ✅ 已完成 |
| 5 | 创建过滤配置 | `Filters/MethodFilterConfig.cs` | ✅ 已完成 |
| 6 | 实现方法过滤器 | `Filters/MethodFilter.cs` | ✅ 已完成 |

### 阶段三：命名规则与描述提供者（依赖基础模型） ✅

| 步骤 | 内容 | 文件 | 状态 |
|------|------|------|------|
| 7 | 创建命名规则接口 | `Naming/INamingConvention.cs` | ✅ 已完成 |
| 8 | 实现命名规则 | `Naming/NamingConventions.cs` | ✅ 已完成 |
| 9 | 创建描述提供者接口 | `Description/IDescriptionProvider.cs` | ✅ 已完成 |
| 10 | 实现描述提供者 | `Description/DescriptionProviders.cs` | ✅ 已完成 |

### 阶段四：委托工厂（依赖 RefectionMethod） ✅

| 步骤 | 内容 | 文件 | 状态 |
|------|------|------|------|
| 11 | 创建委托工厂接口 | `Delegate/IDelegateFactory.cs` | ✅ 已完成 |
| 12 | 实现委托工厂 | `Delegate/DelegateFactory.cs` | ✅ 已完成 |

### 阶段五：提取器实现（核心逻辑） ✅

| 步骤 | 内容 | 文件 | 状态 |
|------|------|------|------|
| 13 | 实现反射提取器 | `Extractors/ReflectionExtractor.cs` | ✅ 已完成 |
| 14 | 实现 Roslyn 提取器 | `Extractors/RoslynExtractor.cs` | ✅ 已完成 |

### 阶段六：缓存模块（可选基础设施） ✅

| 步骤 | 内容 | 文件 | 状态 |
|------|------|------|------|
| 15 | 创建缓存接口 | `Cache/IAutoRegisterCache.cs` | ✅ 已完成 |
| 16 | 创建缓存键生成器 | `Cache/CacheKeyGenerator.cs` | ✅ 已完成 |
| 17 | 创建缓存清单模型 | `Cache/CachedManifest.cs` | ✅ 已完成 |
| 18 | 创建注册表快照 | `Cache/RegistrySnapshot.cs` | ✅ 已完成 |
| 19 | 实现文件缓存 | `Cache/FileAutoRegisterCache.cs` | ✅ 已完成 |

### 阶段七：核心服务（整合所有组件） ✅

| 步骤 | 内容 | 文件 | 状态 |
|------|------|------|------|
| 20 | 实现核心服务 | `AutoRegisterService.cs` | ✅ 已完成 |

### 阶段八：项目配置与示例 ✅

| 步骤 | 内容 | 文件 | 状态 |
|------|------|------|------|
| 21 | 创建独立项目文件 | `Share.Registry.AutoRegister.csproj` | ✅ 已完成 |
| 22 | 编写示例 JSON 配置 | `auto-register-config.json` | ✅ 已完成 |
| 23 | 更新解决方案 | `WorkFlowPrj.sln` | ⏳ 待完成（需手动添加项目引用） |

---

## 九、注意事项

### 9.1 依赖关系

- `ReflectionExtractor` 依赖 `BasicMethod.RefectionMethod` 命名空间
- `RoslynExtractor` 依赖 `BasicMethod.RoslynMethod.Generation` 和 `BasicMethod.RoslynMethod.Core`
- `AutoRegisterService` **不依赖** `Operation.FunctionStorage`，只依赖 `Share.Registry` 自身的 `FunctionRegistry`
- `Registry.csproj` 需要添加对 `BasicMethod` 的项目引用
  - `BasicMethod.csproj` 已引用 `Share.Registry`，注意不要造成循环引用
  - 由于 `BasicMethod` 已引用 `Share.Registry`，`Registry.csproj` 再引用 `BasicMethod` 会形成**循环引用**
  - **解决方案**：将 AutoRegister 模块独立为一个新项目 `Share.Registry.AutoRegister`，或通过接口/抽象层解耦
  - **推荐方案**：AutoRegister 模块放在 `Share/Registry/AutoRegister/` 目录下，但通过依赖注入或回调方式解耦对 `BasicMethod` 的直接引用

### 9.1a 循环依赖解决方案

由于 `BasicMethod.csproj` 已引用 `Share.Registry`，而 AutoRegister 需要引用 `BasicMethod`，直接添加引用会导致循环依赖。有两种解决方案：

**方案 A：独立项目（推荐）**
- 新建 `Share.Registry.AutoRegister.csproj`，同时引用 `Share.Registry` 和 `BasicMethod`
- 优点：依赖关系清晰，无循环引用
- 缺点：多一个项目文件

**方案 B：接口抽象**
- 在 `Share.Registry` 中定义 AutoRegister 所需的抽象接口
- 在 `BasicMethod` 中实现这些接口
- 通过依赖注入将实现注入到 AutoRegisterService
- 优点：不增加项目数
- 缺点：增加接口抽象层复杂度

> **当前设计采用方案 A**，将 AutoRegister 放在 `Share/Registry/AutoRegister/` 目录下，但新建独立项目 `Share.Registry.AutoRegister.csproj`。

### 9.2 性能考虑

- 反射提取器使用 `Cache<Type, TypeMetadata>` 缓存类型元数据
- Roslyn 提取器使用 `RoslynCacheManager` 缓存编译结果
- 委托工厂使用 `DynamicMethodExecutor` 的表达式树编译，避免 `MethodInfo.Invoke` 的性能开销
- Roslyn 路径的编译开销较大，建议在首次初始化时批量注册，避免运行时频繁编译
- 启用文件缓存后，后续启动可跳过扫描/编译，直接反序列化注册清单（毫秒级）

### 9.3 错误处理

- 每个注册条目独立执行，单个条目的失败不影响其他条目
- `AutoRegisterResult` 记录每个条目的详细状态（成功/失败/跳过）
- 提取器内部使用 try-catch 捕获异常，确保单个方法失败不影响其他方法

### 9.4 路径管理原则

- **框架不内置路径处理逻辑** — 路径由调用方管理
- 提取器只接收内存中的对象（`Assembly` 或 `string code`）
- JSON 配置中的 `path` 字段仅作为调用方的配置信息，框架不负责解析
- 调用方可根据需要实现自己的路径解析逻辑（如相对路径、环境变量、配置中心等）

---

## 十、缓存与延迟优化

### 10.1 延迟风险分析

当前设计中，以下环节可能产生打开时的延迟：

| 环节 | 延迟级别 | 说明 |
|------|---------|------|
| `Assembly.LoadFrom(path)` 加载程序集 | 低 | 磁盘 I/O，通常毫秒级 |
| `Type.GetMethods()` 反射扫描 | 低-中 | 方法数量相关，<50 个方法无感知 |
| `DynamicMethodExecutor.CreateInvokerDelegate()` 表达式树编译 | 中 | 每个方法首次调用时编译，一次性开销 |
| `CSharpSyntaxTree.ParseText(code)` 语法树解析 | 中 | 文件大小相关 |
| `DynamicCompilation.CompileAssembly(code)` Roslyn 编译 | **高** | **主要延迟来源**，可能数百毫秒到数秒 |
| 多文件批量注册 | **高** | 每个文件独立编译，可能数秒 |

### 10.2 三级缓存架构

```
┌─────────────────────────────────────────────────────────────────────────┐
│                        三级缓存架构                                      │
│                                                                          │
│  ┌──────────────────────────────────────────────────────────────────┐   │
│  │  第一级：内存缓存（MemoryCache）                                   │   │
│  │  • 存储：已编译的委托 + ExtractedMethodInfo                        │   │
│  │  • 生命周期：进程存活期间                                          │   │
│  │  • 用途：避免同一进程内重复注册                                    │   │
│  │  • 数据结构：ConcurrentDictionary<string, CachedFunction>         │   │
│  │  • 命中耗时：0（直接内存读取）                                    │   │
│  └──────────────────────────────────────────────────────────────────┘   │
│                              │                                           │
│                              ▼                                           │
│  ┌──────────────────────────────────────────────────────────────────┐   │
│  │  第二级：文件缓存（FileCache）                                     │   │
│  │  • 存储：序列化的注册清单（JSON）+ 预编译委托程序集（.dll）        │   │
│  │  • 生命周期：持久化到磁盘，跨进程/跨启动                           │   │
│  │  • 用途：避免重复反射扫描和 Roslyn 编译                            │   │
│  │  • 缓存键：源文件哈希 + 配置哈希 → 缓存文件路径                   │   │
│  │  • 命中耗时：毫秒级（反序列化 JSON）                              │   │
│  └──────────────────────────────────────────────────────────────────┘   │
│                              │                                           │
│                              ▼                                           │
│  ┌──────────────────────────────────────────────────────────────────┐   │
│  │  第三级：注册表快照（RegistrySnapshot）                            │   │
│  │  • 存储：AutoRegisterResult 的完整序列化快照                      │   │
│  │  • 生命周期：按需导出/导入                                        │   │
│  │  • 用途：快速恢复注册结果，跳过所有扫描和编译                      │   │
│  │  • 格式：JSON（可读）                                             │   │
│  │  • 命中耗时：毫秒级（反序列化）                                  │   │
│  └──────────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────┘
```

### 10.3 缓存键生成策略

缓存键用于唯一标识一组注册结果，由输入源的标识和配置的标识共同决定：

```
缓存键 = Hash(SourceIdentifier + ConfigIdentifier)

SourceIdentifier:
  - 反射路径：程序集文件路径 + 最后修改时间 + 文件大小
  - Roslyn 路径：源文件路径 + 最后修改时间 + 文件哈希(SHA256)

ConfigIdentifier:
  - className + methodFilters 序列化 + naming 序列化 + description 序列化
```

```csharp
/// <summary>
/// 缓存键生成器
/// </summary>
public static class CacheKeyGenerator
{
    /// <summary>为反射路径生成缓存键</summary>
    public static string GenerateForAssembly(string assemblyPath, RegistrationEntry entry)
    {
        var fileInfo = new FileInfo(assemblyPath);
        var sourceId = $"{assemblyPath}|{fileInfo.LastWriteTimeUtc:Ticks}|{fileInfo.Length}";
        var configId = JsonSerializer.Serialize(new
        {
            entry.ClassName,
            entry.MethodFilters,
            entry.Naming,
            entry.Description
        });
        return ComputeHash(sourceId + configId);
    }

    /// <summary>为 Roslyn 路径生成缓存键</summary>
    public static string GenerateForSource(string sourcePath, string sourceCode, RegistrationEntry entry)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(sourceCode));
        var sourceHash = Convert.ToHexString(hashBytes).ToLower();
        var configId = JsonSerializer.Serialize(new
        {
            entry.ClassName,
            entry.MethodFilters,
            entry.Naming,
            entry.Description
        });
        return ComputeHash(sourcePath + "|" + sourceHash + configId);
    }

    private static string ComputeHash(string input)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLower()[..16];
    }
}
```

### 10.4 文件缓存实现

#### 10.4.1 缓存目录结构

```
{AppData}/AutoRegisterCache/
├── v1/
│   ├── manifests/
│   │   ├── a1b2c3d4e5f6.json
│   │   └── ...
│   ├── assemblies/
│   │   ├── a1b2c3d4e5f6.dll
│   │   ├── a1b2c3d4e5f6.pdb
│   │   └── ...
│   └── snapshots/
│       ├── default.snapshot
│       └── ...
└── cache-index.json
```

#### 10.4.2 缓存文件格式

```json
{
  "version": "1.0",
  "createdAt": "2026-06-13T22:00:00",
  "sourceInfo": {
    "type": "assembly",
    "path": "Mathematics.dll",
    "fileHash": "sha256-xxxx",
    "lastModified": "2026-06-13T20:00:00"
  },
  "configHash": "sha256-yyyy",
  "registeredFunctions": [
    {
      "name": "Activation_ReLU",
      "className": "MathematicalAlgorithms.Maths",
      "methodName": "ReLU",
      "description": "修正线性单元激活函数",
      "parameters": [
        { "name": "x", "type": "double" }
      ],
      "returnType": "double"
    }
  ]
}
```

#### 10.4.3 缓存管理器接口

```csharp
public interface IAutoRegisterCache
{
    bool TryGetManifest(string cacheKey, out List<ExtractedMethodInfo> methods);
    void SaveManifest(string cacheKey, List<ExtractedMethodInfo> methods, SourceConfig source, string? configHash = null);
    bool TryGetCompiledAssembly(string cacheKey, out Assembly assembly);
    void SaveCompiledAssembly(string cacheKey, Assembly assembly);
    bool IsValid(string cacheKey, SourceConfig source);
    void Cleanup(TimeSpan maxAge);
}
```

#### 10.4.4 缓存管理器实现

```csharp
public class FileAutoRegisterCache : IAutoRegisterCache
{
    private readonly string _manifestDir;
    private readonly string _assemblyDir;
    private readonly string _snapshotDir;
    private readonly ConcurrentDictionary<string, List<ExtractedMethodInfo>> _memoryCache = new();

    public FileAutoRegisterCache(string? baseDir = null)
    {
        baseDir ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AutoRegisterCache", "v1");
        _manifestDir = Path.Combine(baseDir, "manifests");
        _assemblyDir = Path.Combine(baseDir, "assemblies");
        _snapshotDir = Path.Combine(baseDir, "snapshots");
        Directory.CreateDirectory(_manifestDir);
        Directory.CreateDirectory(_assemblyDir);
        Directory.CreateDirectory(_snapshotDir);
    }

    public string SnapshotDir => _snapshotDir;

    public bool TryGetManifest(string cacheKey, out List<ExtractedMethodInfo> methods)
    {
        if (_memoryCache.TryGetValue(cacheKey, out methods!))
            return true;

        var path = Path.Combine(_manifestDir, $"{cacheKey}.json");
        if (File.Exists(path))
        {
            try
            {
                var json = File.ReadAllText(path);
                var manifest = JsonSerializer.Deserialize<CachedManifest>(json);
                if (manifest != null)
                {
                    methods = manifest.ToExtractedMethodInfos();
                    _memoryCache[cacheKey] = methods;
                    return true;
                }
            }
            catch { /* 文件损坏时忽略 */ }
        }
        methods = null!;
        return false;
    }

    public void SaveManifest(string cacheKey, List<ExtractedMethodInfo> methods, SourceConfig source, string? configHash = null)
    {
        var manifest = CachedManifest.FromExtractedMethodInfos(methods, source, configHash);
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(_manifestDir, $"{cacheKey}.json"), json);
        _memoryCache[cacheKey] = methods;
    }

    public bool TryGetCompiledAssembly(string cacheKey, out Assembly assembly)
    {
        var dllPath = Path.Combine(_assemblyDir, $"{cacheKey}.dll");
        if (File.Exists(dllPath))
        {
            try
            {
                assembly = Assembly.LoadFrom(dllPath);
                return true;
            }
            catch { /* 程序集损坏时忽略 */ }
        }
        assembly = null!;
        return false;
    }

    public void SaveCompiledAssembly(string cacheKey, Assembly assembly)
    {
        var dllPath = Path.Combine(_assemblyDir, $"{cacheKey}.dll");
        if (!string.IsNullOrEmpty(assembly.Location) && File.Exists(assembly.Location))
        {
            try
            {
                File.Copy(assembly.Location, dllPath, overwrite: true);
                var pdbPath = Path.ChangeExtension(assembly.Location, ".pdb");
                if (File.Exists(pdbPath))
                    File.Copy(pdbPath, Path.Combine(_assemblyDir, $"{cacheKey}.pdb"), overwrite: true);
            }
            catch { /* 文件复制失败时忽略 */ }
        }
    }

    public bool IsValid(string cacheKey, SourceConfig source)
    {
        var path = Path.Combine(_manifestDir, $"{cacheKey}.json");
        if (!File.Exists(path)) return false;

        try
        {
            var json = File.ReadAllText(path);
            var manifest = JsonSerializer.Deserialize<CachedManifest>(json);
            if (manifest?.SourceInfo == null) return false;

            if (source.Type == "assembly" && File.Exists(source.Path))
            {
                var fi = new FileInfo(source.Path);
                return manifest.SourceInfo.LastModified == fi.LastWriteTimeUtc
                    && manifest.SourceInfo.FileSize == fi.Length;
            }

            if (source.Type == "source" && File.Exists(source.Path))
            {
                var currentHash = ComputeFileHash(source.Path);
                return manifest.SourceInfo.FileHash == currentHash;
            }

            return true;
        }
        catch { return false; }
    }

    public void Cleanup(TimeSpan maxAge)
    {
        var cutoff = DateTime.UtcNow - maxAge;
        if (Directory.Exists(_manifestDir))
        {
            foreach (var f in Directory.GetFiles(_manifestDir, "*.json"))
                try { if (File.GetLastWriteTimeUtc(f) < cutoff) File.Delete(f); } catch { }
        }
        if (Directory.Exists(_assemblyDir))
        {
            foreach (var f in Directory.GetFiles(_assemblyDir, "*.dll"))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(f) < cutoff)
                    {
                        File.Delete(f);
                        var pdb = Path.ChangeExtension(f, ".pdb");
                        if (File.Exists(pdb)) File.Delete(pdb);
                    }
                }
                catch { }
            }
        }
    }

    private static string ComputeFileHash(string path)
    {
        try
        {
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            using var stream = File.OpenRead(path);
            var hash = sha256.ComputeHash(stream);
            return Convert.ToHexString(hash).ToLower();
        }
        catch { return string.Empty; }
    }
}
```

### 10.5 注册表快照

> **说明**：`RegistrySnapshot` 是一个工具类，用于将 AutoRegister 的处理结果导出/导入为 JSON 快照。
> 快照仅包含元数据（名称、描述、参数等），不包含可执行委托。
> 调用方可根据快照信息自行重建注册表。

```csharp
public static class RegistrySnapshot
{
    /// <summary>将 AutoRegisterResult 导出为快照文件</summary>
    public static void Export(AutoRegisterResult result, string filePath)
    {
        var snapshot = new RegistrySnapshotData
        {
            Version = "1.0",
            ExportedAt = DateTime.UtcNow,
            FunctionCount = result.RegisteredItems.Count,
            Functions = result.RegisteredItems.Select(item => new SnapshotEntry
            {
                Name = item.RegisteredName,
                MethodName = item.MethodName,
                ClassName = item.ClassName,
                Description = item.Description,
                ReturnType = item.ReturnType?.FullName ?? "unknown",
                IsAsync = item.IsAsync,
                Parameters = item.Parameters?.Select(p => new SnapshotParameter
                {
                    Name = p.Name,
                    Type = p.Type?.FullName ?? "unknown",
                    IsOptional = p.IsOptional
                }).ToList() ?? new()
            }).ToList()
        };

        File.WriteAllText(filePath,
            JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>从快照文件导入注册信息</summary>
    public static RegistrySnapshotData Import(string filePath)
    {
        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<RegistrySnapshotData>(json)
            ?? throw new InvalidDataException("Invalid snapshot file");
    }
}

public class RegistrySnapshotData
{
    public string Version { get; set; } = "1.0";
    public DateTime ExportedAt { get; set; }
    public int FunctionCount { get; set; }
    public List<SnapshotEntry> Functions { get; set; } = new();
}

public class SnapshotEntry
{
    public string Name { get; set; } = string.Empty;
    public string MethodName { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ReturnType { get; set; } = string.Empty;
    public bool IsAsync { get; set; }
    public List<SnapshotParameter> Parameters { get; set; } = new();
}

public class SnapshotParameter
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool IsOptional { get; set; }
}
```

### 10.6 集成到 AutoRegisterService

> **说明**：AutoRegisterService 不直接调用任何注册表，只负责提取→过滤→命名→委托创建，返回 AutoRegisterResult。
> 缓存模块通过构造函数注入 `IAutoRegisterCache?`，由调用方决定是否启用。

```csharp
public class AutoRegisterService
{
    private readonly IAutoRegisterCache? _cache;

    public AutoRegisterService(IAutoRegisterCache? cache = null)
    {
        _cache = cache;
    }

    public AutoRegisterResult ProcessAssembly(Assembly assembly, RegistrationEntry entry)
    {
        var cacheKey = CacheKeyGenerator.GenerateForAssembly(assembly.Location, entry);

        if (_cache != null && _cache.TryGetManifest(cacheKey, out var cachedMethods))
            return BuildResult(cachedMethods, entry);

        var extractor = new ReflectionExtractor(assembly);
        var methods = extractor.ExtractMethods(entry.ClassName, entry.MethodFilters);
        _cache?.SaveManifest(cacheKey, methods, entry.Source);
        return BuildResult(methods, entry);
    }

    public AutoRegisterResult ProcessSource(string sourceCode, RegistrationEntry entry, string? sourcePath = null)
    {
        // 尝试从缓存获取已编译的程序集
        if (sourcePath != null && _cache != null)
        {
            var cacheKey = CacheKeyGenerator.GenerateForSource(sourcePath, sourceCode, entry);

            // 先尝试从缓存获取已编译的程序集
            if (_cache.TryGetCompiledAssembly(cacheKey, out var cachedAssembly))
            {
                // 使用缓存的程序集通过反射提取
                var extractor = new ReflectionExtractor(cachedAssembly);
                var methods = extractor.ExtractMethods(entry.ClassName, entry.MethodFilters);
                return BuildResult(methods, entry);
            }

            // 再尝试从缓存获取清单
            if (_cache.TryGetManifest(cacheKey, out var cachedMethods))
            {
                return BuildResult(cachedMethods, entry);
            }
        }

        // 执行 Roslyn 提取（编译在 ExtractMethods 内部完成）
        var roslynExtractor = new RoslynExtractor(sourceCode);
        var methods2 = roslynExtractor.ExtractMethods(entry.ClassName, entry.MethodFilters);

        // 保存到缓存
        if (sourcePath != null && _cache != null)
        {
            var cacheKey = CacheKeyGenerator.GenerateForSource(sourcePath, sourceCode, entry);
            _cache.SaveManifest(cacheKey, methods2, entry.Source);

            var compiledAssembly = roslynExtractor.GetCompiledAssembly();
            if (compiledAssembly != null)
            {
                _cache.SaveCompiledAssembly(cacheKey, compiledAssembly);
            }
        }

        return BuildResult(methods2, entry);
    }

    private AutoRegisterResult BuildResult(List<ExtractedMethodInfo> methods, RegistrationEntry entry)
    {
        var result = new AutoRegisterResult();

        // 获取命名规则
        var namingConvention = NamingConventions.GetByName(
            entry.Naming?.Convention ?? "MethodName");

        // 获取描述提供者
        var descriptionProvider = DescriptionProviders.GetByName(
            entry.Description?.Source ?? "none");

        foreach (var method in methods)
        {
            try
            {
                // 生成注册名称
                var registeredName = namingConvention.GenerateName(
                    method.ContainingClassName,
                    method.ContainingNestedClassName,
                    method.MethodName,
                    entry.Naming ?? new NamingConfig());

                // 提取描述
                var description = descriptionProvider.GetDescription(method);
                if (string.IsNullOrEmpty(description) && entry.Description?.Fallback != null)
                {
                    description = entry.Description.Fallback;
                }

                result.AddItem(registeredName, method, description);
            }
            catch (Exception ex)
            {
                result.AddFailure(method.MethodName, ex.Message);
            }
        }

        return result;
    }
}
```

### 10.7 优化后的启动流程

> **说明**：以下流程中"注册到 FunctionStorage"是调用方行为，AutoRegisterService 只负责提取→过滤→命名→委托创建。

```
应用启动
    │
    ▼
调用方加载 JSON 配置
    │
    ▼
遍历每个 RegistrationEntry
    │
    ├── 调用 AutoRegisterService.ProcessAssembly() / ProcessSource()
    │       │
    │       ├── 计算缓存键（源文件哈希 + 配置哈希）
    │       │
    │       ├── 检查文件缓存是否有效？
    │       │   ├── 是 → 从缓存读取注册清单 → 返回 AutoRegisterResult
    │       │   │
    │       │   └── 否 → 执行完整扫描/编译
    │       │           → 写入缓存
    │       │           → 返回 AutoRegisterResult
    │       │
    │       └── 返回 AutoRegisterResult（毫秒级）
    │
    └── 调用方消费 AutoRegisterResult
            ├── 遍历 result.RegisteredItems
            ├── 注册到 FunctionStorageRegistry
            │   → FunctionStorageRegistry.Register(name, executor, desc)
            └── 可选：导出注册表快照供下次快速启动
```

### 10.8 延迟对比

| 场景 | 无缓存 | 有缓存（文件缓存命中） | 有缓存（快照命中） |
|------|--------|----------------------|-------------------|
| 反射路径，50 个方法 | ~50ms | ~5ms | ~2ms |
| 反射路径，200 个方法 | ~200ms | ~10ms | ~5ms |
| Roslyn 路径，单文件 | ~500ms-2s | ~10ms | ~5ms |
| Roslyn 路径，5 个文件 | ~2.5s-10s | ~50ms | ~5ms |
| 混合路径，10 个条目 | ~1s-5s | ~50ms | ~5ms |

---

## 十一、附录：Maths.cs 注册示例

### 11.1 目标类结构

`MathematicalAlgorithms.Maths` 包含以下嵌套类和方法（示例）：

| 嵌套类 | 方法 | 参数 | 返回类型 |
|--------|------|------|---------|
| `Activation` | `ReLU` | `double x` | `double` |
| `Activation` | `Sigmoid` | `double x` | `double` |
| `Activation` | `Tanh` | `double x` | `double` |
| `Distance` | `Euclidean` | `double[] a, double[] b` | `double` |
| `Distance` | `Manhattan` | `double[] a, double[] b` | `double` |
| `Vector` | `DotProduct` | `double[] a, double[] b` | `double` |
| `Vector` | `Normalize` | `double[] v` | `double[]` |
| `Statistics` | `Mean` | `double[] data` | `double` |
| `Statistics` | `StdDev` | `double[] data` | `double` |

### 11.2 AutoRegisterResult 输出示例

> **说明**：以下为 AutoRegisterService 处理完成后返回的 AutoRegisterResult 内容示例。
> 调用方拿到结果后，可自行决定如何消费（如注册到 FunctionStorageRegistry）。

| 注册名称 | 对应方法 | 描述 |
|---------|---------|------|
| `Activation_ReLU` | `Maths.Activation.ReLU` | 修正线性单元激活函数 |
| `Activation_Sigmoid` | `Maths.Activation.Sigmoid` | Sigmoid 激活函数 |
| `Activation_Tanh` | `Maths.Activation.Tanh` | 双曲正切激活函数 |
| `Distance_Euclidean` | `Maths.Distance.Euclidean` | 欧几里得距离 |
| `Distance_Manhattan` | `Maths.Distance.Manhattan` | 曼哈顿距离 |
| `Vector_DotProduct` | `Maths.Vector.DotProduct` | 向量点积 |
| `Vector_Normalize` | `Maths.Vector.Normalize` | 向量归一化 |
| `Statistics_Mean` | `Maths.Statistics.Mean` | 均值计算 |
| `Statistics_StdDev` | `Maths.Statistics.StdDev` | 标准差计算 |

### 11.3 对应的 JSON 配置

```json
{
  "registrations": [
    {
      "source": {
        "type": "assembly",
        "path": "Mathematics.dll",
        "sourceFile": "Mathematics/Maths.cs"
      },
      "className": "MathematicalAlgorithms.Maths",
      "methodFilters": {
        "includeNestedClasses": ["Activation", "Distance", "Vector", "Statistics"],
        "excludeByReturnType": ["void"],
        "maxParams": 5,
        "includeStaticOnly": true,
        "excludeMethodNames": ["GetHashCode", "Equals", "ToString", "GetType"]
      },
      "naming": {
        "convention": "NestedClass_MethodName",
        "separator": "_"
      },
      "description": {
        "source": "xmlDoc",
        "fallback": "Auto-registered method from MathematicalAlgorithms.Maths"
      }
    }
  ]
}
```
       