/**
 * TscBuilder — TypeScript 编译器构建器
 *
 * 使用 tsc 进行 TypeScript 项目的构建和清理。
 * 实现了 Builder 接口。
 *
 * 核心设计：
 *   利用 TypeScript Project References 的 --build 模式一次性构建所有项目，
 *   而非逐个构建。tsc --build 会自动处理依赖顺序、增量编译和 .tsbuildinfo 状态。
 *
 * 功能：
 *   1. 一次性构建所有项目（通过根 tsconfig.json）
 *   2. 支持构建全部或指定项目
 *   3. 收集所有错误，最后汇总报告
 *   4. 增量构建（利用 tsc --build 的 .tsbuildinfo）
 */
import { execSync } from 'child_process'
import { resolve, dirname } from 'path'
import { fileURLToPath } from 'url'
import { existsSync } from 'fs'
import type { Builder, TsProject, BuildResult, BuildOptions } from '../types.js'

const __filename = fileURLToPath(import.meta.url)
const __dirname = dirname(__filename)

/** 根 tsconfig.json 路径（相对于 slnDir） */
const ROOT_TSCONFIG_RELATIVE = 'TsWorkspace/tsconfig.json'

/** TSC 构建器 */
export class TscBuilder implements Builder {
  readonly name = 'tsc'

  /** 获取 tsc 命令路径 */
  private getTscCmd(slnDir?: string): string {
    // 查找策略（按优先级）：
    // 1. 从 slnDir/TsSln/node_modules/.bin/tsc.cmd（TsSln 自身安装）
    // 2. 从 slnDir/TsWorkspace/<子项目>/node_modules/.bin/tsc.cmd（workspace 子项目安装）
    // 3. 从脚本所在目录向上查找
    // 4. 使用 npx tsc（从 PATH 中查找）
    // 注意：Windows 上必须用 .cmd 后缀，否则 execSync 无法识别

    // 策略 1: 从 slnDir/TsSln 查找
    if (slnDir) {
      const fromTsSln = resolve(slnDir, 'TsSln', 'node_modules', '.bin', 'tsc.cmd')
      if (existsSync(fromTsSln)) return fromTsSln

      // 策略 2: 从 workspace 子项目查找（BasicMethod 等有 typescript 依赖）
      const workspaceProjects = ['BasicMethod', 'MCPFramework', 'MCPBridge', 'CsprojContext']
      for (const proj of workspaceProjects) {
        const fromWorkspace = resolve(slnDir, 'TsWorkspace', proj, 'node_modules', '.bin', 'tsc.cmd')
        if (existsSync(fromWorkspace)) return fromWorkspace
      }
    }

    // 策略 3: 从脚本位置向上查找
    let current = resolve(__dirname)
    while (true) {
      const candidate = resolve(current, 'node_modules', '.bin', 'tsc.cmd')
      if (existsSync(candidate)) return candidate
      const parent = resolve(current, '..')
      if (parent === current) break
      current = parent
    }

    // 策略 4: 回退到 npx
    return 'npx tsc'
  }

  /**
   * 判定失败是否为「陈旧增量构建状态」导致的模块解析失败（TS2307）。
   *
   * 背景：TypeScript 项目引用（project references）的 `--build` 增量模式依赖
   * `.tsbuildinfo` 判断某个引用项目是否已构建。若 `build/` 产物被清空（例如
   * `tsc --build --clean` 或手动 `rm -rf build`）但 `.tsbuildinfo` 仍残留，
   * tsc 可能跳过重建被引用项目，导致依赖方解析不到其 `main/types`（如
   * `csg-protocol` 的 `build/index.d.ts`）→ TS2307。
   *
   * 此类失败可通过一次 `tsc --build --force`（忽略增量状态全量重编译）自愈，
   * 无需人工干预。真实源码错误在 --force 重建后仍会失败并如实上报。
   */
  private isStaleIncrementalState(stderr: string): boolean {
    return /error TS2307/.test(stderr)
  }

  /** 解析 tsc 错误输出中的错误数量（支持多语言） */
  private parseErrorCount(stderr: string): number {
    // 英文: "Found 2 errors"
    const enMatch = stderr.match(/Found\s+(\d+)\s+error/i)
    if (enMatch) return parseInt(enMatch[1], 10)
    // 中文: "发现 2 个错误"
    const zhMatch = stderr.match(/发现\s+(\d+)\s+个错误/)
    if (zhMatch) return parseInt(zhMatch[1], 10)
    // 日文: "2 個のエラーが見つかりました"
    const jaMatch = stderr.match(/(\d+)\s*個のエラー/)
    if (jaMatch) return parseInt(jaMatch[1], 10)
    // 通用: "error TS" 计数
    const tsErrorCount = (stderr.match(/error TS\d+/g) || []).length
    if (tsErrorCount > 0) return tsErrorCount
    return 0
  }

  /** 获取根 tsconfig.json 路径 */
  private getRootTsconfig(slnDir: string): string {
    return resolve(slnDir, ROOT_TSCONFIG_RELATIVE)
  }

  /**
   * 构建项目
   *
   * 核心策略：使用根 tsconfig.json 一次性构建所有项目。
   * tsc --build 会自动：
   *   1. 按依赖顺序构建（拓扑排序由 tsc 内部处理）
   *   2. 增量编译（只编译变更的文件）
   *   3. 管理 .tsbuildinfo 状态
   *   4. 避免重复构建
   */
  build(project: TsProject, slnDir: string, options?: BuildOptions): BuildResult {
    const startTime = Date.now()
    const rootTsconfig = this.getRootTsconfig(slnDir)
    const tscCmd = this.getTscCmd(slnDir)

    // 构建参数：强制使用英文 locale 确保错误解析稳定
    const localeFlag = '--locale en-US'
    const buildArgs = `--build "${rootTsconfig}" ${localeFlag}`

    // --force: 先 clean 再 build
    if (options?.force) {
      console.log(`\n🔨 强制构建: ${project.name}`)
      console.log(`   根配置: ${rootTsconfig}`)
      console.log(`   先清理再构建...`)
      try {
        execSync(`"${tscCmd}" --build --clean "${rootTsconfig}"`, {
          cwd: slnDir,
          stdio: 'pipe',
          encoding: 'utf-8',
        })
      } catch {
        // clean 失败不影响 build
      }
    } else {
      console.log(`\n🔨 构建: ${project.name}`)
      console.log(`   根配置: ${rootTsconfig}`)
    }

    try {
      execSync(`"${tscCmd}" ${buildArgs}`, {
        cwd: slnDir,
        stdio: 'pipe',
        encoding: 'utf-8',
      })
      console.log(`✅ ${project.name} 构建成功`)
      return {
        name: project.name,
        success: true,
        duration: Date.now() - startTime,
      }
    } catch (error: any) {
      const stderr = error.stderr || error.stdout || error.message || '未知错误'
      const errorCount = this.parseErrorCount(stderr)

      // 陈旧增量状态自愈：TS2307 且未显式 --force 时，忽略增量状态全量重编译一次。
      // 真实源码错误在 --force 重建后仍会失败（如实上报），故不会掩盖错误。
      if (!options?.force && this.isStaleIncrementalState(stderr)) {
        console.log('')
        console.log(`  ⚙️  检测到陈旧增量状态（TS2307 模块解析失败），执行 --force 全量重编译...`)
        try {
          execSync(`"${tscCmd}" --build --force "${rootTsconfig}" ${localeFlag}`, {
            cwd: slnDir,
            stdio: 'pipe',
            encoding: 'utf-8',
          })
          console.log(`✅ ${project.name} 构建成功（--force 自愈）`)
          return {
            name: project.name,
            success: true,
            duration: Date.now() - startTime,
          }
        } catch (forceError: any) {
          const forceStderr = forceError.stderr || forceError.stdout || forceError.message || '未知错误'
          const forceErrorCount = this.parseErrorCount(forceStderr)
          const forceErrorLines = forceStderr.split('\n').filter((l: string) => l.includes('error TS'))
          console.error(`❌ ${project.name} 构建失败 (--force 重建仍失败, ${forceErrorCount > 0 ? forceErrorCount + ' errors' : '未知错误'})`)
          for (const line of forceErrorLines.slice(0, 20)) {
            console.error(`   ${line}`)
          }
          if (forceErrorLines.length > 20) {
            console.error(`   ... 还有 ${forceErrorLines.length - 20} 个错误`)
          }
          return {
            name: project.name,
            success: false,
            error: forceStderr,
            errorCount: forceErrorCount || undefined,
            duration: Date.now() - startTime,
          }
        }
      }

      // 只输出错误摘要，不输出完整堆栈
      const errorLines = stderr.split('\n').filter((l: string) => l.includes('error TS'))
      console.error(`❌ ${project.name} 构建失败 (${errorCount > 0 ? errorCount + ' errors' : '未知错误'})`)

      // 打印每个错误
      for (const line of errorLines.slice(0, 20)) {
        console.error(`   ${line}`)
      }
      if (errorLines.length > 20) {
        console.error(`   ... 还有 ${errorLines.length - 20} 个错误`)
      }

      return {
        name: project.name,
        success: false,
        error: stderr,
        errorCount: errorCount || undefined,
        duration: Date.now() - startTime,
      }
    }
  }

  /** 清理指定项目（使用根 tsconfig.json 清理） */
  clean(project: TsProject, slnDir: string): void {
    const rootTsconfig = this.getRootTsconfig(slnDir)
    const tscCmd = this.getTscCmd(slnDir)

    console.log(`\n🧹 清理: ${project.name}`)

    try {
      execSync(`"${tscCmd}" --build --clean "${rootTsconfig}"`, {
        cwd: slnDir,
        stdio: 'inherit',
      })
      console.log(`✅ ${project.name} 清理完成`)
    } catch (error) {
      console.error(`❌ ${project.name} 清理失败`)
    }
  }
}
