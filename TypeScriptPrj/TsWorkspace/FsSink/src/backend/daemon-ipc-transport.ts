/**
 * DaemonIpcPersistenceTransport — 经 daemon IPC（engine stdout → daemon → C#）推送 persist 帧到 C#。
 *
 * 在 engine 执行面子进程内：FsSink 的 BridgeToCSharpDbBackend 写侧调用本传输，把
 * `{type:"persist", detail:{kind,sdk,sessionId,payload}}` 写到 stdout；
 * daemon 控制面 onData 中继该帧到 C#（ClineDaemonIpc.Persist 事件 → agent.db / ur_nodes）。
 * 单向 fire-and-forget：读侧由 BridgeToCSharpDbBackend 本地缓存承担，无需请求-响应往返。
 */

import type { ParsedMessage } from "./db-backend.js";
import type { CSharpPersistenceTransport } from "./bridge-to-csharp.js";

/** 帧发射器：默认写进程 stdout（engine 子进程的 JSON-Line 通道）；测试可注入 fake。 */
export type PersistFrameEmitter = (frame: Record<string, unknown>) => void;

export class DaemonIpcPersistenceTransport implements CSharpPersistenceTransport {
  private readonly emit: PersistFrameEmitter;

  constructor(emit?: PersistFrameEmitter) {
    this.emit = emit ?? ((frame) => process.stdout.write(`${JSON.stringify(frame)}\n`));
  }

  writeMessages(sdk: string, sessionId: string, messages: ParsedMessage[]): void {
    this.emit({
      type: "persist",
      detail: { kind: "messages", sdk, sessionId, payload: messages },
    });
  }

  upsertSession(sdk: string, sessionId: string, row: unknown): void {
    this.emit({
      type: "persist",
      detail: { kind: "session", sdk, sessionId, payload: row },
    });
  }
}
