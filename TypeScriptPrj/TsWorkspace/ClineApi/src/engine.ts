#!/usr/bin/env node
/**
 * ClineEngine — 执行面入口（**bootstrap**）。
 *
 * 时序契约（H1，根因 A 的治本）：本文件先安装 FsSink（唯一 patch 时机），再动态加载主体
 * `engine-main`。主体静态图含 `node:fs` 消费者（config/credentials/sdk-transport），
 * 必须在 patch 之后才求值 —— 否则 SDK 的 `import { x } from "node:fs"` 会快照到原函数，拦截静默失效。
 *
 * 入口保持不变：daemon 仍 `spawn(node, [build/engine.js])`（见 daemon.ts resolveEngineScript）。
 * 绑定自检失败即抛错退出（fail-fast），由控制面 daemon 归因上报，不再静默落盘。
 */

async function bootstrap(): Promise<void> {
  const { installSink } = await import("./preload/sink-preload.js");
  // CLINE_FS_SINK_PERSIST_BRIDGE=1 → 写侧实时推 persist 帧到 C# ur_nodes；缺省走 InMemoryDbBackend
  // （archivedMessages 回传兜底），保持可回退。
  const persistBridge = process.env.CLINE_FS_SINK_PERSIST_BRIDGE === "1";
  const installed = await installSink({ persistBridge });

  const { startEngine } = await import("./engine-main.js");
  await startEngine({ sink: installed.sink });
}

bootstrap().catch((err) => {
  const message = err instanceof Error ? err.message : String(err);
  process.stderr.write(`[cline-engine] 启动失败: ${message}\n`);
  process.exit(1);
});