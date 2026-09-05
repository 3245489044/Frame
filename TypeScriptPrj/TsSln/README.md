# TsSln — TypeScript Project Management Framework

## 概述

TsSln 是一个通用的 TypeScript 项目管理框架，支持从 `.sln` 解决方案文件解析项目结构，按依赖顺序构建/清理项目，并提供可扩展的构建器接口和插件系统。

### 核心特性

- **🔗 依赖感知** — 自动解析项目依赖，按拓扑顺序构建
- **📊 错误收集** — 构建失败不中断流程，收集所有错误后汇总报告
- **🔄 增量构建** — 利用 TypeScript Project References，只重新编译变更的文件
- **🔌 可扩展** — 支持自定义构建器和插件
- **⚙️ 可配置** — 支持 `tsm.config.json` 配置文件

## 项目依赖

```
BasicMethod (无依赖)
    └── MCPFramework (依赖 BasicMethod)
            ├── MCPBridge (依赖 MCPFramework)
            └── CsprojContext (依赖 MCPFramework)
```

构建顺序：`BasicMethod → MCPFramework → MCPBridge → CsprojContext`

## 架构

```
src/
├── index.ts              # 主入口（导出公共 API）
├── cli.ts                # CLI 入口
├── types.ts              # 核心类型定义
├── config/               # 配置系统
│   ├── index.ts          # 配置加载器
│   ├── schema.ts         # 配置校验
│   └── defaults.ts       # 默认配置
├── parser/               # 解析器
│   ├── slnParser.ts      # .sln 文件解析
│   └── csprojParser.ts   # .csproj 文件解析
├── builder/              # 构建器
│   ├── index.ts          # 构建器注册表
│   └── tscBuilder.ts     # TypeScript 编译器构建器
├── tasks/                # 任务调度
│   └── index.ts          # 构建/清理任务
├── utils/                # 工具
│   ├── graph.ts          # 拓扑排序
│   ├── logger.ts         # 日志工具
│   └── path.ts           # 路径工具
└── plugins/              # 插件系统
    └── index.ts          # 插件管理器
```

## 快速开始

```bash
# 构建 TsSln 自身
cd TypeScriptPrj/TsSln
npm run build

# 查看项目列表
npm run start:list

# 查看项目信息
npm run start:info

# 构建所有项目
npm start

# 清理所有项目
npm run clean
```

## CLI 用法

```bash
tsm build              # 构建所有项目
tsm build <name>       # 构建指定项目
tsm clean              # 清理所有项目
tsm list               # 列出所有项目
tsm info               # 显示项目信息
tsm watch              # 监听模式（开发中）
```

## 构建能力

### 增量构建

TsSln 利用 TypeScript Project References 的 `--build` 模式：

- **增量构建**：只重新编译变更的文件及其依赖，后续构建速度更快
- **未构建检测**：自动检测尚未构建的项目并优先构建
- **重新编译**：修改代码后再次运行 `tsm build` 即可，无需手动清理

```bash
# 首次构建（完整构建）
tsm build

# 修改代码后重新构建（增量）
tsm build

# 强制重新构建
tsm clean && tsm build
```

### 错误收集

构建过程中遇到错误**不会中断**整个流程：

1. 所有项目按依赖顺序逐个构建
2. 每个项目的错误被单独收集并显示
3. 所有项目构建完成后输出完整的汇总报告
4. 退出码反映整体结果（有失败则返回 1）

### 构建结果

构建完成后会输出汇总报告：

```
📋 构建顺序: BasicMethod → MCPFramework → MCPBridge → CsprojContext

⚠️  以下项目尚未构建: MCPBridge, CsprojContext

🔨 构建: BasicMethod
✅ BasicMethod 构建成功

🔨 构建: MCPFramework
✅ MCPFramework 构建成功

🔨 构建: MCPBridge
❌ MCPBridge 构建失败 (2 errors)
   src/core/bridge.ts:42:3 - error TS2322: Type 'string' is not assignable to type 'number'

🔨 构建: CsprojContext
✅ CsprojContext 构建成功

==================================================
📊 构建结果汇总:
==================================================
  ✅ BasicMethod     — 构建成功 (120ms)
  ✅ MCPFramework   — 构建成功 (85ms)
  ❌ MCPBridge      — 构建失败 (2 errors)
  ✅ CsprojContext   — 构建成功 (95ms)
--------------------------------------------------
  结果: 3/4 成功, 1/4 失败
==================================================
```

## 配置

在项目根目录创建 `tsm.config.json` 文件：

```json
{
  "sources": [
    { "type": "sln", "path": "TypeScript.sln" }
  ],
  "defaultBuilder": "tsc",
  "builders": {
    "tsc": { "module": "tscBuilder" }
  },
  "format": "text",
  "verbose": false,
  "hooks": {
    "preBuild": ["echo '构建开始'"],
    "postBuild": ["echo '构建结束'"]
  }
}
```

### 配置项说明

| 字段 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `sources` | `ProjectSourceConfig[]` | `[{ type: "sln", path: "TypeScript.sln" }]` | 项目源配置 |
| `defaultBuilder` | `string` | `"tsc"` | 默认构建器名称 |
| `builders` | `Record<string, BuilderConfig>` | `{ tsc: { module: "tscBuilder" } }` | 构建器注册配置 |
| `format` | `"text" \| "json" \| "silent"` | `"text"` | 输出格式 |
| `verbose` | `boolean` | `false` | 是否显示详细输出 |
| `hooks.preBuild` | `string[]` | `[]` | 构建前执行的命令 |
| `hooks.postBuild` | `string[]` | `[]` | 构建后执行的命令 |

## 扩展

### 自定义构建器

实现 `Builder` 接口，然后注册到构建器注册表：

```typescript
import type { Builder, TsProject, BuildResult } from 'tssln'
import { builderRegistry } from 'tssln'

class EsbuildBuilder implements Builder {
  readonly name = 'esbuild'

  build(project: TsProject, slnDir: string): BuildResult {
    // 自定义构建逻辑
    return { name: project.name, success: true }
  }

  clean(project: TsProject, slnDir: string): void {
    // 自定义清理逻辑
  }
}

// 注册自定义构建器
builderRegistry.register('esbuild', new EsbuildBuilder())
```

在配置中指定使用自定义构建器：

```json
{
  "defaultBuilder": "esbuild",
  "builders": {
    "esbuild": { "module": "esbuildBuilder" }
  }
}
```

### 自定义插件

实现 `Plugin` 接口：

```typescript
import type { Plugin, PluginContext, TsProject, BuildResult } from 'tssln'

const myPlugin: Plugin = {
  name: 'my-plugin',
  init(context: PluginContext) {
    console.log('插件初始化')
  },
  preBuild(project: TsProject) {
    console.log(`构建前: ${project.name}`)
  },
  postBuild(project: TsProject, result: BuildResult) {
    console.log(`构建后: ${project.name} - ${result.success ? '成功' : '失败'}`)
  }
}
```

## 许可证

MIT
