/**
 * esm-binding-guard.test.ts —— 绑定自检必须能抓住「facade 早于 patch 建立」的 A 类失效（否则 fail-fast 形同虚设）。
 *
 * 本文件**故意**在 patch 前静态导入 node:fs：其 ESM 命名绑定被冻结为原函数，
 * 之后 patch 只改 CJS 属性 → verifyBindings() 必须返回非空，把「静默落盘」暴露为启动期失败。
 */

import { describe, it, expect, afterEach } from "vitest";
import * as fsNs from "node:fs";
import { createRequire } from "node:module";
import * as os from "node:os";
import * as path from "node:path";
import { AgentSinkHost, ClineRouteProvider } from "../src/index.js";

const require = createRequire(import.meta.url);

let host: AgentSinkHost | undefined;

function newHost(): AgentSinkHost {
  return new AgentSinkHost([
    new ClineRouteProvider({
      dataDir: path.join(os.tmpdir(), "fssink-esm-guard", `d-${Date.now()}`),
    }),
  ]);
}

describe("绑定自检（A 类失效可检出）", () => {
  afterEach(() => {
    host?.unpatchAll();
    host = undefined;
  });

  it("patch 前已建立 node:fs facade → verifyBindings 非空（fail-fast 判据成立）", async () => {
    host = newHost();
    host.patchAll();

    // 前置确认：patch 确实改了 CJS 属性（否则「非空」可能只是没 patch 的假阳性）。
    expect((require("node:fs") as { writeFileSync: unknown }).writeFileSync).not.toBe(
      fsNs.writeFileSync
    );

    const bindings = await host.verifyBindings();
    expect(bindings.length).toBeGreaterThan(0);
    expect(bindings).toContain("writeFileSync");
  });

  it("未 patch → verifyBindings 为空（不误报）", async () => {
    host = newHost();
    expect(await host.verifyBindings()).toEqual([]);
  });
});
