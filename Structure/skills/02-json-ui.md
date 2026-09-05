> 本文件为 `skills/SKILL.md` 的模块化拆分 —— **② 创建 JSON 配置 · UI（JsonConfig/*.json）**（原 §2.1 + §2.5）。
> 完整三段式流程、模块导航与端到端示例见 [SKILL.md](SKILL.md)。本模块章节沿用原编号（§2.x），便于与 SKILL.md 导航表对照。
> 相关模块：① 注册 → [01-register-function.md](01-register-function.md) · ② 配置（工作流）→ [03-json-workflow.md](03-json-workflow.md) · ③ 脚本 → [04-json-script.md](04-json-script.md)。

---

## 2.1 UI 配置（`JsonConfig/<ConfigName>.json`）

**顶层结构**：

```json
{
  "description": "功能说明",
  "initCommands": ["exec <WorkflowName> _stepSelector=Init", "exec <FuncName> output=<ControlName>"],
  "form": { "title": "窗体标题", "width": 1200, "height": 800 },
  "views": [{
    "name": "MainView",
    "type": "Panel",
    "controls": [ /* 控件树 */ ]
  }]
}
```

> **`initCommands` 两种形态**（字符串命令 / 函数模式 `initCommandMode: "function"`）见 [03-json-workflow.md §2.4](03-json-workflow.md)。

**控件类型速查**（输出侧控件字段；函数"返回类型→控件"映射见 [SKILL.md §0.2](SKILL.md)）：

| 类型 | 关键字段 | 典型用途 |
|------|----------|----------|
| `label` | `text` | 标题/提示 |
| `textbox` | `text/placeholder/multiline/readOnly/scrollBars/font` | 输入/结果/详情展示 |
| `button` | `text/events` | 动作触发 |
| `combobox` | `items/dropDownStyle` | 下拉选择（模式/配置） |
| `listview` | `view(List/Details)/columns/fullRowSelect/events` | 列表选择（域/函数/历史） |
| `groupbox` | `text/controls` | 分组容器 |
| `panel` | `controls` | 布局容器 |

**布局系统**：`layout: { left, top, width, height }`，取值 0~1 为比例（推荐），或像素绝对值。嵌套容器内部坐标相对父容器。

**事件绑定**（核心）：

```json
{
  "type": "button", "name": "execButton", "text": "执行",
  "events": {
    "click": "<WorkflowName>",
    "bindings": {
      "parameters": [
        { "target": "_stepSelector", "source": "Execute", "sourceType": "literal" },
        { "target": "<paramKey>", "source": "<ControlName>", "sourceType": "control" }
      ],
      "output": "resultTextBox"
    }
  }
}
```

| 绑定字段 | 含义 |
|----------|------|
| `target` | 传给函数/工作流的参数名（须在工作流 `defaultInput` 中存在） |
| `source` | 参数来源：`literal`（取 `source` 字面值）或 `control`（取控件当前值） |
| `output` | 结果写回的控件名 |

**事件名**：`click`（按钮）、`textChanged`（文本框）、`selectedIndexChanged`（列表/下拉）。

### 2.5 模板与样式复用（`TempletConfig/JsonConfig/`）

- **模板**：`composites.json` 内置 `title-bar-with-text` / `action-button` / `status-bar` / `labeled-textbox`，控件中写 `"template": "名称"` 引用。
- **样式**：`styles.json` 定义 `styleRef` 引用的样式。
- **⚠️ `extends` 限制**：`ExtendsConverter` 只解析**模板库中已注册的复合模板**，**不能继承任意 JSON 配置文件**。跨文件复用靠模板/样式 + 独立通用工作流实现，不要用 `extends` 指向其他配置文件。

---
