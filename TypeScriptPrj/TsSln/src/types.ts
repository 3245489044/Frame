/**
 * TsSln — TypeScript 项目管理框架核心类型定义
 */

// ============================================================
// 项目相关
// ============================================================

/** 项目信息 */
export interface TsProject {
  /** 项目名称 */
  name: string
  /** .csproj 文件路径（相对于 .sln 目录） */
  csprojPath: string
  /** 唯一标识 GUID */
  guid: string
  /** 依赖的项目名称列表 */
  dependencies: string[]
  /** 输出目录（相对于 .csproj 目录） */
  outDir: string
  /** .csproj 所在目录的绝对路径 */
  projectDir: string
}

/** .sln 解析结果 */
export interface SlnResult {
  /** .sln 文件所在目录 */
  slnDir: string
  /** 项目列表 */
  projects: TsProject[]
}

// ============================================================
// 构建器接口
// ============================================================

/** 构建选项 */
export interface BuildOptions {
  /** 是否强制重新构建 */
  force?: boolean
  /** 是否显示详细输出 */
  verbose?: boolean
  /** 输出格式 */
  format?: OutputFormat
  /** 构建配置（Debug / Release） */
  configuration?: string
  /** 是否跳过依赖构建（仅构建指定项目） */
  noDeps?: boolean
}

/** 监听选项 */
export interface WatchOptions {
  /** 是否显示详细输出 */
  verbose?: boolean
}

/** 构建结果 */
export interface BuildResult {
  /** 项目名称 */
  name: string
  /** 是否成功 */
  success: boolean
  /** 错误信息（失败时） */
  error?: string
  /** 错误数量（从 tsc 输出中解析） */
  errorCount?: number
  /** 构建耗时（毫秒） */
  duration?: number
}

/** 构建器接口 — 所有构建器需实现此接口 */
export interface Builder {
  /** 构建器名称 */
  readonly name: string
  /** 构建项目 */
  build(project: TsProject, slnDir: string, options?: BuildOptions): BuildResult
  /** 清理项目 */
  clean(project: TsProject, slnDir: string): void
  /** 监听模式（可选） */
  watch?(project: TsProject, slnDir: string, options?: WatchOptions): void
}

// ============================================================
// 配置系统
// ============================================================

/** 输出格式 */
export type OutputFormat = 'text' | 'json' | 'silent'

/** CLI 命令 */
export type CliCommand = 'build' | 'clean' | 'rebuild' | 'list' | 'info' | 'watch' | 'install'

/** 项目源配置 */
export interface ProjectSourceConfig {
  /** 项目源类型 */
  type: 'sln' | 'config' | 'glob'
  /** 路径（sln 文件路径 / glob 模式） */
  path?: string
  /** 直接配置的项目列表（type 为 config 时使用） */
  projects?: TsProject[]
}

/** 构建器配置 */
export interface BuilderConfig {
  /** 构建器模块路径或名称 */
  module: string
  /** 构建器选项 */
  options?: Record<string, unknown>
}

/** 钩子配置 */
export interface HooksConfig {
  /** 构建前执行的命令或脚本 */
  preBuild?: string[]
  /** 构建后执行的命令或脚本 */
  postBuild?: string[]
}

/** TsSln 完整配置 */
export interface TsSlnConfig {
  /** 项目源 */
  sources?: ProjectSourceConfig[]
  /** 构建器注册 */
  builders?: Record<string, BuilderConfig>
  /** 默认构建器名称 */
  defaultBuilder?: string
  /** 钩子 */
  hooks?: HooksConfig
  /** 输出格式 */
  format?: OutputFormat
  /** 是否显示详细输出 */
  verbose?: boolean
}

// ============================================================
// 插件系统
// ============================================================

/** 插件上下文 */
export interface PluginContext {
  /** 项目列表 */
  projects: TsProject[]
  /** 解决方案目录 */
  slnDir: string
  /** 配置 */
  config: TsSlnConfig
  /** 日志方法 */
  log: (message: string) => void
  /** 错误方法 */
  error: (message: string) => void
}

/** 插件接口 */
export interface Plugin {
  /** 插件名称 */
  name: string
  /** 初始化 */
  init?(context: PluginContext): void | Promise<void>
  /** 构建前钩子 */
  preBuild?(project: TsProject): void | Promise<void>
  /** 构建后钩子 */
  postBuild?(project: TsProject, result: BuildResult): void | Promise<void>
  /** 清理前钩子 */
  preClean?(project: TsProject): void | Promise<void>
  /** 清理后钩子 */
  postClean?(project: TsProject): void | Promise<void>
}

// ============================================================
// 任务调度
// ============================================================

/** 任务状态 */
export type TaskStatus = 'pending' | 'running' | 'success' | 'failed' | 'skipped'

/** 任务信息 */
export interface TaskInfo {
  /** 项目名称 */
  name: string
  /** 任务类型 */
  type: 'build' | 'clean' | 'watch'
  /** 状态 */
  status: TaskStatus
  /** 结果 */
  result?: BuildResult
  /** 开始时间 */
  startTime?: number
  /** 结束时间 */
  endTime?: number
}
