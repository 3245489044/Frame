/**
 * 识别声明装载器 —— `Config\recognizers.json` → {@link RecognizerSpec}。
 *
 * W-4（缺失即抛 / ∅ + 诊断，禁回退默认值）：
 *  - 未配置路径 ⇒ 返回 `undefined`（识别层停用；调用方负责可见化诊断，不静默假装识别生效）；
 *  - 路径已配置但文件缺失/非法 ⇒ **抛**（不内置兜底词表，避免"声明被删而识别仍绿"）。
 */

import * as fs from "fs";
import * as path from "path";
import { loadConfig } from "../config.js";
import { resolveProjectRoot } from "../credentials.js";
import { ensureCompatible, UNRECOGNIZED_KIND, type RecognizerSpec } from "./vocabulary.js";

/**
 * 解析识别声明文件路径（**定位单源**）。
 *
 * 优先级：`CLINE_RECOGNIZER_SPEC`（绝对路径；测试/诊断覆盖）
 * > `agent` 实参（`Config\agents\<agent>\recognizers.json`，P1 主路径）
 * > `CLINE_RECOGNIZER_AGENT` 环境变量 > `cline.config.json.recognizerSpecFile`（**legacy**，单文件时代）
 * > 未配置（⇒ 识别层停用）。
 * 注：本函数只解析**位置**；声明**内容**的单源恒为 `Config\agents\<agent>\recognizers.json`（W-1）。
 */
export function resolveRecognizerSpecPath(agent?: string): string | undefined {
  const fromEnv = process.env["CLINE_RECOGNIZER_SPEC"];
  if (fromEnv && fromEnv.trim().length > 0) {
    return fromEnv.trim();
  }

  const effectiveAgent = (agent ?? process.env["CLINE_RECOGNIZER_AGENT"] ?? "").trim();
  if (effectiveAgent.length > 0) {
    return resolveAgentSpecPath(effectiveAgent);
  }

  // legacy：单文件时代的显式路径（保留兼容；agent 维度生效后本分支应归零）
  const configured = loadConfig().recognizerSpecFile;
  if (!configured || configured.trim().length === 0) {
    return undefined;
  }

  const trimmed = configured.trim();
  return path.isAbsolute(trimmed) ? trimmed : path.resolve(resolveProjectRoot(), trimmed);
}

/** agent 目录根（`Config\agents\<agent>`；目录名即 agent 身份）。 */
export function resolveAgentDir(agent: string): string {
  const trimmed = (agent ?? "").trim();
  if (trimmed.length === 0) {
    throw new Error("agent 不能为空（目录名即身份，禁默认推测）");
  }
  // repo 根 = ClineApi 根上溯四级（与 cline.config.json 的 `../../../../` 同范式，禁第二套相对深度）。
  const repoRoot = path.resolve(resolveProjectRoot(), "..", "..", "..", "..");
  return path.resolve(repoRoot, "Projects", "Plug", "Agent", "Config", "agents", trimmed);
}

/** agent 识别声明路径（`Config\agents\<agent>\recognizers.json`）。 */
export function resolveAgentSpecPath(agent: string): string {
  return path.join(resolveAgentDir(agent), "recognizers.json");
}

/** agent 轮次形状契约路径（`Config\agents\<agent>\round-shape.ushar`）。 */
export function resolveRoundShapePath(agent: string): string {
  return path.join(resolveAgentDir(agent), "round-shape.ushar");
}

/** 读取并校验识别声明；未配置路径返回 `undefined`，其余异常一律抛。 */
export function loadRecognizerSpec(
  filePath: string | undefined | null,
  expectedAgent?: string
): RecognizerSpec | undefined {
  if (!filePath || filePath.trim().length === 0) {
    return undefined;
  }

  if (!fs.existsSync(filePath)) {
    throw new Error(`识别声明缺失: ${filePath}（缺失即抛，禁回退默认值）`);
  }

  let parsed: unknown;
  try {
    parsed = JSON.parse(fs.readFileSync(filePath, "utf-8"));
  } catch (err) {
    const message = err instanceof Error ? err.message : String(err);
    throw new Error(`识别声明解析失败: ${filePath} — ${message}`);
  }

  const spec = parsed as Partial<RecognizerSpec>;
  if (!Array.isArray(spec?.kinds) || !Array.isArray(spec?.recognizers)) {
    throw new Error(`识别声明结构非法: ${filePath}（须含 kinds[] 与 recognizers[]）`);
  }

  ensureCompatible(spec.vocabulary ?? "");

  // P1：末路完备（缺省视为 `unrecognized`）+ default.kind ∈ 词表（P5 禁隐式丢弃）。
  const fallback = spec.default?.kind ?? UNRECOGNIZED_KIND;
  if (!spec.kinds.includes(fallback)) {
    throw new Error(`识别声明非法: ${filePath}（default.kind "${fallback}" ∉ kinds；末路完备 P5）`);
  }

  // agent 轴一致性（目录名即身份；声明内 agent 不得与装载维度冲突）。
  if (expectedAgent && spec.agent && spec.agent !== expectedAgent) {
    throw new Error(`识别声明 agent 不一致: ${filePath}（声明 "${spec.agent}" ≠ 装载 "${expectedAgent}"）`);
  }

  return spec as RecognizerSpec;
}
