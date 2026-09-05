/**
 * 插件系统
 *
 * 提供插件注册、管理和生命周期钩子功能。
 * 插件可以在构建/清理的不同阶段执行自定义逻辑。
 */
import type { Plugin, PluginContext, TsProject, BuildResult } from '../types.js'

/** 插件管理器 */
class PluginManager {
  private plugins: Plugin[] = []

  /** 注册插件 */
  register(plugin: Plugin): void {
    if (this.plugins.find((p) => p.name === plugin.name)) {
      console.warn(`⚠️  插件 "${plugin.name}" 已存在，将被覆盖`)
      this.plugins = this.plugins.filter((p) => p.name !== plugin.name)
    }
    this.plugins.push(plugin)
  }

  /** 获取所有已注册的插件名称 */
  getPluginNames(): string[] {
    return this.plugins.map((p) => p.name)
  }

  /** 初始化所有插件 */
  async initAll(context: PluginContext): Promise<void> {
    for (const plugin of this.plugins) {
      if (plugin.init) {
        try {
          await plugin.init(context)
        } catch (error) {
          console.error(`❌ 插件 "${plugin.name}" 初始化失败:`, error)
        }
      }
    }
  }

  /**
   * 执行插件钩子（通用方法，消除重复）
   * @param hookName 钩子名称（用于错误提示）
   * @param hookFn 从插件中获取钩子函数
   * @param args 传递给钩子函数的参数
   */
  private async runHook(
    hookName: string,
    hookFn: (plugin: Plugin) => ((...args: any[]) => void | Promise<void>) | undefined,
    ...args: any[]
  ): Promise<void> {
    for (const plugin of this.plugins) {
      const fn = hookFn(plugin)
      if (fn) {
        try {
          await fn(...args)
        } catch (error) {
          console.error(`❌ 插件 "${plugin.name}" ${hookName} 钩子执行失败:`, error)
        }
      }
    }
  }

  /** 执行构建前钩子 */
  async runPreBuild(project: TsProject): Promise<void> {
    await this.runHook('preBuild', (p) => p.preBuild, project)
  }

  /** 执行构建后钩子 */
  async runPostBuild(project: TsProject, result: BuildResult): Promise<void> {
    await this.runHook('postBuild', (p) => p.postBuild, project, result)
  }

  /** 执行清理前钩子 */
  async runPreClean(project: TsProject): Promise<void> {
    await this.runHook('preClean', (p) => p.preClean, project)
  }

  /** 执行清理后钩子 */
  async runPostClean(project: TsProject): Promise<void> {
    await this.runHook('postClean', (p) => p.postClean, project)
  }
}

/** 全局插件管理器实例 */
export const pluginManager = new PluginManager()
