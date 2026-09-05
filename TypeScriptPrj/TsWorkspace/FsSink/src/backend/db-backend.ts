/**
 * DbBackend — 可插拔存储 SPI。
 *
 * 语义对齐项目现有 `PersistSessionAsync` / `ISnapshotStore`，便于 Phase 2 以
 * `BridgeToCSharpDbBackend` 经 daemon IPC 通道对接 C# 持久化。Phase 1 用 InMemoryDbBackend。
 * 通用化：全部方法加 `sdk` 维度（命名空间隔离），避免跨 SDK sessionId 冲突（Cline 与 Claude 的 id 都可能叫 abc）。
 */

/** 一个被拦截文件（内存卷快照）的解析产物：messages.json 的消息数组 / 会话元数据。 */
export interface ParsedMessage {
  [key: string]: unknown;
}

export interface DbBackend {
  /**
   * 写入/更新会话的元数据行（会话级）。
   * @param sdk SDK 命名空间（"cline" / "claude" / "codex"）。
   * @param sessionId 会话 ID。
   * @param row 会话元数据（manifest.json 等解析产物）。
   */
  upsertSession(sdk: string, sessionId: string, row: unknown): Promise<void>;

  /**
   * 写入某会话的完整消息数组（来自 messages.json 的 JSON.parse 产物）。
   * @param sdk SDK 命名空间。
   * @param sessionId 会话 ID（由路径 `<...>/sessions/<id>/<id>.messages.json` 解析）。
   * @param messages 解析后的消息数组。
   */
  writeMessages(sdk: string, sessionId: string, messages: ParsedMessage[]): Promise<void>;

  /**
   * 读取某会话的消息数组。若从未写入返回 undefined。
   * 供结果提取改从 DB 取（不再读磁盘 messages.json）。
   */
  getMessages(sdk: string, sessionId: string): Promise<ParsedMessage[] | undefined>;

  /**
   * 可选：同步读取消息数组（内存后端即时可得）。后端不支持时可省略，调用方回退磁盘读取。
   */
  getMessagesSync?(sdk: string, sessionId: string): ParsedMessage[] | undefined;
}

