# CsprojContext 功能大全

> MCP 服务器 — 读取 .csproj 中嵌入的 ModuleRule 属性，实现项目架构动态导航  
> 版本: 1.0.0 | TypeScript | Node.js

---

## 1. 项目概述

读取 .NET 项目中 `.csproj` 文件内嵌入的 `ModuleRule` 属性，实现项目架构的动态导航。

| 核心能力 | 说明 |
|---------|------|
| **架构概览** | 按层分组展示所有模块 |
| **模块查询** | 获取单个模块的完整详情 |
| **依赖分析** | 获取 ProjectReference 依赖（支持递归深度） |
| **层筛选** | 按层编号筛选模块 |
| **关键词搜索** | 按标题/描述/关键词/路径搜索 |
| **规则注入** | 向 .csproj 注入或更新 ModuleRule |
| **状态验证** | 验证所有 .csproj 的 ModuleRule 配置状态 |

---

## 2. 设计原则

1. **基础规则在 `.clinerules`，项目规则在 `.csproj`** — 职责分离
2. **按需读取，不进对话** — MCP 沙箱处理，仅摘要进上下文
3. **`.csproj` 是单一数据源** — 模块描述/依赖/规则集中管理
4. **渐进式采用** — 核心模块优先，逐步推广
5. **不影响构建** — ModuleRule 对 MSBuild 透明

---

## 3. 架构总览

```
MCP 客户端 (Cline)
    │ ListTools / CallTool
    ▼
CsprojContext MCP Server
    ├── index.ts              ← 入口：请求路由 + 生命周期
    ├── cli.ts                ← CLI 命令行接口
    ├── tools/registry.ts     ← 工具注册中心
    ├── tools/*.ts            ← 9 个 MCP 工具处理函数
    └── core/
        ├── types.ts          ← 类型定义
        ├── parser.ts         ← .csproj 解析核心
        ├── sln-parser.ts     ← .sln 文件解析
        └── utils.ts          ← 工具函数（格式化/缓存/文件扫描）
```

---

## 4. 层编号映射表

| 编号 | 层名称 | 说明 |
|------|--------|------|
| 0 | 辅助/测试 | 工具脚本、测试项目、CentralConsole |
| 1 | 基础层 (BasicMethod) | 基础工具库（核心抽象层） |
| 2 | 模块层 (Modules) | 模块层 |
| 3 | 共享层 (Share) | 共享复用层 |
| 4 | 操作层 (Operation) | 操作实现层 |
| 5 | 应用层 (FileDriven) | 应用层 |
| 6 | 平台层 (Plate) | 数据管理/数学/Roslyn |
| 7 | 工具层 (Storage/Utilitys) | 独立工具项目/代码分析器 |

> 编号越小越基础，上层依赖下层。

---

## 5. .csproj 嵌入格式

```xml
<PropertyGroup Label="ModuleRule">
  <ModuleRule_Title>模块显示名称</ModuleRule_Title>
  <ModuleRule_Layer>层编号（0-7）</ModuleRule_Layer>
  <ModuleRule_Description>模块功能描述</ModuleRule_Description>
  <ModuleRule_Keywords>关键词1;关键词2</ModuleRule_Keywords>
</PropertyGroup>

<ItemGroup Label="ModuleRule_Rules">
  <ModuleRule_Rule Include="模块级规则1" />
</ItemGroup>

<ItemGroup Label="ModuleRule_Prompts">
  <ModuleRule_Prompt Include="当需要xxx时，优先查看此模块" />
</ItemGroup>
```

| 标签 | 组 | 必填 | 说明 |
|------|----|------|------|
| `ModuleRule_Title` | PropertyGroup | 是 | 模块显示名称 |
| `ModuleRule_Layer` | PropertyGroup | 是 | 层编号（0-7） |
| `ModuleRule_Description` | PropertyGroup | 是 | 模块功能描述 |
| `ModuleRule_Keywords` | PropertyGroup | 否 | 关键词（分号分隔） |
| `ModuleRule_Rule` | ItemGroup | 否 | 模块级规则（可多条） |
| `ModuleRule_Prompt` | ItemGroup | 否 | 模块级提示词（可多条） |

---

## 6. MCP 工具大全

### 6.1 get_overview — 项目架构概览

| 参数 | 类型 | 必填 | 默认 | 说明 |
|------|------|------|------|------|
| `slnPath` | string | 否 | — | .sln 路径，提供后以 .sln 为基准 |
| `projectRoot` | string | 否 | WorkFlowPrj | 项目根目录（与 slnPath 互斥） |
| `detail` | boolean | 否 | false | 是否显示关键词/规则/提示词 |

**输出**：按层分组显示所有模块，detail=true 时展示完整信息。

---

### 6.2 get_detail — 模块详情

| 参数 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `path` | string | **是** | .csproj 相对路径，如 `BasicMethod/BasicMethod.csproj` |
| `slnPath` | string | 否 | .sln 路径 |
| `projectRoot` | string | 否 | 项目根目录 |

**输出**：模块标题、层、描述、关键词、规则、提示词。

---

### 6.3 get_dependencies — 依赖关系

| 参数 | 类型 | 必填 | 默认 | 说明 |
|------|------|------|------|------|
| `path` | string | **是** | — | .csproj 相对路径 |
| `depth` | number | 否 | 1 | 递归深度（最大 3） |
| `slnPath` | string | 否 | — | .sln 路径 |
| `projectRoot` | string | 否 | 默认值 | 项目根目录 |

**输出**：ProjectReference 依赖列表（支持传递依赖）。

---

### 6.4 get_by_layer — 按层筛选

| 参数 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `layer` | number | **是** | 层编号 0-7 |
| `slnPath` | string | 否 | .sln 路径 |
| `projectRoot` | string | 否 | 项目根目录 |

**输出**：指定层的所有模块列表。

---

### 6.5 get_layer_map — 层映射表

| 参数 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `slnPath` | string | 否 | .sln 路径 |
| `projectRoot` | string | 否 | 项目根目录 |

**输出**：层编号 → 名称 → 说明的映射表。

---

### 6.6 get_project_tree — 项目树形结构

| 参数 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `slnPath` | string | 否 | .sln 路径 |
| `projectRoot` | string | 否 | 项目根目录 |

**输出**：按层分组的树形结构。

---

### 6.7 search_modules — 关键词搜索

| 参数 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `keyword` | string | **是** | 搜索关键词 |
| `slnPath` | string | 否 | .sln 路径 |
| `projectRoot` | string | 否 | 项目根目录 |

**匹配字段**：title / description / keywords / path（不区分大小写）

---

### 6.8 set_module_rule — 注入/更新 ModuleRule

| 参数 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `path` | string | **是** | .csproj 相对路径 |
| `title` | string | **是** | 模块显示名称 |
| `layer` | number | **是** | 层编号 0-7 |
| `description` | string | **是** | 模块功能描述 |
| `keywords` | string | 否 | 分号分隔的关键词 |
| `rules` | string | 否 | 分号分隔的规则 |
| `prompts` | string | 否 | 分号分隔的提示词 |
| `slnPath` | string | 否 | .sln 路径 |
| `projectRoot` | string | 否 | 项目根目录 |

**注意**：会修改 .csproj 文件，但对 MSBuild 透明。

---

### 6.9 validate_rules — 验证 ModuleRule 状态

| 参数 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `slnPath` | string | 否 | .sln 路径 |
| `projectRoot` | string | 否 | 项目根目录 |

**输出**：总文件数 / 已配置 / 未配置 + 未配置列表。

---

## 7. CLI 命令行接口

```bash
npm run cli -- <tool> [参数...]
node build/cli.js <tool> [参数...]
```

| 命令 | 说明 |
|------|------|
| `csprojcontext-cli` | 显示帮助 |
| `csprojcontext-cli get_overview` | 架构概览 |
| `csprojcontext-cli get_detail --path BasicMethod/BasicMethod.csproj` | 模块详情 |
| `csprojcontext-cli search_modules --keyword 数据` | 搜索 |
| `csprojcontext-cli get_by_layer --layer 1` | 按层筛选 |
| `csprojcontext-cli get_layer_map` | 层映射表 |
| `csprojcontext-cli validate_rules` | 验证状态 |

**参数规则**：`--key value` 格式，数值自动识别，布尔用 `--flag` 不带值。

---

## 8. 核心架构

### 8.1 类型定义 (`core/types.ts`)

```typescript
interface ModuleRule {
  path: string; title: string; layer: number;
  description: string; keywords: string;
  rules: string[]; prompts: string[];
  hasModuleRule: boolean;
}
interface LayerInfo { id: number; name: string; description: string; }
interface DependencyInfo { path: string; title: string; dependencies: string[]; dependencyCount: number; }
interface SlnProject { name: string; path: string; typeGuid: string; isFolder: boolean; parentFolder?: string; guid: string; }
```

### 8.2 解析器 (`core/parser.ts`)

| 函数 | 说明 |
|------|------|
| `parseModuleRule(filePath, projectRoot)` | 从 .csproj 提取 ModuleRule |
| `getAllModuleRules(projectRoot, slnPath?)` | 获取所有模块 |
| `getModuleDetail(relativePath, projectRoot, slnPath?)` | 获取单个模块 |
| `searchModules(keyword, projectRoot, slnPath?)` | 搜索模块 |
| `getModulesByLayer(layer, projectRoot, slnPath?)` | 按层筛选 |
| `getDependencies(relativePath, projectRoot, depth, slnPath?)` | 递归获取依赖 |
| `setModuleRule(relativePath, options, projectRoot, slnPath?)` | 注入/更新 ModuleRule |
| `validateModuleRules(projectRoot, slnPath?)` | 验证状态 |

**解析策略**：正则提取（.csproj 非标准 XML），先快速检查 `Label="ModuleRule"`，文件列表缓存 30 秒。

### 8.3 .sln 解析器 (`core/sln-parser.ts`)

| 函数 | 说明 |
|------|------|
| `parseSlnFile(slnPath)` | 解析 .sln 提取项目条目（含嵌套关系） |
| `getCsprojPathsFromSln(slnPath, relativeTo?)` | 提取 .csproj 路径 |
| `resolveProjectRootFromSln(slnPath)` | 从 .sln 推导项目根目录 |
| `isValidSlnPath(slnPath)` | 验证 .sln 有效性 |

**规则**：匹配 `Project("{typeGuid}") = "name", "path", "{guid}"`，解析 NestedProjects 节，根目录 = .sln 父目录。

### 8.4 工具函数 (`core/utils.ts`)

| 函数 | 说明 |
|------|------|
| `getLayers()` / `getLayerName(layerId)` | 层定义（带缓存） |
| `resolveProjectRoot(projectRoot?)` / `resolveProjectRootWithSln(slnPath?, projectRoot?)` | 根目录解析 |
| `findCsprojFiles(rootDir, slnPath?)` | 获取 .csproj 列表 |
| `groupModulesByLayer(modules)` | 按层分组 |
| `formatOverview / formatDetailResult / formatLayerResults / formatSearchResults / formatDependenciesResult / formatLayerMap` | 格式化输出 |
| `clearCache()` | 清除缓存 |

### 8.5 工具注册中心 (`tools/registry.ts`)

| 函数 | 说明 |
|------|------|
| `registerHandler(name, handler)` | 注册处理函数 |
| `registerAllHandlers()` | 批量注册 9 个工具 |
| `getHandler(name)` | 获取处理函数（handler name → tool name → config.json 回退） |
| `loadToolDefinitions()` | 加载工具定义 |

---

## 9. 开发指南

```bash
npm install          # 安装依赖
npm run build        # 编译（generate + tsc）
npm run generate     # 重新生成工具定义
npm run dev          # 开发模式（tsx 热重载）
npm test             # 运行测试
npm run lint         # ESLint 检查
npm run format       # Prettier 格式化
npm start            # 启动 MCP 服务器
npm run cli          # CLI 模式
```

---

## 10. 项目文件结构

```
CsprojContext/
├── build/                       # 编译输出
├── src/
│   ├── index.ts                 # MCP 服务器入口
│   ├── cli.ts                   # CLI 接口
│   ├── config/
│   │   ├── defaults.json        # 默认配置
│   │   └── layers.json          # 层定义
│   ├── core/
│   │   ├── types.ts             # 类型定义
│   │   ├── parser.ts            # .csproj 解析
│   │   ├── sln-parser.ts        # .sln 解析
│   │   └── utils.ts             # 工具函数
│   └── tools/
│       ├── registry.ts          # 注册中心
│       ├── overview.ts          # get_overview
│       ├── detail.ts            # get_detail
│       ├── dependencies.ts      # get_dependencies
│       ├── layer.ts             # get_by_layer
│       ├── layer-map.ts         # get_layer_map
│       ├── project-tree.ts      # get_project_tree
│       ├── search.ts            # search_modules
│       ├── set-rule.ts          # set_module_rule
│       └── validate.ts          # validate_rules
├── tools-definitions/           # 9 个工具定义 JSON
├── tools.config.json            # 聚合工具配置
├── package.json
├── tsconfig.json
├── vitest.config.ts
├── eslint.config.js
├── .prettierrc
└── README.md
```
