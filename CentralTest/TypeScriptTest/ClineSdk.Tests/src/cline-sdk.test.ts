import { describe, it, expect } from "vitest";
import { runProbe } from "./probe.js";

const LOCAL_TIMEOUT = 30_000;

/**
 * Cline SDK 独立调用测试 —— 不经过任何框架，只调 @cline/core。
 *
 * 目的：把「Cline 调用本身」从框架中剥离，独立复现/定位挂起，回答
 * 「挂起是不是 Cline 调用本身的问题」——已证实独立 create 约 2s 成功，不挂。
 */
describe("Cline SDK 独立调用（不经过任何框架）", () => {
  it("import + ClineCore.create(backendMode='local') 应成功且不挂", async () => {
    const r = await runProbe({ mode: "local", timeoutMs: LOCAL_TIMEOUT });
    expect(r.status).toBe("ok");
    expect(r.stderr).toContain("stage=create-ok");
    expect(r.durationMs).toBeLessThan(LOCAL_TIMEOUT);
  }, LOCAL_TIMEOUT + 5000);

  it("并发 3 个 create(backendMode='local') 应全部有界返回（复现框架 3-worker 场景）", async () => {
    const results = await Promise.all(
      [0, 1, 2].map(() => runProbe({ mode: "local", timeoutMs: LOCAL_TIMEOUT })),
    );
    // 关键断言：没有无限挂起——全部在超时内有界返回（ok/error/timeout 都算有界）。
    expect(results.every((r) => r.durationMs < LOCAL_TIMEOUT + 2000)).toBe(true);
    // 诊断摘要
    console.log(
      "[并发诊断] status=" + JSON.stringify(results.map((r) => r.status)) +
      " 耗时ms=" + JSON.stringify(results.map((r) => r.durationMs)),
    );
  }, LOCAL_TIMEOUT * 2 + 5000);

  it("create(backendMode='auto') 有界返回（诊断 hub/网络副作用；结果仅记录不断言）", async () => {
    const r = await runProbe({ mode: "auto", timeoutMs: 25_000 });
    console.log(
      "[auto诊断] status=" + r.status + " durationMs=" + r.durationMs +
      " stderr尾=" + r.stderr.trim().split("\n").slice(-3).join(" | "),
    );
    expect(r.durationMs).toBeLessThan(30_000);
  }, 30_000);
});
