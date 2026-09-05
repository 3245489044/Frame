/**
 * AgentRouteProvider — 声明式路由契约（替代写死的 clineRoutes）。
 *
 * 每个 SDK 提供一份声明式路由契约，PathRouter 消费，不感知 SDK：
 *  - roots：会话数据根候选（绝对路径，多个候选 = 显式隔离根 + SDK 默认根）；
 *  - extractSessionId：从命中路径 → sessionId（SDK 专属提取规则）；
 *  - extractors：按 SDK 契约的产物提取器（消息/会话元数据）；
 *  - buildRoutes()：声明式 → Route[]（PathRouter 直接消费）。
 *
 * 接入新 SDK = 新增一个 Provider（+ 提取器），核心零改动。
 */

import type { ArtifactExtractor } from "../extract/artifact-extractor.js";
import type { Route } from "../core/route.js";

/** 路由工厂：声明式 → Route[]（sink 共享、mirror 按 kind 决定）。 */
export interface AgentRouteProvider {
  readonly name: string; // "cline" | "claude" | "codex"
  /** 会话数据根候选（显式隔离根 + SDK 默认根），用于路由 miss 观测。 */
  readonly roots: string[];
  /** 从命中路径解析 sessionId（SDK 专属）。 */
  readonly extractSessionId: (path: string) => string | undefined;
  /** 按 SDK 契约的产物提取器（注册进 DbMirror 的提取器注册表）。 */
  readonly extractors: ArtifactExtractor[];
  buildRoutes(): Route[];
}
