/**
 * 配置 Schema 定义
 *
 * 用于配置文件的校验和类型提示。
 * 当前使用 TypeScript 类型进行编译时校验，
 * 运行时校验将在后续版本中完善。
 */
import type { TsSlnConfig } from '../types.js'

/** 配置文件名 */
export const CONFIG_FILENAME = 'tsm.config.json'

/** 支持的配置文件格式 */
export const SUPPORTED_CONFIG_FORMATS = ['json'] as const

/** 配置校验结果 */
export interface ConfigValidationResult {
  valid: boolean
  errors: string[]
  warnings: string[]
}

/**
 * 校验配置
 */
export function validateConfig(config: unknown): ConfigValidationResult {
  const errors: string[] = []
  const warnings: string[] = []

  if (!config || typeof config !== 'object') {
    errors.push('配置必须是一个对象')
    return { valid: false, errors, warnings }
  }

  const cfg = config as Record<string, unknown>

  // 校验 sources
  if (cfg.sources !== undefined) {
    if (!Array.isArray(cfg.sources)) {
      errors.push('sources 必须是数组')
    } else {
      for (let i = 0; i < cfg.sources.length; i++) {
        const source = cfg.sources[i] as Record<string, unknown>
        if (!source.type || !['sln', 'config', 'glob'].includes(source.type as string)) {
          errors.push(`sources[${i}].type 必须是 "sln" | "config" | "glob"`)
        }
      }
    }
  }

  // 校验 builders
  if (cfg.builders !== undefined) {
    if (typeof cfg.builders !== 'object' || cfg.builders === null) {
      errors.push('builders 必须是对象')
    } else {
      for (const [name, builder] of Object.entries(cfg.builders as Record<string, unknown>)) {
        const b = builder as Record<string, unknown>
        if (!b.module || typeof b.module !== 'string') {
          errors.push(`builders.${name}.module 必须是字符串`)
        }
      }
    }
  }

  // 校验 format
  if (cfg.format !== undefined && !['text', 'json', 'silent'].includes(cfg.format as string)) {
    warnings.push('format 应为 "text" | "json" | "silent"，将使用默认值 "text"')
  }

  return {
    valid: errors.length === 0,
    errors,
    warnings,
  }
}
