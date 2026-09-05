/**
 * 配置加载器
 *
 * 负责：
 *   1. 查找并加载 tsm.config.json 配置文件
 *   2. 与默认配置合并
 *   3. 校验配置有效性
 */
import { readFileSync, existsSync } from 'fs'
import { resolve } from 'path'
import type { TsSlnConfig } from '../types.js'
import { defaultConfig } from './defaults.js'
import { CONFIG_FILENAME, validateConfig } from './schema.js'
import { findFileUpward } from '../utils/path.js'

/** 配置加载结果 */
export interface ConfigLoadResult {
  /** 合并后的配置 */
  config: TsSlnConfig
  /** 配置文件路径（如果找到） */
  configPath?: string
  /** 校验警告 */
  warnings: string[]
}

/**
 * 加载配置
 * @param searchDir 开始搜索配置文件的目录
 */
export function loadConfig(searchDir: string): ConfigLoadResult {
  const warnings: string[] = []
  let userConfig: Partial<TsSlnConfig> = {}

  // 从当前目录向上查找配置文件
  const configPath = findConfigFile(searchDir)

  if (configPath) {
    try {
      const raw = JSON.parse(readFileSync(configPath, 'utf-8'))
      const validation = validateConfig(raw)

      if (!validation.valid) {
        console.error(`❌ 配置文件 ${configPath} 校验失败:`)
        for (const err of validation.errors) {
          console.error(`   - ${err}`)
        }
        console.error('   将使用默认配置')
      } else {
        userConfig = raw as Partial<TsSlnConfig>
        warnings.push(...validation.warnings)
      }
    } catch (error) {
      console.error(`❌ 配置文件 ${configPath} 解析失败:`, error)
      console.error('   将使用默认配置')
    }
  }

  // 合并配置
  const merged = mergeConfig(defaultConfig, userConfig)

  return { config: merged, configPath, warnings }
}

/**
 * 查找配置文件
 * 从 searchDir 开始向上查找，直到找到配置文件或到达根目录
 * 复用 utils/path.ts 中的 findFileUpward 通用工具
 */
function findConfigFile(startDir: string): string | undefined {
  return findFileUpward(CONFIG_FILENAME, startDir)
}

/**
 * 合并配置（深度合并）
 */
function mergeConfig(base: TsSlnConfig, override: Partial<TsSlnConfig>): TsSlnConfig {
  return {
    ...base,
    ...override,
    sources: override.sources ?? base.sources,
    builders: { ...base.builders, ...override.builders },
    hooks: override.hooks
      ? {
          ...base.hooks,
          ...override.hooks,
          preBuild: [...(base.hooks?.preBuild ?? []), ...(override.hooks.preBuild ?? [])],
          postBuild: [...(base.hooks?.postBuild ?? []), ...(override.hooks.postBuild ?? [])],
        }
      : base.hooks,
  }
}
