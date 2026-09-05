/**
 * 日志工具
 *
 * @deprecated 当前代码全部使用 console.log/console.error 直接输出，
 *             未使用 Logger 类。保留此文件仅用于向后兼容，
 *             后续重构时建议移除或统一集成。
 *
 * 提供统一的日志输出接口，支持多种输出格式。
 */
import type { OutputFormat } from '../types.js'

/** 日志级别 */
export type LogLevel = 'info' | 'warn' | 'error' | 'debug' | 'success'

/** 日志配置 */
export interface LoggerOptions {
  /** 输出格式 */
  format: OutputFormat
  /** 是否显示详细输出 */
  verbose: boolean
}

/** 默认日志配置 */
const defaultOptions: LoggerOptions = {
  format: 'text',
  verbose: false,
}

/** 日志条目 */
export interface LogEntry {
  level: LogLevel
  message: string
  timestamp: string
  data?: unknown
}

/**
 * 日志器类
 * @deprecated 未在项目中使用，建议后续移除
 */
export class Logger {
  private options: LoggerOptions

  constructor(options?: Partial<LoggerOptions>) {
    this.options = { ...defaultOptions, ...options }
  }

  /** 设置选项 */
  setOptions(options: Partial<LoggerOptions>): void {
    this.options = { ...this.options, ...options }
  }

  /** 获取当前选项 */
  getOptions(): LoggerOptions {
    return { ...this.options }
  }

  /** 信息日志 */
  info(message: string, data?: unknown): void {
    this.log('info', message, data)
  }

  /** 警告日志 */
  warn(message: string, data?: unknown): void {
    this.log('warn', message, data)
  }

  /** 错误日志 */
  error(message: string, data?: unknown): void {
    this.log('error', message, data)
  }

  /** 调试日志 */
  debug(message: string, data?: unknown): void {
    if (this.options.verbose) {
      this.log('debug', message, data)
    }
  }

  /** 成功日志 */
  success(message: string, data?: unknown): void {
    this.log('success', message, data)
  }

  /** 核心日志方法 */
  private log(level: LogLevel, message: string, data?: unknown): void {
    if (this.options.format === 'silent') return

    const entry: LogEntry = {
      level,
      message,
      timestamp: new Date().toISOString(),
      data,
    }

    switch (this.options.format) {
      case 'json':
        this.outputJson(entry)
        break
      case 'text':
      default:
        this.outputText(entry)
        break
    }
  }

  /** 文本格式输出 */
  private outputText(entry: LogEntry): void {
    const prefix = this.getPrefix(entry.level)
    const output = `${prefix} ${entry.message}`

    switch (entry.level) {
      case 'error':
        console.error(output)
        break
      case 'warn':
        console.warn(output)
        break
      default:
        console.log(output)
    }
  }

  /** JSON 格式输出 */
  private outputJson(entry: LogEntry): void {
    console.log(JSON.stringify(entry))
  }

  /** 获取日志级别前缀 */
  private getPrefix(level: LogLevel): string {
    switch (level) {
      case 'info': return 'ℹ️'
      case 'warn': return '⚠️'
      case 'error': return '❌'
      case 'debug': return '🔍'
      case 'success': return '✅'
    }
  }

  /** 分隔线 */
  separator(char: string = '=', length: number = 50): void {
    if (this.options.format === 'text') {
      console.log(char.repeat(length))
    }
  }

  /** 空行 */
  blankLine(): void {
    if (this.options.format === 'text') {
      console.log('')
    }
  }
}

/** 全局默认日志器实例 */
/** @deprecated 未在项目中使用 */
export let defaultLogger = new Logger()

/** 更新全局日志器 */
/** @deprecated 未在项目中使用 */
export function setDefaultLogger(logger: Logger): void {
  defaultLogger = logger
}
