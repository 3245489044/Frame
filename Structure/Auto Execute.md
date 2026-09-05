# Auto Execute — 自动化执行与迭代调试指南

> **一句话**：Cline 运行 `交互启动.bat` 进入菜单，选择测试脚本运行，从终端输出中收集执行报告，分析失败原因，修改脚本后重新运行，循环直到全部通过。

---

## 目录

- [1. 快速开始](#1-快速开始)
- [2. 交互启动.bat 使用方式](#2-交互启动bat-使用方式)
- [3. 输出解读](#3-输出解读)
- [4. 迭代 Debug 流程（通用）](#4-迭代-debug-流程通用)
- [5. 人工 Debug 流程](#5-人工-debug-流程)
- [6. Cline 自动化运行流程](#6-cline-自动化运行流程)
- [7. 可用测试脚本清单](#7-可用测试脚本清单)
- [8. 注意事项](#8-注意事项)
- [9. 故障排除](#9-故障排除)

---

## 1. 快速开始

### 1.1 前置条件

- 项目已构建（`dotnet build` 通过）
- `交互启动.bat` 位于 `EnableMent\` 目录

### 1.2 基本用法

```batch
EnableMent\交互启动.bat
```

进入交互菜单后，输入对应序号即可运行脚本。

> **入口限制**：必须通过 `交互启动.bat` 调用，禁止直接调用内部的 PowerShell 脚本，否则脚本目录解析会出错。

### 1.3 通过 PowerShell 包装调用

当 Cline 需要通过 PowerShell 包装调用时，使用完整路径：

```powershell
& 'D:\Project\Program\WorkFlowPrj\EnableMent\交互启动.bat'
```

### 1.4 Cline 自动化运行（管道传参）

Cline 可通过 `echo` 管道向交互菜单传递序号，实现自动化运行：

```batch
echo 5 | EnableMent\交互启动.bat
```

其中 `5` 是测试脚本的序号（通过 `--list` 参数可查看当前所有可用脚本）。管道中的 `Read-Host` 会自动读取 `echo` 输出的内容作为输入，选择对应脚本执行。

> **原理**：`echo 5 | 交互启动.bat` 中 PowerShell 的 `Read-Host` 支持从管道标准输入读取，`echo 5` 将 "5" 写入 stdout 管道，`Read-Host` 从管道读取后返回，`ScriptRunner.ps1` 解析为 `Math\math_functions_test.json` 并执行。

---

## 2. 交互启动.bat 使用方式

### 2.1 基本用法

直接运行进入交互菜单，输入序号运行对应脚本：

```batch
EnableMent\交互启动.bat
```

交互菜单中支持：
- 输入 **序号** → 运行对应脚本
- 输入 `mode` → 切换自动/手动模式
- 输入 `log` → 切换 UI 排布打印选项

### 2.2 其他命令行参数

| 参数 | 说明 |
|------|------|
| `--list` | 列出所有可用脚本 |
| `--info` | 显示配置信息 |
| `--show <文件名>` | 显示指定脚本内容 |
| `--dirs` | 显示目录结构 |
| `--help` | 显示帮助信息 |
| `--log <分类>` | 启用指定日志分类 |
| `--analyze` | 分析模式 |

### 2.3 日志分类控制

通过环境变量或 `--log` 参数控制日志输出分类。默认启用 `WdUI.All,FileDriven.All`（全部日志），可通过修改 `交互启动.bat` 中的 `LOG_CATEGORIES` 环境变量调整。

### 2.4 日志输出模式

`交互启动.bat` 默认使用 `console` 模式（日志直接打印到控制台），可通过修改文件中的 `LOG_OUTPUT_MODE` 环境变量切换：

| 模式 | 说明 | 适用场景 |
|------|------|---------|
| `console` | 日志直接打印到控制台，不写入数据库 | 调试开发时使用 |
| `database` | 日志写入内存仓库，退出时刷入 SQLite | 正式运行时使用 |

### 2.5 执行输出示例

启动时的关键输出行：

```
[启动器] : D:\Project\Program\WorkFlowPrj\FileDriven\BuildOutput\Debug\FileDriven.Launcher.exe
[测试脚本] : D:\Project\Program\WorkFlowPrj\FileDriven\DrivenConfig\Scripts\Math\math_functions_test.json
[运行模式] : GUI (--show-gui)
```

执行完成标志：

```
[完成] 测试执行结束（错误码: 0）
```

---

## 3. 输出解读

### 3.1 关键诊断字段速查表

从管道输出中提取以下关键信息：

| 诊断字段 | 匹配模式 | 含义 |
|---------|---------|------|
| `success=` | `success=(true\|false)` | 整体执行是否成功 |
| `totalCommands=` | `totalCommands=(\d+)` | 总命令数 |
| `errorMessage=` | `errorMessage=(.*)` | 错误信息 |
| `[DIAG] ScriptExecutor` | `\[DIAG\] ScriptExecutor` | 脚本执行器诊断 |
| `[ERROR]` | `\[ERROR\]` | 错误日志 |
| `Parse error` | `Parse error:` | 解析错误 |
| `工作流执行完成` | `工作流执行完成: Success=(true\|false)` | 工作流结果 |
| `ExecutionCenter Handler` | `ExecutionCenter Handler AfterExecute` | 执行中心诊断 |


---

## 4. 迭代 Debug 流程

> 本循环遵循 [CR: docs/迭代循环指令.md:LoopPatterns] 定义的 **迭代调试模式**。
> 通用框架：**运行 → 分析 → 修复 → 重试**，直到 `success=true`。
> 终止条件：最大迭代 10 次 / 连续失败 5 次熔断。

### 4.1 常见失败模式与修复方向

| 输出特征 | 可能原因 | 修复方向 |
|---------|---------|---------|
| `success=false, totalCommands=0` | 脚本未被正确解析执行 | 检查 JSON 格式、命令结构 |
| `Parse error: Command cannot be empty` | 命令解析器遇到空命令 | 检查脚本中是否有空行或无效命令 |
| `success=false, totalCommands=N` | 部分命令执行失败 | 检查失败的具体命令 |
| `errorMessage=...` | 特定错误信息 | 根据错误信息定位问题 |
| 控件未找到 | 控件名称/路径错误 | 修正控件名称（大小写、拼写） |
| 超时 | 控件加载慢 | 在失败命令前增加 sleep |
| 参数错误 | 参数值或格式不正确 | 调整参数值或格式 |
| 模板展开异常 | JSON 模板引用问题 | 检查 template 引用和展开后的命令结构 |

### 4.2 Debug 步骤

1. **提取错误行**：从输出中提取 `errorMessage=` 和 `Parse error` 行
2. **定位问题文件**：检查对应的测试脚本 JSON 文件
3. **分析命令结构**：确认 JSON 中的命令格式是否正确
4. **检查注册函数**：确认脚本中调用的函数是否已注册
5. **增加等待**：在失败命令前增加 sleep（等待控件加载）
6. **修正名称**：修正控件名称（大小写、拼写）
7. **调整参数**：调整参数值或格式
8. **增加配置**：增加 sideload 加载配置

---

## 5. 人工 Debug 流程

> 适用场景：手动在终端中运行测试并分析。执行步骤同 §4 通用循环，以手动菜单交互方式运行。

### 5.1 执行示例

#### 成功执行

```
[DIAG] ScriptExecutor start: commandFile=...\math_browser_simple_test.json
...
[DIAG] ScriptExecutor complete: success=true, totalCommands=5
[完成] 测试执行结束
```

#### 失败执行

```
[DIAG] ScriptExecutor start: commandFile=...\math_functions_test.json
...
[DIAG] ExecutionCenter Handler AfterExecute: hasError=true, errorMessage=Parse error: Command cannot be empty
[DIAG] ScriptExecutor complete: success=false, totalCommands=0
[完成] 测试执行结束
```

---

## 6. Cline 自动化运行流程

> 适用场景：Cline 通过管道传参自动运行测试并分析修复。执行循环同 §4。

### 6.1 核心自动化原理

Cline 通过 `echo <序号>` 管道向 `交互启动.bat` 传递输入：

```batch
echo 5 | EnableMent\交互启动.bat
```

**数据流**：`Cline → echo 5（stdout）→ 管道 → 交互启动.bat → SelectScript.ps1 → ScriptRunner.ps1 → Launcher.exe → 管道输出 → Cline 收集`

### 6.2 Cline 特有补充步骤

在 §4 通用循环基础上，Cline 自动化增加准备阶段：

1. 读取目标测试脚本 JSON 文件及模板定义
2. 执行 `echo <序号> | EnableMent\交互启动.bat`，等待 `[完成]` 标记
3. 从输出中提取 `success=`、`totalCommands=`、`errorMessage=` 等诊断字段
4. 对照 §4.1 失败模式分析，修复后重试

### 6.3 自动化运行示例

```batch
echo 5 | EnableMent\交互启动.bat                    # 首次运行
... 分析输出，对照 §4.1 修复 ...
echo 5 | EnableMent\交互启动.bat                    # 重试直到 success=true
```

批量运行多个测试：

```batch
echo 1 | EnableMent\交互启动.bat   # DeepSeek 直接对话
echo 5 | EnableMent\交互启动.bat   # 数学函数综合测试
echo 6 | EnableMent\交互启动.bat   # 数学工作流测试
```

### 6.4 Cline 自动化注意事项

1. **输出捕获**：管道输出的所有内容通过 `execute_command` 的 stdout 收集
2. **超时**：采用空闲超时——只要测试持续产出输出就不中断；仅当连续 120 秒无任何输出时才判定为卡死并强制终止（等待 `[完成]` 标记）
3. **窗口自动关闭**：`--auto-close` 自动关闭 WinForms 窗口
4. **管道连接**：Launcher 需在 15 秒内连接管道
5. **连续运行间隔**：建议间隔 2-3 秒

> **★ 2026-07-26 修复**：`echo 5 | 交互启动.bat` 的管道流被菜单层级消费的问题已修复——
> `运行测试GUI模式.ps1` 现在直接识别数字参数，绕过 `SelectScript.ps1` 的 Read-Host 交互选择。
> **已知问题**：`MathFunctionBrowser.json` 已创建并注册所有 `Math_*` 函数。

---

## 7. 可用测试脚本清单

> 脚本清单随项目动态更新，不在此处维护。运行 `EnableMent\交互启动.bat --list` 获取当前所有可用脚本及其序号。
>
> Cline 通过 `echo <序号> | EnableMent\交互启动.bat` 选择脚本运行。

---

## 8. 注意事项

1. **入口限制**：必须通过 `交互启动.bat` 调用，禁止直接调用内部的 PowerShell 脚本
2. **管道超时**：Launcher 启动后需在 15 秒内连接管道，否则视为超时
3. **执行超时**：空闲超时——只要测试持续产出输出就不中断；仅当连续 120 秒无输出时才判定卡死并强制终止
4. **自动关闭**：测试完成后窗口会自动关闭（`--auto-close` 机制）
5. **日志模式**：`console` 模式日志直接输出到管道，`database` 模式写入 SQLite
6. **JSON 脚本转换**：`.json` 文件会被自动转换为纯文本命令文件再执行
7. **路径别名**：脚本中的路径引用通过 `FILEDRIVEN_ALIAS_*` 环境变量解析
8. **模板引用**：JSON 脚本可能使用 `template` 引用（如 `"template": "math-operation"`），修复时需注意模板展开后的命令结构
9. **输出标记**：输出中的 `[折叠]` 标记表示重复日志被折叠，不影响分析
10. **WARN 级别**：`[WARN]` 级别的日志（如 `Log message not found`）通常不影响执行结果
11. **迭代控制**：终止条件由 Cline 自行判断，每次运行后分析输出，决定是否继续
12. **Cline 自动化**：Cline 通过 `echo <序号> | 交互启动.bat` 模拟交互选择，仅能使用交互菜单方式，不可直接传参

---

## 9. 故障排除

### 9.1 启动失败

| 现象 | 可能原因 | 解决 |
|------|---------|------|
| `找不到指定路径` | 脚本路径错误 | 确认脚本名相对于 `Scripts` 目录 |
| `Launcher.exe 未找到` | 项目未构建 | 先执行 `dotnet build` |
| `管道连接超时` | Launcher 启动异常 | 检查构建输出目录是否存在 |
| `echo 管道无效` | PowerShell 读取问题 | 确认命令格式为 `echo <序号> \| EnableMent\交互启动.bat` |

### 9.2 执行异常

| 现象 | 可能原因 | 解决 |
|------|---------|------|
| 输出为空或极少 | 管道未正确连接 | 检查 `--pipe-name` 参数是否匹配 |
| 长时间无响应 | 测试卡死 | 连续 120 秒无任何输出时自动强制终止 |
| 窗口一闪而过 | `--auto-close` 生效 | 正常行为，查看终端输出即可 |
| 顺序号不匹配 | 脚本清单更新后序号偏移 | 确认使用最新的脚本清单序号 |

### 9.3 结果异常

| 现象 | 可能原因 | 解决 |
|------|---------|------|
| `totalCommands=0` | 脚本解析失败 | 检查 JSON 格式是否正确 |
| 通过率低 | 环境问题或脚本问题 | 检查失败命令的具体错误信息 |
| 重复相同错误 | 修复未生效 | 确认修改的文件路径正确 |
