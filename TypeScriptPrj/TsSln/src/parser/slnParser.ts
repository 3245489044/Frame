/**
 * SlnParser — 解析 .sln 文件
 *
 * .sln 文件格式：
 *   Project("{type}") = "Name", "Path.csproj", "{guid}"
 *   EndProject
 *
 * 解析内容：
 *   - 项目名称
 *   - .csproj 路径
 *   - GUID（唯一标识）
 *   - 项目目录
 */

import { readFileSync, existsSync } from 'fs'
import { resolve, dirname } from 'path'
import type { TsProject, SlnResult } from '../types.js'


/** 解析 .sln 文件，返回项目列表 */
export function parseSln(slnPath: string): SlnResult {
  const absPath = resolve(slnPath)
  if (!existsSync(absPath)) {
    throw new Error(`.sln 文件不存在: ${absPath}`)
  }

  const content = readFileSync(absPath, 'utf-8')
  const slnDir = dirname(absPath)
  const projects: TsProject[] = []

  // 匹配 Project 行: Project("{typeGuid}") = "Name", "Path.csproj", "{projectGuid}"
  // 提取 name、csprojPath 和 projectGuid
  // 示例: Project("{FAE04EC0...}") = "BasicMethod", "TsWorkspace\...csproj", "{A100...}"
  const projectRegex = /Project\("\{([^}]+)\}"\)\s*=\s*"([^"]+)",\s*"([^"]+\.csproj)",\s*"\{([^}]+)\}"/
  // match[1] = 项目类型GUID, match[2] = 项目名称, match[3] = .csproj路径, match[4] = 项目GUID
  const lines = content.split('\n')

  for (const line of lines) {
    const match = line.match(projectRegex)
    if (match) {
      const guid = match[4]    // 项目 GUID（唯一标识）
      const name = match[2]
      const csprojPath = match[3].replace(/\\/g, '/')
      const projectDir = resolve(slnDir, dirname(csprojPath))

      projects.push({
        name,
        csprojPath,
        guid,
        dependencies: [],
        // outDir 由 csprojParser 从 .csproj 的 <OutDir> 中解析
        outDir: '',
        projectDir,
      })
    }
  }

  return { slnDir, projects }
}
