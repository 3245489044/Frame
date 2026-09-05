/**
 * InMemoryDbBackend — Phase 1 默认零依赖后端。
 *
 * 以内存 Map 承载 `sdk -> sessionId -> { row?, messages? }`，为验收与测试提供闭环后端；
 * Phase 2 以桥接 C# 的实现替换。写入为同步落地到 Map，返回 Promise 以对齐 SPI。
 * 带 `sdk` 维度命名空间隔离，跨 SDK 同名 sessionId 不串库。
 */

import type { DbBackend, ParsedMessage } from "./db-backend.js";

interface SessionRecord {
  row?: unknown;
  messages?: ParsedMessage[];
}

export class InMemoryDbBackend implements DbBackend {
  private readonly store = new Map<string, Map<string, SessionRecord>>();

  private rec(sdk: string, sessionId: string): SessionRecord {
    let bySdk = this.store.get(sdk);
    if (!bySdk) {
      bySdk = new Map();
      this.store.set(sdk, bySdk);
    }
    let rec = bySdk.get(sessionId);
    if (!rec) {
      rec = {};
      bySdk.set(sessionId, rec);
    }
    return rec;
  }

  async upsertSession(sdk: string, sessionId: string, row: unknown): Promise<void> {
    this.rec(sdk, sessionId).row = row;
  }

  async writeMessages(sdk: string, sessionId: string, messages: ParsedMessage[]): Promise<void> {
    this.rec(sdk, sessionId).messages = messages;
  }

  async getMessages(sdk: string, sessionId: string): Promise<ParsedMessage[] | undefined> {
    return this.store.get(sdk)?.get(sessionId)?.messages;
  }

  /** 同步读取（InMemory 后端即时可得；供同步结果提取链使用）。 */
  getMessagesSync(sdk: string, sessionId: string): ParsedMessage[] | undefined {
    return this.store.get(sdk)?.get(sessionId)?.messages;
  }

  /** 测试辅助：已记录的全部 sdk。 */
  sdks(): string[] {
    return Array.from(this.store.keys());
  }

  /** 测试辅助：某 sdk 下已记录的全部 sessionId。 */
  keys(sdk: string): string[] {
    return Array.from(this.store.get(sdk)?.keys() ?? []);
  }

  /** 测试辅助：清空。 */
  clear(): void {
    this.store.clear();
  }
}

