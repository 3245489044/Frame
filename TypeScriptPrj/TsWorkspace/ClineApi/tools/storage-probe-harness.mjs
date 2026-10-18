/**
 * storage-probe-harness — strictNoSqlite 运行时实证（由 `node --import probe.mjs` 预加载）。
 *
 * 证明目标（比“代码路径存在”更强）：
 *   1) Provider.sessionBackend 确实注入到 ClineCore.create({ sessionService })；
 *   2) SDK 是否**实际调用**注入后端（sentinel 负对照 + 计数后端正对照）；
 *   3) node:sqlite / DatabaseSync 是否被构造（由 probe.mjs 计数）。
 *
 * 运行：
 *   $env:AGENT_STORAGE_PROFILE='strictNoSqlite'
 *   $env:STORAGE_PROBE_REPORT='BuildOutput\strict-sqlite-probe.json'
 *   node --import <AgentStorage>/tools/probe.mjs <本文件>
 */
const state = (globalThis.__storageProbe ??= {
  nodeSqliteLoaded: 0,
  databaseSyncConstructions: 0,
  sqliteModulesLoaded: [],
  fsWrites: [],
  sessionServiceCalls: 0,
  sessionServiceCallsByMethod: {},
  sessionBackendInjected: false,
  sentinelThrown: false,
});

process.env.AGENT_STORAGE_PROFILE = process.env.AGENT_STORAGE_PROFILE || "strictNoSqlite";

try {
  const { installSink } = await import(new URL("../build/preload/sink-preload.js", import.meta.url));
  const installed = await installSink({ persistBridge: false });
  const host = installed.host;

  state.profile = host.policy.profile;
  state.sessionBackendInjected = host.sessionBackend !== undefined;

  const sdk = await import("@cline/sdk");

  // ---- 负对照：所有方法都抛错的 sentinel；若 SDK 不调用，则注入被静默忽略 ----
  let sentinelCalls = 0;
  const sentinel = new Proxy(
    {},
    {
      get() {
        return async () => {
          sentinelCalls += 1;
          throw new Error("SESSION_BACKEND_NOT_CONSUMED");
        };
      },
    }
  );
  try {
    const core = await sdk.ClineCore.create({
      clientName: "storage-probe-sentinel",
      backendMode: "local",
      sessionService: sentinel,
    });
    try {
      await core.list(1);
    } finally {
      await core.dispose?.();
    }
  } catch (err) {
    state.sentinelError = err instanceof Error ? err.message : String(err);
  }
  state.sentinelThrown = sentinelCalls > 0;
  state.sentinelCalls = sentinelCalls;

  // ---- 正对照：真实内存后端；统计 SDK 实际调用了哪些方法 ----
  if (host.sessionBackend !== undefined) {
    const core = await sdk.ClineCore.create({
      clientName: "storage-probe-counting",
      backendMode: "local",
      sessionService: host.sessionBackend,
    });
    try {
      await core.list(1);
    } finally {
      await core.dispose?.();
    }
    const stats = host.sessionBackend.stats?.();
    state.sessionServiceCalls = Number(stats?.total ?? 0);
    state.sessionServiceCallsByMethod = stats?.calls ?? {};
  }
} catch (err) {
  state.harnessError = err instanceof Error ? (err.stack ?? err.message) : String(err);
}

if (!state.profile) state.profile = process.env.AGENT_STORAGE_PROFILE;
