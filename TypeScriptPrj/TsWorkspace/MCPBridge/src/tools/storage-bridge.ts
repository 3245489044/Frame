/**
 * storage-bridge — Agent.StorageBridge.exe 调用助手
 *
 * node-discovery 工具经此模块定位并调用 Agent.StorageBridge.exe 的 node-* 子命令，
 * 解析 {status,message,data,errors} 信封。路径从 mcp-bridge.config.json 的 paths + nodeDiscovery 读取，
 * 缺省回退仓库约定路径 Agent\BuildOutput\Debug\Agent.StorageBridge.exe。
 */

import { execFileSync } from "child_process";
import * as fs from "fs";
import * as path from "path";
import { loadPathsConfig } from "../utils/paths.js";

export interface BridgeEnvelope {
  status: string;
  message: string;
  data: unknown;
  errors: string[];
}

export interface NodeDiscoveryConfig {
  enabled: boolean;
  defaultBudgetTokens: number;
  storageHost?: string;
}

const DEFAULT_BRIDGE_REL = path.join("Projects", "Agent", "BuildOutput", "Debug", "Agent.StorageBridge.exe");

/** 读取 nodeDiscovery 配置段（缺省返回默认值；配置解析失败不抛错）。 */
export function readNodeDiscovery(): NodeDiscoveryConfig {
  const cfg = loadPathsConfig();
  const configPath = path.resolve(cfg.projectRoot, cfg.bridgeRoot, cfg.configFile);
  try {
    const raw = JSON.parse(fs.readFileSync(configPath, "utf-8")) as Record<string, any>;
    const nd = (raw?.nodeDiscovery ?? {}) as Record<string, any>;
    return {
      enabled: nd.enabled ?? true,
      defaultBudgetTokens: typeof nd.defaultBudgetTokens === "number" ? nd.defaultBudgetTokens : 6000,
      storageHost: nd.storageHost,
    };
  } catch {
    return { enabled: true, defaultBudgetTokens: 6000 };
  }
}

/** 定位 StorageBridge 可执行文件绝对路径（配置 nodeDiscovery.storageHost 优先，否则仓库默认）。 */
export function resolveBridgeExe(): string {
  const cfg = loadPathsConfig();
  const projectRoot = path.resolve(cfg.projectRoot);
  const custom = readNodeDiscovery().storageHost;
  const rel = custom || DEFAULT_BRIDGE_REL;
  const full = path.resolve(projectRoot, rel);
  if (!fs.existsSync(full)) {
    throw new Error(`Agent.StorageBridge.exe 未找到: ${full}（先构建 C# 侧 Agent.sln）`);
  }
  return full;
}

/** 执行 StorageBridge 子命令，返回信封（失败抛错）。非空参数才透传，空字符串跳过。 */
export function invokeBridge(subcommand: string, args: Record<string, string>): BridgeEnvelope {
  const exe = resolveBridgeExe();
  const argv = [subcommand];
  for (const [k, v] of Object.entries(args)) {
    if (v === undefined || v === null || v === "") continue;
    argv.push(`--${k}`, v);
  }
  const stdout = execFileSync(exe, argv, { encoding: "utf-8", maxBuffer: 64 * 1024 * 1024 });
  let parsed: BridgeEnvelope;
  try {
    parsed = JSON.parse(stdout) as BridgeEnvelope;
  } catch {
    throw new Error(`node-discovery ${subcommand} 输出非 JSON 信封: ${stdout.slice(0, 200)}`);
  }
  if (parsed.status !== "success") {
    throw new Error(`node-discovery ${subcommand} 失败: ${parsed.message} ${parsed.errors.join(",")}`);
  }
  return parsed;
}
