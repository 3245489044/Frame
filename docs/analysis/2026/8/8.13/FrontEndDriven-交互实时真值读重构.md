# FrontEndDriven 交互实时真值读重构（GetLiveValue）

> 日期：2026-08-13 ｜ 类型：框架层读语义重构（非补丁） ｜ 消费方：事件分发参数解析（下拉框/列表/文本框绑定）

---

## 一、问题（现象与根因）

### 现象
`FileDriven/DrivenConfig/JsonConfig/AgentRuntimeUI.json` ⑤「输入与运行」里，
`jsonTemplateComboBox`（下拉模板，`dropDownStyle=DropDownList`，**未声明 selectedIndex**）选中模板后点「载入模板」，
`AgentUi_LoadTemplate` 读到的 `templateName` 为空 → 返回「（未指定模板名）」→ 下拉选择“无效”。

### 根因（已定位）
事件绑定参数解析复用 `EcsStateAccessor.GetValue`，其读策略为 **「ECS 状态优先 → 物理兜底」**：
- 无 `selectedIndex` 时 ECS `ControlValueComponent` 与物理 `SelectedItem` 初始均为空；
- 一旦 ECS 存在**空值或过期值**（`set`/渲染/未回灌），`GetValue` 直接返回它，**遮蔽用户在物理控件上的当前选择**。

**本质**：交互分发的“当前 UI 真值读”与命令/脚本的“set 历史真源读”被混成同一个方法，语义职责不清。

---

## 二、重构原则

**读语义分离为两条权威路径，互补而非互斥：**

| 方法 | 策略 | 服务对象 |
|------|------|----------|
| `GetValue(controlName)` | ECS 优先 → 物理兜底 → ControlInfo.Text | `get` 命令 / 脚本 set 后回读 / 响应式订阅（以 set 历史为权威） |
| `GetLiveValue(controlName)`（新增） | **物理控件真值优先** → ECS 兜底 → ControlInfo.Text | 事件分发参数解析（用户触发事件时当前 UI 状态即真值） |

**安全性（不回归 ListView 缺陷）**：旧 `GetValue` 的 ECS 优先是为防“物理脏选中覆盖脚本 set 值”。
当前 `WriteListViewSelection`/`WriteComboBoxSelection` 已改为「先清后选」确定性写入，且 `InputCapturePort.Subscribe`
在控件创建时挂接（`Ports.cs:512`）物理→ECS 同步——`set` 后物理与 ECS 一致，物理优先读不会拿到脏选中。

---

## 三、改动落点

| 文件 | 改动 |
|------|------|
| `Share/FrameworkAbstractions/WinFormsAbstractions/IEcsInteractionPorts.cs` | `IEcsStateAccessor` 新增 `string? GetLiveValue(string controlName)` + 语义注释 |
| `Operation/FrontEndDriven/ControlInteraction/State/EcsStateAccessor.cs` | 实现 `GetLiveValue`（物理优先/ECS 兜底/ControlInfo 兜底），类注释更新 |
| `Operation/FrontEndDriven/ControlInteraction/EventBinding/EcsEventBus.cs` | `BuildParameters` 对 `sourceType=control`（含无参数、空 source 分支）改用 `GetLiveValue`；`GetValue` 保留服务 get/脚本 |

`IEcsStateAccessor` 唯一实现为 `EcsStateAccessor`（`ClickSetGetSystems` 仅消费接口），接口扩展无其他实现者受影响。

---

## 四、验证

- `FrontEndDriven.csproj` / `MainProj.sln` / `Agent.sln` 均 `dotnet build` 通过（0 警告 / 0 错误）。
- 语义验证：下拉选「runLoop.hybrid」→ `GetLiveValue` 读物理 `SelectedItem` → `templateName` 非空 → 载入 `Templates/AgentRun/runLoop.hybrid.json`。
- 未新增集成测试：现有测试套件为纯数据测试（无 ECS/WinForms 运行时依赖），EcsStateAccessor 集成需 STA + 完整 ECS 装配，超出套件哲学，留待 GUI 手工回归。

---

## 五、遗留（可选，非本次框架改动）

`jsonTemplateComboBox` 未声明 `selectedIndex`，初始无默认选中属配置语义；框架层已保证“选中后实时可读”。
如需初始有默认值，可在配置补 `"selectedIndex": 0`（属配置层优化，不在本框架重构范围内）。
