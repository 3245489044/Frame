/**
 * 严格零 SQLite 的内存会话后端（灰度能力）。
 *
 * SDK 的 `sessionService` 目前为 @internal，接口随版本变化；本实现以「结构最小面 + Proxy 兜底」
 * 保证可注入且不会因未知方法崩溃。默认不启用；仅当 AGENT_STORAGE_PROFILE=strictNoSqlite
 * 且 Provider 提供 sessionBackend 时注入。契约测试锁定已知方法。
 */

interface SessionRecord {
  id: string;
  [key: string]: unknown;
}

interface MessageRecord {
  [key: string]: unknown;
}

/** 已知方法名（契约测试断言）。 */
export const SESSION_BACKEND_METHODS = [
  "createSession",
  "getSession",
  "listSessions",
  "updateSession",
  "deleteSession",
  "appendMessage",
  "getMessages",
  "createRootSessionWithArtifacts",
] as const;

/** 内存会话后端（零文件、零 SQLite）。 */
export class InMemorySessionService {
  private readonly sessions = new Map<string, SessionRecord>();
  private readonly messages = new Map<string, MessageRecord[]>();
  private readonly calls = new Map<string, number>();
  private seq = 0;

  /** 记录一次方法调用（strictNoSqlite 实证：SDK 是否真的消费注入后端）。 */
  private record(method: string): void {
    this.calls.set(method, (this.calls.get(method) ?? 0) + 1);
  }

  async createSession(input?: Record<string, unknown>): Promise<SessionRecord> {
    this.record("createSession");
    this.seq += 1;
    const id = String(input?.sessionId ?? input?.id ?? `session-${this.seq}`);
    const record: SessionRecord = { ...(input ?? {}), id, sessionId: id };
    this.sessions.set(id, record);
    this.messages.set(id, []);
    return record;
  }

  async getSession(id: string): Promise<SessionRecord | undefined> {
    this.record("getSession");
    return this.sessions.get(id);
  }

  async listSessions(limit?: number): Promise<SessionRecord[]> {
    this.record("listSessions");
    const all = Array.from(this.sessions.values());
    return typeof limit === "number" && limit > 0 ? all.slice(0, limit) : all;
  }

  async updateSession(id: string, patch?: Record<string, unknown>): Promise<SessionRecord | undefined> {
    this.record("updateSession");
    const current = this.sessions.get(id);
    if (!current) return undefined;
    const next = { ...current, ...(patch ?? {}), id, sessionId: id };
    this.sessions.set(id, next);
    return next;
  }

  async deleteSession(id: string): Promise<boolean> {
    this.record("deleteSession");
    this.messages.delete(id);
    return this.sessions.delete(id);
  }

  async appendMessage(id: string, message: MessageRecord): Promise<void> {
    this.record("appendMessage");
    const list = this.messages.get(id) ?? [];
    list.push(message);
    this.messages.set(id, list);
  }

  async getMessages(id: string): Promise<MessageRecord[]> {
    this.record("getMessages");
    return [...(this.messages.get(id) ?? [])];
  }

  /** SDK 0.0.95 可能调用的复合入口：创建根会话 + 产物。 */
  async createRootSessionWithArtifacts(
    input?: Record<string, unknown>,
    artifacts?: unknown
  ): Promise<{ session: SessionRecord; artifacts: unknown }> {
    this.record("createRootSessionWithArtifacts");
    const session = await this.createSession(input);
    return { session, artifacts };
  }

  /** 观测：当前会话数（诊断用）。 */
  size(): number {
    return this.sessions.size;
  }

  /** 观测：方法调用计数（strictNoSqlite 实证：0 = SDK 未消费注入后端）。 */
  stats(): { total: number; calls: Record<string, number> } {
    const calls = Object.fromEntries(this.calls.entries());
    const total = [...this.calls.values()].reduce((sum, n) => sum + n, 0);
    return { total, calls };
  }

  /** 未列名方法的兜底：返回 undefined 而非抛错（fail-open，保证 SDK 启动不被未知 API 阻断）。 */
  static wrap(service: InMemorySessionService): Record<string, unknown> {
    return new Proxy(service as unknown as Record<string, unknown>, {
      get(target, prop, receiver) {
        if (prop in target) return Reflect.get(target, prop, receiver);
        return () => undefined;
      },
    });
  }
}

/** 工厂：返回可直接注入 `ClineCore.create({ sessionService })` 的对象。 */
export function createInMemorySessionService(): Record<string, unknown> {
  return InMemorySessionService.wrap(new InMemorySessionService());
}
