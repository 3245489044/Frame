/**
 * DbMirror — 把内存卷中的会话数据异步镜像到 DbBackend。
 *
 * - 订阅 Sink 写事件（MemoryVolume 在写后回调）；
 * - 经 `ArtifactExtractorRegistry` 按路径 kind 匹配提取器 → `parse` → `backend.writeMessages/upsertSession`；
 * - 会话 ID 提取由 SDK Provider 提供（`extractSessionId`），核心不做 SDK 假设；
 * - flush 时序：写后 debounce（默认 200ms）+ 显式 flushAll()（在 SDK 的 session_snapshot/ended
 *   事件触发时调用，保证终态落库）。
 */

import type { DbBackend } from "../backend/db-backend.js";
import type { Sink, SnapshotEntry } from "../core/sink.js";
import type { ArtifactExtractorRegistry } from "../extract/artifact-extractor.js";

const DEFAULT_DEBOUNCE_MS = 200;

export interface DbMirrorOptions {
  /** SDK 命名空间（"cline" / "claude"），写入 DbBackend 的 sdk 维度。 */
  sdk: string;
  /** 按路径 kind 匹配的提取器注册表。 */
  registry: ArtifactExtractorRegistry;
  /** 从命中路径解析 sessionId（由 SDK Provider 提供）。 */
  extractSessionId: (path: string) => string | undefined;
  debounceMs?: number;
}

export class DbMirror {
  private readonly debounceMs: number;
  private readonly sdk: string;
  private readonly registry: ArtifactExtractorRegistry;
  private readonly extractSessionId: (path: string) => string | undefined;
  private debounceTimer?: ReturnType<typeof setTimeout>;
  private pending = new Set<string>();
  private _attempted = 0;
  private _failed = 0;

  constructor(
    private readonly sink: Sink,
    private readonly backend: DbBackend,
    opts: DbMirrorOptions
  ) {
    this.debounceMs = opts.debounceMs ?? DEFAULT_DEBOUNCE_MS;
    this.sdk = opts.sdk;
    this.registry = opts.registry;
    this.extractSessionId = opts.extractSessionId;
    sink.onWrite((path) => this.onWrite(path));
  }

  /** 镜像统计（供 observe() 观测帧：归档尝试/失败归因）。 */
  get stats(): { attempted: number; failed: number } {
    return { attempted: this._attempted, failed: this._failed };
  }

  /** 写事件入口（MemoryVolume 在写后回调）。仅镜像有匹配提取器的文件（messages/session）。 */
  onWrite(path: string): void {
    if (!this.registry.match(path)) return;
    this.pending.add(path);
    this.schedule();
  }

  private schedule(): void {
    if (this.debounceTimer) clearTimeout(this.debounceTimer);
    this.debounceTimer = setTimeout(() => {
      this.debounceTimer = undefined;
      void this.flushPending();
    }, this.debounceMs);
  }

  private async flushPending(): Promise<void> {
    const paths = Array.from(this.pending);
    this.pending.clear();
    for (const p of paths) {
      const entry = this.findSnapshot(p);
      if (entry) await this.mirror(entry);
    }
  }

  private findSnapshot(path: string): SnapshotEntry | undefined {
    return this.sink.snapshotAll().find((e) => e.path === path);
  }

  /** 核心镜像：按路径匹配提取器 → parse → 写入 backend（按 kind 分流）。 */
  private async mirror(entry: SnapshotEntry): Promise<void> {
    const ex = this.registry.match(entry.path);
    if (!ex) return;
    const sessionId = this.extractSessionId(entry.path);
    if (!sessionId) return;
    const parsed = ex.parse(entry);
    if (parsed === undefined) return; // 非完整 / 非本 SDK 形状，待下次 flush
    this._attempted++;
    try {
      if (ex.kind === "messages") {
        await this.backend.writeMessages(this.sdk, sessionId, parsed as Record<string, unknown>[]);
      } else if (ex.kind === "session") {
        await this.backend.upsertSession(this.sdk, sessionId, parsed);
      }
    } catch {
      this._failed++; // 后端写入失败不阻断内存闭环；由显式 flushAll 的调用方感知（观测帧）
    }
  }

  /**
   * 显式落库：取消 pending debounce，直接遍历内存卷全部快照，把 messages/manifest 镜像到后端。
   * 用于 SDK session_snapshot / ended 事件触发，保证终态落库。
   */
  async flushAll(): Promise<void> {
    if (this.debounceTimer) {
      clearTimeout(this.debounceTimer);
      this.debounceTimer = undefined;
    }
    this.pending.clear();
    for (const entry of this.sink.snapshotAll()) {
      await this.mirror(entry);
    }
  }
}
