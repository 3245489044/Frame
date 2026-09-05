# CLine MCP 配置指南

## 概述

本文档说明如何在 WorkFlowPrj 项目中配置 CLine，使其优先使用 MCP（Model Context Protocol）工具。

## 配置方式

CLine 支持两种方式配置"优先使用 MCP"的行为：

### 方式一：项目级 `.clinerules` 文件（已配置）

项目根目录的 `.clinerules` 文件已创建，CLine 会自动将其内容注入到每次对话的系统提示中。

**文件位置：** `d:\Project\Program\WorkFlowPrj\.clinerules`

### 方式二：全局 Custom Instructions（需要手动配置）

在 CLine 扩展的设置界面中配置全局自定义指令，步骤如下：

1. 在 VS Code 中点击左侧活动栏的 CLine 图标
2. 点击设置齿轮图标 → "Extension Settings"
3. 找到 "Cline › Custom Instructions" 配置项
4. 在文本框中粘贴以下内容：

```
你被配置为优先使用 MCP（Model Context Protocol）工具。

在每次执行任务时，请按以下优先级选择工具：
1. 首先检查是否有可用的 MCP 工具能完成当前操作
2. 如果没有合适的 MCP 工具，再使用 CLine 内置工具

MCP 工具与内置工具的对应关系：
- 文件读取：MCP filesystem 的 read_text_file / read_multiple_files
- 文件写入：MCP filesystem 的 write_file
- 文件编辑：MCP filesystem 的 edit_file
- 文件搜索：MCP filesystem 的 search_files
- 目录列表：MCP filesystem 的 list_directory / directory_tree
- 命令执行：context-mode 的 ctx_execute
- 文件分析：context-mode 的 ctx_execute_file
- 批量操作：context-mode 的 ctx_batch_execute
- 知识检索：context-mode 的 ctx_search
- 文档索引：context-mode 的 ctx_index / ctx_fetch_and_index
- 知识图谱：memory 的 create_entities / search_nodes 等
- 文档查询：context7-mcp 的 resolve-library-id / query-docs
- 复杂推理：mcp-sequentialthinking-tools 的 sequentialthinking_tools（替代官方 sequential-thinking）
```

## 已配置的 MCP 服务器

| MCP 服务器 | 用途 | autoApprove 工具数 |
|---|---|---|
| `mcp-sequentialthinking-tools` | 复杂问题分解推理（替代官方 sequential-thinking） | 1 |
| `context7-mcp` | 库文档查询 | 2 |
| `context-mode` | 代码沙箱执行、知识库索引 | 11 |
| `memory` | 知识图谱存储与查询 | 9 |
| `filesystem` | 文件系统操作（限定项目目录） | 13 |
| `lexis-mcp` | 词法+结构代码搜索（28+工具） | 13 |
| `csproj-context` | .csproj ModuleRule 查询（架构概览/模块详情/搜索/依赖/验证） | 8 |

## 验证配置

运行以下 PowerShell 命令验证配置状态：

```powershell
# 加载配置分析模块
. .\Script\Tools\modules\Update-ClineMCPConfig.ps1

# 运行分析
Update-ClineMCPConfig
```

## MCP 工具使用速查

### 文件操作
```powershell
# 读取文件（优先）
filesystem.read_text_file(path: "file.txt")

# 写入文件（优先）
filesystem.write_file(path: "file.txt", content: "...")

# 编辑文件（优先）
filesystem.edit_file(path: "file.txt", edits: [{oldText: "...", newText: "..."}])

# 搜索文件（优先）
filesystem.search_files(path: ".", pattern: "*.cs")
```

### 代码执行
```javascript
// 运行代码（优先）
context-mode.ctx_execute(language: "javascript", code: "console.log('hello')")

// 分析文件（优先）
context-mode.ctx_execute_file(path: "file.log", language: "javascript", code: "// process FILE_CONTENT")
```

### 知识管理
```javascript
// 创建实体（优先）
memory.create_entities(entities: [{name: "...", entityType: "...", observations: [...]}])

// 搜索知识图谱（优先）
memory.search_nodes(query: "...")
```
