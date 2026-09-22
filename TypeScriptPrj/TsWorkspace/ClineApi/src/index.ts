/**
 * ClineApi — 库导出
 */

export * from "./types.js";
export { ClineClient, type ClineClientOptions } from "./client.js";
export { runCline, runCliCommand } from "./cline-cli.js";
export { SdkTransport, readSdkVersion } from "./transport/sdk-transport.js";
export {
  installSink,
  getInstalledSink,
  observeSink,
  dbObserve,
  disposeSink,
  type SinkInstallOptions,
  type InstalledSink,
} from "./preload/sink-preload.js";
export type { DbSinkObserveFrame, DbSinkMode } from "fs-sink";
export {
  resolveClineDataRoot,
  resolveDefaultClineDataRoot,
  fromClineDir,
  type ClineDataRootPaths,
} from "./runtime/cline-data-root.js";
export { loadConfig, loadConfigFile, type ClineApiConfig } from "./config.js";
export {
  loadCredentialsFile,
  resolveCredentialsFile,
  getProviderCredential,
  type ClineCredentialsFile,
  type ProviderCredential,
} from "./credentials.js";
