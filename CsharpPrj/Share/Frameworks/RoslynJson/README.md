
# RoslynJsonFramework — Roslyn→JSON 通用转换框架

## 定位

`Share\Frameworks` 下的 **Roslyn 语法树 → 转换 IR** 转换框架：把「Roslyn 语法树」映射为**转换内核的 IR**，
再发射为 JSON 文本或「C# 内嵌 JSON」。不承载任何领域概念（无 AgentTest / 无 spec / 无 kind 词表）。

**定位与 TFM（2026-09-13 收敛）**：
- 本工程为 **Share 层 Windows 项目（`net10.0-windows`）**，可直引 `Modules`/`Plate` 层能力；
- 曾由本工程承载的「分析器侧通用件」已按职责归位，不再在本工程内：
  - **纯 BCL 件**（文本前端原语 / 展开管线 / 词表 / 配置读取 / 宿主原语 / 框架级诊断码）
    → `Modules\LogicComposition\Conversion`（`netstandard2.0` 零依赖，运行期与分析器侧共用单源）；
  - **Roslyn 件**（增量生成器脚手架 / 诊断描述符目录 / 诊断桥）
    → `Environment\CentralTest\SpecSourceGen\Analyzer`（`netstandard2.0` + `IsRoslynComponent=true`）。
- 层序合规：`Share(2) → Modules(1)/Plate(1)`，仅依赖 BasicMethod/Modules/Plate/Protocol（`LayerBoundary.targets`）。

```
源文本 ──CSharpSourceProvider──▶ SyntaxTree ──SyntaxToConversionIrMapper──▶ IR(LogicComposition.Conversion)
                                                     │  （规则表命中 → 精确映射；未命中 → 兜底映射 + 诊断）
                                                     ▼
                                        JsonTextEmitter（P1）/ CSharpEmbeddedJsonEmitter（本框架）
```

**为什么独立成 `Share\Frameworks` 工程**：
- Share 层出口：仅依赖 BasicMethod/Modules/Plate/Protocol（`LayerBoundary.targets`）；
- Roslyn 依赖限定在本工程（`Microsoft.CodeAnalysis.CSharp` 以 `PrivateAssets=all` 声明，不透传到下游）。

## 核心抽象（本工程现存 = 「Roslyn 语法树 → IR」链路）

| 类型 | 职责 |
|------|------|
| `IRoslynSourceProvider` / `CSharpSourceProvider` | 文本 → 语法树（**来自 RoslynMethod.Core**，与 RoslynMethod 共用单一实现；解析选项可注入，行为可复现） |
| `ISyntaxToIrMapper` / `SyntaxToConversionIrMapper` | 语法节点 → IR（规则表 + 兜底映射 + 深度上限） |
| `SyntaxRuleTable` | `SyntaxKind → 映射委托` 规则表（重复注册 fail-fast，单一注册点） |
| `CSharpEmbeddedJsonEmitter` | IR → 「类 + `const string` 内嵌 JSON」C# 源码（**源生成器唯一合法产物形态**）；结构经内核 `RoslynGeneratorBase`（`SyntaxFactory`）构建 |
| `RoslynJsonConversionService` | 默认装配入口：`ToIr` / `ToJson` / `ToEmbeddedCSharp` |
| `SourceLocation` | Roslyn 位置 → 转换内核 `SourceSpan` 的适配点（行列换算唯一实现在内核 `RoslynSourceLocator`） |

## 已迁出的通用件（2026-09-13 收敛）

| 迁出件 | 现落点 | 说明 |
|--------|--------|------|
| `JsonLiteralParser` / `JsonLiteralParseException` / `SourceOffsetMap` / `LineSplitter` / `LineTokenizer`（`TextToken`） / `BracketBlockScanner` / `StringScan` | `Conversion\Json\` · `Conversion\Lexing\` | 文本前端原语；`StringScan` 已并入 `Lexing\TextScan`（字符串边界与括号深度的**唯一实现**） |
| `SourceExpansionPipeline<TModel>` / `ISourceFrontEnd<TModel>` / `ExpansionOutcome` / `GeneratorArtifactInfo` | `Conversion\Generation\` | 文本→IR 展开管线 + 产物约定 |
| `GeneratorConfigReader` / `GeneratorInputMatcher` | `Conversion\Config\` | 生成器配置读取骨架与输入判定 |
| `OrderedWhitelist` | `Conversion\Vocabulary\` | 追加式有序白名单 |
| `LogicalFileWriter` / `BuildLogEntry` / `ConversionDiagnosticLogMap` | `Conversion\Hosting\` | 宿主无关写盘原语与「诊断 → 日志条目」换算 |
| `RoslynJsonDiagnosticCodes`（RJ1xxx 唯一来源 + `ToErrorClass`） | `Conversion\Diagnostics\` | 映射器/发射器（本工程）与分析器侧领域目录共用同一码段 |
| `JsonSourceGeneratorScaffold` / `GeneratorInputOrdering` | `SpecSourceGen.Analyzer\Scaffolding\` | 依赖 Roslyn（`IIncrementalGenerator` / `AdditionalText`） |
| `DiagnosticCatalog` / `RoslynDiagnosticBridge` / `RoslynJsonDiagnostics` | `SpecSourceGen.Analyzer\Diagnostics\` | 依赖 Roslyn（`DiagnosticDescriptor` / `Location`） |

> 依赖方向：本工程（`net10.0-windows`）→ `Conversion`（`netstandard2.0`），TFM 高吃低；
> `Conversion` 不再经本工程进入分析器闭包（分析器侧自持其 Roslyn 件）。

## 通用生成前端（2026-09-13 已迁出）

原「与 Roslyn 无关的通用生成链路」（文本前端原语 / 展开管线 / 产物约定 / 配置与词表 / 诊断目录 / 宿主原语）
已按职责迁出本工程，**本框架不再承载领域生成器共用层**：

| 能力 | 现落点 |
|------|--------|
| 文本前端原语（JSON 字面量 / 行切分 / 行内词法 / 区块扫描 / 偏移换算 / `TextScan`） | `Modules\LogicComposition\Conversion\{Json,Lexing}` |
| 展开管线（`SourceExpansionPipeline<TModel>` / `ISourceFrontEnd<TModel>` / `ExpansionOutcome`）/ 产物约定 | `Modules\LogicComposition\Conversion\Generation` |
| 配置读取与输入判定 / 追加式白名单 | `Conversion\{Config,Vocabulary}` |
| 宿主原语（写盘 / 诊断→日志条目） | `Conversion\Hosting` |
| 框架级诊断码（RJ1xxx + `ToErrorClass`） | `Conversion\Diagnostics` |
| 生成器脚手架与路径序 / 诊断目录与位置桥 | `Environment\CentralTest\SpecSourceGen\Analyzer\{Scaffolding,Diagnostics}` |

> 消费指引：声明式 DSL 生成器（如 `SpecSourceGen`）与命令解析（`Modules\Parsing`）一律引用
> `LogicComposition.Conversion`（分析器侧另自持其 Roslyn 件），**不再引用本框架**。

---

## 与 RoslynMethod 的共用边界（RoslynMethod.Core）

本框架**不重复实现** Roslyn 基础设施：源提供器、源位置换算、C# 生成基底统一来自同仓 **`Plate\RoslynMethod\RoslynMethod.Core`**
（RoslynMethod 模块的低层共享内核，`netstandard2.0`），RoslynMethod（运行期，`net10.0-windows`）与本框架消费同一实现，
避免「同能力两份实现」漂移。

| 能力 | 内核类型（`RoslynMethod.Core`） | 消费方 |
|------|------------------------------|--------|
| 文本 → 语法树 | `RoslynSpace.RoslynSyntaxUtilities.Base.CSharpSourceProvider`（+ `IRoslynSourceProvider`） | RoslynJson；RoslynMethod 侧已全量收敛（唯一 `ParseText` 落点见内核说明） |
| 源位置（1 基行列） | `RoslynSpace.RoslynSyntaxUtilities.Base.RoslynSourceLocator` | RoslynJson `SourceLocation`；RoslynMethod `CompilationErrorAnalyzer` / `SecurityRuleBase`（已收敛） |
| C# 生成基底 | `RoslynSpace.RoslynStringCodeGeneration.RoslynGeneratorBase<TModel>` + `CodeGenerationContext`/`CodeGenerationResult` | RoslynJson 内嵌 JSON 发射器；RoslynMethod `CodeGenerators` |

> 为什么内核落位 `RoslynMethod.Core` 并取 `netstandard2.0`（而非新建工程 / 多目标）：
> ① 内核取 `netstandard2.0` 单目标，`net10.0-windows` 消费方（RoslynMethod / Plate / Registry / RoslynJson）照常引用（TFM 高吃低）；
> ② 本仓库全局 `AppendTargetFrameworkToOutputPath=false`（单一烘焙点），**禁用多目标**（两个 TFM 会覆盖同一 DLL 路径），
>    且 `Frame\CallCenter\CallCenter.Adapters` 以 HintPath 直接引用 `RoslynMethod.Core.dll`；
> ③ 层序合规：`Share`(层 2) → `Plate`(层 1) 为允许方向；
> ④ 内核 `Microsoft.CodeAnalysis.CSharp` 以 `PrivateAssets=all` 声明，Roslyn 不进入下游运行期闭包。
>
> 注（2026-09-13）：本框架已改为 `net10.0-windows`、分析器侧亦不再消费该内核 —— ①中「分析器装载约束」对本内核已不构成压力；
> 是否把内核收敛为单一 TFM 属独立简化项（不在本次收敛范围）。

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

### 增量源生成器脚手架（已迁出）

`JsonSourceGeneratorScaffold` / `GeneratorInputOrdering`（`AdditionalTexts` 接线 + 路径序 + 异常转诊断）
已迁入 `Environment\CentralTest\SpecSourceGen\Analyzer\Scaffolding\`（`SpecSourceGen.Scaffolding` 命名空间）；
诊断描述符目录与位置桥（`DiagnosticCatalog` / `RoslynDiagnosticBridge` / 框架级描述符）同地在 `Analyzer\Diagnostics\`。
派生生成器请引用分析器工程，而非本框架。

## 边界（明确不做）

- **不写磁盘**：Roslyn 输出通道只有 `AddSource`（C# 源码）。需要物化 `.json`/清单时，由 MSBuild Task / CLI 宿主调用本框架的 `ToJson` 落盘。
- **不定义领域语义**：kind 词表/算子白名单/用例 schema 属于领域生成器。
- **不做通用表达式求值**：只做语法结构到数据的映射。
- **不在分析器内做运行期 JSON**：分析器进程加载不了 `Plate.JsonMethod`（net10.0-windows），JSON 文本由转换内核零依赖发射器产出；
  运行期的解析/序列化/计量/路径/合并一律走 `Plate.JsonMethod`（见 `Modules\LogicComposition\ConversionRuntime\README.md`）。
  **分侧判据（硬性）**：分析器侧 = 零依赖、严格、逐字保真（`Conversion\Json\JsonLiteralParser`）；运行期 = `Plate.JsonMethod`（宽松选项）。
  两侧**语义不等价是已知且唯一允许的双实现**，禁止把运行期宽松选项（`ReadCommentHandling.Skip` / `AllowTrailingCommas`）引入分析器侧。

## 接线状态（避免"存量未接线"腐化）

| 链路 | 状态 | 消费方 |
|------|------|--------|
| 通用生成前端 | **已迁出**（纯件 → `Conversion`；Roslyn 件 → `SpecSourceGen.Analyzer`，两处均已接线） | `Environment\CentralTest\SpecSourceGen`（分析器 + Tasks 宿主 + 重生成守卫）、`Modules\Parsing` |
| Roslyn 语法树 → IR（`ISyntaxToIrMapper` / `SyntaxRuleTable` / `SyntaxToConversionIrMapper` / `SyntaxMappingOptions` / `RoslynJsonConversionService` / `CSharpEmbeddedJsonEmitter` / `Roslyn\SourceLocation`） | **未接线（预留）** | 暂无（规划：`Agent.Spec.SourceGen` 阶段 2.5 生成壳类） |

> 未接线链路在接线或废弃前**不得增量修改行为**；其上新增实现须同步补测试，否则按 `RedundancyElimination` 处置（废弃或移入 `_deprecated/`）。

## 依赖关系

```
LogicComposition.Conversion（netstandard2.0，零外部依赖：IR / 发射器 / 文本前端原语 / 展开管线 / 宿主原语 / 框架级诊断码）
        ▲
RoslynMethod.Core（netstandard2.0；Microsoft.CodeAnalysis.CSharp PrivateAssets=all）◀── RoslynMethod（net10.0-windows，Plate 层）
        ▲
RoslynJsonFramework（net10.0-windows，Share 层）—— 仅「Roslyn 语法树 → IR」链路（当前未接线）
```
