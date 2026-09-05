/**
 * 默认配置
 */
import type { TsSlnConfig } from '../types.js'

/** 默认配置 */
export const defaultConfig: TsSlnConfig = {
  sources: [{ type: 'sln', path: 'TypeScript.sln' }],
  defaultBuilder: 'tsc',
  builders: {
    tsc: { module: 'tscBuilder' },
  },
  format: 'text',
  verbose: false,
}
