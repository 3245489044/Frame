/**
 * ArtifactExtractor — 契约化解析：按 SDK 契约把内存卷快照条目解析为「可落库的数据单元」。
 *
 * 承接 DbMirror 里硬编码的 Cline 解析（messages.json 形状 / manifest 形状），使其成为
 * 按路径 kind 匹配、可按 SDK 注册的提取器。Claude/Codex 接入只需新增一个提取器类，不改 DbMirror。
 */

import type { SnapshotEntry } from "../core/sink.js";

/** 产物语义类别：决定镜像到 DbBackend 的哪个写方法。 */
export type ArtifactKind = "messages" | "session" | "ignored";

/**
 * 把内存卷快照条目解析为「可写入 DbBackend 的结构化数据」。
 * - kind=messages → parse 返回消息数组（写 backend.writeMessages）。
 * - kind=session  → parse 返回会话元数据（写 backend.upsertSession）。
 * - kind=ignored  → 不镜像 DB，仅驻内存卷（回读闭环仍成立）。
 * parse 返回 undefined 表示解析失败（文件未写完 / 非本 SDK 形状），待下次 flush 重试。
 */
export interface ArtifactExtractor {
  readonly kind: ArtifactKind;
  /** 路径是否归属本提取器（如 `.messages.json` 后缀 / `.manifest.json`）。 */
  matches(path: string): boolean;
  parse(snapshot: SnapshotEntry): unknown | undefined;
}

/** 提取器注册表：按路径 kind 匹配，供 DbMirror 查询。 */
export class ArtifactExtractorRegistry {
  private readonly extractors: ArtifactExtractor[] = [];

  register(ex: ArtifactExtractor): void {
    this.extractors.push(ex);
  }

  /** 返回第一个匹配路径的提取器；无匹配返回 undefined（不镜像）。 */
  match(path: string): ArtifactExtractor | undefined {
    return this.extractors.find((ex) => ex.matches(path));
  }
}
