# 统一 JSON 配置模型 — 重构方案

> **目标**：将工作流定义、函数注册、UI 配置合并到一个 JSON 文件中，基于通用框架解析再分发
> **状态**：📋 方案设计阶段
> **基于** `AgentConfigUI_TreeView工作流加载分析_重构方案.md` 和 `AgentConfigUI_TreeView工作流加载分析_重构步骤.md`

---

## 1. 问题分析

### 1.1 当前架构：两条分离的注册路径

```
路径 A：MathFunctions.json（UI JSON）
  │  events.bindings.parameters → BindingMetadataTransformer
  │  → EnsureWorkflowDefinitions 自动包装 SetValue 为单节点工作流（含参数和输出目标）
  │  → EventBindingOrchestrator → RegisterWorkflows() → _workflows["SetValue"]
  │
路径 B：MathFunctions.registration.json（函数注册 JSON）
  │  SetValue type: "workflow", workflowRef: "Workflows/SetValue.workflow.json"（文件不存在）
  │  → FunctionRegistrationLoader.RegisterWorkflow → 只注册空 TrConfig（无 Delegate）
```

### 1.2 问题本质

| 问题 | 描述 | 影响 |
|------|------|------|
| P0 | `SetValue` 的工作流定义在路径 A（UI JSON）中，但路径 B 的 `workflowRef` 指向不存在的文件 | 两条路径各自为政，没有统一 |
| P1 | `FunctionRegistrationLoader.RegisterWorkflow()` 只注册空的 `ITrConfig`（`Delegate = null`） | `WorkflowExecutionLayer.ExecuteStepsCore()` 通过 `TryGetDelegate()` 找不到委托 |
| P2 | `SetValue` 不是一个真正的 C# 函数，其逻辑是"将参数值写回到 UI 控件" | 无法像 Add/Subtract 那样注册为 delegate |

### 1.3 SetValue 的特殊性

`SetValue` 没有对应的 C# 静态方法（不像 `Add`/`Subtract`/`Multiply`/`Divide` 有 `MathLibrary.Add` 等）。它的语义是"将参数值写回到 UI 控件"，其工作流定义中的**参数绑定和输出目标完全来自 UI JSON 的 `events.bindings`**，每个控件的绑定不同：

```json
{
  "type": "textbox",
  "name": "valueATextBox",
  "events": {
    "textchanged": "SetValue",
    "bindings": {
      "parameters": [
        { "target": "value", "source": "valueATextBox", "sourceType": "control" }
      ],
      "output": "resultTextBox"
    }
  }
}
```

这意味着 `SetValue` 的工作流定义无法在函数注册中静态定义——它必须由 UI 配置驱动。

---

## 2. 方案设计：三层统一配置模型

### 2.1 设计原则

1. **一个 JSON 文件包含所有信息** — 函数注册、工作流定义、UI 配置都在一个地方
2. **通用加载器解析后分发给不同处理方** — `UnifiedConfigLoader` 负责解析和分发
3. **SetValue 的工作流定义由 UI 配置驱动** — 参数绑定和输出目标来自 `events.bindings`，不由 functions 静态定义
4. **框架复用** — 不重新造轮子，Registry 负责注册，UniversalMiddleware 负责管线，BackEndDriven 负责编排

### 2.2 统一 JSON 格式

**文件路径**：`FileDriven/DrivenConfig/UnifiedConfig/MathFunctions.unified.json`

```json
{
  "$schema": "unified-function-config",
  "version": "1.0",
  "description": "内置数学函数测试",
  
  "functions": [
    {
      "name": "Add",
      "type": "delegate",
      "source": "FunctionStorage.MathLibrary, FileDriven.Business",
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
      "source": "FunctionStorage.MathLibrary, FileDriven.Business",
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
      "source": "FunctionStorage.MathLibrary, FileDriven.Business",
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
      "source": "FunctionStorage.MathLibrary, FileDriven.Business",
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
      "description": "设置值"
      // ★ 没有 workflowRef，没有 workflow 内联定义
      // ★ 工作流定义由 UI JSON 的 BindingMetadataTransformer 自动包装
      // ★ 此处只注册 TrConfig，让 _registry.TryGetConfig 能找到
    }
  ],
  
  "ui": {
    "form": {
      "title": "Built-in Math Functions Test",
      "width": 500,
      "height": 480
    },
    "views": [
      {
        "name": "MainView",
        "type": "Panel",
        "controls": [
          {
            "template": "title-bar-with-text",
            "name": "titleLabel",
            "text": "Built-in Math Functions"
          },
          {
            "type": "groupbox",
            "name": "inputGroupBox",
            "text": "Input Values",
            "style": "groupbox-default",
            "layout": { "left": 0.05, "top": 0.05, "width": 0.9, "height": 0.22 },
            "controls": [
              {
                "type": "label",
                "name": "valueALabel",
                "text": "Value A:",
                "layout": { "left": 0.05, "top": 0.2, "width": 0.15, "height": 0.25 }
              },
              {
                "type": "textbox",
                "name": "valueATextBox",
                "placeholder": "Enter number",
                "layout": { "left": 0.22, "top": 0.2, "width": 0.3, "height": 0.3 },
                "events": {
                  "textchanged": "SetValue",
                  "bindings": {
                    "parameters": [
                      { "target": "value", "source": "valueATextBox", "sourceType": "control" }
                    ],
                    "output": "resultTextBox"
                  }
                }
              },
              {
                "type": "label",
                "name": "valueBLabel",
                "text": "Value B:",
                "layout": { "left": 0.55, "top": 0.2, "width": 0.15, "height": 0.25 }
              },
              {
                "type": "textbox",
                "name": "valueBTextBox",
                "placeholder": "Enter number",
                "layout": { "left": 0.72, "top": 0.2, "width": 0.25, "height": 0.3 },
                "events": {
                  "textchanged": "SetValue",
                  "bindings": {
                    "parameters": [
                      { "target": "value", "source": "valueBTextBox", "sourceType": "control" }
                    ],
                    "output": "resultTextBox"
                  }
                }
              }
            ]
          },
          {
            "type": "groupbox",
            "name": "functionGroupBox",
            "text": "Math Functions",
            "layout": { "left": 0.05, "top": 0.28, "width": 0.9, "height": 0.28 },
            "controls": [
              {
                "template": "action-button",
                "name": "addButton",
                "text": "Add (+)",
                "functionName": "Add",
                "events": {
                  "click": "Add",
                  "bindings": {
                    "parameters": [
                      { "target": "a", "source": "valueATextBox", "sourceType": "control", "type": "double" },
                      { "target": "b", "source": "valueBTextBox", "sourceType": "control", "type": "double" }
                    ],
                    "output": "resultTextBox"
                  }
                },
                "layout": { "left": 0.05, "top": 0.15, "width": 0.2, "height": 0.3 }
              },
              {
                "template": "action-button",
                "name": "subtractButton",
                "text": "Subtract (-)",
                "functionName": "Subtract",
                "events": {
                  "click": "Subtract",
                  "bindings": {
                    "parameters": [
                      { "target": "a", "source": "valueATextBox", "sourceType": "control", "type": "double" },
                      { "target": "b", "source": "valueBTextBox", "sourceType": "control", "type": "double" }
                    ],
                    "output": "resultTextBox"
                  }
                },
                "layout": { "left": 0.28, "top": 0.15, "width": 0.2, "height": 0.3 }
              },
              {
                "template": "action-button",
                "name": "multiplyButton",
                "text": "Multiply (*)",
                "functionName": "Multiply",
                "events": {
                  "click": "Multiply",
                  "bindings": {
                    "parameters": [
                      { "target": "a", "source": "valueATextBox", "sourceType": "control", "type": "double" },
                      { "target": "b", "source": "valueBTextBox", "sourceType": "control", "type": "double" }
                    ],
                    "output": "resultTextBox"
                  }
                },
                "layout": { "left": 0.51, "top": 0.15, "width": 0.2, "height": 0.3 }
              },
              {
                "template": "action-button",
                "name": "divideButton",
                "text": "Divide (/)",
                "functionName": "Divide",
                "events": {
                  "click": "Divide",
                  "bindings": {
                    "parameters": [
                      { "target": "a", "source": "valueATextBox", "sourceType": "control", "type": "double" },
                      { "target": "b", "source": "valueBTextBox", "sourceType": "control", "type": "double" }
                    ],
                    "output": "resultTextBox"
                  }
                },
                "layout": { "left": 0.74, "top": 0.15, "width": 0.2, "height": 0.3 }
              },
              {
                "type": "label",
                "name": "selectedFuncLabel",
                "text": "Selected Function:",
                "layout": { "left": 0.05, "top": 0.55, "width": 0.25, "height": 0.25 }
              },
              {
                "type": "combobox",
                "name": "functionComboBox",
                "items": ["Add", "Subtract", "Multiply", "Divide"],
                "layout": { "left": 0.32, "top": 0.55, "width": 0.35, "height": 0.3 }
              }
            ]
          },
          {
            "type": "groupbox",
            "name": "resultGroupBox",
            "text": "Result",
            "layout": { "left": 0.05, "top": 0.58, "width": 0.9, "height": 0.18 },
            "controls": [
              {
                "type": "label",
                "name": "resultLabel",
                "text": "Result:",
                "layout": { "left": 0.05, "top": 0.25, "width": 0.15, "height": 0.3 }
              },
              {
                "type": "textbox",
                "name": "resultTextBox",
                "style": "readonly-bg",
                "readOnly": true,
                "font": { "name": "Consolas", "size": 11, "bold": true },
                "layout": { "left": 0.22, "top": 0.25, "width": 0.7, "height": 0.4 }
              }
            ]
          },
          {
            "template": "status-bar",
            "name": "statusLabel",
            "text": "选择数值并点击运算按钮查看结果"
          }
        ]
      }
    ]
  }
}
```

### 2.3 统一配置模型（C# 模型）

```csharp
// UnifiedConfig.cs — 统一配置模型

namespace BackEndDriven.UnifiedConfig
{
    /// <summary>
    /// 统一配置 — 包含函数注册和 UI 配置
    /// </summary>
    public class UnifiedConfig
    {
        public string? Schema { get; set; }
        public string? Version { get; set; }
        public string? Description { get; set; }
        public List<RegistrationEntry> Functions { get; set; } = new();
        public UnifiedUiConfig? Ui { get; set; }
    }

    /// <summary>
    /// UI 配置部分
    /// </summary>
    public class UnifiedUiConfig
    {
        public UnifiedFormConfig? Form { get; set; }
        public List<UnifiedViewConfig> Views { get; set; } = new();
    }

    public class UnifiedFormConfig
    {
        public string? Title { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }

    public class UnifiedViewConfig
    {
        public string? Name { get; set; }
        public string? Type { get; set; }
        public List<object> Controls { get; set; } = new();
    }
}
```

---

## 3. 协作代码位置：BackEndDriven

### 3.1 为什么放在 BackEndDriven？

`BackEndDriven` 已经是 WorkFlowPrj 的核心执行框架，提供 Builder、Pipeline、Invoke 等能力。**统一 JSON 配置的加载和分发**作为"后端驱动"的入口配置层，自然应该放在 `BackEndDriven` 中。

| 维度 | 理由 |
|------|------|
| **职责匹配** | BackEndDriven 负责"后端驱动执行"，统一配置加载是执行的入口 |
| **Builder 模式** | BackEndDrivenBuilder 已是 Facade 入口，新增 `WithUnifiedConfig()` 自然扩展 |
| **依赖完备** | BackEndDriven 已依赖 Registry（函数注册）和 UniversalMiddleware（管线渲染） |
| **避免循环依赖** | 放在 FileDriven.Business 会导致 FileDriven → BackEndDriven 的逆向依赖 |

### 3.2 文件目录结构

```
CsharpPrj/Operation/BackEndDriven/
├── BackEndDriven.csproj
├── BackEndDrivenBuilder.cs              # 统一构建入口（Facade）— 新增 UnifiedConfig 相关方法
├── README.md
│
├── UnifiedConfig/                       # ★ 新增：统一配置目录
│   ├── README.md                        # 使用理念文档
│   ├── UnifiedConfigLoader.cs           # 统一配置加载器（解析→分发）
│   ├── UnifiedConfig.cs                 # 统一配置模型（functions + ui）
│   └── UnifiedConfigExtensions.cs       # 扩展方法（如 ToUIConfiguration 转换）
│
├── Pipeline/                            # 管线系统（已有）
│   ├── RenderPipelineOrchestrator.cs    # 渲染管线编排器
│   └── ...
│
├── Invoke/                              # 参数执行工作流（已有）
│   └── ...
│
└── WorkFlowMethodBridge/                # 工作流方法桥接（已有）
    └── BackEndDrivenBuilder.cs          # 已有，新增 UnifiedConfig 配置方法
```

### 3.3 使用理念

**核心理念**：一个 JSON 文件 = 一个完整的功能模块定义。`BackEndDrivenBuilder` 作为统一入口，通过链式调用完成"加载→解析→注册→渲染"全流程。

```
UnifiedConfig JSON（一个文件）
  │
  ▼
BackEndDrivenBuilder
  │  .WithUnifiedConfig("MathFunctions.unified.json")
  │  .WithWorkflowMethodExecution()
  │  .BuildEngine()
  │
  ├── UnifiedConfigLoader 解析 JSON
  │     ├── functions → FunctionRegistry（Registry 框架）
  │     │     ├── delegate 类型 → Register(name, delegate, params)
  │     │     └── workflow 类型 → Register(name, trConfig)
  │     │
  │     └── ui → RenderPipelineOrchestrator（UniversalMiddleware 管线）
  │           ├── JsonConfigLoadStage → 加载 UIConfiguration
  │           ├── BindingMetadataTransformer → 自动包装 SetValue 工作流
  │           └── EventBindingOrchestrator → 绑定事件
  │
  └── 执行引擎就绪
```

**设计原则**：

1. **单一 JSON 入口** — 一个 `.unified.json` 文件包含函数注册 + UI 配置 + 工作流定义
2. **框架复用** — Registry 负责注册，UniversalMiddleware 负责管线，BackEndDriven 负责编排
3. **Builder 链式配置** — 通过 `BackEndDrivenBuilder` 的扩展方法完成所有配置
4. **SetValue 特殊处理** — 工作流定义由 UI 配置的 `events.bindings` 自动包装，不在 functions 中静态定义

### 3.4 BackEndDrivenBuilder 新增方法

| 方法 | 用途 |
|------|------|
| `WithUnifiedConfig(configPath)` | 加载统一 JSON 配置文件 |
| `WithUnifiedConfigLoader(loader)` | 注入自定义 UnifiedConfigLoader |
| `BuildWithUnifiedConfig()` | 一步完成：加载配置 + 注册函数 + 渲染 UI |

### 3.5 使用示例

```csharp
// 方式一：一步完成（推荐）
var engine = new BackEndDrivenBuilder()
    .WithPipeline(pipeline)
    .WithCommandRegistry(registry)
    .WithCommandParser(parser)
    .WithUnifiedConfig("MathFunctions.unified.json")  // 加载统一配置
    .WithWorkflowMethodExecution()                     // 使用工作流执行
    .BuildEngine();

// 方式二：分步控制
var builder = new BackEndDrivenBuilder()
    .WithPipeline(pipeline)
    .WithCommandRegistry(registry);

// 加载统一配置（注册函数 + 渲染 UI）
builder.LoadUnifiedConfig("MathFunctions.unified.json", targetForm);

// 构建引擎
var engine = builder
    .WithWorkflowMethodExecution()
    .BuildEngine();
```

---

## 4. 通用加载器框架

### 4.1 加载器设计

```
UnifiedConfigLoader.LoadFromFile("MathFunctions.unified.json")
  │
  ├── 1. 解析 JSON → UnifiedConfig 模型
  │
  ├── 2. 分发 functions → FunctionRegistrationLoader.LoadFunctions()
  │     ├── delegate 类型 → _registry.Register(name, delegate, params)
  │     └── workflow 类型 → _registry.Register(name, trConfig)  // 只注册 TrConfig
  │
  └── 3. 分发 ui → EventBindingOrchestrator.ExecuteWithWalk()
        └── BindingMetadataTransformer 自动包装 SetValue 工作流定义
              → RegisterWorkflows() → _workflows["SetValue"]（含参数和输出目标）
```

### 4.2 依赖关系

```
BackEndDriven（编排层）
  │
  ├── 依赖 → Registry（Modules.Registry）
  │     └── FunctionRegistry — 注册 delegate/workflow 函数
  │
  ├── 依赖 → UniversalMiddleware（Share.UniversalMiddleware）
  │     ├── JsonPipelineBuilder — JSON 解析管线
  │     ├── ConfigRenderPipeline — 配置渲染管线
  │     └── EventMappingService — 事件映射
  │
  └── 依赖 → FileDriven.Business（原有）
        └── FunctionRegistrationLoader — 函数注册加载
```

### 4.3 框架复用分析

| 能力 | Registry 框架 | UniversalMiddleware | 新代码 |
|------|---------------|-------------------|--------|
| 函数注册 | ✅ FunctionRegistry | ❌ | 直接复用 |
| 注册中心 | ✅ UnifiedRegistryCenter | ❌ | 直接复用 |
| 冲突解决 | ✅ IConflictResolver | ❌ | 直接复用 |
| 生命周期 | ✅ IRegistryLifecycle | ❌ | 直接复用 |
| JSON 管线 | ❌ | ✅ JsonPipelineBuilder | 直接复用 |
| 渲染管线 | ❌ | ✅ ConfigRenderPipeline | 直接复用 |
| 事件映射 | ❌ | ✅ EventMappingService | 直接复用 |
| 统一加载 | ❌ | ❌ | **UnifiedConfigLoader** |
| Builder 扩展 | ❌ | ❌ | **BackEndDrivenBuilder 扩展** |

---

## 5. 执行流程对比

### 5.1 当前（问题）流程

```
MathFunctions.json
  → BindingMetadataTransformer → _workflows["SetValue"](含参数和输出目标)

MathFunctions.registration.json
  → FunctionRegistrationLoader → TrConfig(空, Delegate=null)

ExecuteFunctionAsync("SetValue")
  → 找到 _workflows 定义 → 执行节点
  → _registry.TryGetConfig("SetValue") → 找到空 TrConfig
  → 自动包装为单节点工作流(丢失参数和输出目标)
```

### 5.2 新流程

```
MathFunctions.unified.json
  → UnifiedConfigLoader.LoadFromFile()
    │
    ├── FunctionRegistrationLoader.LoadFunctions()
    │   → _registry.Register("Add", delegate)     // delegate 类型
    │   → _registry.Register("SetValue", trConfig) // workflow 类型，只注册 TrConfig
    │
    └── EventBindingOrchestrator.ExecuteWithWalk()
        → BindingMetadataTransformer
          → EnsureWorkflowDefinitions 自动包装 SetValue（含参数和输出目标）
        → RegisterWorkflows() → _workflows["SetValue"]

ExecuteFunctionAsync("SetValue")
  → 找到 _workflows 定义(含参数和输出目标) → 执行节点
  → _registry.TryGetConfig("SetValue") → 找到 TrConfig → 正常执行
```

---

## 6. 需要新增/修改的文件

| 文件 | 变更类型 | 说明 |
|------|---------|------|
| `FileDriven/DrivenConfig/JsonConfig/MathFunctions.json` | **删除** | 内容合并到 UnifiedConfig |
| `FileDriven/DrivenConfig/FunctionRegistrations/MathFunctions.registration.json` | **删除** | 内容合并到 UnifiedConfig |
| `FileDriven/DrivenConfig/UnifiedConfig/MathFunctions.unified.json` | **新增** | 统一配置新文件 |
| `CsharpPrj/Operation/BackEndDriven/UnifiedConfig/UnifiedConfigLoader.cs` | **新增** | 统一加载器 |
| `CsharpPrj/Operation/BackEndDriven/UnifiedConfig/UnifiedConfig.cs` | **新增** | 统一配置模型 |
| `CsharpPrj/Operation/BackEndDriven/UnifiedConfig/UnifiedConfigExtensions.cs` | **新增** | 扩展方法 |
| `CsharpPrj/Operation/BackEndDriven/UnifiedConfig/README.md` | **新增** | 使用理念文档 |
| `CsharpPrj/Operation/BackEndDriven/BackEndDrivenBuilder.cs` | **修改** | 新增 `WithUnifiedConfig()` 等方法 |
| `FileDriven/FileDriven.Business/Registrations/FunctionRegistrationLoader.cs` | **修改** | 新增 `LoadFunctions(List<RegistrationEntry>)` 方法 |
| `FileDriven/FileDriven.Business/Registrations/FunctionRegistrationLoader.cs` | **修改** | `RegisterWorkflow` 移除 `workflowRef` 逻辑 |

---

## 7. 验证清单

- [x] `UnifiedConfig` 模型 JSON 反序列化正常（`MathFunctions.unified.json` 运行时验证：Functions=5、Ui 含 MainView + 15 控件）
- [ ] `FunctionRegistrationLoader.LoadFunctions()` 正确处理 delegate 和 workflow 类型
- [ ] `RegisterWorkflow` 不再处理 `workflowRef`（移除不存在的文件引用）
- [x] `UnifiedConfigLoader` 正确将 `ui` 部分转换为 `UIConfiguration`（2026-08-05 实施：`UnifiedViewConfig.Controls`→`List<ControlConfig>`、`ToUIConfiguration` 映射控件；新增 `IsUnifiedFormat`/`ParseFromFile` 静态解析入口；`FileDriven.Execution/Initial/UnifiedConfigLoadAdapter.cs` 接入 `MainFormGateFactory` 两处 configLoader，统一格式自动探测）
- [ ] `EventBindingOrchestrator.ExecuteWithWalk` 正常执行 UI 事件绑定
- [ ] `BindingMetadataTransformer.EnsureWorkflowDefinitions` 正常自动包装 SetValue
- [ ] `WorkflowMethodExecutionService.ExecuteFunctionAsync("SetValue")` 找到含参数和输出目标的工作流定义
- [ ] 构建无编译错误

---

## 8. 回退方案

如果重构后出现问题，可以：

1. **文件回退**：恢复 `MathFunctions.json` 和 `MathFunctions.registration.json`
2. **代码回退**：恢复 `FunctionRegistrationLoader` 的旧实现
3. **删除新文件**：删除 `UnifiedConfigLoader.cs`、`UnifiedConfig.cs` 和 `MathFunctions.unified.json`
