/**
 * 存储策略解析 — 单一入口（显式 > 新 env > 旧 env > 默认）。
 *
 * 默认 = zeroDisk：会话产物走内存卷、索引库走 :memory:，磁盘零残留。
 * 回退 = AGENT_STORAGE_PROFILE=off 或旧 CLINE_DB_SINK=off（一键回到真实落盘）。
 */

import type { DbSinkMode } from "fs-sink";
import type { StoragePolicy, StorageProfile } from "./contract.js";

/** 新档位开关（权威）。 */
export const STORAGE_PROFILE_ENV = "AGENT_STORAGE_PROFILE";
/** 隔离 worker 根（C# 宿主注入；缺省由 Provider 从 CLINE_* 推导）。 */
export const STORAGE_WORKER_ROOT_ENV = "AGENT_STORAGE_WORKER_ROOT";
/** 旧开关（向后兼容；优先级低于 STORAGE_PROFILE_ENV）。 */
export const LEGACY_DB_SINK_ENV = "CLINE_DB_SINK";

/** 全部合法档位。 */
export const STORAGE_PROFILES: readonly StorageProfile[] = [
  "off",
  "relocated",
  "zeroDisk",
  "strictNoSqlite",
];

/** 默认档位：零落盘。 */
export const DEFAULT_STORAGE_PROFILE: StorageProfile = "zeroDisk";

function normalize(value: string | undefined): string | undefined {
  const v = value?.trim().toLowerCase();
  return v ? v : undefined;
}

/** 解析旧 CLINE_DB_SINK（合法值透传，非法值 off —— 与 DbSink 语义一致）。 */
export function parseLegacyDbMode(raw: string | undefined): DbSinkMode | undefined {
  const v = normalize(raw);
  if (!v) return undefined;
  return v === "observe" || v === "relocate" || v === "memory" || v === "off"
    ? (v as DbSinkMode)
    : "off";
}

/** 解析档位：新 env > 旧 env > 默认。 */
export function parseStorageProfile(env: NodeJS.ProcessEnv = process.env): StorageProfile {
  const explicit = normalize(env[STORAGE_PROFILE_ENV]);
  if (explicit === "zeroDisk".toLowerCase() || explicit === "zerodisk") return "zeroDisk";
  if (explicit === "strictnosqlite") return "strictNoSqlite";
  if (explicit === "relocated") return "relocated";
  if (explicit === "off") return "off";

  const legacy = parseLegacyDbMode(env[LEGACY_DB_SINK_ENV]);
  if (legacy !== undefined) {
    if (legacy === "off") return "off";
    if (legacy === "relocate") return "relocated";
    // observe 保持“不改变落盘”，但不再作为新档位；memory 走 zeroDisk。
    return legacy === "memory" ? "zeroDisk" : "off";
  }
  return DEFAULT_STORAGE_PROFILE;
}

/** 档位 → 策略（唯一映射）。 */
export function policyForProfile(profile: StorageProfile): StoragePolicy {
  switch (profile) {
    case "off":
      return { profile, fs: "off", db: "off", strictNoSqlite: false };
    case "relocated":
      return { profile, fs: "memory", db: "relocate", strictNoSqlite: false };
    case "strictNoSqlite":
      return { profile, fs: "memory", db: "memory", strictNoSqlite: true };
    case "zeroDisk":
    default:
      return { profile: "zeroDisk", fs: "memory", db: "memory", strictNoSqlite: false };
  }
}

/** 解析策略（显式 policy 由调用方直接给出时优先）。 */
export function resolveStoragePolicy(
  env: NodeJS.ProcessEnv = process.env,
  explicit?: StorageProfile
): StoragePolicy {
  return policyForProfile(explicit ?? parseStorageProfile(env));
}
