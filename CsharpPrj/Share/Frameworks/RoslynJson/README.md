
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
| `IRoslynSourceProvider` | 文本 → 语法树（解析选项可注入，行为可复现） |
| `CSharpSourceProvider` | 默认实现（`CSharpParseOptions` + `LanguageVersion.Latest`） |
| `ISyntaxToIrMapper` / `SyntaxToConversionIrMapper` | 语法节点 → IR（规则表 + 兜底映射 + 深度上限） |
| `SyntaxRuleTable` | `SyntaxKind → 映射委托` 规则表（重复注册 fail-fast，单一注册点） |
| `CSharpEmbeddedJsonEmitter` | IR → 「类 + `const string` 内嵌 JSON」C# 源码（**源生成器唯一合法产物形态**） |
| `JsonSourceGeneratorScaffold` | `IIncrementalGenerator` 脚手架（AdditionalTexts 接线 + 路径序 + 异常转诊断） |
| `GeneratorInputOrdering` | 输入按路径升序（消除生成顺序抖动） |
| `RoslynJsonDiagnostics` / `RoslynDiagnosticBridge` | 框架级诊断描述符 + 转换诊断 → Roslyn 诊断/位置换算 |
| `RoslynJsonConversionService` | 默认装配入口：`ToIr` / `ToJson` / `ToEmbeddedCSharp` |
| `SourceLocation` | 唯一位置换算点（语法节点/Token/诊断 → `SourceSpan`） |

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
RoslynJsonFramework（netstandard2.0；Microsoft.CodeAnalysis.CSharp PrivateAssets=all）
        ▲
Agent.Spec.SourceGen（netstandard2.0 分析器，待阶段 1 spike 后落地）
```
