/**
 * ClaudeStorageProvider — Claude 文件型布局的通用声明（真实 SDK 未接入时的稳定落点）。
 *
 * 布局（宿主可覆盖 dataDir）：
 *   <dataDir>/projects/<encoded-cwd>/<sessionId>.jsonl   ← 会话正文
 *   <dataDir>/history.jsonl / todos/*.json               ← 其他事实（按需扩展）
 *
 * 文件型 Agent 无 DB 面（dbClaims = []）；核心 host/contract 零修改。
 * 真实 Claude SDK 探针待 SDK 可用后另行接入（当前仓库只有 @ai-sdk/anthropic，不是存储 SDK）。
 */

import * as path from "node:path";
import { MemoryVolume, type ArtifactExtractor, type Route } from "fs-sink";
import type {
  AgentStorageContext,
  AgentStorageProvider,
  DbClaim,
} from "../contract.js";

export interface ClaudeProviderOptions {
  /** 权威数据根（缺省 = CLAUDE_CONFIG_DIR 或 ~/.claude）。 */
  dataDir?: string;
  /** 会话子目录（缺省 projects）。 */
  projectsSubdir?: string;
  /** 产物提取器（缺省空 = 不镜像 DB）。 */
  extractors?: ArtifactExtractor[];
}

/** 解析 Claude 数据根（宿主显式注入优先）。 */
export function resolveClaudeDataDir(env: NodeJS.ProcessEnv = process.env): string {
  const explicit = env.CLAUDE_CONFIG_DIR?.trim();
  if (explicit) return explicit;
  const home = env.HOME?.trim() || env.USERPROFILE?.trim() || "~";
  return path.join(home, ".claude");
}

/** 从 `projects/<cwd>/<sessionId>.jsonl` 解析 sessionId（取最后一段文件名）。 */
export function extractClaudeSessionId(pathName: string): string | undefined {
  const normalized = pathName.replace(/\\/g, "/");
  const m = /\/projects\/(?:[^/]+\/)*([^/]+)\.jsonl$/i.exec(normalized);
  return m?.[1];
}

/** Claude 文件型 Provider（零 DB 面）。 */
export class ClaudeStorageProvider implements AgentStorageProvider {
  readonly name = "claude";
  readonly roots: string[];
  readonly dbRoots: string[] = [];
  readonly extractors: ArtifactExtractor[];
  readonly extractSessionId = extractClaudeSessionId;

  private readonly dataDir: string;
  private readonly projectsDir: string;

  constructor(opts: ClaudeProviderOptions = {}) {
    this.dataDir = opts.dataDir ?? resolveClaudeDataDir();
    this.projectsDir = path.join(this.dataDir, opts.projectsSubdir ?? "projects");
    this.roots = [this.projectsDir];
    this.extractors = opts.extractors ?? [];
  }

  context(env: NodeJS.ProcessEnv): AgentStorageContext {
    return {
      dataDir: this.dataDir,
      sessionsDir: this.projectsDir,
      dbDir: path.join(this.dataDir, "db"),
      env,
    };
  }

  buildRoutes(): Route[] {
    const root = this.dataDir.replace(/\\/g, "/");
    const sink = new MemoryVolume();
    return [
      { spec: { name: "claude-projects", prefix: `${root}/projects/` }, sink, mirror: true },
      { spec: { name: "claude-transcripts", glob: `${root}/**/*.jsonl` }, sink, mirror: true },
      { spec: { name: "claude-manifest", glob: `${root}/**/*.manifest.json` }, sink, mirror: false },
    ];
  }

  /** 文件型 Agent 无索引库接管面。 */
  dbClaims(): DbClaim[] {
    return [];
  }

  /** 显式可接受残留：Claude 自身日志/缓存（不含 projects 会话正文）。 */
  residualPaths(context: AgentStorageContext): string[] {
    return [path.join(context.dataDir, "logs"), path.join(context.dataDir, "cache")];
  }
}
