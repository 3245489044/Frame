#!/usr/bin/env node
/**
 * Cline 调用执行器（框架的「执行面」，独立子进程）。
 *
 * 用法: node runner.mjs <sdkEntryAbsPath> <backendMode> [--start]
 *
 * 阶段日志 → stderr（同步 flush，保证阻塞发生前的阶段必然可见）；
 * 终态 → stdout 一行 `RESULT:<ok|error> ...`（供父进程结构化判读）。
 *
 * 父进程（probe.ts / run-all.mjs）以「进程级硬超时」包住本脚本：
 * 即便 @cline/core 同步阻塞事件循环（JS 超时失效），父进程也能 kill 兜底。
 */
import { pathToFileURL } from "node:url";

const entry = process.argv[2];
const mode = process.argv[3] || "local";
const doStart = process.argv.includes("--start");

const t0 = Date.now();
const log = (...a) => console.error(`[runner ${Date.now() - t0}ms]`, ...a);

try {
  log("node", process.version);
  log("entry", entry);
  log("mode", mode, "| start", doStart);

  // ① 模块加载
  log("stage=import");
  const mod = await import(pathToFileURL(entry).href);
  log("stage=import-ok", "ClineCore=" + typeof mod.ClineCore);

  // ② ClineCore.create（挂起高发点）
  log(`stage=create backendMode=${mode}`);
  const core = await mod.ClineCore.create({ clientName: "cline-sdk-test", backendMode: mode });
  log("stage=create-ok", "runtimeAddress=" + (core?.runtimeAddress ?? "n/a"));

  // ③（可选）start —— 验证能否真正起一个会话
  let sessionId = "";
  if (doStart) {
    log("stage=start");
    const r = await core.start({
      config: { providerId: "local", modelId: "local-llm", apiKey: "local" },
      prompt: "请用一句话回复",
      source: "cline-sdk-test",
      mode: "act",
    });
    sessionId = r?.sessionId ?? r?.startSessionResult?.sessionId ?? "?";
    log("stage=start-ok", "sessionId=" + sessionId);
  }

  console.log(`RESULT:ok mode=${mode} start=${doStart} durationMs=${Date.now() - t0} sessionId=${sessionId}`);
  process.exit(0);
} catch (e) {
  const msg = e instanceof Error ? e.message : String(e);
  console.log(`RESULT:error mode=${mode} durationMs=${Date.now() - t0} message=${msg.slice(0, 600)}`);
  process.exit(1);
}
