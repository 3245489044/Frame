#!/usr/bin/env node
/**
 * ClineApi — CLI 入口（**bootstrap**）。
 *
 * 时序契约（H1）：先安装 FsSink（唯一 patch 时机）→ 再动态 import 主体（`cli-main`）。
 * 本体静态图含 `node:fs` 消费者（config/credentials/sdk-transport），必须在 patch 之后才求值，
 * 否则 SDK 的 ESM 命名导入会快照到原函数 → 拦截静默失效（根因 A）。
 *
 * 用法：`node build/cli.js <命令> [选项]`（帮助见 `cli-main`）。
 */

async function bootstrap(): Promise<void> {
  const { installSink } = await import("./preload/sink-preload.js");
  // CLI 通道：无 C# 持久化桥（persistBridge 恒 false），拦截只做磁盘零残留。
  await installSink({ persistBridge: false });
  const { startCli } = await import("./cli-main.js");
  await startCli();
}

bootstrap()
  .then(() => process.exit(0))
  .catch((err) => {
    const message = err instanceof Error ? err.message : String(err);
    console.error(JSON.stringify({ ok: false, error: message }));
    process.exit(1);
  });