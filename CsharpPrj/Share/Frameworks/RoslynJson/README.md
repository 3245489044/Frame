
# RoslynJsonFramework — Roslyn→JSON 通用转换框架

## 定位

`Share\Frameworks` 下的**通用转换架构**：把「Roslyn 语法树」映射为**转换内核的 IR**，再发射为 JSON 文本或「C# 内嵌 JSON」。
不承载任何领域概念（无 AgentTest / 无 spec / 无 kind 词表）；领域生成器在 `Environment\CentralTest` 侧继承本框架。

```
源文本 ──CSharpSourceProvider──▶ SyntaxTree ──SyntaxToConversionIrMapper──▶ IR(LogicComposition.Conversion)
                                                     │  （规则表命中 → 精确映射；未命中 → 兜底映射 + 诊断）
                                                     ▼
                                        JsonTextEmitter（P1）/ CSharpEmbeddedJsonEmitter（本框架）
```

**为什么独立成 `Share\Frameworks` 工程**：
- 目标 `netstandard2.0` + Roslyn 依赖限定在本工程（`PrivateAssets=all`），使分析器可直接引用而不污染运行期闭包；
- 层序合规：`Share(2) → Modules(1)`，仅依赖 BasicMethod/Modules/Plate/Protocol（`LayerBoundary.targets`）。

## 核心抽象

| 类型 | 职责 |
|------|------|
| `IRoslynSourceProvider` / `CSharpSourceProvider` | 文本 → 语法树（**来自 RoslynMethod.Core**，与 RoslynMethod 共用单一实现；解析选项可注入，行为可复现） |
| `ISyntaxToIrMapper` / `SyntaxToConversionIrMapper` | 语法节点 → IR（规则表 + 兜底映射 + 深度上限） |
| `SyntaxRuleTable` | `SyntaxKind → 映射委托` 规则表（重复注册 fail-fast，单一注册点） |
| `CSharpEmbeddedJsonEmitter` | IR → 「类 + `const string` 内嵌 JSON」C# 源码（**源生成器唯一合法产物形态**）；结构经内核 `RoslynGeneratorBase`（`SyntaxFactory`）构建 |
| `JsonSourceGeneratorScaffold` | `IIncrementalGenerator` 脚手架（AdditionalTexts 接线 + 路径序 + 异常转诊断） |
| `GeneratorInputOrdering` | 输入按路径升序（消除生成顺序抖动） |
| `RoslynJsonDiagnostics` / `RoslynDiagnosticBridge` | 框架级诊断描述符 + 转换诊断 → Roslyn 诊断/位置换算 |
| `RoslynJsonConversionService` | 默认装配入口：`ToIr` / `ToJson` / `ToEmbeddedCSharp` |
| `SourceLocation` | Roslyn 位置 → 转换内核 `SourceSpan` 的适配点（行列换算唯一实现在内核 `RoslynSourceLocator`） |

## 与 RoslynMethod 的共用边界（RoslynMethod.Core）

本框架**不重复实现** Roslyn 基础设施：源提供器、源位置换算、C# 生成基底统一来自同仓 **`Plate\RoslynMethod\RoslynMethod.Core`**
（RoslynMethod 模块的低层共享内核，`netstandard2.0`），RoslynMethod（运行期，`net10.0-windows`）与本框架（分析器侧）消费同一实现，
避免「同能力两份实现」漂移。

| 能力 | 内核类型（`RoslynMethod.Core`） | 消费方 |
|------|------------------------------|--------|
| 文本 → 语法树 | `RoslynSpace.RoslynSyntaxUtilities.Base.CSharpSourceProvider`（+ `IRoslynSourceProvider`） | RoslynJson（分析器侧）；RoslynMethod 侧已全量收敛（唯一 `ParseText` 落点见内核说明） |
| 源位置（1 基行列） | `RoslynSpace.RoslynSyntaxUtilities.Base.RoslynSourceLocator` | RoslynJson `SourceLocation`；RoslynMethod `CompilationErrorAnalyzer` / `SecurityRuleBase`（已收敛） |
| C# 生成基底 | `RoslynSpace.RoslynStringCodeGeneration.RoslynGeneratorBase<TModel>` + `CodeGenerationContext`/`CodeGenerationResult` | RoslynJson 内嵌 JSON 发射器；RoslynMethod `CodeGenerators` |

> 为什么内核落位 `RoslynMethod.Core` 并取 `netstandard2.0`（而非新建工程 / 多目标）：
> ① RoslynJson 受源生成器约束必须为 `netstandard2.0`，无法引用 `net10.0-windows` 程序集 —— 故内核取 `netstandard2.0` 单目标，
>    `net10.0-windows` 消费方（RoslynMethod/Plate/Registry）照常引用；
> ② 本仓库全局 `AppendTargetFrameworkToOutputPath=false`（单一烘焙点），**禁用多目标**（两个 TFM 会覆盖同一 DLL 路径），
>    且 `Frame\CallCenter\CallCenter.Adapters` 以 HintPath 直接引用 `RoslynMethod.Core.dll`；
> ③ 层序合规：`Share`(层 2) → `Plate`(层 1) 为允许方向；
> ④ 内核 `Microsoft.CodeAnalysis.CSharp` 以 `PrivateAssets=all` 声明，Roslyn 不进入本框架（分析器）的运行期闭包。

## 使用示例

### 文本 → JSON（框架默认装配）

```csharp
var service = new RoslynJsonConversionService();
var json = service.ToJson(sourceText, "Cases/axioms.x.specs",
    ConversionOptions.Default.WithHeader("由 compact.specs 展开，勿手改"));

if (!json.IsSuccess)
{
    Console.WriteLine(json.GetDiagnosticsString());   // 含 文件(行,列)
    return;
}
Console.WriteLine(json.Artifact);
```

### 自定义规则表（领域生成器扩展点）

```csharp
var rules = new SyntaxRuleTable()
    .Register(SyntaxKind.ClassDeclaration, (node, ctx) =>
        new ConversionObject().Add("className", ((ClassDeclarationSyntax)node).Identifier.Text))
    .Register(SyntaxKind.MethodDeclaration, (node, ctx) => /* … */ ConversionScalar.Null);

var service = new RoslynJsonConversionService(
    mapper: new SyntaxToConversionIrMapper(rules, SyntaxMappingOptions.Default));
```

### 源提供器（来自 RoslynMethod.Core）

```csharp
// 解析行为可复现：注入固定语言版本；与 RoslynMethod 共用同一实现
var provider = new CSharpSourceProvider(LanguageVersion.CSharp13);
var service = new RoslynJsonConversionService(sourceProvider: provider);
```

### 增量源生成器脚手架（分析器派生）

```csharp
[Generator]
public sealed class MyJsonGenerator : JsonSourceGeneratorScaffold
{
    protected override bool IsSource(AdditionalText text)
        => text.Path.EndsWith(".myspec", StringComparison.OrdinalIgnoreCase);

    protected override void Execute(SourceProductionContext context, IReadOnlyList<AdditionalText> inputs)
    {
        foreach (var input in inputs)                 // 已按路径升序
        {
            var text = input.GetText(context.CancellationToken)?.ToString();
            if (text is null) continue;
            // …转换后经 CSharpEmbeddedJsonEmitter 发射，再 context.AddSource(...)
        }
    }
}
```

## 边界（明确不做）

- **不写磁盘**：Roslyn 输出通道只有 `AddSource`（C# 源码）。需要物化 `.json`/清单时，由 MSBuild Task / CLI 宿主调用本框架的 `ToJson` 落盘。
- **不定义领域语义**：kind 词表/算子白名单/用例 schema 属于领域生成器。
- **不做通用表达式求值**：只做语法结构到数据的映射。
- **不在分析器内做运行期 JSON**：分析器进程加载不了 `Plate.JsonMethod`（net10.0-windows），JSON 文本由转换内核零依赖发射器产出；
  运行期的解析/序列化/计量/路径/合并一律走 `Plate.JsonMethod`（见 `Modules\LogicComposition\ConversionRuntime\README.md`）。

## 依赖关系

```
LogicComposition.Conversion（netstandard2.0，零外部依赖）
        ▲
RoslynMethod.Core（netstandard2.0；Microsoft.CodeAnalysis.CSharp PrivateAssets=all）◀── RoslynMethod（net10.0-windows，Plate 层）
        ▲
RoslynJsonFramework（netstandard2.0）
        ▲
Agent.Spec.SourceGen（netstandard2.0 分析器，待阶段 1 spike 后落地）
```
