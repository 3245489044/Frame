## 测试优先级分析

基于代码结构和依赖关系分析，以下是按优先级排序的测试建议：

---

### 🔴 P0 — 核心逻辑（必须测试）

**1. `EventExecutionService.ExecuteFunctionAsync`**（BackEndDriven/Execution）
- **为什么**：这是事件绑定链路的最终执行入口，所有 UI 事件最终都经过此方法
- **测试内容**：
  - 函数名称为空 → 返回 `Fail("函数名为空")`
  - 函数未注册 → 返回 `Fail("函数 'xxx' 未注册")`
  - 委托为 null → 返回 `Fail("函数 'xxx' 的委托为空")`
  - 同步委托执行成功 → 返回 `Ok(result)`
  - 异步委托（Task）执行成功 → 返回 `Ok(result)`
  - 委托抛出异常 → 返回 `Fail("函数 'xxx' 执行异常: ...")`
- **依赖**：`IFunctionRegistry`（可 Mock）
- **复杂度**：低，纯逻辑，无 UI 依赖

**2. `FunctionRegistry`**（Modules/Registry/06_ConcreteRegistries.cs）
- **为什么**：`IFunctionRegistry` 是整个系统的函数注册中心，`EventExecutionService` 依赖它
- **测试内容**：
  - `Register` + `Contains` — 注册后能查到
  - `TryGetDelegate` — 能获取到注册的委托
  - `TryGetDescriptor` — 描述符信息正确
  - `GetRegisteredNames` — 返回所有注册名
  - `Unregister` — 注销后查不到
  - `Clear` — 清空后为空
  - 大小写不敏感（默认 `OrdinalIgnoreCase`）
  - 重复注册覆盖
- **依赖**：无（纯内存操作）
- **复杂度**：低

---

### 🟠 P1 — 关键链路（应该测试）

**3. `EventBinderTransformer.Transform`**（EventTriggerFramework/EventBinding）
- **为什么**：这是事件绑定的核心转换器，负责遍历事件类型并调用底层绑定
- **测试内容**：
  - 标准事件绑定（Click、TextChanged 等）
  - 自定义事件绑定
  - 脚本模式跳过（`ScriptModeSkipped = true`）
  - 控件为 null 时的跳过逻辑
  - 绑定失败时的异常处理
- **依赖**：`IWinFormsEventBinder`（可 Mock）、`IEventExecutionService`（可 Mock）
- **复杂度**：中，需要 Mock 多个接口

**4. `ExecutionResult` 结构体**（Share/EventBindingOrchestrator/Abstractions）
- **为什么**：这是整个事件执行链路的返回值类型，`Success` 判断影响所有调用方
- **测试内容**：
  - `Ok()` → `Success = true`
  - `Fail("error")` → `Success = false, Error = "error"`
  - `implicit operator bool` — `true` → `Ok()`, `false` → `Fail("Unknown error")`
- **依赖**：无
- **复杂度**：极低

---

### 🟡 P2 — 集成测试（重要但可延后）

**5. `EventExecutionService` + `FunctionRegistry` 集成测试**
- **为什么**：验证两个核心组件的协作
- **测试内容**：
  - 注册函数 → 通过 `EventExecutionService` 执行
  - 异步函数执行
  - 带参数的函数执行
  - 函数抛出异常时的错误传播

**6. `DefaultEventBindingService.BindEvents`**（EventTriggerFramework）
- **为什么**：这是事件绑定的外观（Facade），整合了管线执行
- **测试内容**：
  - 参数验证（target/config 为 null）
  - 幂等性检查（同一 target + config 跳过重复绑定）
  - 非 Form target 的 FindForm 回退
  - 委托给 `EventBindingPipelineRunner` 执行
- **依赖**：`EventBindingPipelineRunner`、`IWinFormsEventBinder`、`IEventExecutionService`
- **复杂度**：高，需要搭建完整管线环境

---

### 🟢 P3 — 端到端测试（长期目标）

**7. 完整链路测试**
- `FunctionRegistry.Register` → `EventExecutionService.ExecuteFunctionAsync` → 委托执行
- 验证从函数注册到执行的完整路径

**8. WinForms 集成测试**
- 创建 Form → 绑定事件 → 模拟点击 → 验证函数执行
- 需要 WinForms 测试基础设施（`Application.Run` 等）

---

### 推荐实施顺序

| 优先级 | 测试目标 | 预估工作量 | 风险降低 |
|--------|---------|-----------|---------|
| P0-1 | `EventExecutionService` | 小 | 高 |
| P0-2 | `FunctionRegistry` | 小 | 高 |
| P1-3 | `EventBinderTransformer` | 中 | 高 |
| P1-4 | `ExecutionResult` | 极小 | 中 |
| P2-5 | 集成测试（EventExec + FuncRegistry） | 中 | 中 |
| P2-6 | `DefaultEventBindingService` | 大 | 中 |
| P3-7/8 | 端到端测试 | 大 | 低（前期已覆盖） |

**建议从 P0 开始**：`EventExecutionService` + `FunctionRegistry` 的单元测试，这两个组件无 UI 依赖、可纯 Mock 测试，且覆盖了事件绑定链路的核心逻辑。