/**
 * 任务调度器
 *
 * 负责任务的调度和执行，包括：
 *   1. 按依赖顺序执行构建任务
 *   2. 收集和汇总结果
 *   3. 支持构建全部或指定项目
 *   4. 集成插件系统生命周期
 *   5. 执行配置中的 hooks
 */
import { execSync } from 'child_process'
import type { TsProject, BuildResult, BuildOptions, TsSlnConfig } from '../types.js'
import { builderRegistry } from '../builder/index.js'
import { topologicalSort } from '../utils/graph.js'
import { pluginManager } from '../plugins/index.js'

/** 打印构建结果汇总 */
function printBuildSummary(results: BuildResult[]): void {
  const successCount = results.filter((r) => r.success).length
  const failCount = results.filter((r) => !r.success).length

  console.log('\n' + '='.repeat(50))
  console.log('📊 构建结果汇总:')
  console.log('='.repeat(50))

  for (const result of results) {
    if (result.success) {
      const duration = result.duration ? ` (${result.duration}ms)` : ''
      console.log(`  ✅ ${result.name} — 构建成功${duration}`)
    } else {
      const errInfo = result.errorCount ? ` (${result.errorCount} errors)` : ''
      console.log(`  ❌ ${result.name} — 构建失败${errInfo}`)
    }
  }

  console.log('-'.repeat(50))
  console.log(`  结果: ${successCount}/${results.length} 成功, ${failCount}/${results.length} 失败`)
  console.log('='.repeat(50))
}

/**
 * 执行配置中的 hooks
 * @param hooks 钩子命令列表
 * @param slnDir 解决方案目录
 */
function executeHooks(hooks: string[] | undefined, slnDir: string): void {
  if (!hooks || hooks.length === 0) return
  for (const hook of hooks) {
    try {
      console.log(`  ⚡ 执行钩子: ${hook}`)
      execSync(hook, { cwd: slnDir, stdio: 'pipe', encoding: 'utf-8' })
    } catch (error: any) {
      console.warn(`  ⚠️  钩子执行失败: ${hook} — ${error.message || error}`)
    }
  }
}

/**
 * 构建所有项目（逐个构建，收集所有错误）
 * @param projects 项目列表
 * @param slnDir 解决方案目录
 * @param builderName 构建器名称
 * @param options 构建选项
 */
export async function buildAll(
  projects: TsProject[],
  slnDir: string,
  builderName: string = 'tsc',
  options?: BuildOptions,
  config?: TsSlnConfig,
): Promise<BuildResult[]> {
  const sorted = topologicalSort(projects)
  const results: BuildResult[] = []

  console.log(`📋 构建顺序: ${sorted.map((p) => p.name).join(' → ')}`)

  // 执行 preBuild hooks
  if (config?.hooks?.preBuild) {
    console.log('\n⚡ 执行构建前钩子...')
    executeHooks(config.hooks.preBuild, slnDir)
  }

  // 构建策略：
  // tsc --build 使用根 tsconfig.json 一次性构建所有项目（自动处理依赖顺序）。
  // 因此只需对第一个项目调用构建，其余项目标记为 "skipped"（由同一构建覆盖）。
  // 如果使用其他构建器（非 tsc），则逐个构建。
  // 修复 #2: 当第一个项目失败时，后续项目标记为 "failed" 而非 "success"，确保结果一致。
  const isTscBuilder = builderName === 'tsc'
  let firstBuildFailed = false

  for (let i = 0; i < sorted.length; i++) {
    const project = sorted[i]

    // 插件 preBuild 钩子
    await pluginManager.runPreBuild(project)

    if (isTscBuilder && i > 0) {
      // tsc --build 已一次性构建所有项目，跳过后续项目
      // 修复 #2: 如果第一个项目构建失败，后续项目也标记为失败
      if (firstBuildFailed) {
        console.log(`   ❌ ${project.name} — 因前置构建失败而跳过`)
        results.push({
          name: project.name,
          success: false,
          error: '前置构建失败',
          duration: 0,
        })
      } else {
        console.log(`   ⏭️  ${project.name} — 已由 tsc --build 覆盖`)
        results.push({
          name: project.name,
          success: true,
          duration: 0,
        })
      }
    } else {
      const result = builderRegistry.build(builderName, project, slnDir, options)
      if (!result.success) {
        firstBuildFailed = true
      }
      results.push(result)
    }

    // 插件 postBuild 钩子
    await pluginManager.runPostBuild(project, results[results.length - 1])
  }


  // 执行 postBuild hooks
  if (config?.hooks?.postBuild) {
    console.log('\n⚡ 执行构建后钩子...')
    executeHooks(config.hooks.postBuild, slnDir)
  }

  // 打印汇总
  printBuildSummary(results)

  return results
}

/**
 * 构建指定项目
 * @param project 项目
 * @param slnDir 解决方案目录
 * @param builderName 构建器名称
 * @param options 构建选项
 */
export async function buildProject(
  project: TsProject,
  slnDir: string,
  builderName: string = 'tsc',
  options?: BuildOptions,
): Promise<BuildResult> {
  // 插件 preBuild 钩子
  await pluginManager.runPreBuild(project)

  const result = builderRegistry.build(builderName, project, slnDir, options)

  // 插件 postBuild 钩子
  await pluginManager.runPostBuild(project, result)

  return result
}

/**
 * 清理所有项目
 * @param projects 项目列表
 * @param slnDir 解决方案目录
 * @param builderName 构建器名称
 */
export async function cleanAll(
  projects: TsProject[],
  slnDir: string,
  builderName: string = 'tsc',
): Promise<void> {
  console.log(`\n🧹 清理所有项目...`)

  // 使用 builderRegistry 逐个项目清理（按依赖顺序）
  const sorted = topologicalSort(projects)
  for (const project of sorted) {
    // 插件 preClean 钩子
    await pluginManager.runPreClean(project)

    builderRegistry.clean(builderName, project, slnDir)

    // 插件 postClean 钩子
    await pluginManager.runPostClean(project)
  }
  console.log('\n🧹 全部清理完成')
}

/**
 * 重新构建所有项目（桥接 dotnet rebuild）
 * 等价于 clean + build
 * @param projects 项目列表
 * @param slnDir 解决方案目录
 * @param builderName 构建器名称
 * @param options 构建选项
 */
export async function rebuildAll(
  projects: TsProject[],
  slnDir: string,
  builderName: string = 'tsc',
  options?: BuildOptions,
  config?: TsSlnConfig,
): Promise<BuildResult[]> {
  console.log(`\n🔄 重新构建所有项目...`)
  await cleanAll(projects, slnDir, builderName)
  return await buildAll(projects, slnDir, builderName, options, config)
}

/**
 * 重新构建指定项目（桥接 dotnet rebuild <project>）
 * 等价于 clean + build
 * @param project 项目
 * @param slnDir 解决方案目录
 * @param builderName 构建器名称
 * @param options 构建选项
 */
export async function rebuildProject(
  project: TsProject,
  slnDir: string,
  builderName: string = 'tsc',
  options?: BuildOptions,
): Promise<BuildResult> {
  console.log(`\n🔄 重新构建: ${project.name}`)
  // 插件 preClean 钩子
  await pluginManager.runPreClean(project)
  builderRegistry.clean(builderName, project, slnDir)
  // 插件 postClean 钩子
  await pluginManager.runPostClean(project)

  // 插件 preBuild 钩子
  await pluginManager.runPreBuild(project)
  const result = builderRegistry.build(builderName, project, slnDir, options)
  // 插件 postBuild 钩子
  await pluginManager.runPostBuild(project, result)

  return result
}



