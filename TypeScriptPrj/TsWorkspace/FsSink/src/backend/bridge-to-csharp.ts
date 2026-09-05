/**
 * BridgeToCSharpDbBackend — 经桥接传输对接 C# 持久化（agent.db / ur_nodes）的 DbBackend。
 *
 * 写侧：同步落地到本地内存缓存（保证引擎内 getMessages/getMessagesSync 即时可读），
 * 并单向推送到 C#（persist 帧 → agent.db），磁盘零残留与真落库同时成立。
 * 读侧：由本地缓存承担（同一进程内写入后即时可读），无需跨 IPC 请求-响应往返。
 * 未配置/传输失败不阻断内存闭环（写侧 best-effort；主归档路径仍走 archivedMessages）。
 */

import type { DbBackend, ParsedMessage } from "./db-backend.js";
import { InMemoryDbBackend } from "./in-memory-db.js";

/** 到 C# 持久化的桥接传输：写侧单向推送，读侧由本地缓存承担。 */
export interface CSharpPersistenceTransport {
  writeMessages(sdk: string, sessionId: string, messages: ParsedMessage[]): void | Promise<void>;
  upsertSession(sdk: string, sessionId: string, row: unknown): void | Promise<void>;
}

export class BridgeToCSharpDbBackend implements DbBackend {
  private readonly local = new InMemoryDbBackend();

  constructor(private readonly transport: CSharpPersistenceTransport) {}

  async upsertSession(sdk: string, sessionId: string, row: unknown): Promise<void> {
    await this.local.upsertSession(sdk, sessionId, row);
    try {
      await this.transport.upsertSession(sdk, sessionId, row);
    } catch {
      /* 传输失败不阻断内存闭环；主归档路径（archivedMessages）兜底 */
    }
  }

  async writeMessages(sdk: string, sessionId: string, messages: ParsedMessage[]): Promise<void> {
    await this.local.writeMessages(sdk, sessionId, messages);
    try {
      await this.transport.writeMessages(sdk, sessionId, messages);
    } catch {
      /* 同上 */
    }
  }

  async getMessages(sdk: string, sessionId: string): Promise<ParsedMessage[] | undefined> {
    return this.local.getMessages(sdk, sessionId);
  }

  /** 同步读取（本地缓存即时可得，供结果提取链使用）。 */
  getMessagesSync(sdk: string, sessionId: string): ParsedMessage[] | undefined {
    return this.local.getMessagesSync(sdk, sessionId);
  }
}
