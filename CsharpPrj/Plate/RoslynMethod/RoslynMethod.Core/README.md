# RoslynMethod.Core — Roslyn 低层共享内核（netstandard2.0）

## 定位

`Plate\RoslynMethod\RoslynMethod.Core` 是 RoslynMethod 模块的**低层共享内核**：承载解析/编译/语法查询/位置换算/C# 生成基底等
**与领域无关的 Roslyn 基础设施**，供两个方向共用：

| 消费方 | 目标框架 | 用途 |
|--------|----------|------|
| `Plate\RoslynMethod`（同模块上层：Analysis/Generation/Security/…） | `net10.0-windows` | 编译构建、语义模型、有界查询、C# 生成基底 |
| `Share\Frameworks\RoslynJson` | `netstandard2.0`（分析器硬约束） | 源提供器、源定位、C# 生成基底（源生成器侧复用） |
| `Modules\Registry` | `net10.0-windows` | 编译相关能力 |

## 为什么本工程为 netstandard2.0

1. Roslyn 源生成器/分析器**只能加载 netstandard2.0** 依赖；RoslynJson 必须保持 netstandard2.0，其被复用的能力须来自 netstandard2.0 工程；
2. `net10.0-windows` 消费方可正常引用 netstandard2.0 工程（无需多目标）；
3. 本仓库全局 `AppendTargetFrameworkToOutputPath=false`（单一烘焙点：`BuildOutput\$(Configuration)\`），
   **禁用多目标**——两个 TFM 会覆盖同一 DLL 路径，且 `Frame\CallCenter\CallCenter.Adapters` 以 HintPath 直接引用 `RoslynMethod.Core.dll`；
4. 因此「共享内核」不另建工程，直接落位于本工程（RoslynMethod 模块内，单一实现）。

## 共享内核类型

| 类型 | 职责 |
|------|------|
| `RoslynSpace.RoslynSyntaxUtilities.Base.IRoslynSourceProvider` / `CSharpSourceProvider` | 文本 → 语法树（`CSharpParseOptions` 可注入，解析行为可复现） |
| `RoslynSpace.RoslynSyntaxUtilities.Base.RoslynSourceLocator` | 源位置换算唯一实现（节点/Token/诊断 → 1 基行列 + 文件路径） |
| `RoslynSpace.RoslynStringCodeGeneration.RoslynGeneratorBase<TModel>` | C# 生成基底：`SyntaxFactory` 构建语法树 → 规范化渲染（`Render` 可覆盖） |
| `RoslynSpace.RoslynStringCodeGeneration.CodeGenerationContext` / `CodeGenerationResult` | 生成上下文与结果契约 |

## 兼容层（`Compatibility\`）

netstandard2.0 缺失的原语在此补齐（`#if NETSTANDARD2_0` 守门）：`IsExternalInit`（record/init）、`Enumerable.ToHashSet` 重载。
另有少量签名适配以适配 netstandard2.0 API 面：`UsingConfig.*` 返回 `IReadOnlyCollection<string>`（`IReadOnlySet<T>` 为 net5+）、
`CompilationReferenceHelper.MetadataReferenceComparer` 使用 `StringComparer.GetHashCode`。

## 依赖流约定

`Microsoft.CodeAnalysis.CSharp` 以 `PrivateAssets=all` 声明：本工程被 RoslynJson 引用时，**不得**让 Roslyn 传递进入分析器运行期闭包；
各消费方（RoslynMethod / Registry / RoslynJson）各自声明所需版本。

## 收敛进度

| 能力 | 状态 |
|------|------|
| 源提供器 | **已全量收敛**：唯一 `CSharpSyntaxTree.ParseText` 落点 = `CSharpSourceProvider`。RoslynJson、RoslynMethod（Analysis/Generation/Refactoring/Security 及内核内 `UsingManager`/`SyntaxHelper`/`CompilationSkeleton`/`AnalysisContext` 等 30 处）、`Plate\RoslynMethod.Tools\ClearCSharpComments` 均经本工程解析 |
| 源位置换算 | RoslynJson、RoslynMethod（`CompilationErrorAnalyzer` / `SecurityRuleBase`）已收敛；本工程内 `SyntaxHelper.GetPosition` / `SyntaxInfoTool.GetPosition` 待收敛 |
| C# 生成基底 | RoslynJson 与 RoslynMethod `CodeGenerators` 均已使用本工程实现（原定义已上移） |
| 语义通道（编译 + 语义模型） | **已收敛**：唯一 `CSharpCompilation.Create` 落点 = `CompilationSkeleton`（`Build` / `BuildOrThrow` / `BuildAndEmit`）。`AnalysisContext`、`SyntaxHelper.ParseCode`、`UsingManager.AddMissingUsings`、Optimization/Prompt/Comment/Refactoring 各站点均经此通道；只取语法树/语义模型时用 `CompilationConfig.CollectDiagnostics=false` 跳过诊断收集 |
| 工程隔离 | **已修复**：`RoslynMethod.csproj` 以 `<Compile Remove="RoslynMethod.Core\**\*.cs" />` + `ProjectReference` 引用本工程，内核源码不再被重复编译进 `RoslynMethod.dll`（`-getItem:Compile` 可复核） |

## 内核纯净度

只被 RoslynMethod 消费、内核自身零引用的类型从本工程迁出（迁出后为 RoslynMethod `internal`），
遗留 `[Obsolete]` 重复实现与零引用类型一并删除，消费方改用 `SyntaxHelper` / `AccessibilityConverter`：

| 处置 | 类型 | 落点 |
|------|------|------|
| 迁出 | `ComplexityCalculator` / `VariableMetricsCalculator` | `RoslynMethod.Analysis\CodeMetricsCalculator.cs` |
| 迁出 | `SymbolAnalyzer` / `PurityAnalyzer` | `RoslynMethod.Analysis\SymbolAnalyzer.cs` |
| 迁出 | `RecommendationAnalysis` | `RoslynMethod.Analysis\Skeletons\RecommendationAnalysis.cs` |
| 删除 | `SyntaxAccessor` / `SyntaxLocator` / `SyntaxMatcher` / `SyntaxParser` / `CodeCompilation` | 重复实现（原 `Utilities\SyntaxUtilities.cs`） |
| 删除 | `LocationInfo` / `RecommendationResult` / 内核内 `AccessibilityConverter`（`Base` 命名空间） | 零引用 |

> `MethodSignatureExtractor` 内的 `TypeDisplayHelper` 与 `Core.Utilities.TypeDisplayHelper` **不是重复实现**（前者处理 `System.Reflection.Type` 的显示名与值转换，后者处理 Roslyn `ITypeSymbol`/`IMethodSymbol`），按分工各自保留。
