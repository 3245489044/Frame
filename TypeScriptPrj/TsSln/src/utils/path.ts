/**
 * 路径工具
 *
 * 提供路径解析和查找相关的工具函数。
 */
import { existsSync } from 'fs'
import { resolve, dirname } from 'path'

/**
 * 查找文件：从 startDir 开始向上查找
 * @param filename 要查找的文件名
 * @param startDir 起始目录
 * @returns 找到的绝对路径，未找到返回 undefined
 */
export function findFileUpward(filename: string, startDir: string): string | undefined {
  let current = resolve(startDir)

  while (true) {
    const candidate = resolve(current, filename)
    if (existsSync(candidate)) {
      return candidate
    }

    const parent = resolve(current, '..')
    if (parent === current) break // 到达根目录
    current = parent
  }

  return undefined
}

/**
 * 查找可执行文件路径
 * @param cmd 命令名称
 * @param searchDirs 搜索目录列表
 * @returns 找到的路径，未找到返回 undefined
 */
export function findExecutable(cmd: string, searchDirs: string[]): string | undefined {
  for (const dir of searchDirs) {
    const candidate = resolve(dir, cmd)
    if (existsSync(candidate)) {
      return candidate
    }
    // Windows 下尝试添加 .cmd 或 .exe 后缀
    const withExt = resolve(dir, `${cmd}.cmd`)
    if (existsSync(withExt)) return withExt
    const withExe = resolve(dir, `${cmd}.exe`)
    if (existsSync(withExe)) return withExe
  }
  return undefined
}

/**
 * 获取项目根目录
 * 从当前文件路径向上查找，找到包含 package.json 的目录
 */
export function findProjectRoot(startDir: string): string {
  const pkgPath = findFileUpward('package.json', startDir)
  return pkgPath ? dirname(pkgPath) : startDir
}

/**
 * 确保路径使用正斜杠
 */
export function toForwardSlashes(path: string): string {
  return path.replace(/\\/g, '/')
}
