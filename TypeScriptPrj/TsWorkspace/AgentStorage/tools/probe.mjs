/**
 * storage-probe — 严格零 SQLite / 零落盘探针（通过 `node --import` 预加载）。
 *
 * 用法：
 *   $env:STORAGE_PROBE_REPORT = "D:\tmp\storage-probe.json"
 *   node --import ./AgentStorage/tools/probe.mjs ./ClineApi/build/engine.js
 *
 * 输出（exit 时写）：{ nodeSqliteLoaded, databaseSyncConstructions, sqliteModulesLoaded, fsWrites }
 */

import Module from "node:module";
import * as fs from "node:fs";

const reportPath = process.env.STORAGE_PROBE_REPORT ?? "";
const state = {
  nodeSqliteLoaded: 0,
  databaseSyncConstructions: 0,
  sqliteModulesLoaded: [],
  fsWrites: [],
};

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
  const original = fs[method];
  if (typeof original !== "function") continue;
  fs[method] = function patched(...args) {
    try {
      state.fsWrites.push({ method, path: String(args[0]) });
    } catch {
      /* 记录失败不影响调用 */
    }
    return original.apply(this, args);
  };
}

function flush() {
  if (!reportPath) return;
  try {
    fs.writeFileSync(reportPath, JSON.stringify(state, null, 2), "utf8");
  } catch {
    /* 报告写失败不阻断退出 */
  }
}

process.on("exit", flush);
process.on("SIGTERM", () => {
  flush();
  process.exit(0);
});
