# WorkFlowPrj 设计哲学

> **一个自洽的代码操作系统 —— 万物皆函数，一切皆配置**

---

## 一、核心理念

### 理念一：万物皆函数（Everything is a Function）

WorkFlowPrj 最底层的哲学：**任何可执行的操作，最终都归约为一个函数。**

```
外部世界 → 适配器 → 标准化函数 → FunctionStorageRegistry
  ↓                    ↓
  Python 脚本          ChatComplete(userInput)
  WinForms 控件        SetControlValue(control, value)
  AI 模型调用          AnalyzeCode(code)
  数学计算             Add(a, b)
  文件操作             SaveResultToFile(content)
```

**为什么是函数？** 函数是最小、最通用、最可组合的执行单元。函数有输入、有输出、有名称——这三样东西构成了 WorkFlowPrj 的"通用语言"。

**推论：**
- 所有功能最终都是函数
- 所有函数通过名称寻址
- 所有函数通过 Registry 注册
- 所有函数通过 Pipeline 组合

### 理念二：声明式优于命令式（Declarative over Imperative）

WorkFlowPrj 的演进方向是从"写代码"到"写配置"：

```
阶段 1：命令式 C# 代码
  FunctionStorageRegistry.Register("Add", new MathFunction());

阶段 2：声明式 JSON 配置
  {
    "type": "button",
    "functionName": "Add",
    "events": { "click": "Add" }
  }

阶段 3：声明式工作流
  {
    "steps": [
      { "funcName": "Add", "parameters": ["${a}", "${b}"] }
    ]
  }
```

**声明式的本质是"告诉系统你要什么，而不是告诉系统怎么做"。**

### 理念三：适配即标准化（Adaptation is Standardization）

外部世界是混乱的——不同的语言、不同的框架、不同的协议。WorkFlowPrj 不试图消除这种混乱，而是通过**适配器层**将其标准化：

```
混乱的外部世界         适配器层          标准化的内部世界
─────────────────────────────────────────────────────
Python 脚本     →  PythonModel.Bridge  →  IFunctionUnit
WinForms 控件   →  WdUIAdapter          →  IControlValueWriter
AI 模型 API     →  WebInvoke 适配器     →  IFunctionUnit
文件系统        →  FileProcess 适配器   →  IFunctionUnit
```

**适配不是"额外的负担"，而是"标准化的代价"。** 一旦适配完成，外部世界就变成了 WorkFlowPrj 内部的一个标准函数，可以参与 Pipeline、可以被工作流编排、可以被其他函数调用。

### 理念四：组合优于继承（Composition over Inheritance）

WorkFlowPrj 的架构不是"类继承树"，而是"管道组合网"：

```
工作流（DAG 组合）
  ↓
Pipeline（线性组合）
  ↓
Middleware（链式组合）
  ↓
函数（原子单元）
```

每一层都是通过**组合**来构建更复杂的行为：
- 多个函数组合成工作流
- 多个 Pipeline 阶段组合成完整处理链
- 多个 Middleware 组合成请求处理管道

### 理念五：主导设计与演进式重构并存（Design & Evolution Coexist）

WorkFlowPrj 的架构不是"纯演进"的，而是**主导设计与演进式重构的辩证统一**。

**主导设计（Top-Down）：**
核心抽象层是事先设计好的，不是演进出来的：

```
FunctionStorageRegistry      ← 主导设计：统一的注册和执行入口
Pipeline 体系                ← 主导设计：标准化的处理管道
CommandFramework             ← 主导设计：命令模式的核心抽象
UniversalMiddleware           ← 主导设计：中间件链式处理模型
```

这些核心抽象在写第一行代码之前就已经在脑子里了。它们定义了 WorkFlowPrj 的"世界观"——所有功能最终都是函数，所有处理都是管道，所有操作都是命令。

**演进式重构（Bottom-Up）：**
具体实现细节是在实践中逐步修正的：

```
★ Step 4（2026-06-14）：职责拆分
★ Step 5（2026-06-14）：实现 IRegistryLifecycle
★ Step 6（2026-06-14）：解除对 FileDriven 层的编译时依赖
```

这些重构不是"发现设计错了"，而是"发现实现可以更好"——核心抽象不变，但实现方式在演进。

**两者的关系：**

```
主导设计（不变）          演进式重构（可变）
─────────────────────────────────────────────
万物皆函数                Register 还是 Adapter？
Pipeline 模型             同步 Pipeline 还是异步？
Command 模式              单一 Command 还是组合 Command？
Registry 体系             静态注册还是动态发现？
```

**主导设计保证了一致性，演进式重构保证了灵活性。** 没有主导设计，架构会变成拼凑；没有演进式重构，架构会变成僵化。

---

## 二、架构层次

WorkFlowPrj 是一个**三层架构**的代码操作系统：

```
┌──────────────────────────────────────────────────┐
│  用户态 3：工作流 JSON（声明业务逻辑的 DAG）       │
│  ┌──────────────────────────────────────────────┐ │
│  │ steps: [{funcName, parameters, inputKeys}]   │ │
│  │ 链图转换、数据流编排、AI 工作流               │ │
│  └──────────────────────────────────────────────┘ │
├──────────────────────────────────────────────────┤
│  用户态 2：UI JSON（声明界面布局和数据绑定）       │
│  ┌──────────────────────────────────────────────┐ │
│  │ type, layout, events, bindings, template     │ │
│  │ 比例布局、事件绑定、数据流、模板引用           │ │
│  └──────────────────────────────────────────────┘ │
├──────────────────────────────────────────────────┤
│  用户态 1：C# 函数注册（声明可执行单元）           │
│  ┌──────────────────────────────────────────────┐ │
│  │ IFunctionUnit → FunctionStorageRegistry      │ │
│  │ 适配器、注册器、生命周期管理                   │ │
│  └──────────────────────────────────────────────┘ │
├──────────────────────────────────────────────────┤
│  内核态：基础设施层                                │
│  ┌──────────────────────────────────────────────┐ │
│  │ Pipeline  │  CommandFramework  │  Middleware │ │
│  │ 管道       │  命令              │  中间件     │ │
│  └──────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────┘
```

### 用户态 3：工作流 JSON

通过 JSON 定义有向无环图（DAG）的工作流：

```json
{
  "steps": [
    { "stepName": "需求解析", "funcName": "PromptCommandParse", "parameters": ["${requirement}"] },
    { "stepName": "AI生成代码", "funcName": "WebInvoke", "parameters": ["${parsedPrompt}"] },
    { "stepName": "编译检查", "funcName": "CompileCheck", "parameters": ["${generatedCode}"] }
  ]
}
```

**不需要写任何 C# 代码**，只需要声明步骤之间的依赖关系和数据流。

### 用户态 2：UI JSON

通过 JSON 声明完整的用户界面：

```json
{
  "type": "button",
  "name": "executeButton",
  "text": "▶ 执行工作流",
  "functionName": "ExecuteWorkflow",
  "inputs": ["workflowComboBox", "inputTextBox"],
  "output": "outputTextBox",
  "events": { "click": "ExecuteWorkflow" }
}
```

**支持的特性：**
- 比例布局（left/top/width/height 百分比）
- 事件绑定（click、textchanged、selectedIndexChanged）
- 数据绑定（inputs → output 的数据流）
- 模板引用（template: "action-button"）
- 样式引用（style: "dark-panel"）

### 用户态 1：C# 函数注册

当 JSON 配置无法满足需求时，通过 C# 编写函数并注册：

```csharp
public class MyFunction : IFunctionUnit
{
    public Task<IFunctionExecutionResult> ExecuteAsync(IFunctionExecutionRequest request)
    {
        // 实现业务逻辑
    }
}

// 注册
FunctionStorageRegistry.Register("my_function", new MyFunction());
```

### 内核态：基础设施

- **Pipeline**：线性处理管道，支持中间件链
- **CommandFramework**：命令注册、解析、执行
- **UniversalMiddleware**：通用中间件体系
- **FunctionStorageRegistry**：函数注册和执行的核心入口

---

## 三、与 MCP 的关系

### 两种基础理念的对比

| 维度 | WorkFlowPrj | MCP |
|------|-------------|-----|
| **核心抽象** | 函数（Function） | 协议（Protocol） |
| **通用语言** | 函数名 + 参数 + 返回值 | JSON-RPC 消息 |
| **组合方式** | Pipeline / 工作流 DAG | 工具链 / 请求-响应 |
| **扩展方式** | 注册函数 + JSON 配置 | 实现协议接口 |
| **标准化手段** | 适配器（Adapter） | 协议实现（Implementation） |
| **演进方式** | 自由演进（重构驱动） | 版本化演进（协议驱动） |
| **设计哲学** | 先实现，后抽象 | 先定义，后实现 |
| **控制粒度** | 细粒度（函数级） | 粗粒度（工具级） |
| **内部一致性** | 高（同一人设计） | 低（多方协商） |
| **外部兼容性** | 低（需要适配） | 高（协议标准） |

### 互补而非对立

**WorkFlowPrj 解决的是"系统内"的问题：**
- 如何组织代码
- 如何组合功能
- 如何声明 UI
- 如何编排工作流

**MCP 解决的是"系统间"的问题：**
- 如何发现工具
- 如何调用功能
- 如何交换数据
- 如何协商能力

**两者可以完美结合：**
1. WorkFlowPrj 内部用函数注册 + JSON 配置 + 工作流编排
2. WorkFlowPrj 通过 MCP 服务器暴露内部函数
3. 外部系统通过 MCP 协议调用 WorkFlowPrj 的功能
4. WorkFlowPrj 的工作流可以调用外部 MCP 服务器的工具

---

## 四、护城河：每一行代码都是壁垒

WorkFlowPrj 的"护城河"不是封闭，而是**自洽**：

### 理解成本 = 护城河的宽度

要真正理解 WorkFlowPrj 并为其贡献代码，需要穿越：

```
第一层：理解 Pipeline 体系（Share/Pipeline）
第二层：理解 CommandFramework（Share/CommandFramework）
第三层：理解 UniversalMiddleware（Share/UniversalMiddleware）
第四层：理解 FunctionStorage（Operation/FunctionStorage）
第五层：理解业务函数（MathFunction、WdUI、FileDriven）
```

**每一层抽象都是一道城墙。**

### 重构历史 = 护城河的深度

代码注释中的日期和步骤说明了"为什么代码长成这样"——只有亲身经历过这些重构的人，才知道背后的权衡和取舍。

### 命名体系 = 护城河的暗语

- `FunctionStorageRegistry`——为什么叫 Storage 而不是 Manager？
- `NonEventCommandCenter`——什么是"非事件命令"？
- `UnifiedCommandRegistry`——"统一"什么？
- `AutoRegisterEngine`——"自动"到什么程度？

这些命名背后有一套**内部语言**，只有深入参与的人才能理解。

---

## 五、未来愿景

> **WorkFlowPrj 不是一个"封闭的项目"，而是一个"自洽的代码操作系统"。**
>
> 它现在的"封闭"不是因为拒绝开放，而是因为**基础设施尚未完工**。它的核心——FunctionStorageRegistry——是一条已经铺好的主干道，但通往各个街区的支路还在施工中。
>
> 当这座城市建成之日，它自然可以通过 MCP 协议与外部世界连接。到那时，"开源但封闭"的评价将变成**"内核稳定，生态开放"**。

### 演进路线

1. **函数发现**——在上亿个函数中找到需要的那个
2. **函数版本管理**——管理不同版本的函数
3. **函数依赖**——管理函数之间的依赖关系
4. **函数隔离**——防止一个函数的崩溃影响其他函数
5. **函数治理**——确保函数的质量、安全、合规

这些问题不需要一次性解决，而是在演进过程中逐步攻克——这正是 WorkFlowPrj 的设计哲学所擅长的。

---

## 六、总结

> **WorkFlowPrj 是一个以函数注册表（FunctionStorageRegistry）为统一内核、通过演进式设计逐步构建的、志在上亿行代码规模的个人软件框架。**
>
> **它现在的封闭是演进中的专注，而非设计上的排外。**
>
> **每一行代码都是护城河——但这条护城河围起来的不是封闭的城堡，而是一个自给自足的代码城市。城市内部有完整的道路（Pipeline）、建筑（函数）、交通规则（工作流）、城市规划（JSON 配置）。外人进入需要学习这座城市的规则，但一旦进入，就能利用整座城市的基础设施。**
>
> **当这座城市通过 MCP 与外部世界连接时，护城河就变成了护城体系——既保护内部的一致性，又开放对外的接口。**

---

*WorkFlowPrj —— 让代码成为思想的载体，让架构成为力量的源泉。*
