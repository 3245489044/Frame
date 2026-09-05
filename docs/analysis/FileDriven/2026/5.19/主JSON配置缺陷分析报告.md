# 主 JSON 配置缺陷分析报告

## 分析日期
2026-05-20

## 分析范围
FileDriven/DrivenConfig/JsonConfig/ 下的所有 JSON 配置文件

---

## 🔴 严重配置缺陷

### 1. WorkflowInputUI.json — 布局格式不兼容
**文件**: `DrivenConfig/JsonConfig/WorkflowInputUI.json`
**问题**: 该文件使用 `"x"`/`"y"` 作为布局字段名，但 WdUI 框架要求使用 `"left"`/`"top"`。
```json
// ❌ 当前写法（不兼容）
"layout": { "x": 0, "y": 0, "width": 1, "height": 0.4 }

// ✅ 正确写法
"layout": { "left": 0.0, "top": 0.0, "width": 1.0, "height": 0.4 }
```
**影响**: 所有控件的布局定位完全失效，控件会堆叠在左上角。
**涉及控件**: inputGroup、param1Label、param1Input、param2Label、param2Input、actionGroup、executeBtn、resultGroup、resultOutput

### 2. WorkflowInputUI.json — 缺少 `"type"` 字段
**文件**: `DrivenConfig/JsonConfig/WorkflowInputUI.json` 第12行
**问题**: `mainView` 视图缺少 `"type": "Panel"` 字段。
```json
// ❌ 当前写法
{
  "name": "mainView",
  "controls": [...]

// ✅ 正确写法
{
  "name": "mainView",
  "type": "Panel",
  "controls": [...]
```
**影响**: 视图可能无法正确渲染。

### 3. WorkflowInputUI.json — container 控件缺少 `"type"` 字段
**文件**: `DrivenConfig/JsonConfig/WorkflowInputUI.json` 第14、77、108行
**问题**: `inputGroup`、`actionGroup`、`resultGroup` 声明为 `"type": "container"`，但 WdUI 框架不支持 `"container"` 类型，应使用 `"groupbox"` 或 `"panel"`。
```json
// ❌ 当前写法
{ "type": "container", "name": "inputGroup", ... }

// ✅ 正确写法
{ "type": "groupbox", "name": "inputGroup", ... }
```
**影响**: 这三个容器控件无法创建，其子控件也不会显示。

### 4. WorkflowInputUI.json — `"readonly"` 字段名错误
**文件**: `DrivenConfig/JsonConfig/WorkflowInputUI.json` 第123行
**问题**: 使用 `"readonly": true`（小写），但 WdUI 框架要求 `"readOnly": true`（驼峰命名）。
```json
// ❌ 当前写法
"readonly": true

// ✅ 正确写法
"readOnly": true
```
**影响**: `resultOutput` 文本框的只读属性可能不生效。

### 5. WorkflowInputUI.json — `"title"` 字段名错误
**文件**: `DrivenConfig/JsonConfig/WorkflowInputUI.json` 第79行
**问题**: `actionGroup` 使用 `"title"` 字段，但 GroupBox 应使用 `"text"` 字段。
```json
// ❌ 当前写法
"title": "操作"

// ✅ 正确写法
"text": "操作"
```
**影响**: 分组框标题不显示。

---

## 🟡 中等严重性配置缺陷

### 6. WorkflowRunner.json — 文档与实际配置不一致
**文件**: `DrivenConfig/JsonConfig/WorkflowRunner.json`
**问题**: 工作流运行文档（`DrivenConfig/Usings/工作流运行文档.md`）中描述了 `apiKeyLabel`、`apiKeyTextBox`、`loadEnvKeyButton` 三个控件，但实际 `WorkflowRunner.json` 配置中**不存在**这些控件。
```json
// 文档描述（第57-61行）：
// - apiKeyLabel (Label)：显示"API Key:"
// - apiKeyTextBox (TextBox)：API Key 输入框
// - loadEnvKeyButton (Button)：📥 加载环境变量按钮

// 实际 WorkflowRunner.json 的 inputGroup 中只有：
// - inputLabel (Label)
// - inputTextBox (TextBox)
```
**影响**: 
- 测试脚本 `test_deepseek_chat_workflow.txt` 第39行 `click loadEnvKeyButton` 会失败
- 测试脚本 `test_math_workflow.txt` 中虽然没有直接引用，但工作流执行时缺少 API Key 输入
- 用户无法在界面上输入 API Key

### 7. WorkflowRunner.json — executeButton 缺少 apiKeyTextBox 绑定
**文件**: `DrivenConfig/JsonConfig/WorkflowRunner.json` 第183-190行
**问题**: 即使未来添加了 `apiKeyTextBox`，`executeButton` 的 `bindings.inputs` 中也没有包含它。
```json
// ❌ 当前
"inputs": ["workflowComboBox", "inputTextBox"]

// ✅ 应改为（如果添加了 apiKeyTextBox）
"inputs": ["workflowComboBox", "inputTextBox", "apiKeyTextBox"]
```
**影响**: 需要 API Key 的工作流（如 DeepSeekChatWorkflow、BatchRefactorWorkflow）无法获取 API Key。

### 8. DeepSeekChatWorkflow.json — 第二步参数引用可能错误
**文件**: `DrivenConfig/WorkFlow/DeepSeekChatWorkflow.json` 第25-28行
**问题**: 第二步 `AI生成回复` 的 parameters 中同时使用了 `${analysisResult}` 和 `${userInput}`，但 `inputKeys` 中只声明了 `["analysisResult"]`。
```json
"inputKeys": ["analysisResult"],
"parameters": [
  "你是一个有帮助的AI助手...\n\n以下是提示词分析结果：\n${analysisResult}",
  "${userInput}"   // ← userInput 未在 inputKeys 中声明
]
```
**影响**: 如果工作流引擎依赖 `inputKeys` 来传递参数，`${userInput}` 可能无法正确解析。

### 9. CodeReviewWorkflow.json — 第二步参数引用错误
**文件**: `DrivenConfig/WorkFlow/CodeReviewWorkflow.json` 第19-24行
**问题**: 第二步 `代码优化` 的 `inputKeys` 声明为 `["analysisResult"]`，但 parameters 中使用了 `${codeInput}`（原始输入），而不是 `${analysisResult}`。
```json
"inputKeys": ["analysisResult"],
"parameters": ["${apiKey}", "${codeInput}"]  // ← 应使用 ${analysisResult}？
```
**影响**: 代码优化步骤可能使用了未声明的输入依赖。

---

## 🟢 轻微配置缺陷

### 10. MainLayout.json — contentContainer 和 sideContainer 使用 container 类型
**文件**: `DrivenConfig/JsonConfig/MainLayout.json` 第119、223行
**问题**: `contentContainer` 和 `sideContainer` 使用 `"type": "container"`，但 WdUI 框架可能不支持此类型。
```json
{ "type": "container", "name": "contentContainer", ... }
```
**影响**: 虽然 `MainForm.cs` 中通过 `Controls.Find` 查找 `ConfigContainer` 控件，但如果 `"container"` 类型不被识别，可能导致加载失败。

### 11. MainLayout.json — splitcontainer 类型可能不标准
**文件**: `DrivenConfig/JsonConfig/MainLayout.json` 第85、107行
**问题**: 使用 `"type": "splitcontainer"`，但标准 WinForms 控件名为 `SplitContainer`。WdUI 框架可能期望 `"splitContainer"`（驼峰命名）。
```json
// ❌ 当前
"type": "splitcontainer"

// ✅ 可能期望
"type": "splitContainer"
```
**影响**: 分割容器可能无法创建，导致主布局完全失效。

### 12. ConfigSelector.json — loadSelButton 使用 bottom 定位但缺少 top
**文件**: `DrivenConfig/JsonConfig/ConfigSelector.json` 第55-59行
**问题**: `loadSelButton` 使用 `bottom` 定位，但同一容器中其他控件使用 `top` 定位，混合使用可能导致布局冲突。
```json
"layout": {
  "left": 0.02,
  "bottom": 0.12,   // ← 底部定位
  "width": 0.12,
  "height": 0.06
}
```
**影响**: 在窗口大小变化时，底部定位的按钮可能与顶部定位的控件重叠。

### 13. default.json — 配置格式与 WdUI 框架不兼容
**文件**: `DrivenConfig/Configs/default.json`
**问题**: 该文件使用完全不同的格式（`Components`、`Location`、`Size`、`Events` 数组），与 WdUI 框架的 `UIConfiguration` 模型不兼容。
```json
// default.json 格式
{
  "Name": "AutoLoadWindow",
  "Components": [
    {
      "Name": "label1",
      "Type": "Label",
      "Location": { "X": 20, "Y": 20 },
      "Size": { "Width": 300, "Height": 30 }
    }
  ]
}

// WdUI 框架期望的格式
{
  "form": { "title": "...", ... },
  "views": [
    {
      "name": "MainView",
      "type": "Panel",
      "controls": [
        {
          "type": "label",
          "name": "label1",
          "layout": { "left": ..., "top": ..., ... }
        }
      ]
    }
  ]
}
```
**影响**: `default.json` 无法被 WdUI 框架加载，如果作为回退配置使用会导致加载失败。

### 14. framework.config.json — loaders.extensions 与 pipeline.stages 不匹配
**文件**: `framework.config.json`
**问题**: `loaders.extensions` 支持 `.workflow`、`.json`、`.txt`、`.cmd` 四种扩展名，但 `pipeline.stages` 中定义的 5 个阶段（LoadStage、ParseStage、ResolveStage、ExecuteStage、OutputStage）没有对应的具体实现配置。
```json
"pipeline": {
  "stages": [
    { "type": "LoadStage", "order": 1, "settings": {} },
    { "type": "ParseStage", "order": 2, "settings": {} },
    ...
  ]
}
```
**影响**: 管线阶段只有类型名没有具体实现，可能导致管线执行时找不到对应的 stage handler。

---

## 修复建议优先级

| 优先级 | 缺陷编号 | 文件 | 建议操作 |
|--------|---------|------|---------|
| 🔴 P0 | #1, #2, #3, #4, #5 | WorkflowInputUI.json | 完全重写该文件，使用正确的 WdUI 格式 |
| 🔴 P0 | #6 | WorkflowRunner.json | 添加缺失的 apiKeyLabel、apiKeyTextBox、loadEnvKeyButton 控件 |
| 🟡 P1 | #7 | WorkflowRunner.json | executeButton 的 bindings.inputs 添加 apiKeyTextBox |
| 🟡 P1 | #8, #9 | DeepSeekChatWorkflow.json, CodeReviewWorkflow.json | 修正 inputKeys 与 parameters 的一致性 |
| 🟡 P1 | #10, #11 | MainLayout.json | 验证 container/splitcontainer 类型是否被支持 |
| 🟢 P2 | #12 | ConfigSelector.json | 统一使用 top 定位或 bottom 定位 |
| 🟢 P2 | #13 | default.json | 重写为 WdUI 兼容格式或移除 |
| 🟢 P2 | #14 | framework.config.json | 补充 stage 实现配置 |

---

## 总结

共发现 **14 个配置缺陷**，其中：
- **🔴 严重**: 5 个（WorkflowInputUI.json 完全不可用）
- **🟡 中等**: 4 个（功能缺失或行为异常）
- **🟢 轻微**: 5 个（兼容性或一致性问题）

最严重的问题是 `WorkflowInputUI.json` 使用了完全错误的布局格式（`x`/`y` 替代 `left`/`top`），以及 `WorkflowRunner.json` 缺少文档中描述的 API Key 相关控件。
