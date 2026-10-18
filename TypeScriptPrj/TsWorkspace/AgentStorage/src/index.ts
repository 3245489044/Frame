/** AgentStorage — 通用 Agent 存储契约（FS 路由 + DB 接管 + 统一策略/观测）。 */

export type {
  AgentStorageContext,
  AgentStorageProvider,
  AgentStorageRoute,
  DbClaim,
  StoragePolicy,
  StorageProfile,
} from "./contract.js";
export {
  DEFAULT_STORAGE_PROFILE,
  LEGACY_DB_SINK_ENV,
  STORAGE_PROFILES,
  STORAGE_PROFILE_ENV,
  STORAGE_WORKER_ROOT_ENV,
  parseStorageProfile,
  parseLegacyDbMode,
  policyForProfile,
  resolveStoragePolicy,
} from "./policy.js";
export { AgentStorageHost } from "./host.js";
export type { AgentStorageInstallOptions, AgentStorageObserveFrame } from "./host.js";
export { InMemorySessionService, SESSION_BACKEND_METHODS, createInMemorySessionService } from "./session-backend.js";
export { NODE_SQLITE_DRIVER } from "./driver.js";
export type { DbDriverAdapter } from "./driver.js";
export { FileAgentProvider } from "./providers/file-agent-provider.js";
export type { FileAgentProviderOptions } from "./providers/file-agent-provider.js";

