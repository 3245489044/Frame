/**
 * CsprojParser — 解析 .csproj 文件
 *
 * 从 .csproj 中提取：
 *   - ProjectReference → 项目依赖
 *   - OutDir → 输出目录
 *
 * .csproj 文件位于 TsWorkspace/<ProjectName>/<ProjectName>.csproj
 */

import { readFileSync, existsSync } from 'fs'
import { resolve } from 'path'
import type { TsProject } from '../types.js'


/** 解析 .csproj 文件，填充依赖和配置信息 */
export function parseCsproj(project: TsProject): void {
  // .csproj 文件路径：projectDir/<ProjectName>.csproj
  const csprojFile = resolve(project.projectDir, `${project.name}.csproj`)

  if (!existsSync(csprojFile)) {
    // .csproj 文件不存在，使用默认值
    project.dependencies = []
    project.outDir = ''
    return
  }

  const content = readFileSync(csprojFile, 'utf-8')

  // 提取 ProjectReference
  const refRegex = /<ProjectReference\s+Include="([^"]+)"/g
  const refMatches = content.matchAll(refRegex)
  project.dependencies = []
  for (const match of refMatches) {
    const refPath = match[1].replace(/\\/g, '/')
    // 从引用路径中提取项目名称（去掉 .csproj 后缀）
    // 例如 "../BasicMethod/BasicMethod.csproj" → "BasicMethod"
    const refName = refPath.split('/').pop()?.replace('.csproj', '') || ''
    if (refName) {
      project.dependencies.push(refName)
    }
  }

  // 提取 OutDir（注意：实际 tsc 输出由 tsconfig.json 的 outDir 控制）
  const outDirMatch = content.match(/<OutDir>([^<]+)<\/OutDir>/)
  project.outDir = outDirMatch ? outDirMatch[1] : ''
}
