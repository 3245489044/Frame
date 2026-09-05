#!/usr/bin/env node

/**
 * invoke-ps-script — PowerShell 脚本调用工具
 *
 * 在宿主机上通过 pwsh 子进程执行 PowerShell 脚本/模块中的函数。
 * 不经过 Docker 容器，直接在 Windows 上执行。
 *
 * 安全限制：
 * - script_path 必须在项目根目录下（防止路径穿越攻击）
 * - 只允许执行 .ps1 和 .psm1 文件
 */

import * as fs from "fs";
import * as path from "path";
import { execSync } from "child_process";
import { loadPathsConfig } from "../utils/paths.js";

// ─── 常量 ───────────────────────────────────────────────────

/** 允许的脚本文件扩展名 */
const ALLOWED_EXTENSIONS = [".ps1", ".psm1"];

/** PowerShell 命令超时（毫秒） */
const PS_EXEC_TIMEOUT = 60_000;

// ─── 类型定义 ───────────────────────────────────────────────

export interface InvokePsScriptArgs {
  script_path: string;
  function_name?: string;
  arguments?: Record<string, unknown>;
}

// ─── 核心逻辑 ───────────────────────────────────────────────

/**
 * 验证脚本路径是否安全
 *
 * @param scriptPath 脚本文件路径（相对于项目根目录）
 * @returns 解析后的绝对路径
 * @throws 路径不安全时抛出错误
 */
function validateScriptPath(scriptPath: string): string {
  // 1. 获取项目根目录
  const cfg = loadPathsConfig();
  const projectRoot = path.resolve(cfg.projectRoot);

  // 2. 解析绝对路径
  const fullPath = path.resolve(projectRoot, scriptPath);

  // 3. 检查是否在项目根目录下（防止路径穿越）
  if (!fullPath.startsWith(projectRoot)) {
    throw new Error(
      `路径不安全: "${scriptPath}" 不在项目根目录 "${projectRoot}" 下`
    );
  }

  // 4. 检查文件扩展名
  const ext = path.extname(fullPath).toLowerCase();
  if (!ALLOWED_EXTENSIONS.includes(ext)) {
    throw new Error(
      `不支持的文件类型: "${ext}"。只允许: ${ALLOWED_EXTENSIONS.join(", ")}`
    );
  }

  // 5. 检查文件是否存在
  if (!fs.existsSync(fullPath)) {
    throw new Error(`文件未找到: ${fullPath}`);
  }

  return fullPath;
}

/**
 * 获取 PScriptManager/_init.ps1 的路径
 *
 * 通过 PScriptManager 统一加载 BasicMethod 模块，TypeScript 侧无需维护依赖拓扑。
 *
 * @param projectRoot 项目根目录
 * @returns PScriptManager/_init.ps1 的绝对路径，如果不存在则返回 null
 */
function getPScriptManagerInitPath(projectRoot: string): string | null {
  const psmInitPath = path.join(projectRoot, "PowerShellScript", "PScriptManager", "_init.ps1");
  if (fs.existsSync(psmInitPath)) {
    return psmInitPath;
  }
  return null;
}

/**
 * 判断脚本路径是否在 BasicMethod 目录下
 */
function isInBasicMethod(fullPath: string): boolean {
  return fullPath.includes("PowerShellScript\\BasicMethod") ||
         fullPath.includes("PowerShellScript/BasicMethod");
}

/**
 * 构造 PowerShell 命令
 *
 * 对于 BasicMethod 目录下的模块，自动按依赖拓扑加载所有依赖模块。
 *
 * @param fullPath 脚本文件的绝对路径
 * @param functionName 要调用的函数名（可选）
 * @param args 函数参数（可选）
 * @returns 完整的 PowerShell 命令字符串
 */
function buildPsCommand(
  fullPath: string,
  functionName?: string,
  args?: Record<string, unknown>
): string {
  const ext = path.extname(fullPath).toLowerCase();

  // 构建参数列表
  const paramStr = args
    ? Object.entries(args)
        .map(([k, v]) => {
          if (typeof v === "string") {
            return `-${k} '${v.replace(/'/g, "''")}'`;
          } else if (typeof v === "boolean") {
            return `-${k}:$${v}`;
          } else if (v === null || v === undefined) {
            return "";
          } else {
            return `-${k} (ConvertFrom-Json '${JSON.stringify(v).replace(/'/g, "''")}')`;
          }
        })
        .filter(Boolean)
        .join(" ")
    : "";

  // 判断是否需要自动加载 BasicMethod 依赖
  const needsBasicMethodInit = isInBasicMethod(fullPath) && ext === ".psm1";

  if (functionName) {
    if (needsBasicMethodInit) {
      // BasicMethod 模块：通过 PScriptManager/_init.ps1 统一加载所有依赖，
      // 然后 Import-Module 目标模块，最后调用函数。
      // TypeScript 侧无需维护依赖拓扑，全部由 PowerShell 侧的 _init.ps1 管理。
      const cfg = loadPathsConfig();
      const projectRoot = path.resolve(cfg.projectRoot);
      const psmInitPath = getPScriptManagerInitPath(projectRoot);

      const initCommand = psmInitPath
        ? `. '${psmInitPath}' -ProjectRoot '${projectRoot}'`
        : `$ErrorActionPreference = 'Stop'`;

      return [
        initCommand,
        `Import-Module '${fullPath}' -Force -ErrorAction Stop`,
        `try {`,
        `  ${functionName} ${paramStr} | ConvertTo-Json -Depth 10 -Compress`,
        `} catch {`,
        `  Write-Error "函数执行失败: $($_.Exception.Message)"`,
        `  exit 1`,
        `}`,
      ].join("; ");
    } else if (ext === ".psm1") {
      // 普通模块文件：Import-Module 后调用函数
      return [
        `$ErrorActionPreference = 'Stop'`,
        `Import-Module '${fullPath}' -Force -ErrorAction Stop`,
        `try {`,
        `  ${functionName} ${paramStr} | ConvertTo-Json -Depth 10 -Compress`,
        `} catch {`,
        `  Write-Error "函数执行失败: $($_.Exception.Message)"`,
        `  exit 1`,
        `}`,
      ].join("; ");
    } else {
      // 脚本文件：点源后调用函数
      return [
        `$ErrorActionPreference = 'Stop'`,
        `. '${fullPath}'`,
        `try {`,
        `  ${functionName} ${paramStr} | ConvertTo-Json -Depth 10 -Compress`,
        `} catch {`,
        `  Write-Error "函数执行失败: $($_.Exception.Message)"`,
        `  exit 1`,
        `}`,
      ].join("; ");
    }
  } else {
    // 直接执行脚本
    const argStr = args
      ? Object.values(args)
          .map((v) => {
            if (typeof v === "string") return `'${v.replace(/'/g, "''")}'`;
            if (typeof v === "boolean") return v ? "$true" : "$false";
            return String(v);
          })
          .join(" ")
      : "";

    return [
      `$ErrorActionPreference = 'Stop'`,
      `try {`,
      `  & '${fullPath}' ${argStr}`,
      `} catch {`,
      `  Write-Error "脚本执行失败: $($_.Exception.Message)"`,
      `  exit 1`,
      `}`,
    ].join("; ");
  }
}

/**
 * 获取可用的 PowerShell 可执行文件路径
 *
 * MCPBridge 运行在宿主机（Windows）上，优先使用 pwsh (PowerShell Core)，
 * 回退到 Windows PowerShell (powershell.exe)。
 *
 * @returns PowerShell 可执行文件路径
 */
function getPowerShellPath(): string {
  const isWin = process.platform === "win32";
  const whichCmd = isWin ? "where" : "which";

  try {
    execSync(`${whichCmd} pwsh`, { encoding: "utf-8", stdio: "ignore" });
    return "pwsh";
  } catch {
    try {
      execSync(`${whichCmd} powershell`, { encoding: "utf-8", stdio: "ignore" });
      return "powershell";
    } catch {
      throw new Error(
        "未找到 PowerShell。请安装 PowerShell Core (pwsh) 或使用 Windows 自带的 PowerShell:\n" +
        "  Windows: https://github.com/PowerShell/PowerShell/releases\n" +
        "  Linux: apt-get install -y powershell"
      );
    }
  }
}

/**
 * 执行 PowerShell 命令
 *
 * MCPBridge 运行在 Docker 容器内，直接在容器内通过 pwsh 执行 PowerShell 脚本。
 * 项目目录通过 volume 挂载到 /workspace/WorkFlowPrj，所有文件可访问。
 *
 * @param psCommand PowerShell 命令字符串
 * @returns 命令输出
 */
function executePsCommand(psCommand: string): string {
  const psPath = getPowerShellPath();

  try {
    const result = execSync(
      `${psPath} -NoProfile -NonInteractive -Command "${psCommand.replace(/"/g, '\\"')}"`,
      {
        encoding: "utf-8",
        timeout: PS_EXEC_TIMEOUT,
        maxBuffer: 10 * 1024 * 1024, // 10MB
      }
    );
    return result.trim();
  } catch (err: unknown) {
    if (err instanceof Error && "stderr" in err) {
      const execErr = err as Error & { stderr?: unknown; stdout?: unknown };
      const stderr = execErr.stderr?.toString() || "";
      const stdout = execErr.stdout?.toString() || "";
      const errorMsg = stderr || stdout || err.message;
      throw new Error(`PowerShell 执行失败: ${errorMsg.trim()}`);
    }
    throw err;
  }
}

/**
 * 调用 PowerShell 脚本/模块中的函数
 *
 * @param args 工具参数
 * @returns 执行结果文本
 */
export async function invokePsScript(args: InvokePsScriptArgs): Promise<string> {
  // 1. 验证脚本路径
  const fullPath = validateScriptPath(args.script_path);

  // 2. 构造 PowerShell 命令
  const psCommand = buildPsCommand(fullPath, args.function_name, args.arguments);

  // 3. 执行
  const output = executePsCommand(psCommand);

  // 4. 尝试解析 JSON 输出（美化显示）
  try {
    if (output.startsWith("{") || output.startsWith("[")) {
      const parsed = JSON.parse(output);
      return JSON.stringify(parsed, null, 2);
    }
  } catch {
    // 不是 JSON，原样返回
  }

  return output || "(无输出)";
}

// ─── 导出 handler（供 MCPFramework registry 使用） ──────────

export default invokePsScript;
