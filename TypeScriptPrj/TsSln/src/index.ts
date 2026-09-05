/**
 * TsSln — TypeScript 项目管理框架
 *
 * 主入口文件，导出所有公共 API。
 *
 * 用法:
 *   import { parseSln, buildAll, builderRegistry } from 'tssln'
 */

// 类型导出
export type {
  TsProject,
  SlnResult,
  BuildResult,
  BuildOptions,
  WatchOptions,
  Builder,
  CliCommand,
  OutputFormat,
  TsSlnConfig,
  ProjectSourceConfig,
  BuilderConfig,
  HooksConfig,
  Plugin,
  PluginContext,
  TaskInfo,
  TaskStatus,
} from './types.js'

// 解析器导出
export { parseSln } from './parser/slnParser.js'
export { parseCsproj } from './parser/csprojParser.js'

// 构建器导出
export { builderRegistry } from './builder/index.js'
export { TscBuilder } from './builder/tscBuilder.js'

// 任务导出
export { buildAll, buildProject, cleanAll } from './tasks/index.js'

// 工具导出
export { topologicalSort, getAllDependencies, getAffectedProjects } from './utils/graph.js'
export { Logger, defaultLogger, setDefaultLogger } from './utils/logger.js'
export { findFileUpward, findProjectRoot, toForwardSlashes } from './utils/path.js'
export {
  installAllDependencies,
  installProjectDependencies,
  checkDependencies,
} from './utils/dependencyInstaller.js'
export type { InstallResult, InstallOptions } from './utils/dependencyInstaller.js'

// 配置导出
export { loadConfig } from './config/index.js'
export { defaultConfig } from './config/defaults.js'
export { validateConfig, CONFIG_FILENAME } from './config/schema.js'

// 插件导出
export { pluginManager } from './plugins/index.js'
