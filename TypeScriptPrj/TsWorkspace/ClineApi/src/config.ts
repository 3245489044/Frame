/**
 * ClineApi — 配置加载
 *
 * 配置文件：<ClineApi>/cline.config.json
 * 环境变量覆盖：
 *   CLINE_PROVIDER   覆盖默认 provider
 *   CLINE_MODEL      覆盖默认 model
 *   CLINE_CWD        覆盖默认工作目录
 *   CLINE_TIMEOUT    覆盖默认超时（秒）
 */

import * as fs from "fs";
import * as path from "path";
import type { ClineThinking } from "./types.js";
import { resolveProjectRoot } from "./credentials.js";

/** 命令权限策略（映射到 CLINE_COMMAND_PERMISSIONS 环境变量） */
export interface CommandPermissions {
  allow?: string[];
  deny?: string[];
  allowRedirects?: boolean;
}

/** ClineApi 配置结构（cline.config.json） */
export interface ClineApiConfig {
  /** 默认 provider */
  provider?: string;
  /** 默认模型 */
  model?: string;
  /** 默认工作目录 */
  cwd?: string;
  /** 默认超时（秒） */
  timeoutSec?: number;
  /** 默认自动批准全部工具 */
  autoApprove?: boolean;
  /** 默认推理努力级别 */
  thinking?: ClineThinking;
  /** 集中凭据文件路径（相对 ClineApi 项目根） */
  credentialsFile?: string;
  /** 命令权限策略 */
  commandPermissions?: CommandPermissions;
}

const DEFAULT_CONFIG_FILE = "cline.config.json";

/** 读取 cline.config.json，不存在时返回空配置 */
export function loadConfigFile(): ClineApiConfig {
  const filePath = path.join(resolveProjectRoot(), DEFAULT_CONFIG_FILE);
  if (!fs.existsSync(filePath)) {
    return {};
  }
  try {
    const raw = fs.readFileSync(filePath, "utf-8");
    return JSON.parse(raw) as ClineApiConfig;
  } catch (err) {
    const message = err instanceof Error ? err.message : String(err);
    throw new Error(`配置解析失败: ${filePath} — ${message}`);
  }
}

/**
 * 加载配置并应用环境变量覆盖。
 * 优先级：环境变量 > cline.config.json
 */
export function loadConfig(): ClineApiConfig {
  const file = loadConfigFile();

  const fromEnv: ClineApiConfig = {};
  if (process.env["CLINE_PROVIDER"]) {
    fromEnv.provider = process.env["CLINE_PROVIDER"];
  }
  if (process.env["CLINE_MODEL"]) {
    fromEnv.model = process.env["CLINE_MODEL"];
  }
  if (process.env["CLINE_CWD"]) {
    fromEnv.cwd = process.env["CLINE_CWD"];
  }
  if (process.env["CLINE_TIMEOUT"]) {
    const parsed = Number(process.env["CLINE_TIMEOUT"]);
    if (Number.isFinite(parsed) && parsed >= 0) {
      fromEnv.timeoutSec = parsed;
    }
  }

  return { ...file, ...fromEnv };
}
