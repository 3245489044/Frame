# FunctionLibraryLoader 反射装载重构 —— 插件层（scoped ALC + AssemblyDependencyResolver + PrjBridge 聚合器）实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把「被反射部署、不在 deps.json 的扩展程序集（Agent.\* / GameEditor.\* / PrjBridge.dll）」升级为真正的**插件层**：PrjBridge 作为扩展层聚合器（单一真源），其构建输出（DLL 全闭包 + `PrjBridge.deps.json`）整体部署到 FileDriven 运行目录，运行时由专属 scoped ALC 经 `AssemblyDependencyResolver` 按 .NET 原生语义解析装载。移除 `AssemblyLoadContext.Default.Resolving` 全局兜底与 `ReferenceOutputAssembly=false`，实现「部署即注册、拷贝清单==装载清单」。

**Architecture:** 三层解耦。① 构建期：PrjBridge 正常引用 Agent.\* 与 Engine.\*（去掉 Engine 的 `ReferenceOutputAssembly=false`），自身 `PrjBridge.deps.json` 自然涵盖扩展全闭包（含 native 资产）；部署目标把 PrjBridge 整个输出递归搬进 FileDriven 运行目录。② 运行期：`ExtensionAssemblyHost` 持 `ExtensionLoadContext`（scoped ALC），以 `AssemblyDependencyResolver(PrjBridge.deps.json)` 解析；`Load` 覆写先复用默认 ALC 已加载的同名程序集（防 FileDriven.Abstractions 类型身份分裂），再走 resolver。③ 消费端：`FunctionLibraryLoader` 与 `PrjBridgeWiringHook` 一律从 scoped ALC 显式发现/装载。FileDriven 仍不引用 `Projects\*`，红线与编译瘦身保留。

**Tech Stack:** .NET 10 (`net10.0-windows`)、`System.Runtime.Loader.AssemblyLoadContext` / `AssemblyDependencyResolver`、MSBuild Target、MSTest。

***

## 方案取舍（为何这是"更完善"路径）

相较「scoped ALC + 手写 manifest.txt」：用平台原生 `AssemblyDependencyResolver` 处理传递依赖/版本区间/native 资产/卫星程序集；复用 PrjBridge 已生成的 `PrjBridge.deps.json` 作为装配清单（不再自维护第三份文件）；移除 `ReferenceOutputAssembly=false`（Engine 闭包首次被 deps.json 捕获）。
不选 6.3（FileDriven 直接引用 PrjBridge）：虽最原生，但把 Engine→D3D11→Win32 依赖拖回 FileDriven 编译图（编译膨胀回归）且字面越过红线。本路径的代价（PrjBridge 构建含 Engine 闭包）**仅限构建期**，运行期惰性加载，FileDriven 侧无膨胀。

## 现状基线（改动前）

- `CsharpPrj\Modules\Registry\FunctionLibraryLoader.cs`：静态构造注册 `AssemblyLoadContext.Default.Resolving`（L19-26）；`ResolveTypeFromQualifiedName`（L133-167）先 `Type.GetType` 后 `Default.LoadFromAssemblyPath`。

- `Projects\PrjBridge\PrjBridge.csproj`：Engine 5 项 `ReferenceOutputAssembly="false"`（L25-29）；`DeployBridgeToFileDrivenOutput`（L42-70）从 Agent/Engine 权威输出目录分别拷贝；`StripProjectsCopiesFromBridgeBin`（L76-84）删除 bin 内 Agent/Engine 副本。

- `FileDriven.Execution\Lifecycle\PostInitHooks\PrjBridgeWiringHook.cs`：`Assembly.Load("PrjBridge")`（L25）依赖全局 `Resolving`。

- `FileDriven.Execution\Initial\FrameworkInitializer.cs`（L78-98）：Registry 阶段调 `FunctionLibraryLoader.Load(functions, configPath, autoDiscover:false)`。

- `FileDriven\DrivenConfig\Configs\function-libraries.json`：16 库，其中 6 个为扩展程序集（Agent.Launcher/Core/Guardrails/McpRegistryHost + GameEditor.Host）。

**不变量：** FileDriven 正常库（FileDriven.Business / FrontEndDriven / Operation.StateBlock）在 FileDriven deps.json 内 → `Type.GetType` 走默认 ALC；仅扩展层走 scoped ALC。

***

## 文件结构

| 操作     | 文件                                                                       | 职责                                                                        |
| ------ | ------------------------------------------------------------------------ | ------------------------------------------------------------------------- |
| Create | `CsharpPrj\Modules\Registry\ExtensionAssemblyHost.cs`                    | 静态宿主 + `ExtensionLoadContext`；定位 deps.json、建 scoped ALC、按名解析类型/程序集、可选部署校验 |
| Modify | `Projects\PrjBridge\PrjBridge.csproj`                                    | Engine 改正常引用；部署目标改「整体搬 PrjBridge 输出」；删 Strip 目标                           |
| Modify | `CsharpPrj\Modules\Registry\FunctionLibraryLoader.cs`                    | 移除全局 `Resolving`；`ResolveTypeFromQualifiedName` 改「默认 ALC → scoped ALC」两段式 |
| Modify | `FileDriven.Execution\Lifecycle\PostInitHooks\PrjBridgeWiringHook.cs`    | `Assembly.Load("PrjBridge")` → `ExtensionAssemblyHost.Load("PrjBridge")`  |
| Test   | `CentralTest\CSharpTest\Integration.Tests\ExtensionAssemblyHostTests.cs` | scoped ALC 显式发现 + 默认 ALC 回退                                               |

***

## Task 1: 新增 ExtensionAssemblyHost（AssemblyDependencyResolver 插件装载）

**Files:**

- Create: `CsharpPrj\Modules\Registry\ExtensionAssemblyHost.cs`

- [ ] **Step 1: 创建文件**

```csharp
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

namespace Modules.Registry;

/// <summary>
/// 扩展程序集宿主 —— 插件层装载入口。
/// 扩展层（Agent.* / GameEditor.* / PrjBridge.dll）不在 FileDriven 的 deps.json，
/// 由 PrjBridge 聚合其输出整体部署到运行目录，并携带自身 PrjBridge.deps.json（扩展全闭包清单）。
/// 启动期用 AssemblyDependencyResolver 定位该 deps.json，建专属 scoped ALC 按 .NET 原生语义装载
/// （传递依赖/版本/native 均由平台处理）。缺失 deps.json 时保持空 ALC，降级（契合 TryRegister* 约定）。
/// </summary>
public static class ExtensionAssemblyHost
{
    private static readonly object _gate = new();
    private static ExtensionLoadContext? _context;
    private static bool _initialized;
    private static string? _depsJsonPath;

    /// <summary>是否已初始化。</summary>
    public static bool IsInitialized => _initialized;

    /// <summary>定位到的扩展 deps.json（未部署时为 null）。</summary>
    public static string? DepsJsonPath => _depsJsonPath;

    /// <summary>启动期调用一次（幂等）：在运行目录定位扩展 deps.json 并建 scoped ALC。</summary>
    public static void Initialize(string? depsJsonPath = null)
    {
        lock (_gate)
        {
            if (_initialized)
                return;
            _initialized = true;

            _depsJsonPath = depsJsonPath
                ?? Path.Combine(AppContext.BaseDirectory, "PrjBridge.deps.json");

            if (!File.Exists(_depsJsonPath))
            {
                _context = null; // 未部署 → 降级
                return;
            }

            _context = new ExtensionLoadContext(_depsJsonPath);
        }
    }

    /// <summary>按程序集限定名在 scoped ALC 里解析类型（显式发现，不撞 Type.GetType 运气）。</summary>
    public static Type? ResolveType(string typeStr)
    {
        var context = EnsureInitialized();
        if (context == null)
            return null;

        var comma = typeStr.IndexOf(',');
        if (comma < 0)
            return null;

        var fullTypeName = typeStr.Substring(0, comma).Trim();
        var asmName = typeStr.Substring(comma + 1).Trim();
        var nextComma = asmName.IndexOf(',');
        if (nextComma >= 0)
            asmName = asmName.Substring(0, nextComma).Trim();

        if (fullTypeName.Length == 0 || asmName.Length == 0)
            return null;

        try
        {
            var asm = context.Load(asmName);
            return asm?.GetType(fullTypeName, throwOnError: false);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>按程序集简单名在 scoped ALC 里加载（供 PrjBridgeWiringHook 等反射装载用）。</summary>
    public static Assembly? Load(string simpleName)
    {
        var context = EnsureInitialized();
        return context?.Load(simpleName);
    }

    /// <summary>
    /// 部署校验：解析 PrjBridge.deps.json 的 runtime/native 资产，报告磁盘缺失项（快速定位缺哪个依赖）。
    /// 仅诊断用，不抛异常。
    /// </summary>
    public static IReadOnlyList<string> ValidateDeployment()
    {
        var missing = new List<string>();
        if (!InitializeOnceAndTryGetDepsJson(out var depsPath, out _))
            return missing;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(depsPath));
            var root = doc.RootElement;
            if (!root.TryGetProperty("libraries", out var libs))
                return missing;

            foreach (var lib in libs.EnumerateObject())
            {
                if (!lib.Value.TryGetProperty("runtime", out var runtime))
                    continue;
                foreach (var asset in runtime.EnumerateObject())
                {
                    var rel = asset.Name;
                    if (!rel.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!File.Exists(Path.Combine(AppContext.BaseDirectory, rel)))
                        missing.Add(rel);
                }
            }
        }
        catch
        {
            // 解析失败仅影响诊断
        }

        return missing;
    }

    private static bool InitializeOnceAndTryGetDepsJson(out string? depsPath, out ExtensionLoadContext? context)
    {
        context = EnsureInitialized();
        depsPath = _depsJsonPath;
        return context != null && depsPath != null;
    }

    private static ExtensionLoadContext? EnsureInitialized()
    {
        if (!_initialized)
            Initialize();
        return _context;
    }
}

/// <summary>
/// 专属扩展装载上下文：以 AssemblyDependencyResolver 从扩展 deps.json 解析。
/// Load 覆写先复用默认 ALC 已加载的同名程序集（防 FileDriven.Abstractions 等类型身份分裂），
/// 再用 resolver 定位 DLL；找不到返回 null → 交还默认 ALC。
/// </summary>
public sealed class ExtensionLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    public ExtensionLoadContext(string depsJsonPath)
        : base("ExtensionContext", isCollectible: false)
    {
        _resolver = new AssemblyDependencyResolver(depsJsonPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name == null)
            return null;

        // 默认 ALC 已加载同名程序集则复用，避免同一类型两份身份
        foreach (var loaded in AssemblyLoadContext.Default.Assemblies)
        {
            if (string.Equals(loaded.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase))
                return loaded;
        }

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path != null ? LoadFromAssemblyPath(path) : null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path != null ? LoadUnmanagedDllFromPath(path) : IntPtr.Zero;
    }
}
```

- [ ] **Step 2: 编译验证 Modules 聚合**

Run: `dotnet build "D:\Project\Program\WorkFlowPrj\CsharpPrj\Modules\Modules.csproj" -c Debug`
Expected: 编译通过，0 error（`AssemblyDependencyResolver` 位于 `System.Runtime.Loader`，net10.0 可用）。

***

## Task 2: PrjBridge 成为扩展层聚合器（正常引用 Engine + 整体部署）

**Files:**

- Modify: `Projects\PrjBridge\PrjBridge.csproj`

- [ ] **Step 1: Engine 5 项改回正常引用**

将 `PrjBridge.csproj` L25-29 的 5 个 Engine `ProjectReference` 去掉 `ReferenceOutputAssembly="false"`，并更新上方注释：

```xml
    <!-- 适配对象：Engine 侧（GameEditor 嵌入式宿主注册库 + 4x 承载模块）。
         正常引用（含编译闭包），使本桥 PrjBridge.deps.json 捕获 Engine 全闭包（含 native 资产），
         作为扩展层聚合器的单一真源；本桥源码不引用 Engine 类型，仅借其编译产出物与 deps.json。 -->
    <ProjectReference Include="$(EngineProjectRoot)\GameEditor.Scene\GameEditor.Scene.csproj" />
    <ProjectReference Include="$(EngineProjectRoot)\GameEditor.Graphics\GameEditor.Graphics.csproj" />
    <ProjectReference Include="$(EngineProjectRoot)\GameEditor.Graphics.D3D11\GameEditor.Graphics.D3D11.csproj" />
    <ProjectReference Include="$(EngineProjectRoot)\GameEditor.VirtualMatrix\GameEditor.VirtualMatrix.csproj" />
    <ProjectReference Include="$(EngineProjectRoot)\GameEditor.Host\GameEditor.Host.csproj" />
```

- [ ] **Step 2: 部署目标改为「整体搬 PrjBridge 输出」，并删除 Strip 目标**

将 `DeployBridgeToFileDrivenOutput` Target（L42-70）整体替换为（递归拷贝 `$(TargetDir)`，保留子目录结构以携带 `runtimes\` native 资产）：

```xml
  <!-- ★ 部署：桥是反射加载的扩展层聚合器（FileDriven 不引用 PrjBridge，维持"唯一引用 Projects\*"红线）。
       本桥正常引用 Agent\* + Engine\*，其输出（全部 DLL + PrjBridge.deps.json + PrjBridge.runtimeconfig.json
       + runtimes\ native 资产）即扩展层单一真源。此处把整个 $(TargetDir) 递归搬进 FileDriven 启动目录，
       PrjBridge.deps.json 作为运行时 AssemblyDependencyResolver 的装配清单 → 拷贝清单 == 装载清单。 -->
  <Target Name="DeployBridgeToFileDrivenOutput" AfterTargets="Build">
    <PropertyGroup>
      <_LauncherOutputDir Condition="'$(_LauncherOutputDir)'==''">$(FileDrivenProjectRoot)\BuildOutput\$(Configuration)\</_LauncherOutputDir>
    </PropertyGroup>
    <ItemGroup>
      <_BridgeOutputFiles Include="$(TargetDir)**\*.*" />
    </ItemGroup>
    <Copy SourceFiles="@(_BridgeOutputFiles)"
          DestinationFiles="@(_BridgeOutputFiles->'$(_LauncherOutputDir)%(RecursiveDir)%(Filename)%(Extension)')"
          SkipUnchangedFiles="true" />
  </Target>
```

删除 `StripProjectsCopiesFromBridgeBin` Target（L76-84）——PrjBridge 作为部署源，bin 内持有 Agent/Engine 副本是"正确"而非"冗余"，不应再清掉。

> 说明：`$(TargetDir)` 含 FileDriven.Abstractions.dll / CsharpPrj 聚合 DLL（本桥正常引用）。它们与 FileDriven 自身 DLL 同源同版本，`SkipUnchangedFiles` 与运行期「默认 ALC 复用」护栏共同保证无身份分裂。

- [ ] **Step 3: 构建 PrjBridge 并核对 deps.json 完整性**

Run: `dotnet build "D:\Project\Program\WorkFlowPrj\Projects\PrjBridge\PrjBridge.csproj" -c Debug`

Expected: 构建成功；运行目录出现完整扩展产物：

Run: `Get-ChildItem "D:\Project\Program\WorkFlowPrj\FileDriven\BuildOutput\Debug\net10.0-windows" | Where-Object { $_.Name -match '^(PrjBridge|Agent|GameEditor)' } | Select-Object Name`
Expected: 含 `PrjBridge.deps.json`、`PrjBridge.runtimeconfig.json`、`PrjBridge.dll`、`Agent.Launcher.dll`、`Agent.Core.dll`、`Agent.Guardrails.dll`、`Agent.McpRegistryHost.dll`、`GameEditor.Host.dll`、`GameEditor.Graphics.D3D11.dll` 等。

Run: `Select-String -Path "D:\Project\Program\WorkFlowPrj\FileDriven\BuildOutput\Debug\net10.0-windows\PrjBridge.deps.json" -Pattern "GameEditor.Host" -SimpleMatch`
Expected: 命中（Engine 闭包已被捕获，不再依赖 `ReferenceOutputAssembly=false` 手工搬运）。

***

## Task 3: 改造 FunctionLibraryLoader（移除全局兜底，接入 scoped ALC）

**Files:**

- Modify: `CsharpPrj\Modules\Registry\FunctionLibraryLoader.cs`

- [ ] **Step 1: 移除静态构造的全局** **`Resolving`** **兜底**

删除 `FunctionLibraryLoader.cs` L18-26 静态构造（含 `using System.Runtime.Loader;` 若因此不再使用）。

- [ ] **Step 2: 重写** **`ResolveTypeFromQualifiedName`** **为「默认 ALC → scoped ALC」两段式**

将 `ResolveTypeFromQualifiedName`（L133-167）整体替换为：

```csharp
    /// <summary>
    /// 按程序集限定名解析函数库类型。
    /// 两段式：① 默认 ALC（deps.json 已登记的正常库，如 FileDriven.Business/FrontEndDriven）用 Type.GetType；
    /// ② 默认 ALC 失败时交 ExtensionAssemblyHost 的 scoped ALC 显式发现（扩展层 Agent.* / GameEditor.*）。
    /// 不再依赖全局 AssemblyLoadContext.Default.Resolving 隐式兜底。
    /// </summary>
    private static Type? ResolveTypeFromQualifiedName(string typeStr)
    {
        var type = Type.GetType(typeStr, throwOnError: false);
        if (type != null)
            return type;

        return ExtensionAssemblyHost.ResolveType(typeStr);
    }
```

- [ ] **Step 3: 编译验证**

Run: `dotnet build "D:\Project\Program\WorkFlowPrj\CsharpPrj\Modules\Modules.csproj" -c Debug`
Expected: 编译通过，0 error。

***

## Task 4: 改造 PrjBridgeWiringHook（桥也从 scoped ALC 装载）

**Files:**

- Modify: `FileDriven.Execution\Lifecycle\PostInitHooks\PrjBridgeWiringHook.cs`

- [ ] **Step 1: 将** **`Assembly.Load("PrjBridge")`** **改为** **`ExtensionAssemblyHost.Load("PrjBridge")`**

将 `PrjBridgeWiringHook.cs` L25 替换为：

```csharp
            // 从 scoped ALC 解析（PrjBridge.dll 由扩展 deps.json 登记；不在 FileDriven deps.json）。
            // 未部署/未登记时返回 null → 降级灰态。
            var assembly = Modules.Registry.ExtensionAssemblyHost.Load("PrjBridge");
            if (assembly is null)
                return PostInitHookResult.Failure("PrjBridge.dll 未部署（扩展层 deps.json 缺失或未登记）", status: "degraded");
```

> 保留 `catch (Exception ...)` 分支兼容其它装载异常；`ExtensionAssemblyHost.Load` 对未登记程序集返回 null（不抛），故以显式 null 判定降级。

- [ ] **Step 2: 编译 FileDriven 全量**

Run: `dotnet build "D:\Project\Program\WorkFlowPrj\FileDriven\FileDriven.sln" -c Debug`
Expected: 编译通过，0 error。

***

## Task 5: 新增 ExtensionAssemblyHost 单元测试

**Files:**

- Create: `CentralTest\CSharpTest\Integration.Tests\ExtensionAssemblyHostTests.cs`

- [ ] **Step 1: 编写测试**

```csharp
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Modules.Registry;

namespace WorkFlowMethod.Tests.UsageTests;

[TestClass]
[TestCategory("FunctionLibraryLoader")]
public class ExtensionAssemblyHostTests
{
    [TestMethod]
    public void ResolveType_ForUnknownAssembly_ShouldReturnNull()
    {
        var type = ExtensionAssemblyHost.ResolveType("NoSuch.Namespace.X, NoSuch.Assembly");
        Assert.IsNull(type);
    }

    [TestMethod]
    public void Load_ForUnknownAssembly_ShouldReturnNull()
    {
        var asm = ExtensionAssemblyHost.Load("NoSuch.Assembly");
        Assert.IsNull(asm);
    }

    [TestMethod]
    public void ResolveType_ForExtensionLibrary_ShouldResolveFromDepsJson()
    {
        var deps = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PrjBridge.deps.json");
        if (!File.Exists(deps))
        {
            Assert.Inconclusive("运行目录无 PrjBridge.deps.json，跳过扩展库解析断言");
            return;
        }

        var type = ExtensionAssemblyHost.ResolveType("Agent.Launcher.AgentUiLibrary, Agent.Launcher");
        Assert.IsNotNull(type, $"扩展库类型 'Agent.Launcher.AgentUiLibrary, Agent.Launcher' 应经 scoped ALC 解析");
    }

    [TestMethod]
    public void ValidateDeployment_ShouldReportNoMissingForKnownLibrary()
    {
        var missing = ExtensionAssemblyHost.ValidateDeployment();
        // 若部署完整，GameEditor.Host.dll 不应缺失（缺失则返回列表中包含它）
        Assert.IsFalse(missing.Contains("lib/net10.0-windows/GameEditor.Host.dll"),
            "GameEditor.Host 运行时资产不应缺失");
    }
}
```

- [ ] **Step 2: 运行测试**

Run: `dotnet test "D:\Project\Program\WorkFlowPrj\CentralTest\CSharpTest\Integration.Tests\Integration.Tests.csproj" --filter "TestCategory=FunctionLibraryLoader" -c Debug`
Expected: 前两个断言 PASS；第三/四条在部署态 PASS（无 deps.json 时 Inconclusive，不阻塞）。核心验证以 Task 6 运行时冒烟为准。

***

## Task 6: 运行时回归（FileDriven 全量启动 + 扩展层加载）

- [ ] **Step 1: 重新部署（重建 PrjBridge 与 FileDriven.Launcher）**

Run: `dotnet build "D:\Project\Program\WorkFlowPrj\Projects\PrjBridge\PrjBridge.csproj" -c Debug`
Run: `dotnet build "D:\Project\Program\WorkFlowPrj\FileDriven\FileDriven.Launcher\FileDriven.Launcher.csproj" -c Debug`
Expected: 两构建均成功；运行目录含 `PrjBridge.deps.json` 与全部扩展 DLL（含 `runtimes\` 子目录若存在）。

- [ ] **Step 2: 启动 FileDriven，验证 6 个扩展库注册成功且无 fallback 日志**

启动 FileDriven（GUI 或 CLI），观察日志与 `FileDriven_FunctionLibFallback_*.log` 是否生成。
Expected:

- 无 `函数库注册失败` / `无法加载类型 'Agent.Launcher.AgentUiLibrary, Agent.Launcher'` 等错误；

- `AgentUiLibrary / RuleMatchFunction / PromptFrameworkFunction / GuardFunction / McpToolFunction / EngineLibrary` 全部注册成功；

- `PrjBridgeWiringHook` 返回 `ports_wired`（而非 `degraded`）；

- Math / Workflow / Cline / Agent UI 冒烟脚本行为与改造前一致。

- [ ] **Step 3: 回归确认无残留全局兜底依赖**

Run: Grep `AssemblyLoadContext.Default.Resolving`（路径 `CsharpPrj\Modules\Registry`）
Expected: 不再出现；`LoadFromAssemblyPath` 仅存于 `ExtensionAssemblyHost.cs` 的 scoped ALC 内（属预期显式装载）。

- [ ] **Step 4: 提交**

```bash
git add CsharpPrj/Modules/Registry/ExtensionAssemblyHost.cs \
        CsharpPrj/Modules/Registry/FunctionLibraryLoader.cs \
        Projects/PrjBridge/PrjBridge.csproj \
        FileDriven/FileDriven.Execution/Lifecycle/PostInitHooks/PrjBridgeWiringHook.cs \
        CentralTest/CSharpTest/Integration.Tests/ExtensionAssemblyHostTests.cs
git commit -m "refactor(registry): 插件层 AssemblyDependencyResolver + PrjBridge 聚合器实现扩展层部署即注册，移除全局 Resolving 与 ReferenceOutputAssembly=false"
```

***

## 自审清单（Self-Review）

**1. 规格覆盖**

- 插件装载（AssemblyDependencyResolver + scoped ALC）→ Task 1。

- PrjBridge 聚合器（正常引用 Engine + 整体部署 + 去 Strip）→ Task 2。

- 移除全局 `Resolving` 兜底 → Task 3 Step 1。

- PrjBridge 自身也纳入 scoped ALC → Task 4。

- 依赖缺失快速定位 → `ValidateDeployment`（Task 1 / Task 5）。

- 6.3 明确不入本计划，文末开放问题。

**2. 占位符扫描**

- 所有代码步骤给出完整代码；命令给出精确路径与预期输出。无 TBD/TODO。

**3. 类型一致性**

- `ExtensionAssemblyHost.Initialize / ResolveType / Load / ValidateDeployment / DepsJsonPath / IsInitialized` 与 `ExtensionLoadContext(string depsJsonPath) / Load / LoadUnmanagedDll` 在 Task 1 唯一确定；Task 3/4 按签名调用；`AssemblyDependencyResolver` 构造参数与 `ResolveAssemblyToPath / ResolveUnmanagedDllToPath` 为平台 API，无命名漂移。

***

## 开放问题（不阻塞本计划，需业务侧后续拍板）

1. **是否连 6.3 一并评估**：若日后愿以"FileDriven 编译膨胀"为代价换取最小自定义代码，可改 FileDriven 正常引用 PrjBridge；本计划不采纳以维持隔离与瘦身。
2. **部署校验是否纳入启动硬失败**：当前 `ValidateDeployment` 仅诊断；若需"缺扩展依赖即阻断启动"，可在 `FrameworkInitializer` Registry 阶段失败时调用并写入 fallback 日志（沿用现有失败显式化逻辑）。
3. **`PrjBridge`** **是否纳入** **`FileDriven.sln`**：沿用 `架构设计.md` 第 9 节开放问题 4，需在 CI/构建脚本固化。

***

*plan mtime: 2026-09-02（已按「插件层 AssemblyDependencyResolver」路径更新）*
*关联文件：`CsharpPrj\Modules\Registry\FunctionLibraryLoader.cs`、`Projects\PrjBridge\PrjBridge.csproj`、`FileDriven.Execution\Lifecycle\PostInitHooks\PrjBridgeWiringHook.cs`、`FileDriven.Execution\Initial\FrameworkInitializer.cs`*
