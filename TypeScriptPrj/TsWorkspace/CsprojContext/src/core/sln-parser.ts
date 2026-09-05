import * as fs from "fs";
import * as path from "path";

/**
 * .sln 文件中的项目条目
 */
export interface SlnProject {
  /** 项目名称（如 "BasicMethod"） */
  name: string;
  /** 项目路径（相对于 .sln 所在目录，如 "BasicMethod\\BasicMethod.csproj"） */
  path: string;
  /** 项目类型 GUID */
  typeGuid: string;
  /** 是否为解决方案文件夹（非 .csproj 项目） */
  isFolder: boolean;
  /** 父文件夹 GUID（嵌套项目用） */
  parentFolder?: string;
  /** 项目 GUID */
  guid: string;
}

/**
 * 解析 .sln 文件，提取所有项目条目
 *
 * .sln 格式示例：
 *   Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "BasicMethod", "BasicMethod\BasicMethod.csproj", "{30383259-20CD-4A50-BD5F-70040DF8AEFD}"
 *   EndProject
 *
 * 解决方案文件夹：
 *   Project("{2150E333-8FDC-42A3-9474-1A3956D46DE8}") = "PythonModel", "PythonModel", "{2CA76A81-61CE-43EC-8C4B-981D7C1D8398}"
 *   EndProject
 */
export function parseSlnFile(slnPath: string): SlnProject[] {
  const resolvedPath = path.resolve(slnPath);

  if (!fs.existsSync(resolvedPath)) {
    throw new Error(`.sln 文件不存在: ${resolvedPath}`);
  }

  const content = fs.readFileSync(resolvedPath, "utf-8");
  const projects: SlnProject[] = [];

  // 解决方案文件夹的 GUID
  const solutionFolderGuid = "{2150E333-8FDC-42A3-9474-1A3956D46DE8}";

  // 正则匹配 Project 行
  // Project("{typeGuid}") = "name", "path", "{guid}"
  const projectRegex = /^Project\("(\{[A-F0-9-]+\})"\)\s*=\s*"([^"]+)",\s*"([^"]+)",\s*"(\{[A-F0-9-]+\})"\s*$/gim;

  let match: RegExpExecArray | null;
  while ((match = projectRegex.exec(content)) !== null) {
    const typeGuid = match[1].toUpperCase();
    const name = match[2];
    const projectPath = match[3];
    const guid = match[4].toUpperCase();

    const isFolder = typeGuid === solutionFolderGuid;

    projects.push({
      name,
      path: projectPath,
      typeGuid,
      isFolder,
      guid,
    });
  }

  // 解析嵌套关系（NestedProjects 节）
  // {childGUID} = {parentGUID}
  const nestedSectionMatch = content.match(
    /GlobalSection\(NestedProjects\)\s*=\s*preSolution\s*([\s\S]*?)EndGlobalSection/i
  );

  if (nestedSectionMatch) {
    const nestedContent = nestedSectionMatch[1];
    const nestedRegex = /\{([A-F0-9-]+)\}\s*=\s*\{([A-F0-9-]+)\}/gi;
    let nestedMatch: RegExpExecArray | null;
    while ((nestedMatch = nestedRegex.exec(nestedContent)) !== null) {
      const childGuid = `{${nestedMatch[1].toUpperCase()}}`;
      const parentGuid = `{${nestedMatch[2].toUpperCase()}}`;
      const childProject = projects.find((p) => p.guid === childGuid);
      if (childProject) {
        childProject.parentFolder = parentGuid;
      }
    }
  }

  return projects;
}

/**
 * 规范化路径分隔符（将反斜杠统一替换为正斜杠）
 * 解决 .sln 文件中 Windows 反斜杠路径在 Docker Linux 容器中的兼容性问题
 */
function normalizePath(p: string): string {
  return p.replace(/\\/g, '/');
}

/**
 * 从 .sln 文件中提取所有 .csproj 项目路径
 *
 * @param slnPath .sln 文件路径
 * @param relativeTo 返回路径相对于哪个目录（默认：.sln 所在目录）
 * @returns .csproj 路径列表（相对于 relativeTo，统一使用正斜杠）
 */
export function getCsprojPathsFromSln(
  slnPath: string,
  relativeTo?: string
): string[] {
  const projects = parseSlnFile(slnPath);
  const slnDir = path.dirname(path.resolve(slnPath));
  const baseDir = relativeTo ? path.resolve(relativeTo) : slnDir;

  const csprojPaths: string[] = [];

  for (const proj of projects) {
    if (proj.isFolder) continue;
    if (!proj.path.endsWith(".csproj")) continue;

    // proj.path 是相对于 .sln 所在目录的（可能包含反斜杠）
    // 先规范化 proj.path 再 resolve，避免混合分隔符
    const normalizedProjPath = normalizePath(proj.path);
    const absolutePath = path.resolve(slnDir, normalizedProjPath);
    const relativePath = path.relative(baseDir, absolutePath);
    csprojPaths.push(normalizePath(relativePath));
  }

  return csprojPaths;
}

/**
 * 从 .sln 文件路径推导项目根目录
 *
 * 规则：.sln 所在目录的父目录即为项目根目录
 * 例如：CsharpPrj/MainProj.sln → CsharpPrj 的父目录 → WorkFlowPrj
 *
 * @param slnPath .sln 文件路径（绝对或相对）
 * @returns 推导出的项目根目录绝对路径
 */
export function resolveProjectRootFromSln(slnPath: string): string {
  const absoluteSlnPath = path.resolve(slnPath);
  const slnDir = path.dirname(absoluteSlnPath);
  // .sln 所在目录的父目录作为项目根目录
  return path.resolve(slnDir, "..");
}

/**
 * 验证 .sln 文件路径是否有效
 */
export function isValidSlnPath(slnPath: string): boolean {
  try {
    const resolvedPath = path.resolve(slnPath);
    if (!fs.existsSync(resolvedPath)) return false;
    const content = fs.readFileSync(resolvedPath, "utf-8");
    return content.includes("Microsoft Visual Studio Solution File");
  } catch {
    return false;
  }
}
