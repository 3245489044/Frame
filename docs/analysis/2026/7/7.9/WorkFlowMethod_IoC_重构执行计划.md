# WorkFlowMethod IoC 重构执行计划

> **文档状态**：执行计划  
> **创建日期**：2026-07-08  
> **目标**：根据 `WorkFlowMethod_IoC_重构方案.md` 制定精确到文件、精确到行的代码重构步骤  
> **前置文档**：`docs/analysis/WorkFlowMethod_IoC_重构方案.md`

---

## 一、当前状态总览

### 1.1 已完成的工作

| 步骤 | 文件 | 状态 | 说明 |
|------|------|------|------|
| Step 1 | `NodeExecutor.cs` | ✅ **已完成** | 构造函数已改为接收 `IFunctionExecutor` 接口 |
| Step 2 | `WorkflowMethodExecutionService.cs` | ✅ **已完成** | 构造函数已注入 `IFunctionRegistry` + `INodeExecutor` |
| Step 3 | `FrameworkInitializer.cs` | ✅ **已完成** | `InitializeAppServiceProvider()` 已包含完整 DI 注册 |

### 1.2 待完成的工作

| 步骤 | 涉及文件 | 状态 | 预估工时 |
|------|---------|------|---------|
| Step 4 | 新增 `FunctionRegistrationLoader.cs` + JSON 配置 | ❌ 待完成 | 2h |
| Step 5 | `FileDrivenLifecycle.cs` | ❌ 待完成 | 1h |
| Step 6 | `WorkFlowMethodUsage.cs` | ❌ 待完成 | 1h |
| Step 7 | 编译验证 + 修复 | ❌ 待完成 | 1h |

---

## 二、Step 4：创建配置驱动注册器

### 2.1 新增文件：`FunctionRegistrationLoader.cs`

**路径**：`FileDriven/FileDriven.Business/Registrations/FunctionRegistrationLoader.cs`

**说明**：从 JSON 配置文件加载函数注册，替代 `MathLibrary.RegisterAll(_registry)` 等硬编码调用。

**代码**（直接复制自重构方案 6.2 节，需调整命名空间）：

```csharp
// ============================================================
// FunctionRegistrationLoader.cs — 函数注册加载器
//
// 从 JSON 配置文件加载函数注册，支持 delegate 和 workflow 两种注册类型。
// 替代 MathLibrary.RegisterAll(_registry) 等硬编码调用。
// ============================================================

using System.Reflection;
using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TConfig;

namespace FileDriven.Business.Registrations
{
    /// <summary>
    /// 函数注册加载器 — 从 JSON 配置加载函数注册
    /// <para>支持 delegate 和 workflow 两种注册类型</para>
    /// </summary>
    public class FunctionRegistrationLoader
    {
        private readonly IFunctionRegistry _registry;
        private readonly ILogger? _logger;

        public FunctionRegistrationLoader(IFunctionRegistry registry, ILogger? logger = null)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _logger = logger;
        }

        /// <summary>
        /// 从配置文件加载注册
        /// </summary>
        /// <param name="configPath">JSON 配置文件路径</param>
        public void LoadFromConfig(string configPath)
        {
            if (!File.Exists(configPath))
            {
                _logger?.LogWarning($"Registration config not found: {configPath}");
                return;
            }

            var json = File.ReadAllText(configPath);
            var config = JsonSerializer.Deserialize<RegistrationConfig>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (config?.Registrations == null || config.Registrations.Count == 0)
            {
                _logger?.LogWarning($"No registrations found in: {configPath}");
                return;
            }

            foreach (var reg in config.Registrations)
            {
                try
                {
                    switch (reg.Type?.ToLowerInvariant())
                    {
                        case "delegate":
                            RegisterDelegate(reg);
                            break;
                        case "workflow":
                            RegisterWorkflow(reg);
                            break;
                        default:
                            _logger?.LogWarning($"Unknown registration type '{reg.Type}' for '{reg.Name}'");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError($"Failed to register '{reg.Name}': {ex.Message}");
                }
            }
        }

        private void RegisterDelegate(RegistrationEntry reg)
        {
            // 通过反射加载类型和方法
            var type = Type.GetType(reg.Source);
            if (type == null)
                throw new InvalidOperationException($"Type not found: {reg.Source}");

            var method = type.GetMethod(reg.Method,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (method == null)
                throw new InvalidOperationException($"Method not found: {type.FullName}.{reg.Method}");

            // 创建委托
            var delegateType = GetDelegateType(method);
            var dlg = method.CreateDelegate(delegateType);

            // 构建参数配置
            var paramConfigs = reg.Parameters?.Select(p => new ParamConfig
            {
                Name = p.Name,
                Type = p.Type,
                IsOptional = p.IsOptional,
                DefaultValue = p.DefaultValue
            }).ToArray() ?? Array.Empty<IParamConfig>();

            // 注册到注册表
            _registry.Register(reg.Name, dlg, paramConfigs, reg.Description);
            _logger?.LogInformation($"Registered delegate function: {reg.Name}");
        }

        private void RegisterWorkflow(RegistrationEntry reg)
        {
            // 注册工作流引用
            var trConfig = TrConfig.FromWorkflowRef(reg.WorkflowRef);
            trConfig.Name = reg.Name;
            trConfig.Description = reg.Description;
            ((ITrConfigRegistry)_registry).Register(reg.Name, trConfig);
            _logger?.LogInformation($"Registered workflow function: {reg.Name}");
        }

        private static Type GetDelegateType(MethodInfo method)
        {
            var paramTypes = method.GetParameters()
                .Select(p => p.ParameterType)
                .ToArray();

            if (method.ReturnType == typeof(void))
                return Expression.GetActionType(paramTypes);
            else
                return Expression.GetFuncType(paramTypes.Concat(new[] { method.ReturnType }).ToArray());
        }
    }

    // ─── 配置模型 ───

    public class RegistrationConfig
    {
        public List<RegistrationEntry> Registrations { get; set; } = new();
    }

    public class RegistrationEntry
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "delegate";  // "delegate" | "workflow"
        public string Source { get; set; } = "";         // 程序集限定类型名
        public string Method { get; set; } = "";         // 方法名
        public string WorkflowRef { get; set; } = "";    // 工作流文件引用
        public List<ParameterConfig> Parameters { get; set; } = new();
        public string Description { get; set; } = "";
    }

    public class ParameterConfig
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "System.String";
        public bool IsOptional { get; set; }
        public string DefaultValue { get; set; } = "";
    }
}
```

### 2.2 新增目录和配置文件

**目录**：`FileDriven/DrivenConfig/FunctionRegistrations/`

**新增文件**：`FileDriven/DrivenConfig/FunctionRegistrations/MathFunctions.registration.json`

```json
{
  "registrations": [
    {
      "name": "Add",
      "type": "delegate",
      "source": "FileDriven.Business.Registrations.MathLibrary, FileDriven.Business",
      "method": "Add",
      "parameters": [
        { "name": "a", "type": "System.Double" },
        { "name": "b", "type": "System.Double" }
      ],
      "description": "加法运算: a + b"
    },
    {
      "name": "Subtract",
      "type": "delegate",
      "source": "FileDriven.Business.Registrations.MathLibrary, FileDriven.Business",
      "method": "Subtract",
      "parameters": [
        { "name": "a", "type": "System.Double" },
        { "name": "b", "type": "System.Double" }
      ],
      "description": "减法运算: a - b"
    },
    {
      "name": "Multiply",
      "type": "delegate",
      "source": "FileDriven.Business.Registrations.MathLibrary, FileDriven.Business",
      "method": "Multiply",
      "parameters": [
        { "name": "a", "type": "System.Double" },
        { "name": "b", "type": "System.Double" }
      ],
      "description": "乘法运算: a * b"
    },
    {
      "name": "Divide",
      "type": "delegate",
      "source": "FileDriven.Business.Registrations.MathLibrary, FileDriven.Business",
      "method": "Divide",
      "parameters": [
        { "name": "a", "type": "System.Double" },
        { "name": "b", "type": "System.Double" }
      ],
      "description": "除法运算: a / b"
    },
    {
      "name": "SetValue",
      "type": "workflow",
      "workflowRef": "Workflows/SetValue.workflow.json",
      "description": "设置值"
    }
  ]
}
```

### 2.3 注意事项

1. **命名空间**：`FileDriven.Business.Registrations` — 与 `MathLibrary` 同命名空间
2. **程序集限定名**：`source` 字段使用 `类型全名, 程序集名` 格式（如 `FileDriven.Business.Registrations.MathLibrary, FileDriven.Business`）
3. **路径解析**：配置文件路径使用 `FileDriven.Utilities.Path.ProjectPathManager` 或 `AppDomain.CurrentDomain.BaseDirectory` 解析
4. **`TrConfig.FromWorkflowRef`**：需确认该方法是否存在，若不存在则使用 `TrConfig.CreateWorkflowRef(workflowRef)`

---

## 三、Step 5：修改 FileDrivenLifecycle.cs

### 3.1 文件路径

`FileDriven/FileDriven.Business/FileDrivenLifecycle.cs`

### 3.2 当前代码分析

`FileDrivenLifecycle` 构造函数已接收 `IFunctionRegistry` 参数。`Initialize()` 方法中需要将 `MathLibrary.RegisterAll(_registry)` 等硬编码调用改为配置驱动。

### 3.3 修改内容

**位置**：`FileDrivenLifecycle.Initialize()` 方法中，找到 `MathLibrary.RegisterAll(_registry)` 调用处

**当前代码**（约第 50-70 行）：
```csharp
// 注册数学函数
MathLibrary.RegisterAll(_registry);
// 注册内置单位函数
Libraries.BuiltInUnitsLibrary.RegisterAll(_registry);
// 注册其他函数...
```

**修改后**：
```csharp
// ★ IoC 重构：使用配置驱动注册替代硬编码
var loader = new FunctionRegistrationLoader(_registry);

// 加载所有注册配置文件
var configDir = Path.Combine(
    AppDomain.CurrentDomain.BaseDirectory,
    "DrivenConfig", "FunctionRegistrations");

if (Directory.Exists(configDir))
{
    foreach (var configFile in Directory.GetFiles(configDir, "*.registration.json"))
    {
        loader.LoadFromConfig(configFile);
    }
}
else
{
    // 回退：使用代码注册（配置目录不存在时）
    MathLibrary.RegisterAll(_registry);
    Libraries.BuiltInUnitsLibrary.RegisterAll(_registry);
}
```

### 3.4 注意事项

1. **保留回退**：当配置目录不存在时，回退到代码注册方式
2. **添加 using**：需要添加 `using FileDriven.Business.Registrations;`
3. **`FunctionRegistrationLoader` 的 `ILogger` 参数**：如果 `FileDrivenLifecycle` 没有 `ILogger`，可以传 `null`

---

## 四、Step 6：消除静态门面

### 4.1 文件路径

`CsharpPrj/Operation/WorkFlowMethod/WorkflowMethodUseage.cs`

### 4.2 当前代码分析

`WorkFlowMethodUsage` 类包含多个静态属性（如 `BasicRegistry`），这些属性通过 `RegistryCoordinator` 访问共享注册表。重构后应改为实例属性，内部转发到 DI 实例。

### 4.3 修改内容

**修改 `WorkFlowMethodUsage` 类**：

```csharp
public class WorkFlowMethodUsage : IMethodUsageProvider
{
    // ★ 保留静态 Instance 作为向后兼容入口
    public static WorkFlowMethodUsage Instance { get; } = new();

    // ★ 实例字段替代静态属性
    private IFunctionRegistry? _registry;
    private IFunctionExecutor? _executor;
    private IWorkflowRegistry? _workflowRegistry;

    /// <summary>
    /// 初始化实例（由 DI 容器或 FrameworkInitializer 调用）
    /// </summary>
    public void Initialize(
        IFunctionRegistry registry,
        IWorkflowRegistry workflowRegistry,
        IFunctionExecutor executor)
    {
        _registry = registry;
        _workflowRegistry = workflowRegistry;
        _executor = executor;
    }

    /// <summary>
    /// 获取注册表（优先使用实例字段，回退到 RegistryCoordinator）
    /// </summary>
    public IFunctionRegistry Registry =>
        _registry ?? (IFunctionRegistry)Share.Registry.RegistryCoordinator.Registry;

    /// <summary>
    /// 获取执行器
    /// </summary>
    public IFunctionExecutor Executor =>
        _executor ?? throw new InvalidOperationException("WorkFlowMethodUsage 未初始化");
}
```

### 4.4 需要更新的调用点

全局搜索以下模式，逐一更新：

| 搜索模式 | 替换为 | 说明 |
|---------|--------|------|
| `WorkFlowMethodUsage.BasicRegistry` | `WorkFlowMethodUsage.Instance.Registry` | 静态属性 → 实例属性 |
| `WorkFlowMethodUsage.WorkflowRegistry` | `WorkFlowMethodUsage.Instance.WorkflowRegistry` | 同上 |
| `WorkFlowMethodUsage.FunctionExecutor` | `WorkFlowMethodUsage.Instance.Executor` | 同上 |

### 4.5 注意事项

1. **向后兼容**：保留 `BasicRegistry` 等静态属性作为委托，内部转发到 `Instance.Registry`
2. **`IMethodUsageProvider` 接口**：确保 `WorkFlowMethodUsage` 实现该接口
3. **`FileDrivenMethodUsage`**：如果存在类似的静态门面，同步改造

---

## 五、Step 7：编译验证

### 5.1 构建命令

```bash
cd CsharpPrj
dotnet build MainProj.sln 2>&1 | findstr /i "error"
```

### 5.2 预期错误及修复

| 错误类型 | 可能原因 | 修复方案 |
|---------|---------|---------|
| `CS0246: 找不到类型或命名空间"INodeExecutor"` | `WorkflowMethodExecutionService.cs` 缺少 `using WorkflowExecution.Abstractions;` | 添加 `using WorkflowExecution.Abstractions;` |
| `CS0117: "WorkFlowMethodUsage" 不包含"BasicRegistry"的定义` | 静态属性已移除 | 改为 `WorkFlowMethodUsage.Instance.Registry` |
| `CS1061: "IFunctionRegistry" 不包含"Register"的定义` | `Register` 方法在 `ITrConfigRegistry` 上 | 强制转换为 `ITrConfigRegistry` |
| `CS0246: 找不到类型"FunctionRegistrationLoader"` | 缺少 using | 添加 `using FileDriven.Business.Registrations;` |

### 5.3 运行时验证

1. 启动应用，观察日志中 `count` 是否 > 0
2. 触发按钮事件，验证 `Add` 等函数是否能正确执行
3. 检查日志中是否有 `Registered delegate function: Add` 等注册成功日志

---

## 六、执行顺序

```
Step 4a: 创建 FunctionRegistrationLoader.cs
    ↓
Step 4b: 创建 DrivenConfig/FunctionRegistrations/ 目录 + JSON 配置文件
    ↓
Step 5: 修改 FileDrivenLifecycle.cs（配置驱动注册）
    ↓
Step 6: 修改 WorkFlowMethodUsage.cs（消除静态门面）
    ↓
Step 7a: dotnet build 编译验证
    ↓
Step 7b: 运行时验证（启动应用 + 触发事件）
```

---

## 七、回退方案

如果某个步骤导致编译错误且无法快速修复：

1. **Step 5 回退**：注释掉配置驱动注册代码，恢复 `MathLibrary.RegisterAll(_registry)` 等硬编码调用
2. **Step 6 回退**：保留 `BasicRegistry` 等静态属性，内部转发到 `RegistryCoordinator`
3. **整体回退**：`git checkout -- <file>` 恢复单个文件

---

## 八、附录：关键文件路径速查

| 文件 | 路径 |
|------|------|
| 重构方案 | `docs/analysis/WorkFlowMethod_IoC_重构方案.md` |
| 本执行计划 | `docs/analysis/WorkFlowMethod_IoC_重构执行计划.md` |
| NodeExecutor | `CsharpPrj/Operation/WorkFlowMethod/WorkflowExecution/Engine/NodeExecutor.cs` |
| WorkflowMethodExecutionService | `CsharpPrj/Operation/WorkFlowMethod/WorkflowMethodExecutionService.cs` |
| FrameworkInitializer | `FileDriven/FileDriven.Execution/Lifecycle/FrameworkInitializer.cs` |
| FileDrivenLifecycle | `FileDriven/FileDriven.Business/FileDrivenLifecycle.cs` |
| WorkFlowMethodUsage | `CsharpPrj/Operation/WorkFlowMethod/WorkflowMethodUseage.cs` |
| FunctionRegistrationLoader（新增） | `FileDriven/FileDriven.Business/Registrations/FunctionRegistrationLoader.cs` |
| MathFunctions 配置（新增） | `FileDriven/DrivenConfig/FunctionRegistrations/MathFunctions.registration.json` |
| TypeAliases | `CsharpPrj/Modules/ExecutionFramework/TypeAliases.cs` |
| Directory.Build.props | `CsharpPrj/Directory.Build.props` |
