/**
 * storage-probe — 严格零 SQLite / 零落盘探针（通过 `node --import` 预加载）。
 *
 * 用法：
 *   $env:STORAGE_PROBE_REPORT = "D:\tmp\storage-probe.json"
 *   node --import ./AgentStorage/tools/probe.mjs ./ClineApi/build/engine.js
 *
 * 输出（exit 时写）：{ nodeSqliteLoaded, databaseSyncConstructions, sqliteModulesLoaded, fsWrites }
 */

import Module, { createRequire } from "node:module";

// ESM 命名空间是只读的；打补丁必须落在 CJS 导出对象上（probe 先于目标加载）。
const requireCjs = createRequire(import.meta.url);
const fsCjs = requireCjs("node:fs");

const reportPath = process.env.STORAGE_PROBE_REPORT ?? "";
const state = {
  nodeSqliteLoaded: 0,
  databaseSyncConstructions: 0,
  sqliteModulesLoaded: [],
  fsWrites: [],
  // 由 storage-probe-harness 合并：注入后端被 SDK 实际调用的方法总数。
  sessionServiceCalls: 0,
  sessionServiceCallsByMethod: {},
  sessionBackendInjected: false,
  sentinelThrown: false,
};

// 供 harness 合并额外判据（同一对象 ⇒ exit 时统一写盘）。
globalThis.__storageProbe = state;

const sqliteIds = new Set(["node:sqlite", "sqlite", "better-sqlite3", "bun:sqlite"]);
const originalLoad = Module._load;
Module._load = function patchedLoad(request, parent, isMain) {
  if (sqliteIds.has(request)) {
    state.nodeSqliteLoaded += request === "node:sqlite" || request === "sqlite" ? 1 : 0;
    state.sqliteModulesLoaded.push(request);
  }
  const exports = originalLoad.call(this, request, parent, isMain);
  if (
    (request === "node:sqlite" || request === "sqlite") &&
    exports &&
    typeof exports === "object" &&
    typeof exports.DatabaseSync === "function"
  ) {
    const Real = exports.DatabaseSync;
    exports.DatabaseSync = new Proxy(Real, {
      construct(target, args) {
        state.databaseSyncConstructions += 1;
        return Reflect.construct(target, args);
      },
    });
  }
  return exports;
};

// 记录写盘路径（仅用于诊断；不改变行为）。
for (const method of ["writeFileSync", "appendFileSync", "mkdirSync", "openSync"]) {
  const original = fsCjs[method];
  if (typeof original !== "function") continue;
  try {
    fsCjs[method] = function patched(...args) {
      try {
        state.fsWrites.push({ method, path: String(args[0]) });
      } catch {
        /* 记录失败不影响调用 */
      }
      return original.apply(this, args);
    };
  } catch {
    /* 只读绑定（运行时不允许 patch）⇒ 放弃该方法的诊断计数，不影响主判据 */
  }
}

function flush() {
  if (!reportPath) return;
  try {
    // 排除报告自身写入（否则每次探针都自证一次 fsWrites）
    state.fsWrites = state.fsWrites.filter((w) => String(w.path) !== reportPath);
    fsCjs.writeFileSync(reportPath, JSON.stringify(state, null, 2), "utf8");
  } catch {
    /* 报告写失败不阻断退出 */
  }
}

process.on("exit", flush);
process.on("SIGTERM", () => {
  flush();
  process.exit(0);
});
