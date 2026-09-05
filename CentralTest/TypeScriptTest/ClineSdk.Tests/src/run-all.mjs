#!/usr/bin/env node
/**
 * Cline SDK 独立测试 —— 无 vitest 依赖、纯 node 运行（`npm run test:cli`）。
 *
 * 与 vitest 用例（cline-sdk.test.ts）共用同一套子进程机制（runner.mjs + 进程级硬超时），
 * 供未安装 vitest / 无网络的环境快速复现与验证 Cline 调用本身。
 */
import { spawn } from "node:child_process";
import path from "node:path";
import { existsSync } from "node:fs";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));

function resolveSdkEntry() {
  let dir = here;
  for (let i = 0; i < 8; i++) {
    const c = path.join(dir, "TypeScriptPrj", "TsWorkspace", "ClineApi", "node_modules", "@cline", "sdk", "dist", "index.js");
    if (existsSync(c)) return c;
    const parent = path.dirname(dir);
    if (parent === dir) break;
    dir = parent;
  }
  throw new Error("未找到 @cline/sdk 入口");
}

const sdkEntry = resolveSdkEntry();
const runner = path.join(here, "runner.mjs");

function runProbe({ mode, timeoutMs }) {
  return new Promise((resolve) => {
    const child = spawn(process.execPath, [runner, sdkEntry, mode], { windowsHide: true });
    let out = "", err = "", timedOut = false;
    child.stdout.on("data", (d) => (out += d));
    child.stderr.on("data", (d) => (err += d));
    const t0 = Date.now();
    const timer = setTimeout(() => { timedOut = true; try { child.kill("SIGKILL"); } catch {} }, timeoutMs);
    child.on("close", () => {
      clearTimeout(timer);
      resolve({ status: timedOut ? "timeout" : out.includes("RESULT:ok") ? "ok" : "error", durationMs: Date.now() - t0, stderr: err });
    });
  });
}

async function main() {
  console.log("=== Cline SDK 独立测试（纯 node，不经框架）===");
  console.log("SDK 入口:", sdkEntry.replaceAll("\\", "/"), "\n");

  for (const c of [
    { name: "[1] create(backendMode=local)", run: () => runProbe({ mode: "local", timeoutMs: 25_000 }) },
    { name: "[2] create(backendMode=auto)", run: () => runProbe({ mode: "auto", timeoutMs: 25_000 }) },
  ]) {
    process.stdout.write(c.name + " ... ");
    const r = await c.run();
    console.log(`${r.status.padEnd(7)} ${r.durationMs}ms`);
    if (r.stderr.trim()) {
      console.log("    stderr尾: " + r.stderr.trim().split("\n").slice(-2).join(" | "));
    }
  }

  console.log("\n[3] 并发 3×create(local) ...");
  const t0 = Date.now();
  const res = await Promise.all([0, 1, 2].map(() => runProbe({ mode: "local", timeoutMs: 25_000 })));
  console.log("    状态:", res.map((r) => r.status).join(","), "| 耗时ms:", JSON.stringify(res.map((r) => r.durationMs)), "| 总耗时", Date.now() - t0, "ms");

  console.log("\n完成。");
}

main().catch((e) => { console.error("框架执行失败:", e.message); process.exit(1); });
