/**
 * esm-binding-run.mjs —— ESM 绑定闭环的独立进程取证脚本（由 esm-binding-happy.test.ts 以子进程拉起）。
 *
 * 为什么必须独立进程：ESM 命名绑定是「facade 首次求值时的快照」，测试框架自身会提前建立 node:fs facade，
 * 同进程内无法复现「patch 先于任何 ESM 导入 node:fs」的真实时序。
 *
 * 用法: node esm-binding-run.mjs <dataDir> <buildIndexPath>
 * 输出: 单行 JSON { bindings, hits, diskExists, memExists }
 *   - bindings: verifyBindings() 未对齐方法名（期望 []）
 *   - hits: 命中内存卷的调用计数（期望 > 0）
 *   - diskExists: 用「patch 前捕获的原函数」查真实磁盘（期望 false）
 *   - memExists: patch 后的 existsSync 查内存卷（期望 true）
 */

import { createRequire } from "node:module";
import { pathToFileURL } from "node:url";
import * as path from "node:path";

const require = createRequire(import.meta.url);
const [dataDir, buildIndexPath] = process.argv.slice(2);

// patch 前捕获原函数引用 —— patch 会替换 CJS 属性，故必须先取。
const realFs = require("node:fs");
const realExistsSync = realFs.existsSync.bind(realFs);

const { AgentSinkHost, ClineRouteProvider } = await import(pathToFileURL(buildIndexPath).href);

const host = new AgentSinkHost([new ClineRouteProvider({ dataDir })]);
host.patchAll();

const bindings = await host.verifyBindings();

const sessionDir = path.join(dataDir, "sessions", "esm-sid");
const target = path.join(sessionDir, "esm-sid.messages.json");
const { write } = await import(new URL("./esm-consumer.mjs", import.meta.url).href);
write(target);

// patch 后的 CJS 属性（= 包装函数）查内存卷。
const memExists = require("node:fs").existsSync(target);

console.log(
  JSON.stringify({
    bindings,
    hits: host.hits,
    diskExists: realExistsSync(target),
    memExists,
  })
);
