/**
 * FileAgentProvider — 文件型 Agent 的通用 Provider 骨架（Claude/Codex 等）。
 *
 * 仅需声明：数据根、会话子目录、提取器；DB 面默认空（文件型 Agent 无 DB）。
 * 用于证明「新增 Agent = 新增 Provider，核心零修改」。
 */

import * as path from "node:path";
import { MemoryVolume, type ArtifactExtractor, type Route } from "fs-sink";
import type {
  AgentStorageContext,
  AgentStorageProvider,
  DbClaim,
} from "../contract.js";

export interface FileAgentProviderOptions {
  /** Agent 名（如 claude）。 */
  name: string;
  /** 权威数据根（宿主注入的隔离根或默认根）。 */
  dataDir: string;
  /** 会话产物子目录（缺省 sessions）。 */
  sessionsSubdir?: string;
  /** 产物提取器（缺省空 = 不镜像 DB）。 */
  extractors?: ArtifactExtractor[];
  /** 从路径解析 sessionId；缺省按 sessions/<id>/ 解析。 */
  extractSessionId?: (pathName: string) => string | undefined;
}

/** 文件型 Agent Provider（零 DB 面）。 */
export class FileAgentProvider implements AgentStorageProvider {
  readonly name: string;
  readonly extractors: ArtifactExtractor[];
  readonly roots: string[];
  readonly dbRoots: string[] = [];
  readonly extractSessionId: (pathName: string) => string | undefined;

  private readonly dataDir: string;
  private readonly sessionsDir: string;

  constructor(opts: FileAgentProviderOptions) {
    this.name = opts.name;
    this.dataDir = opts.dataDir;
    this.sessionsDir = path.join(opts.dataDir, opts.sessionsSubdir ?? "sessions");
    this.roots = [this.sessionsDir];
    this.extractors = opts.extractors ?? [];
    this.extractSessionId =
      opts.extractSessionId ??
      ((pathName: string) => {
        const normalized = pathName.replace(/\\/g, "/");
        const m = /\/([^/]+)\/[^/]+\.(messages|manifest)\.json$/i.exec(normalized);
        return m?.[1];
      });
  }

  context(env: NodeJS.ProcessEnv): AgentStorageContext {
    return {
      dataDir: this.dataDir,
      sessionsDir: this.sessionsDir,
      dbDir: path.join(this.dataDir, "db"),
      env,
    };
  }

  buildRoutes(): Route[] {
    const sink = new MemoryVolume();
    const root = this.dataDir.replace(/\\/g, "/");
    return [
      { spec: { name: `${this.name}-sessions`, prefix: `${root}/sessions/` }, sink, mirror: true },
      { spec: { name: `${this.name}-messages`, glob: `${root}/**/*.messages.json` }, sink, mirror: true },
      { spec: { name: `${this.name}-manifest`, glob: `${root}/**/*.manifest.json` }, sink, mirror: false },
    ];
  }

  dbClaims(): DbClaim[] {
    return [];
  }
}
