# Solution Generator 设计文档

## 1. 概述

### 1.1 目标

构建一个基于 JSON 项目清单的解决方案文件生成器，解决以下问题：

- `.sln` 文件庞大冗余（尤其是 `ProjectConfigurationPlatforms` 部分）
- 添加/删除项目时需要手动编辑 `.sln`，易出错
- 不支持按模块生成多个 `.sln` 文件
- x64/x86 等冗余配置需要手动清理

### 1.2 核心思路

维护一个 JSON 格式的项目清单作为**唯一数据源**，脚本根据此清单自动生成完整的 `.sln` 文件。

```
WorkFlowPrj.projects.json  (项目清单，唯一数据源)
         │
         ▼
GenerateSolution.ps1       (生成脚本)
         │
         ├──▶ WorkFlowPrj.sln          (完整解决方案)
         ├──▶ WorkFlowPrj.Core.sln     (仅核心模块)
         ├──▶ WorkFlowPrj.Tests.sln    (仅测试模块)
         └──▶ ...                      (按需生成)
```

---

## 2. JSON 项目清单设计

### 2.1 文件位置

`WorkFlowPrj.projects.json` 放在项目根目录。

### 2.2 完整结构

```json
{
  "$schema": "v1",
  "description": "WorkFlowPrj 解决方案项目清单",

  "solution": {
    "name": "WorkFlowPrj",
    "version": "12.00",
    "visualStudioVersion": "17.5.320.0",
    "minimumVisualStudioVersion": "10.0.40219.1"
  },

  "configurations": [
    "Debug|Any CPU",
    "Release|Any CPU"
  ],

  "folders": [
    {
      "name": "BasicMethod",
      "id": "{165BA845-EE91-45EA-84DF-681D701F1BB1}"
    },
    {
      "name": "CentralConsole",
      "id": "{BD24FE40-C563-4801-A4D4-ABB7925179E0}"
    }
  ],

  "projects": [
    {
      "name": "BasicMethod",
      "path": "BasicMethod\\BasicMethod.csproj",
      "type": "csproj",
      "id": "{8E83C8E9-F18E-2146-CBB8-21D9B38A76E8}",
      "folder": "BasicMethod"
    },
    {
      "name": "Common",
      "path": "BasicMethod\\Common\\Common.csproj",
      "type": "csproj",
      "id": "{357F18DE-C6DF-80D6-5B13-EA840AA23D40}",
      "folder": "BasicMethod"
    }
  ],

  "nestedProjects": [
    { "child": "{8E83C8E9-...}", "parent": "{165BA845-...}" },
    { "child": "{357F18DE-...}", "parent": "{165BA845-...}" }
  ]
}
```

### 2.3 字段说明

#### `solution` — 解决方案元信息

| 字段 | 说明 | 默认值 |
|------|------|--------|
| `name` | 解决方案名称 | `"WorkFlowPrj"` |
| `version` | .sln 格式版本 | `"12.00"` |
| `visualStudioVersion` | VS 版本 | `"17.5.320.0"` |
| `minimumVisualStudioVersion` | 最低 VS 版本 | `"10.0.40219.1"` |

#### `configurations` — 构建配置列表

每项格式为 `"{配置名}|{平台名}"`，例如：
- `"Debug|Any CPU"`
- `"Release|Any CPU"`

脚本会自动为每个项目生成每项配置的 `ActiveCfg` 和 `Build.0` 映射。

#### `folders` — 解决方案文件夹

| 字段 | 说明 |
|------|------|
| `name` | 文件夹显示名称 |
| `id` | 固定 GUID（用于 NestedProjects 引用） |

#### `projects` — 项目列表

| 字段 | 说明 |
|------|------|
| `name` | 项目显示名称 |
| `path` | 项目文件路径（相对于 .sln） |
| `type` | 项目类型：`csproj`、`folder` 等 |
| `id` | 项目 GUID |
| `folder` | 所属解决方案文件夹名称（可选） |

**项目类型与 GUID 映射：**

| 类型 | 类型 GUID |
|------|-----------|
| `csproj` (C#) | `{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}` |
| `folder` (解决方案文件夹) | `{2150E333-8FDC-42A3-9474-1A3956D46DE8}` |
| `vcxproj` (C++) | `{8BC9CEB8-8B4A-11D0-8D11-00A0C91BC942}` |
| `fsproj` (F#) | `{F2A71F9B-5D33-465A-A702-920D77279786}` |
| `sqlproj` (SQL) | `{00D1A9C2-B5F0-4AF3-8072-F6C62B433612}` |

#### `nestedProjects` — 项目嵌套关系

| 字段 | 说明 |
|------|------|
| `child` | 子项目/文件夹的 GUID |
| `parent` | 父文件夹的 GUID |

---

## 3. 脚本设计

### 3.1 文件位置

- `Tools\GenerateSolution.ps1` — PowerShell 主脚本
- `Tools\GenerateSolution.bat` — 批处理启动器

### 3.2 命令行接口

```
Tools\GenerateSolution.bat                    # 交互模式
Tools\GenerateSolution.bat --all              # 生成完整解决方案
Tools\GenerateSolution.bat --module Core      # 生成指定模块的解决方案
Tools\GenerateSolution.bat --list-modules     # 列出可用模块
Tools\GenerateSolution.bat --check            # 检查 JSON 与 .sln 是否一致
Tools\GenerateSolution.bat --help             # 显示帮助
```

### 3.3 生成逻辑

```
1. 读取 WorkFlowPrj.projects.json
2. 解析项目列表、文件夹、嵌套关系
3. 生成 .sln 文件头
4. 生成 Project 声明块
5. 生成 Global 块：
   a. SolutionConfigurationPlatforms（从 configurations 生成）
   b. ProjectConfigurationPlatforms（为每个项目生成每项配置的映射）
   c. SolutionProperties（固定）
   d. NestedProjects（从 nestedProjects 生成）
6. 写入 .sln 文件
```

### 3.4 模块支持（未来扩展）

在 JSON 中为项目添加 `module` 字段：

```json
{
  "projects": [
    {
      "name": "BasicMethod",
      "path": "BasicMethod\\BasicMethod.csproj",
      "type": "csproj",
      "id": "{8E83C8E9-...}",
      "folder": "BasicMethod",
      "module": "Core"
    },
    {
      "name": "FileDriven.Tests",
      "path": "CentralConsole\\FileDriven.Tests\\FileDriven.Tests.csproj",
      "type": "csproj",
      "id": "{67D0B4FC-...}",
      "folder": "FileDriven",
      "module": "Tests"
    }
  ]
}
```

`--module Core` 时只包含 `module: "Core"` 的项目。

---

## 4. 与现有脚本的关系

### 4.1 当前脚本（保留）

`Tools\GenerateSolution.ps1` 当前功能：检测并删除 `.sln` 中的 x64/x86 冗余配置。

### 4.2 升级计划

| 阶段 | 内容 |
|------|------|
| **阶段一** | 创建 JSON 项目清单 + 文档（当前） |
| **阶段二** | 重写 GenerateSolution.ps1，实现从 JSON 生成 .sln |
| **阶段三** | 支持 `--module` 参数，按模块生成多个 .sln |
| **阶段四** | 支持 `--check` 检查 JSON 与 .sln 一致性 |
| **阶段五** | 支持从现有 .sln 反向生成 JSON（迁移工具） |

---

## 5. 使用流程

### 5.1 日常使用

```
1. 添加新项目 → 编辑 WorkFlowPrj.projects.json，添加一条记录
2. 运行 Tools\GenerateSolution.bat --all
3. VS 打开生成的 WorkFlowPrj.sln
```

### 5.2 按模块开发

```
# 只打开核心模块
Tools\GenerateSolution.bat --module Core
code WorkFlowPrj.Core.sln

# 只打开测试模块
Tools\GenerateSolution.bat --module Tests
code WorkFlowPrj.Tests.sln
```

### 5.3 迁移现有 .sln

```
# 从现有 .sln 反向生成 JSON（阶段五）
Tools\GenerateSolution.bat --reverse
# 生成 WorkFlowPrj.projects.json
```

---

## 6. 优势总结

| 对比项 | 传统 .sln | JSON + 脚本生成 |
|--------|-----------|-----------------|
| 文件大小 | 630 行（40 项目） | JSON 约 100 行 |
| 添加项目 | 手动加 12 行配置 | JSON 加 1 条记录 |
| 修改配置 | 手动改每个项目 | 改 JSON 一处即可 |
| 多模块支持 | 需维护多个 .sln | 脚本按需生成 |
| 冗余清理 | 手动或额外脚本 | 生成时自动处理 |
| 可读性 | 差（大量重复） | 好（结构化数据） |
