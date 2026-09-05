#!/usr/bin/env node

/**
 * TsSln CLI — TypeScript 项目管理工具（桥接 C# .sln/.csproj 构建管理）
 *
 * 桥接映射:
 *   dotnet build              → tsm build
 *   dotnet build --force      → tsm build --force
 *   dotnet clean              → tsm clean
 *   dotnet rebuild            → tsm rebuild
 *   dotnet build <proj>       → tsm build <name>
 *   dotnet build --no-deps    → tsm build <name> --no-deps
 *
 * 用法:
 *   tsm build                    构建所有项目
 *   tsm build <name>             构建指定项目
 *   tsm build --force            强制重新构建（先 clean 再 build）
 *   tsm build <name> --no-deps   只构建指定项目，不构建依赖
 *   tsm clean                    清理所有项目
 *   tsm rebuild                  重新构建所有项目（clean + build）
 *   tsm rebuild <name>           重新构建指定项目
 *   tsm list                     列出所有项目
 *   tsm info                     显示项目信息
 *   tsm watch                    监听模式（开发中）
 */

import { resolve, dirname } from 'path'
import { fileURLToPath } from 'url'
import { existsSync, readdirSync } from 'fs'
import { parseSln } from './parser/slnParser.js'
import { parseCsproj } from './parser/csprojParser.js'
import { buildAll, buildProject, cleanAll, rebuildAll, rebuildProject } from './tasks/index.js'
import { builderRegistry } from './builder/index.js'
import { topologicalSort } from './utils/graph.js'
import { findFileUpward } from './utils/path.js'
import { loadConfig } from './config/index.js'
import { installAllDependencies, installProjectDependencies } from './utils/dependencyInstaller.js'
import type { CliCommand, TsProject, BuildOptions } from './types.js'
import type { InstallOptions } from './utils/dependencyInstaller.js'


const __filename = fileURLToPath(import.meta.url)
const __dirname = dirname(__filename)

/**
 * 从当前脚本目录向上查找 TypeScript.sln 文件
 * 编译后 cli.js 在 build/TsSln/，源码在 TsSln/src/
 * 从 __dirname 开始向上逐级查找，直到找到 .sln 文件或到达根目录
 * 复用 utils/path.ts 中的 findFileUpward 通用工具
 */
function findSlnDir(): string {
  // 优先从脚本目录向上查找
  const slnPath = findFileUpward('TypeScript.sln', __dirname)
  if (slnPath) {
    return dirname(slnPath)
  }
  // fallback: 尝试当前工作目录
  const cwdSln = resolve(process.cwd(), 'TypeScript.sln')
  if (existsSync(cwdSln)) {
    return process.cwd()
  }
  console.error(`❌ 找不到 TypeScript.sln`)
  console.error(`   从 ${__dirname} 向上查找未找到`)
  console.error(`   当前工作目录: ${process.cwd()}`)
  process.exit(1)
}

const prjDir = findSlnDir()
const slnPath = resolve(prjDir, 'TypeScript.sln')

/** 解析命令行参数中的选项 */
function parseBuildOptions(args: string[]): { projectName?: string; options: BuildOptions } {
  const options: BuildOptions = {}
  let projectName: string | undefined

  for (let i = 0; i < args.length; i++) {
    const arg = args[i]
    if (arg === '--force' || arg === '-f') {
      options.force = true
    } else if (arg === '--verbose' || arg === '-v') {
      options.verbose = true
    } else if (arg === '--no-deps') {
      options.noDeps = true
    } else if (arg.startsWith('--config=') || arg.startsWith('-c=')) {
      options.configuration = arg.split('=')[1]
    } else if (arg === '--config' || arg === '-c') {
      // 读取下一个参数作为配置名
      if (i + 1 < args.length && !args[i + 1].startsWith('-')) {
        options.configuration = args[++i]
      }
    } else if (!arg.startsWith('-')) {
      projectName = arg
    }
  }

  return { projectName, options }
}

/**
 * 查找项目，未找到时打印错误并退出
 * 提取公共逻辑消除重复
 */
function findProjectOrExit(projects: TsProject[], name: string): TsProject {
  const project = projects.find((p: TsProject) => p.name === name)
  if (!project) {
    console.error(`❌ 未找到项目: ${name}`)
    console.log(`   可用项目: ${projects.map((p: TsProject) => p.name).join(', ')}`)
    process.exit(1)
  }
  return project
}

/**
 * 检查是否已通过 tsc --build 构建过（防重复构建检测）
 * 当通过 dotnet build TypeScript.sln 触发时，MSBuild 会逐个构建每个 .csproj，
 * 每个 .csproj 的 TsBridge 都会调用 tsm build <ProjectName>。
 * 但 tsc --build 是一次性构建所有项目的，所以只需执行一次。
 * 返回 true 表示已构建过，应跳过。
 */
function isAlreadyBuiltByTsc(slnDir: string): boolean {
  const tsWorkspaceBuild = resolve(slnDir, 'TsWorkspace', 'build')
  if (existsSync(tsWorkspaceBuild)) {
    const buildInfoFiles = readdirSync(tsWorkspaceBuild).filter(f => f.endsWith('.tsbuildinfo'))
    return buildInfoFiles.length > 0
  }
  return false
}

async function main(): Promise<void> {
  const args = process.argv.slice(2)
  const command = (args[0] || 'build') as CliCommand

  // 加载配置（可选，不影响现有功能）
  const { config, configPath, warnings } = loadConfig(prjDir)
  if (configPath) {
    console.log(`📄 配置文件: ${configPath}`)
  }
  for (const warn of warnings) {
    console.warn(`⚠️  ${warn}`)
  }

  console.log(`📂 解决方案: ${slnPath}`)

  const { slnDir, projects } = parseSln(slnPath)

  // 解析每个项目的 .csproj
  for (const project of projects) {
    parseCsproj(project)
  }

  switch (command) {
    case 'build': {
      const { projectName, options } = parseBuildOptions(args.slice(1))

      if (projectName) {
        const project = findProjectOrExit(projects, projectName)

        // --force 优先处理：先 clean 再 build（修复 #4: --no-deps 与 --force 交互）
        if (options.force) {
          console.log(`\n🔧 强制重新构建: ${project.name}`)
          builderRegistry.clean(config.defaultBuilder || 'tsc', project, slnDir)
        }

        // 防重复构建检测（修复 #1: 改为跳过而非 process.exit(0)）
        if (!options.force && config.defaultBuilder === 'tsc' && isAlreadyBuiltByTsc(slnDir)) {
          console.log(`   ⏭️  ${project.name} — 已构建，跳过（使用 --force 强制重新构建）`)
          process.exit(0)
        }

        // --no-deps: 只构建指定项目，不构建依赖
        if (options.noDeps) {
          const result = await buildProject(project, slnDir, config.defaultBuilder, options)
          process.exit(result.success ? 0 : 1)
        }

        const result = await buildProject(project, slnDir, config.defaultBuilder, options)
        process.exit(result.success ? 0 : 1)
      } else {
        // --force: 先 clean 所有再 build 所有
        if (options.force) {
          console.log('\n🔧 强制重新构建所有项目...')
          await cleanAll(projects, slnDir, config.defaultBuilder)
        }
        const results = await buildAll(projects, slnDir, config.defaultBuilder, options, config)
        const hasFailures = results.some((r) => !r.success)
        process.exit(hasFailures ? 1 : 0)
      }
      break
    }

    case 'clean':
      await cleanAll(projects, slnDir, config.defaultBuilder)
      break

    case 'rebuild': {
      const { projectName, options } = parseBuildOptions(args.slice(1))

      if (projectName) {
        const project = findProjectOrExit(projects, projectName)

        // 防重复重建检测（修复 #1: 改为跳过而非 process.exit(0)）
        if (config.defaultBuilder === 'tsc' && isAlreadyBuiltByTsc(slnDir)) {
          console.log(`   ⏭️  ${project.name} — 已重建，跳过`)
          process.exit(0)
        }

        const result = await rebuildProject(project, slnDir, config.defaultBuilder, options)
        process.exit(result.success ? 0 : 1)
      } else {
        // 修复 #5: rebuildAll 也添加防重复检测，与单个 rebuild 行为一致
        if (config.defaultBuilder === 'tsc' && isAlreadyBuiltByTsc(slnDir)) {
          console.log('   ⏭️  所有项目已重建，跳过')
          process.exit(0)
        }
        const results = await rebuildAll(projects, slnDir, config.defaultBuilder, options, config)
        const hasFailures = results.some((r) => !r.success)
        process.exit(hasFailures ? 1 : 0)
      }
      break
    }

    case 'list':
      console.log('\n📋 项目列表:')
      for (const project of projects) {
        const deps = project.dependencies.length > 0
          ? ` → 依赖: ${project.dependencies.join(', ')}`
          : ' (无依赖)'
        console.log(`   ${project.name}${deps}`)
      }
      break

    case 'info':
      console.log(`\n📊 项目信息:`)
      console.log(`   解决方案: ${slnPath}`)
      console.log(`   项目数量: ${projects.length}`)
      console.log(`   构建顺序: ${topologicalSort(projects).map((p) => p.name).join(' → ')}`)
      break

    case 'install': {
      const installArgs = args.slice(1)
      const installOptions: InstallOptions = {
        reinstall: installArgs.includes('--reinstall') || installArgs.includes('-r'),
        verbose: installArgs.includes('--verbose') || installArgs.includes('-v'),
        skipNpm: installArgs.includes('--skip-npm'),
      }
      const projectName = installArgs.find((a) => !a.startsWith('-'))

      if (projectName) {
        const project = findProjectOrExit(projects, projectName)
        const result = installProjectDependencies(project, slnDir, installOptions)
        process.exit(result.success ? 0 : 1)
      } else {
        const results = installAllDependencies(projects, slnDir, installOptions)
        const hasFailures = results.some((r) => !r.success)
        process.exit(hasFailures ? 1 : 0)
      }
      break
    }

    case 'watch':
      console.log('\n👀 监听模式 (开发中)')
      console.log('   此功能将在后续版本中实现')
      break

    default:
      console.error(`❌ 未知命令: ${command}`)
      console.log('   可用命令: build, clean, rebuild, list, info, install, watch')
      process.exit(1)
  }
}

main()
