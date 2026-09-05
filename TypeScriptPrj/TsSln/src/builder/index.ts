/**
 * 构建器注册表
 *
 * 管理所有注册的构建器，提供构建器的查找和调用功能。
 */
import type { Builder, TsProject, BuildResult, BuildOptions } from '../types.js'
import { TscBuilder } from './tscBuilder.js'

/** 构建器注册表 */
class BuilderRegistry {
  private builders = new Map<string, Builder>()

  constructor() {
    // 注册默认的 tsc 构建器
    this.register('tsc', new TscBuilder())
  }

  /** 注册构建器 */
  register(name: string, builder: Builder): void {
    if (this.builders.has(name)) {
      console.warn(`⚠️  构建器 "${name}" 已存在，将被覆盖`)
    }
    this.builders.set(name, builder)
  }

  /** 获取构建器 */
  get(name: string): Builder | undefined {
    return this.builders.get(name)
  }

  /** 获取所有已注册的构建器名称 */
  getRegisteredNames(): string[] {
    return [...this.builders.keys()]
  }

  /** 使用指定构建器构建项目 */
  build(
    builderName: string,
    project: TsProject,
    slnDir: string,
    options?: BuildOptions,
  ): BuildResult {
    const builder = this.get(builderName)
    if (!builder) {
      return {
        name: project.name,
        success: false,
        error: `未找到构建器 "${builderName}"，可用构建器: ${this.getRegisteredNames().join(', ')}`,
      }
    }
    return builder.build(project, slnDir, options)
  }

  /** 使用指定构建器清理项目 */
  clean(builderName: string, project: TsProject, slnDir: string): void {
    const builder = this.get(builderName)
    if (!builder) {
      console.error(`❌ 未找到构建器 "${builderName}"`)
      return
    }
    builder.clean(project, slnDir)
  }
}

/** 全局构建器注册表实例 */
export const builderRegistry = new BuilderRegistry()
