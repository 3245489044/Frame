/**
 * 图算法工具
 *
 * 提供拓扑排序等图论算法，用于按依赖顺序处理项目。
 */
import type { TsProject } from '../types.js'

/**
 * 拓扑排序：按依赖关系排序，依赖者在前
 * @param projects 项目列表
 * @returns 按依赖顺序排序后的项目列表
 * @throws 检测到循环依赖时抛出错误
 */
export function topologicalSort(projects: TsProject[]): TsProject[] {
  const visited = new Set<string>()
  const sorted: TsProject[] = []
  const projectMap = new Map(projects.map((p) => [p.name, p]))

  function visit(name: string, path: Set<string> = new Set()): void {
    if (path.has(name)) {
      throw new Error(`检测到循环依赖: ${[...path, name].join(' → ')}`)
    }
    if (visited.has(name)) return
    visited.add(name)

    const project = projectMap.get(name)
    if (!project) return

    path.add(name)
    for (const dep of project.dependencies) {
      visit(dep, path)
    }
    path.delete(name)
    sorted.push(project)
  }

  for (const project of projects) {
    if (!visited.has(project.name)) {
      visit(project.name)
    }
  }

  return sorted
}

/**
 * 获取项目的所有传递依赖（包括间接依赖）
 * @param projectName 项目名称
 * @param projects 所有项目列表
 * @returns 所有依赖的项目名称列表（包括间接依赖）
 */
export function getAllDependencies(projectName: string, projects: TsProject[]): string[] {
  const projectMap = new Map(projects.map((p) => [p.name, p]))
  const deps = new Set<string>()

  function collect(name: string): void {
    const project = projectMap.get(name)
    if (!project) return
    for (const dep of project.dependencies) {
      if (!deps.has(dep)) {
        deps.add(dep)
        collect(dep)
      }
    }
  }

  collect(projectName)
  return [...deps]
}

/**
 * 获取受变更影响的项目列表
 * @param changedProject 变更的项目名称
 * @param projects 所有项目列表
 * @returns 需要重新构建的项目列表（包括变更项目本身和依赖它的项目）
 */
export function getAffectedProjects(changedProject: string, projects: TsProject[]): TsProject[] {
  const affected = new Set<string>()
  affected.add(changedProject)

  // 查找所有直接或间接依赖变更项目的项目
  for (const project of projects) {
    const deps = getAllDependencies(project.name, projects)
    if (deps.includes(changedProject)) {
      affected.add(project.name)
    }
  }

  return projects.filter((p) => affected.has(p.name))
}
