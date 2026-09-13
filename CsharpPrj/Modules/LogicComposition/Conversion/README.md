# LogicComposition.Conversion — 通用转换内核

## 定位

`LogicComposition` 的**转换子工程**：与源语法、目标格式均无关的转换内核。

```
源（紧凑作者格式 / C# 语法树 / 任意文本）
        │  解析
        ▼
   IR（ConversionNode：Object/Array/Scalar/Raw）   ← 管线只操作 IR
        │  规则链 / 阶段
        ▼
   发射器（JsonTextEmitter / CSharpEmbeddedJsonEmitter …）→ 目标产物
```

**为什么单独成工程**：本工程目标为 `netstandard2.0` 且**零外部依赖**，因此可被 Roslyn 源生成器（分析器硬性要求 `netstandard2.0`）间接复用；
`LogicComposition` 本体是 `net10.0-windows`（引用 `BasicMethod`），无法下沉到 `netstandard2.0`，故内核独立成工程。

## 核心抽象

| 接口 | 职责 | 方法 |
|------|------|------|
| `IConversionStage` | IR → IR 的一次确定性变换（解析/展开/规范化/校验） | `Execute(ConversionNode, ConversionContext)` |
| `IConversionPipeline` | 按声明顺序执行阶段并统一收集诊断（不抛异常） | `Run(ConversionNode, ConversionContext?)` |
| `IConversionRule` | 单点匹配的 IR 重写（表/链驱动） | `TryApply(node, ctx, out result, out diagnostic)` |
| `IArtifactEmitter<T>` | IR → 目标产物（JSON 文本 / C# 内嵌 / 清单） | `Emit(ConversionNode, ConversionOptions?)` |
| `IConversionDiagnosticSink` | 诊断汇集（管线末端一次性读取） | `Report(d)` / `Diagnostics` / `HasErrors` |

## 数据模型

| 模型 | 说明 |
|------|------|
| `ConversionNode` / `ConversionObject` / `ConversionArray` / `ConversionScalar` / `ConversionRaw` | IR 节点（对象为**有序**键值对，保证产物顺序可复现） |
| `ConversionContext` | 一次转换的共享状态（选项 + 源文本 + 诊断汇集器 + 阶段间槽） |
| `ConversionOptions` | 缩进/键序/头注释/诊断策略/最大深度（含 `Default`/`Compact` 预设） |
| `ConversionResult<T>` | 产物 + 诊断 + 成功判定（`IsSuccess` ⇔ 无 Error 诊断） |
| `ConversionDiagnostic` / `ConversionSeverity` | 稳定错误码 + 消息 + `SourceSpan`（`文件:行:列`） |
| `SourceSpan` | 源定位（1 基行列；`HasPosition` 判定可用性） |
| `JsonArtifact` | 已发射文本 + 根节点 + 节点数/字节数（可观测计量） |
| `ConversionNodeWalker` | IR 遍历（先根序、计数、最大深度、查找） |

## 使用示例

### 组合管线并转换

```csharp
var pipeline = new DefaultConversionPipeline(
    new ParseStage(),                              // 自定义 IConversionStage
    new RuleChainStage("normalize", new IConversionRule[] { new KindWhitelistRule() }));

var context = new ConversionContext(
    ConversionOptions.Default.WithHeader("由 compact.specs 展开，勿手改"),
    sourceFile: "Cases/axioms.x.specs",
    sourceText: rawText);

var ir = pipeline.Run(ConversionScalar.Of(rawText), context);
if (!ir.IsSuccess)
{
    Console.WriteLine(ir.GetDiagnosticsString());  // 全量失败可见
    return;
}
```

### 发射 JSON / 取可观测产物

```csharp
var text = JsonTextEmitter.Instance.Emit(ir.Artifact!);
var artifact = JsonArtifactEmitter.Instance.Emit(ir.Artifact!);   // Text / NodeCount / ByteLength

// 便捷写法（失败即抛 ConversionStageException）
var json = ir.Artifact!.ToJson(ConversionOptions.Compact);
```

### 构造 IR

```csharp
var root = new ConversionObject()
    .Add("suite", "axioms.strategy-guard")
    .Add("cases", new ConversionArray()
        .Add(new ConversionObject().Add("id", "case.a").Add("then", new ConversionArray().Add("ok"))));
```

## 设计原则

- **格式无关**：管线只搬运 IR；新增目标格式 = 新增发射器，不触碰阶段与规则。
- **确定性**：键序稳定（默认保留插入序）+ InvariantCulture 数字 + 固定头注释前缀 → diff 可评审。
- **不抛异常**：阶段异常经 `ConversionStageException` 统一转诊断；`CollectAllDiagnostics=false` 才短路。
- **单一发射**：JSON 文本生成逻辑唯一来源是 `JsonTextEmitter`（`JsonArtifactEmitter` 只叠加计量）。
- **零外部依赖**：不引用 `BasicMethod`/`Transformers` 等上层能力（分析器复用前提）。

## 双出口约定（重要）

转换内核是 `netstandard2.0` **零依赖**工程（Roslyn 分析器可加载的前提），因此**不引用**平台级 JSON 框架 `Plate.JsonMethod`。
JSON 出口按运行环境分为两条，实现互不重复：

| 环境 | 出口 | 说明 |
|---|---|---|
| **分析器侧**（编译期：源生成器 / MSBuild Task 内联） | 本工程 `Emitters\JsonTextEmitter` | 零依赖，进程内可用；头注释由 `JsonHeaderComment` 统一渲染 |
| **运行期**（宿主 / CLI / 测试基础设施） | `Share.LogicComposition.Conversion.Runtime`（`ConversionRuntime\`） | 桥接到 `Plate.JsonMethod`：缩进/编码/诊断/计量/路径/合并全部复用平台单一实现 |
| **运行期·结构性投影**（进程内已构造 IR，只需 IR→JSON 文本） | 本工程 `Emitters\JsonTextEmitter` | 内核发射器是「IR→JSON 文本」的唯一实现；Plate 层模块（如 `RoslynMethod` 的 `FunctionGraphVisualizer.ToJson`）复用同一发射器，而非另写 JSON 文本生成 |

> 判据：JSON 能力（解析/序列化/路径/合并/计量）在运行期一律用 `Plate.JsonMethod`（或经 `ConversionRuntime` 桥接），只有分析器进程（加载不了 net10.0 程序集）才用内核发射器；
> 但**只要已是「IR → JSON 文本」这一形态**，无论运行期还是分析器侧，都复用本工程 `JsonTextEmitter`（唯一实现，保证键序/数字格式/转义一致），不再新增第二份 JSON 文本发射。

## 依赖关系

```
LogicComposition.Conversion（netstandard2.0，零外部依赖）
   ↑ 被引用
LogicComposition（net10.0-windows）        ← 运行期可在此基础上写 Transformers 适配实现
  └── ConversionRuntime\                   ← 运行期 JSON 出口：桥接 Plate.JsonMethod（详见 ConversionRuntime\README.md）
Share.RoslynJsonFramework（netstandard2.0）← Roslyn→JSON 通用框架
Agent.Spec.SourceGen（netstandard2.0）      ← 实际源生成器（待阶段 1 spike 后落地）
```
