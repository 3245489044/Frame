/**
 * ClineApi — 集中凭据管理
 *
 * 所有 Provider 的 API Key 集中在工作区凭据文件中管理：
 *   <TypeScriptPrj>/credentials/cline.json
 *
 * 该目录已被 git 忽略（.gitignore: credentials/），禁止提交。
 *
 * 读取优先级（高 → 低）：
 *   1. 环境变量 CLINE_CREDENTIALS_PATH（绝对路径；Agent 外置装配期会把外部凭据路径写入，TS 子进程继承）
 *   2. cline.config.json 中 credentialsFile 字段（相对 ClineApi 项目根）
 *   3. 环境变量 ROUTE_STATE_ROOT → <root>/credentials/cline.json（与 Agent(.NET) 数据库同外部根，跨语言一致）
 *   4. 默认 <TypeScriptPrj>/credentials/cline.json
 */

import * as fs from "fs";
import * as path from "path";
import { fileURLToPath } from "url";

/** 单个 Provider 的凭据 */
export interface ProviderCredential {
  apiKey: string;
  model?: string;
  /** 自定义 Provider 的 API 基础 URL（如 OpenAI 兼容端点 base_url） */
  baseUrl?: string;
  /**
   * 自定义 Provider 的 handler 客户端类型（wire_api 对应值）：
   * 如 "openai-responses"（OpenAI Responses API）/"openai-compatible"（OpenAI 兼容 /v1/chat/completions）。
   * 内置 provider（如 deepseek/openai-compatible）无需设置；仅自定义 providerId 需配合路由。
   */
  clientType?: string;
  /** 可选：内置 provider 家族，用于自定义 provider 的 handler 路由（如 "openai"） */
  routingProviderId?: string;
  /** 可选：自定义请求头 */
  headers?: Record<string, string>;
  /** 可选：端点是否要求 OpenAI 认证头（仅信息标注，桥接层始终以 apiKey 注入 Bearer） */
  requiresOpenaiAuth?: boolean;
  reasoning?: {
    enabled?: boolean;
    effort?: string;
  };
}

/** 集中凭据文件结构 */
export interface ClineCredentialsFile {
  version?: number;
  defaultProvider?: string;
  providers: Record<string, ProviderCredential>;
}

/** 解析当前模块所在目录（开发: src/，编译后: build/） */
function getModuleDir(): string {
  return path.dirname(fileURLToPath(import.meta.url));
}

/**
 * 定位 ClineApi 项目根目录。
 * 模块位于 <ClineApi>/src 或 <ClineApi>/build 目录下，其上一级目录即为 ClineApi 项目根。
 */
export function resolveProjectRoot(): string {
  return path.resolve(getModuleDir(), "..");
}

/**
 * 解析集中凭据文件绝对路径。
 *
 * @param credentialsFile cline.config.json 中的相对路径（相对项目根），或绝对路径
 */
export function resolveCredentialsFile(credentialsFile?: string): string {
  // 1. 环境变量优先
  const fromEnv = process.env["CLINE_CREDENTIALS_PATH"];
  if (fromEnv) {
    return path.resolve(fromEnv);
  }

  // 2. 配置指定的路径
  if (credentialsFile) {
    const resolved = path.resolve(resolveProjectRoot(), credentialsFile);
    if (fs.existsSync(resolved)) {
      return resolved;
    }
  }

  // 3. 外部状态根（ROUTE_STATE_ROOT）凭据：与 Agent(.NET) 数据库同外部根，跨语言读取同一份（文件存在方认可）
  const stateRoot = process.env["ROUTE_STATE_ROOT"];
  if (stateRoot) {
    const external = path.resolve(stateRoot, "credentials", "cline.json");
    if (fs.existsSync(external)) {
      return external;
    }
  }

  // 4. 默认：<TypeScriptPrj>/credentials/cline.json（仓库级集中管理）
  return path.resolve(resolveProjectRoot(), "../../../credentials/cline.json");
}

/**
 * 加载集中凭据文件。
 * 文件不存在或解析失败时返回空对象（不抛异常，由调用方决定行为）。
 */
export function loadCredentialsFile(credentialsFile?: string): ClineCredentialsFile {
  const filePath = resolveCredentialsFile(credentialsFile);

  if (!fs.existsSync(filePath)) {
    return { providers: {} };
  }

  try {
    const raw = fs.readFileSync(filePath, "utf-8");
    const parsed = JSON.parse(raw) as ClineCredentialsFile;
    return {
      version: parsed.version,
      defaultProvider: parsed.defaultProvider,
      providers: parsed.providers ?? {},
    };
  } catch (err) {
    const message = err instanceof Error ? err.message : String(err);
    throw new Error(`集中凭据文件解析失败: ${filePath} — ${message}`);
  }
}

/**
 * 把字符串中的 ${VAR} 环境变量引用展开为实际值；未设置的变量替换为空串。
 */
function expandEnv(value?: string): string | undefined {
  if (value === undefined || value === null || value === "") return value;
  return value.replace(/\$\{([A-Za-z_][A-Za-z0-9_]*)\}/g, (_m, name) => {
    const v = process.env[name];
    return v === undefined ? "" : v;
  });
}

/**
 * 克隆凭据并把字符串字段中的环境变量引用展开（不修改原始对象）。
 */
function resolveProviderCredential(cred: ProviderCredential): ProviderCredential {
  const headers = cred.headers
    ? Object.fromEntries(Object.entries(cred.headers).map(([k, v]) => [k, expandEnv(v) ?? ""]))
    : cred.headers;
  return {
    apiKey: expandEnv(cred.apiKey) ?? "",
    model: expandEnv(cred.model),
    baseUrl: expandEnv(cred.baseUrl),
    clientType: cred.clientType,
    routingProviderId: cred.routingProviderId,
    headers,
    requiresOpenaiAuth: cred.requiresOpenaiAuth,
    reasoning: cred.reasoning,
  };
}

/**
 * 获取指定 Provider 的凭据。
 * provider 未指定时使用凭据文件中的 defaultProvider。
 * 返回的凭据已展开其中的 ${VAR} 环境变量引用。
 */
export function getProviderCredential(
  provider: string | undefined,
  credentialsFile?: string
): { provider: string; credential?: ProviderCredential; filePath: string } {
  const creds = loadCredentialsFile(credentialsFile);
  const resolvedProvider = provider ?? creds.defaultProvider ?? "";

  if (!resolvedProvider) {
    return { provider: "", credential: undefined, filePath: resolveCredentialsFile(credentialsFile) };
  }

  const raw = creds.providers[resolvedProvider];
  return {
    provider: resolvedProvider,
    credential: raw ? resolveProviderCredential(raw) : undefined,
    filePath: resolveCredentialsFile(credentialsFile),
  };
}
