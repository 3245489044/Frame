/**
 * DependencyInstaller — 依赖安装器
 *
 * 现在委托给 npm workspace + Turborepo 处理依赖安装。
 * 保留此模块作为 CLI 入口，实际安装由 npm install 完成。
 *
 * 用法:
 *   tsm install                    安装所有项目的依赖（委托 npm install）
 *   tsm install --verbose          显示详细输出
 *   tsm install --reinstall        重新安装（先删除所有 node_modules）
 */

import { existsSync, rmSync } from 'fs'
import { resolve } from 'path'
import { execSync } from 'child_process'
import type { TsProject } from '../types.js'

// ============================================================
// 类型定义
// ============================================================

/** 依赖安装结果 */
export interface InstallResult {
  name: string
  success: boolean
  error?: string
}

/** 依赖安装选项 */
export interface InstallOptions {
  /** 是否重新安装（先删除所有 node_modules） */
  reinstall?: boolean
  /** 是否显示详细输出 */
  verbose?: boolean
  /** 是否跳过外部 npm 安装 */
  skipNpm?: boolean
}

// ============================================================
// 核心逻辑
// ============================================================

/**
 * 获取 npm workspace 根目录（TypeScriptPrj/）
 */
function getWorkspaceRoot(slnDir: string): string {
  // slnDir 是 TypeScriptPrj/，根 package.json 就在那里
  return slnDir
}

/**
 * 安装所有项目的依赖（委托给 npm workspace）
 */
export function installAllDependencies(
  projects: TsProject[],
  slnDir: string,
  options: InstallOptions = {},
): InstallResult[] {
  const workspaceRoot = getWorkspaceRoot(slnDir)

  console.log(`📂 工作区根目录: ${workspaceRoot}`)
  console.log(`📋 项目数: ${projects.length}`)
  console.log('')

  // --reinstall: 先删除所有 node_modules
  if (options.reinstall) {
    console.log('🧹 正在清理所有 node_modules...')
    for (const project of projects) {
      const nmPath = resolve(project.projectDir, 'node_modules')
      if (existsSync(nmPath)) {
        try {
          rmSync(nmPath, { recursive: true, force: true })
        } catch {
          // 忽略
        }
      }
    }
    // 也清理根 node_modules
    const rootNm = resolve(workspaceRoot, 'node_modules')
    if (existsSync(rootNm)) {
      try {
        rmSync(rootNm, { recursive: true, force: true })
      } catch {
        // 忽略
      }
    }
    console.log('   ✅ 清理完成')
    console.log('')
  }

  // 委托给 npm install（npm workspace 自动处理符号链接）
  console.log('📦 正在安装依赖（npm workspace）...')
  if (options.verbose) {
    console.log('   命令: npm install')
    console.log('')
  }

  try {
    const stdio = options.verbose ? 'inherit' : 'pipe'
    execSync('npm install', {
      cwd: workspaceRoot,
      stdio,
      encoding: 'utf-8',
    })

    console.log('')
    console.log('='.repeat(50))
    console.log('📊 依赖安装汇总:')
    console.log('='.repeat(50))
    console.log(`  结果: ${projects.length}/${projects.length} 成功`)
    console.log('  方式: npm workspace (Turborepo)')
    console.log('='.repeat(50))

    return projects.map((p) => ({
      name: p.name,
      success: true,
    }))
  } catch (err: any) {
    console.error(`\n❌ npm install 失败: ${err.message || err}`)
    return projects.map((p) => ({
      name: p.name,
      success: false,
      error: err.message || String(err),
    }))
  }
}

/**
 * 安装指定项目的依赖（委托给 npm workspace）
 */
export function installProjectDependencies(
  project: TsProject,
  slnDir: string,
  options: InstallOptions = {},
): InstallResult {
  // 单个项目安装也委托给 npm install（workspace 会自动处理）
  const results = installAllDependencies([project], slnDir, options)
  return results[0]
}

/**
 * 检查项目的依赖是否已安装
 */
export function checkDependencies(project: TsProject): {
  installed: boolean
  missing: string[]
} {
  const nmPath = resolve(project.projectDir, 'node_modules')
  if (!existsSync(nmPath)) {
    return { installed: false, missing: ['node_modules 不存在'] }
  }
  return { installed: true, missing: [] }
}
