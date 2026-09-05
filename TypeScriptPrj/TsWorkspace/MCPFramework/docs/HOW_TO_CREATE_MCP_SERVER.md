# 基于 MCPFramework 创建 MCP 服务器

## 概述

MCPFramework 是一个通用的 MCP 服务器框架，基于 CsprojContext 的架构抽象而来。它提供了 MCP 服务器的骨架代码，让你只需关注业务逻辑实现。

## 架构图

```
┌──────────────────────────────────────────────────────────┐
│  你的 MCP 服务器项目                                       │
│                                                           │
│  tools-definitions/*.json  ← ★ JSON 定义（单一数据源）     │
│       ↓ 自动生成 (npm run generate)                       │
│  tools.config.json         ← 工具定义（配置驱动）           │
│  src/tools/*.ts            ← ★ 自动生成的 handler          │
│                                                           │
│  src/index.ts              ← 入口（调用框架 API）           │
│  src/cli.ts                ← CLI（框架提供，无需修改）      │
│  src/core/                 ← 框架核心文件（复制自框架）     │
│  │  ├── types.ts           ← 类型定义                      │
│  │  ├── registry.ts        ← 工具注册表                    │
│  │  ├── server.ts          ← Server 工厂                   │
│  │  └── utils.ts           ← 通用工具（fast-glob）          │
│  ├── core/business.ts      ← ★ 你的业务逻辑                │
│  └── tools/*.ts            ← ★ 自动/手动工具处理函数        │
│                                                           │
│  build/generate.js         ← 生成器脚本（代码生成）         │
└──────────────────────────────────────────────────────────┘

```

## 快速开始（5 步）

### 步骤 1：复制模板

```bash
# 从 MCPFramework 复制模板到新项目目录
cp -r MCPFramework/templates/project/ MyNewMcpServer/
cd MyNewMcpServer
```

### 步骤 2：复制框架核心文件

```bash
# 复制框架的核心文件到项目
cp MCPFramework/src/core/*.ts src/core/
```

### 步骤 3：安装依赖

```bash
npm install
```

### 步骤 4：实现业务逻辑

编辑 `src/core/business.ts`，添加你的业务方法：

```typescript
export const business = {
  // 你的业务方法
  analyzeData(filePath: string): string {
    // 实现你的业务逻辑
    return `分析结果: ${filePath}`;
  },

  validateConfig(config: any): boolean {
    // 实现验证逻辑
    return true;
  },
};
```

### 步骤 5：定义工具（JSON 模板方式）

在 `tools-definitions/` 目录下创建 JSON 定义文件，每个工具一个文件：

```json
// tools-definitions/analyze.json
{
  "name": "analyze_data",
  "description": "分析数据文件",
  "handler": "analyze",
  "template": "query",
  "inputSchema": {
    "properties": {
      "filePath": {
        "type": "string",
        "description": "文件路径"
      }
    },
    "required": ["filePath"]
  },
  "implementation": {
    "imports": [
      { "name": "getBusinessService", "from": "../core/registry" }
    ],
    "requiredParams": ["filePath"],
    "mainCall": "getBusinessService().analyzeData(filePath)",
    "outputCall": "`# 分析结果\\n\\n${results}`"
  }
}
```

然后运行生成器：

```bash
npm run generate
```

这会自动生成：
- `tools.config.json` — 工具定义配置
- `src/tools/analyze.ts` — 工具处理函数（代码生成）


### 步骤 6：构建

```bash
npm run build
```

### 步骤 7：配置到 MCP 客户端

在 MCP 客户端配置中添加：

```json
{
  "mcpServers": {
    "my-mcp-server": {
      "command": "node",
      "args": ["path/to/MyNewMcpServer/build/index.js"]
    }
  }
}
```

## JSON 模板驱动生成

### 为什么用 JSON 模板？

传统方式需要手动编写 `tools.config.json` + `src/tools/*.ts` 两个文件，存在大量重复：
- `inputSchema` 中的参数名与 handler 中的参数提取代码重复
- 每个 handler 的 try/catch 结构相同
- 参数校验逻辑重复

JSON 模板方式将 **单一数据源** 定义为 JSON，自动生成所有文件。

### 模板类型

| 模板类型 | 说明 | 适用场景 |
|---------|------|---------|
| `query` | 查询类工具 | 读取数据、搜索、列表 |
| `query-with-detail` | 带 detail 参数的查询类工具 | 列表/概览，支持 detail 切换详细/简洁模式 |
| `detail` | 详情类工具 | 获取单个对象详情 |
| `write` | 写入类工具 | 创建、更新、删除 |
| `no-args` | 无参数工具 | 固定输出、版本信息 |
| `custom` | 自定义模板 | 复杂逻辑，保留手写 |

生成器直接生成 TypeScript 代码，无需外部模板引擎。


### JSON 定义字段说明

```json
{
  "name": "工具名称（snake_case）",
  "description": "工具描述",
  "handler": "文件名（不含扩展名）",
  "template": "query | detail | write | no-args | custom",
  "inputSchema": {
    "properties": { ... },
    "required": ["必填参数"]
  },
  "implementation": {
    "imports": [
      { "name": "函数名", "from": "模块路径" }
    ],
    "requiredParams": ["必填参数名"],
    "extraParams": [
      { "name": "depth", "type": "number", "default": "1" }
    ],
    "optionalParams": [
      { "name": "keywords", "type": "string" }
    ],
    "mainCall": "核心业务调用代码",
    "emptyCheck": "空结果时的提示信息",
    "notFoundCheck": "未找到时的提示信息",
    "outputCall": "输出格式化代码"
  }
}
```

### 生成的文件

| 输入 | 输出 | 说明 |
|------|------|------|
| `tools-definitions/*.json` | `tools.config.json` | 工具定义配置 |
| `tools-definitions/*.json` | `src/tools/{handler}.ts` | 工具处理函数（代码生成） |


## 文件说明

| 文件 | 必须修改？ | 说明 |
|------|-----------|------|
| `package.json` | ✅ 是 | 修改 name、description、bin 名称 |
| `tools-definitions/*.json` | ✅ 是 | 定义你的工具（JSON 模板） |
| `src/core/business.ts` | ✅ 是 | 实现你的业务逻辑 |
| `src/index.ts` | ⚠️ 建议 | 修改服务器名称和版本 |
| `tools.config.json` | ❌ 否 | 自动生成，请勿手动修改 |
| `src/tools/*.ts` | ❌ 否 | 自动生成，请勿手动修改 |
| `src/core/types.ts` | ❌ 否 | 框架提供，无需修改 |
| `src/core/registry.ts` | ❌ 否 | 框架提供，无需修改 |
| `src/core/server.ts` | ❌ 否 | 框架提供，无需修改 |
| `src/core/utils.ts` | ❌ 否 | 框架提供，无需修改 |

## 工具开发规范

### 每个工具文件必须

1. **export `handle` 函数**：`export async function handle(args: Record<string, unknown>): Promise<string>`
2. **返回 Markdown 字符串**：MCP 协议要求返回 text 类型的内容
3. **通过 `getBusinessService()` 访问业务逻辑**：不要直接在 handler 中实现复杂业务

### handler 命名约定

- `tools.config.json` 中的 `handler` 字段 = 文件名（不含 `.ts` 扩展名）
- 例如：`handler: "analyze"` → 文件 `src/tools/analyze.ts`
- 文件名使用 kebab-case（如 `my-tool.ts`）

## 可用 npm 包

MCPFramework 及其子项目模板预装了以下 npm 包：

| 包名 | 类型 | 用途 |
|------|------|------|
| `@modelcontextprotocol/sdk` | runtime | MCP 协议核心 |
| `commander` | runtime | CLI 参数解析（支持子命令、--help） |
| `fast-glob` | runtime | 文件 glob 匹配（替代手写递归） |

| `eslint` | dev | 代码检查 |
| `prettier` | dev | 代码格式化 |
| `tsx` | dev | 直接运行 TypeScript（`tsx src/index.ts`） |
| `vitest` | dev | 单元测试 |
| `typescript` | dev | TypeScript 编译器 |

## 示例：完整的最小项目

```
my-mcp-server/
├── package.json
├── tsconfig.json
├── tools.config.json          # 自动生成
├── tools-definitions/         # ★ 你的 JSON 模板
│   └── hello.json
├── build/
│   └── generate.js            # 生成器脚本
├── src/
│   ├── index.ts
│   ├── core/
│   │   ├── types.ts           # 框架提供
│   │   ├── registry.ts        # 框架提供
│   │   ├── server.ts          # 框架提供
│   │   ├── utils.ts           # 框架提供（fast-glob）
│   │   └── business.ts        # 你的业务逻辑
│   └── tools/
│       └── hello.ts           # 自动生成
└── build/                     # 构建输出
```

## 常见问题

### Q: 如何添加多个工具？

A: 在 `tools-definitions/` 目录下创建多个 JSON 文件，每个工具一个文件，然后运行 `npm run generate`。

### Q: 如何处理错误？
A: 生成器自动添加 try/catch 包装，无需手动处理。

### Q: 如何访问配置文件？
A: 使用框架提供的 `loadConfig<T>()` 函数加载 JSON 配置文件。

### Q: 框架更新后如何同步？
A: 重新复制 `src/core/*.ts` 文件到你的项目即可。

### Q: 复杂工具如何实现？
A: 设置 `template: "custom"`，然后手动编写 handler 文件。生成器会跳过自定义模板。

### Q: 如何自定义生成的代码模板？
A: 编辑 `build/generate.js` 中的代码生成逻辑，修改对应模板类型的生成函数。


