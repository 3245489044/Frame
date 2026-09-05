# Cline 上下文转储

> 生成时间：2026-06-28 03:03
> 说明：当前对话中注入到 Cline 上下文的所有内容整理

---

## 一、系统信息

| 项目 | 值 |
|------|-----|
| 操作系统 | Windows 11 |
| IDE | Visual Studio Code |
| 默认 Shell | `C:\Windows\System32\cmd.exe` |
| 用户目录 | `C:\Users\Ljy` |
| 当前工作目录 | `d:\Project\Program\WorkFlowPrj` |

### 工作区配置

```json
{
  "workspaces": {
    "d:\\Project\\Program\\WorkFlowPrj": {
      "hint": "WorkFlowPrj",
      "associatedRemoteUrls": [
        "github: https://github.com/3245489044/ZhiLianStorage.git",
        "origin: https://gitee.com/Ljy_111/ZhiLianStorage.git"
      ],
      "latestGitCommitHash": "ead594b9c1d11571adbd9f127b9345d095dd45e7"
    }
  }
}
```

### 检测到的 CLI 工具

`git`, `docker`, `kubectl`, `npm`, `pnpm`, `pip`, `curl`, `python`, `node`, `code`, `dotnet`

---

## 二、Cline 核心 System Prompt（官方源码）

> 来源：Cline 官方仓库 `sdk/packages/shared/src/prompt/system.ts`
> 变量 `{{...}}` 在运行时由 `buildClineSystemPrompt()` 函数替换

### 默认 System Prompt（`DEFAULT_CLINE_SYSTEM_PROMPT`）

```
You are Cline, an AI coding agent. Your primary goal is to assist users with various coding tasks by leveraging your knowledge and the tools at your disposal. Given the user's prompt, you should use the tools available to you to answer user's question.

Always gather all the necessary context before starting to work on a task. For example, if you are generating a unit test or new code, make sure you understand the requirement, the naming conventions, frameworks and libraries used and aligned in the current codebase, and the environment and commands used to run and test the code etc. Always validate the new unit test at the end including running the code if possible for live feedback.
Review each question carefully and answer it with detailed, accurate information.
If you need more information, use one of the available tools or ask for clarification instead of making assumptions or lies.

Environment you are running in:
<env>
1. Platform: {{PLATFORM_NAME}}
2. Date: {{CURRENT_DATE}}
3. IDE: {{IDE_NAME}}
4. Working Directory: {{CWD}}
</env>

Remember:
- Always adhere to existing code conventions and patterns.
- Use only libraries and frameworks that are confirmed to be in use in the current codebase.
- Provide complete and functional code without omissions or placeholders.
- Be explicit about any assumptions or limitations in your solution.
- Always show your planning process before executing any task. This will help ensure that you have a clear understanding of the requirements and that your approach aligns with the user's needs.
- Always use absolute paths when referring to files.
- You can call multiple tools in a single response. Before using tools, identify every independent read, search, command, or edit needed for the next step and emit all of those tool calls now, either as multiple tool calls or as one batched input for tools that accept arrays. Do not wait for one independent result before requesting another. Do not split independent reads, searches, checks, or edits across separate turns.
- Good parallelism examples: read all known relevant files in one read_files call; run independent inspection commands in one run_commands call; emit independent read_files, search_codebase, and run_commands calls together in one response; emit multiple editor calls together when editing different files or non-overlapping regions.
- Always verify the files you have edited or created at the end of the task to ensure they are completed and working as expected.

Begin by analyzing the user's input and gathering any necessary additional context. Then, present your plan at the start of your response along with tool calls before proceeding with the task. It's OK for this section to be quite long.

REMEMBER, be helpful and proactive! Don't ask for permission to do something when you can do it! Do not indicates you will be using a tool unless you are actually going to use it.

IMPORTANT: Always includes tool calls in your response until the task is completed. Response without tool calls will considered as completed with final answer.

When you have completed the task, please provide a summary of what you did and any relevant information that the user should know. This will help ensure that the user understands the changes made and can easily follow up if they have any questions or need further assistance. Do not indicate that you will perform an action without actually doing it. Always provide the final result in your response. Always validate your answer with checking the code and running it if possible. 

If user asked a simple question without any coding context, answer it directly without using any tools.
{{CLINE_RULES}}
{{CLINE_METADATA}}
```

### YOLO 模式 System Prompt（`YOLO_CLINE_SYSTEM_PROMPT`）

```
You are Cline, a careful and helpful coding agent that works in the background.
You are tasked to solve an issue reported by the user who you cannot communicate with directly.
Your goal is to utilize the tools at your disposal to investigate and answer the question according to user's instructions with the aim to verify that the issue is resolved.

RULES:
- Always match output format exactly as shown in examples or existing files.
- Use only libraries and frameworks that are confirmed and compatible to be in use in the current codebase.
- Provide complete and functional code without omissions or placeholders.
- Always show your planning process without repeating yourself before executing any task.
- Always use absolute paths when referring to files.
- You can call multiple tools in a single response.
- Always verify the files you have edited or created at the end of the task.

Environment you are running in:
<env>
1. Platform: {{PLATFORM_NAME}}
2. Date: {{CURRENT_DATE}}
3. IDE: {{IDE_NAME}}
4. Working Directory: {{CWD}}
</env>

IMPORTANT: 
- When the user describes a bug, unexpected behavior, or provides a bug report, your primary goal is to produce a correct fix in the source code that resolves the issue. 
- A correct fix means the underlying behavior is fixed — not just the symptoms addressed superficially. 
- After applying your fix, you must run the relevant test suite to confirm your changes actually resolve the problem.
- Do not consider the task complete until the test suite related to the files you have touched passes.
- Always includes tool calls in your response until the task is completed. You should only end the task when all the requirements are met by calling the 'submit_and_exit' tool.
{{CLINE_RULES}}
{{CLINE_METADATA}}
```

### 模板变量替换逻辑

`buildClineSystemPrompt()` 函数（来自 `sdk/packages/shared/src/prompt/cline.ts`）执行以下替换：

| 变量 | 替换为 |
|------|--------|
| `{{PLATFORM_NAME}}` | `platform` 参数（默认 `"unknown"`） |
| `{{CWD}}` | `workspaceRoot` 或 `rootPath` |
| `{{CURRENT_DATE}}` | `new Date().toLocaleDateString()` |
| `{{IDE_NAME}}` | `ide` 参数（默认 `"Terminal Shell"`） |
| `{{CLINE_METADATA}}` | 工作区元数据 JSON（仅当 provider 为 Cline 时注入） |
| `{{CLINE_RULES}}` | `rules` 参数（即 `.clinerules/` 内容） |

### 工作区元数据格式

```json
{
  "workspaces": {
    "[rootPath]": {
      "hint": "[workspaceName]",
      "associatedRemoteUrls": ["..."],
      "latestGitCommitHash": "...",
      "latestGitBranchName": "..."
    }
  }
}
```

### 当前对话中实际注入的值

根据当前上下文，实际替换后的值：

| 变量 | 实际值 |
|------|--------|
| `{{PLATFORM_NAME}}` | `Windows 11` |
| `{{CURRENT_DATE}}` | `2026/6/28` |
| `{{IDE_NAME}}` | `Visual Studio Code` |
| `{{CWD}}` | `d:\Project\Program\WorkFlowPrj` |
| `{{CLINE_METADATA}}` | 见上方"工作区配置" |
| `{{CLINE_RULES}}` | 见第四章 `.clinerules/` 内容 |


---

## 三、项目文件结构

```
WorkFlowPrj/
├── .gitignore
├── LICENSE
├── README.md
├── task_progress.md
├── CentralTest/
│   ├── RunTestForm.ps1
│   ├── RunWdUITests.bat
│   ├── RunWdUITests.ps1
│   ├── CSharpTest/
│   ├── PowerShellTests/
│   └── TypeScriptTest/
├── CsharpPrj/
│   ├── Directory.Build.props
│   ├── MainProj.sln
│   ├── versions.json
│   ├── BasicMethod/
│   ├── FileDriven/
│   ├── generated/
│   ├── Modules/
│   ├── Operation/
│   ├── Plate/
│   ├── Protocol/
│   └── Share/
├── DataStorage/
│   └── DataBase/
├── Destoried/
│   ├── ContextFrame.cs
│   ├── Pipeline.cs
│   ├── Component/
│   ├── ExecutionEngine/
│   ├── GraphFramework/
│   ├── Orchestration/
│   ├── Pipeline/
│   └── Skeletons/
├── DevEnvironment/
│   ├── Docker_MCP_环境搭建指南.md
│   ├── Docker容器使用.md
│   ├── README.md
│   └── Docker/
├── docs/
│   ├── 环境配置指南.md
│   ├── CLine_MCP_配置指南.md
│   ├── task_progress.md
│   ├── WorkFlowPrj设计哲学.md
│   ├── analysis/
│   ├── Introduction/
│   ├── KnowLedge/
│   ├── Map/
│   ├── Project/
│   ├── Prompts/
│   ├── Script/
│   └── templates/
├── EnableMent/
│   ├── 编译C#项目.bat
│   ├── 交互启动.bat
│   ├── CLI启动.bat
│   ├── GUI模式自动化迭代运行方案.md
│   └── UI启动.bat
├── PowerShellScript/
│   ├── ConfigDrivenUIAutomationFramework.md
│   ├── launcher.config.json
│   ├── README.md
│   ├── ref_report.json
│   ├── ScriptLauncher.bat
│   ├── ScriptLauncher.ps1
│   ├── AIDecisionFramework/
│   ├── Analysis/
│   ├── backups/
│   ├── BasicMethod/
│   ├── Build/
│   ├── FileDriven/
│   ├── Git/
│   ├── Modules/
│   ├── PScriptManager/
│   ├── Solution/
│   ├── Temp/
│   ├── Tools/
│   └── vscode-script-ui/
├── PythonModel/
│   ├── __init__.py
│   ├── DeepSeekInvoke.py
│   ├── python_integration_summary.md
│   ├── requirements.txt
│   ├── setup_python.ps1
│   ├── clients/
│   └── PythonModel.Bridge/
├── Storage/
│   ├── UnityRelated.cs
│   └── Utilitys/
├── TypeScriptPrj/
│   ├── Directory.Build.props
│   ├── package.json
│   ├── pnpm-lock.yaml
│   ├── pnpm-workspace.yaml
│   ├── ts-versions.json
│   ├── TypeScript.sln
│   ├── TsSln/
│   └── TsWorkspace/
└── vscode-plugin-framework/
    ├── install.bat
    ├── README.md
    ├── runner.bat
    ├── config/
    ├── core/
    ├── plugins/
    └── tests/
```

---

## 四、.clinerules/ 项目规则

### 00-README.md — 文件夹说明

> **Cline 工作指南 - WorkFlowPrj**
> 目标：最大化工具使用效益，最小化上下文消耗

| 文件 | 内容 | 说明 |
|------|------|------|
| `01-core-principles.md` | 核心原则 | 优先级、Think-in-Code、上下文预算管理 |
| `02-scenarios.md` | 常用场景模式 | 核心模式速览、工具组合流程 |
| `03-performance.md` | 性能与错误处理 | 上下文预算、批量操作、大文件、后台进程 |
| `04-project-rules.md` | 项目规范 | 架构概览、MCP配置、知识图谱 |

**设计原则：**
- 基础规则（本文件夹）— 固定不变，Cline 自动注入
- 项目规则（`.csproj` 文件）— 动态变化，Cline 按需读取
- 职责分离，各文件独立关注一个主题
- 批量操作 — 优先使用 MCP 批量读写，避免逐文件操作消耗上下文

---

### 01-core-principles.md — 核心原则

**优先级总则：**
```
ctx_batch_execute（最优先）→ invoke_ps_script → BatchRead/Write/Replace → CLine 内置工具 → CLI 命令（最后手段）
```

> 文件读写操作优先使用 MCP 工具（如 `invoke_ps_script` 调用 `BasicMethod-FileAccess.psm1` 中的 `BatchRead-File`、`BatchWrite-File`、`BatchReplace-InFile` 等），而非直接调用 CLine 内置的 `read_file`、`write_to_file`、`replace_in_file`。
> 原因：脚本层提供编码检测、备份、校验、差异预览、交互确认等安全机制，CLine 内置工具不具备这些能力。

**MCP 工具桥接：**
> 通过 MCPBridge 的 `invoke_ps_script` 工具，可直接在宿主机上调用 PowerShell 脚本/模块中的函数。

```json
{
  "tool": "invoke_ps_script",
  "script_path": "PowerShellScript/BasicMethod/BasicMethod-FileAccess.psm1",
  "function_name": "BatchRead-File",
  "arguments": {
    "Path": "CsharpPrj/**/*.cs",
    "Pattern": "class.*Registry"
  }
}
```

**Think-in-Code 原则：**
> **数据不进对话，只进沙箱。** 处理数据的代码在沙箱中运行，只有摘要/结论进入对话上下文。

```
❌ 错误：read_file(大文件) → 全部内容进入上下文 → 手动分析
✅ 正确：ctx_execute_file(大文件) → 代码处理 → 仅输出摘要
```

**上下文预算管理：**
- 小文件（<50KB）直接读取，大文件用沙箱分析
- 批量操作用 `ctx_batch_execute` + `queries` 一次返回
- 文档/网页内容用 `ctx_index` / `ctx_fetch_and_index` 索引后检索
- 复杂推理用 `mcp-sequentialthinking-tools` 分解问题（替代官方 sequential-thinking）
- 文件读写优先使用 MCP 工具（`invoke_ps_script` → `BasicMethod-FileAccess.psm1`），而非 CLine 内置工具

---

### 02-scenarios.md — 常用场景模式

> **决策速查**：≥2 文件读/写/替换 → `invoke_ps_script` → `Batch*`；≥3 独立命令 → `ctx_batch_execute`

**核心模式速览：**

| 场景 | 核心思路 | 说明 |
|------|---------|------|
| C# 代码分析 | 遍历 .cs 文件统计行数/复杂度 | 用沙箱执行 JS 脚本处理 |
| .NET 构建测试 | 并行 build + test 捕获错误 | 批量命令配合查询 |
| PowerShell 执行 | 调用 pwsh 并捕获错误输出 | 管道 `2>&1` |
| Python 模型调用 | 导入模块执行，输出摘要结果 | 控制台输出 |
| 文档索引检索 | 先索引后检索 | 内容不进对话 |
| Git 操作 | 批量 git 命令分析变更 | 配合查询 |
| 大文件日志分析 | 沙箱处理 | FILE_CONTENT 在沙箱中 |
| **文件读写** | **优先使用 BasicMethod-FileAccess.psm1 脚本** | **BatchRead-File / BatchWrite-File / BatchReplace-InFile** |
| **批量代码修改** | **使用 SEARCH/REPLACE 块精确匹配** | **BatchReplace-InFile -Blocks / BatchWrite-File -Merge** |
| **MCP 桥接调用** | **通过 invoke_ps_script 调用 PS 脚本** | **invoke_ps_script → BasicMethod-FileAccess.psm1** |

**工具组合流程：**
```
代码分析：搜索文件 → 沙箱执行 → 推理 → 记录知识
知识构建：索引网页 → 检索 → 创建实体 → 创建关系
问题解决：推理 → 批量执行 → 检索 → 推理 → 记录
代码审查：批量读取 → 沙箱执行 → 推理 → 创建实体
性能优化：沙箱执行 → 沙箱分析文件 → 推理 → 编辑 → 沙箱执行
```

---

### 03-performance.md — 性能与错误处理

**上下文预算：**
- 文件读取尽量用 `head/tail` 参数，只读需要的部分
- 代码执行用 `console.log` 输出摘要，不打印全部原始数据
- 批量操作用 `ctx_batch_execute` + `queries` 只返回匹配结果
- 文档/网页用 `ctx_index` / `ctx_fetch_and_index`，内容不进对话

**批量操作策略：**
- **2+ 独立操作** → 使用 `ctx_batch_execute` 替代多次单独调用
- **2+ 文件读/写/替换** → 使用 `BatchRead-File` / `BatchWrite-File` / `BatchReplace-InFile`
- **I/O 密集型** → 设置 `concurrency: 4-8` 并行执行
- **CPU 密集型** → 保持 `concurrency: 1` 避免资源竞争
- **带查询的批量** → 在 `queries` 中列出所有问题，一次返回

**大文件处理：**
```
< 50KB  → 直接读取
50KB-1MB → 沙箱分析
> 1MB   → 沙箱分析 + 分段处理
```

**后台进程管理：**
- 开发服务器/监听器用 `background: true` 避免超时中断
- 长时间构建设置明确的 `timeout` 值
- 服务器 + 请求放在同一个调用中

**通用回退链：**
```
MCP 工具（优先）→ CLine 内置工具（备选）→ CLI 命令（最后手段）
```

**路径与超时：**
- ⚠️ **filesystem MCP 必须使用绝对路径**（如 `d:\Project\Program\WorkFlowPrj\...`）
- 路径解析失败时，尝试使用 `d:\Project\Program\WorkFlowPrj\` 作为前缀
- `ctx_execute` 中的文件路径使用双反斜杠 `\\` 或正斜杠 `/`
- 默认不设置 timeout（由 MCP host 的 RPC 超时控制）

---

### 04-project-rules.md — 项目规范

**项目概览：**
- **类型**：C# (.NET) + PowerShell + Python 混合项目
- **解决方案**：`MainProj.sln`（位于 `CsharpPrj/`）
- **构建输出**：`BuildOutput/$(Configuration)/$(TargetFramework)/`
- **设计哲学**：万物皆函数，一切皆配置
- **架构导航**：项目规则和模块详情存储在 `.csproj` 的 `ModuleRule` 属性中，通过 **CsprojContext MCP** 动态读取

> **代码风格、Git 工作流、关键配置文件**等约束已迁移到 `MainProj.sln` 文件头注释中，请直接查看 `.sln` 文件。

**MCP 注意事项：**
- `ctx_execute` 支持 JavaScript/TypeScript/Python/Shell/C# 等多种语言
- `ctx_execute_file` 的 `FILE_CONTENT` 变量在沙箱中，不进入对话上下文
- `ctx_batch_execute` 的 `queries` 参数可以一次返回多个问题的匹配结果
- `memory` MCP 用于持久化项目知识图谱，适合记录架构决策和设计模式
- `context7-mcp` 用于查询第三方库文档
- `lexis-mcp` 用于代码搜索（search_code / get_symbol / find_references 等 28+ 工具），优先于内置 search_files

**废弃代码处理策略：**
> **核心原则：标记为"已废弃/待删除"的代码，编译报错时直接删除，不要修复。**

- 遇到废弃代码的编译错误时，先判断代码是否属于已迁移/已替换的旧体系
- 确认无其他引用后直接删除，而非尝试修复编译错误
- 删除前使用 `find_references` 或 `search_code` 确认调用方已迁移
- 不确定时，先询问用户"这段代码是应该删除还是修复？"

**知识图谱维护：**
- **核心实体**：项目模块（BasicMethod、FileDriven、Operation 等）
- **关系类型**：`depends_on`、`implements`、`extends`、`configures`
- **观察记录**：架构决策、重构记录、Bug 修复方案

---

## 五、.gitignore

```
# Visual Studio
.vs/
**/bin/
**/obj/
*.suo
*.user
*.userosscache
*.sln.docstates

# Build output
bin/
obj/
**/bin/
**/obj/
BuildOutput/

# TypeScript build output
TypeScriptPrj/**/build/
TypeScriptPrj/build/
**/*.tsbuildinfo

# Test results
**/TestResults/
coverage/
*.lcov
.nyc_output

# Dependency directories
node_modules/
jspm_packages/
package-lock.json
.npm
.eslintcache
.rpt2_cache/
.rts2_cache_cjs/
.rts2_cache_es/
.rts2_cache_umd/
.node_repl_history
*.tgz
.yarn-integrity

# Environment
.env
.env.test

# Build cache
.cache
.parcel-cache
.next
out
.nuxt
dist
.cache/
.vuepress/dist
.serverless/
.fusebox/
.dynamodb/
.tern-port
.vscode-test

# yarn v2
.yarn/cache
.yarn/unplugged
.yarn/build-state.yml
.yarn/install-state.gz
.pnp.*

# Python
.venv/

# Logs
logs/
*.log

# Database
*.db
*.sqlite

# Executables
*.exe
*.dll
*.pdb

# IDE cache
*.lscache

# Plugin
Plugin/

# Godot
Godot/**/bin/
Godot/**/obj/
Godot/**/*.lscache

# Token Savior
.token-savior-cache.json

# Build output
diff_output.txt
build_output.txt

# NuGet tool cache
.tools/

# VSCode Counter
.VSCodeCounter/

# PScriptManager registry
PowerShellScript/PScriptManager/psm-registry.json

# Turbo build cache
TypeScriptPrj/.turbo/
TypeScriptPrj/turbo.json
```

---

## 六、README.md（项目文档）

> 完整内容见 `README.md`（518 行），以下是摘要：

**ZhiLianStorage（智联存储框架）** — 基于 .NET 10.0 的企业级模块化软件开发框架。

### 技术架构
- **基础平台**：.NET 10.0 + C# 12，启用可空引用类型
- **核心依赖**：Microsoft.Extensions.DependencyInjection、Microsoft.Extensions.Logging、Roslyn、AutoMapper、Newtonsoft.Json、LiteDB、SQLite、SemanticKernel、Selenium 等

### 核心模块
| 模块 | 说明 |
|------|------|
| BasicMethod | 基础方法库（反射、框架方法、算法、通用功能） |
| UniversalCore | 通用核心库（最小依赖） |
| WorkFlow | 工作流引擎（核心业务模块） |
| DataManage | 数据管理层 |
| FrameManage.Modular | 模块化框架 |
| MetaCommand | 元命令框架 |
| FunctionExecution | 函数执行引擎 |
| RoslynMethod | Roslyn 代码分析 |
| Script | 脚本引擎 |
| WdUI | Windows 桌面 UI |
| CellFrame | 单元格框架 |
| FileDrivenFramework | 文件驱动框架 |
| CentralConsole | 中央控制台 |
| Prompts | 提示词工程 |
| UniversalReference | 通用引用库 |
| WorkFlowMethod | 工作流方法库 |
| MoudleProject | 模块化项目模板 |

### 开发规范
- **代码风格**：PascalCase 类型/方法名，camelCase 私有字段（下划线前缀），Allman 大括号风格，4 空格缩进，120 字符行限制
- **注释规范**：公共 API 必须包含 XML 文档注释，使用中文
- **测试要求**：核心模块单元测试覆盖率 80%+，核心业务逻辑 90%+

### 许可证
Apache License 2.0

---

## 七、MainProj.sln

```
Microsoft Visual Studio Solution File, Format Version 12.00
# Visual Studio Version 17
VisualStudioVersion = 17.5.320.0
MinimumVisualStudioVersion = 10.0.40219.1
```

**项目列表：**
| 项目 | 路径 |
|------|------|
| BasicMethod | `BasicMethod\BasicMethod.csproj` |
| FileDriven | `FileDriven\FileDriven.csproj` |
| Modules | `Modules\Modules.csproj` |
| Share | `Share\Share.csproj` |
| Operation | `Operation\Operation.csproj` |
| Plate | `Plate\Plate.csproj` |
| PythonModel.Bridge | `..\PythonModel\PythonModel.Bridge\PythonModel.Bridge.csproj` |

**解决方案文件夹：**
- PythonModel
- PowerShellScript

---

## 八、task_progress.md

```markdown
# 任务进度：整合 PipelineContext 重复定义

## 目标
让 `WorkflowExecution.Pipeline.PipelineContext` 继承 `Modules.Pipeline.PipelineContext`，
消除 `IPipelineContext` 的重复实现。

## 步骤
- [x] 分析两个 PipelineContext 的差异
- [x] 分析 SkeletonContext 的 Items 类型（ConcurrentDictionary）
- [ ] 修改 WorkflowContext.cs：让 PipelineContext 继承 Modules.Pipeline.PipelineContext
- [ ] 修改 PipelineBuilder.cs：消除 CS0104 歧义
- [ ] 编译验证
```

---

## 九、可用工具列表

### CLine 内置工具
| 工具 | 说明 |
|------|------|
| `ask_followup_question` | 向用户提问以澄清或收集信息 |
| `execute_command` | 在系统上执行 CLI 命令 |
| `read_file` | 读取文件内容 |
| `write_to_file` | 写入文件内容 |
| `replace_in_file` | 替换文件中指定内容 |
| `search_files` | 跨文件正则搜索 |
| `list_files` | 列出目录内容 |
| `list_code_definition_names` | 列出源码定义名称 |
| `access_mcp_resource` | 访问 MCP 资源 |
| `attempt_completion` | 完成任务后呈现结果 |
| `plan_mode_respond` | PLAN MODE 下回复用户 |
| `load_mcp_documentation` | 加载 MCP 文档 |
| `generate_explanation` | 生成代码变更解释 |

### MCP 工具（docker-mcp-bridge）

**Sequential Thinking：**
| 工具 | 说明 |
|------|------|
| `mcp0sequentialthinking_tools` | 记录推理步骤 |
| `get_thinking_history` | 查看推理历史 |
| `clear_thinking_history` | 清除推理历史 |

**Context7 文档查询：**
| 工具 | 说明 |
|------|------|
| `resolve-library-id` | 解析库 ID |
| `query-docs` | 查询文档 |

**沙箱执行：**
| 工具 | 说明 |
|------|------|
| `ctx_execute` | 沙箱中运行代码 |
| `ctx_execute_file` | 沙箱中分析文件 |
| `ctx_index` | 索引内容到知识库 |
| `ctx_search` | 搜索知识库 |
| `ctx_fetch_and_index` | 获取网页并索引 |
| `ctx_batch_execute` | 批量执行命令 |
| `ctx_stats` | 上下文消耗统计 |
| `ctx_doctor` | 诊断安装状态 |
| `ctx_upgrade` | 升级 context-mode |
| `ctx_purge` | 删除索引内容 |
| `ctx_insight` | 打开 Insight 仪表板 |

**知识图谱：**
| 工具 | 说明 |
|------|------|
| `create_entities` | 创建实体 |
| `create_relations` | 创建关系 |
| `add_observations` | 添加观察 |
| `delete_entities` | 删除实体 |
| `delete_observations` | 删除观察 |
| `delete_relations` | 删除关系 |
| `read_graph` | 读取知识图谱 |
| `search_nodes` | 搜索节点 |
| `open_nodes` | 打开节点 |
| `switch_project` | 切换项目 |

**代码分析（Lexis）：**
| 工具 | 说明 |
|------|------|
| `get_git_status` | Git 状态 |
| `replace_symbol_source` | 替换符号源码 |
| `add_field_to_model` | 添加模型字段 |
| `get_function_source` | 获取函数源码 |
| `find_symbol` | 查找符号 |
| `get_full_context` | 符号完整上下文 |
| `get_call_chain` | 调用链 |
| `search_codebase` | 搜索代码库 |
| `analyze_config` | 审计配置文件 |
| `find_dead_code` | 查找死代码 |
| `detect_breaking_changes` | 检测破坏性变更 |
| `get_entry_points` | 获取入口点 |
| `find_semantic_duplicates` | 查找语义重复 |
| `ts_search` | 搜索 Token Savior 工具 |

**CsprojContext：**
| 工具 | 说明 |
|------|------|
| `get_dependencies` | 获取模块依赖 |
| `get_detail` | 获取模块详情 |
| `get_layer_map` | 获取层编号映射 |
| `get_by_layer` | 按层筛选模块 |
| `get_overview` | 项目架构概览 |
| `get_project_tree` | 项目树形结构 |
| `search_modules` | 搜索模块 |
| `set_module_rule` | 设置模块规则 |
| `validate_rules` | 验证模块规则 |

**PowerShell 桥接：**
| 工具 | 说明 |
|------|------|
| `invoke_ps_script` | 调用 PowerShell 脚本/模块函数 |

---

## 十、用户自定义指令

- **首选语言**：zh-CN（简体中文）
- **`.clinerules/` 目录**：提供项目级工作指南，Cline 自动注入

---

*文档结束*
