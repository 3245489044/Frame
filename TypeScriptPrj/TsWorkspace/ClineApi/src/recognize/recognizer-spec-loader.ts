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
import { ensureCompatible, type RecognizerSpec } from "./vocabulary.js";

/**
 * 解析识别声明文件路径（**定位单源**）。
 *
 * 优先级：`CLINE_RECOGNIZER_SPEC`（绝对路径；测试/诊断覆盖） > `cline.config.json.recognizerSpecFile`
 * （相对 = 相对 ClineApi 项目根，与既有 `credentialsFile` 同范式） > 未配置（⇒ 识别层停用）。
 * 注：本函数只解析**位置**；声明**内容**的单源恒为 `Config\recognizers.json`（W-1）。
 */
export function resolveRecognizerSpecPath(): string | undefined {
  const fromEnv = process.env["CLINE_RECOGNIZER_SPEC"];
  if (fromEnv && fromEnv.trim().length > 0) {
    return fromEnv.trim();
  }

  const configured = loadConfig().recognizerSpecFile;
  if (!configured || configured.trim().length === 0) {
    return undefined;
  }

  const trimmed = configured.trim();
  return path.isAbsolute(trimmed) ? trimmed : path.resolve(resolveProjectRoot(), trimmed);
}

/** 读取并校验识别声明；未配置路径返回 `undefined`，其余异常一律抛。 */
export function loadRecognizerSpec(filePath: string | undefined | null): RecognizerSpec | undefined {
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
  return spec as RecognizerSpec;
}
