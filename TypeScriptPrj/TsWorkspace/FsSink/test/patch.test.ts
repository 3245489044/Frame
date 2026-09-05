/**
 * patch.ts 全局入口测试 —— patch 幂等 / isPatched / unpatch 恢复。
 */

import { describe, it, expect } from "vitest";
import * as fs from "node:fs";
import { createRequire } from "node:module";
import * as path from "node:path";
import * as os from "node:os";
import { patchFs, unpatchFs, isPatched, getFsSink } from "../src/core/patch.js";
import { clineRoutes } from "../src/cline/cline-route.js";

const require = createRequire(import.meta.url);
const rf = (): Record<string, any> => require("node:fs") as Record<string, any>;

describe("patch.ts — preload 入口", () => {
  it("patchFs 幂等 + isPatched 状态 + 单例复用", () => {
    const home = path.join(os.tmpdir(), "fssink-patch", `idem-${Date.now()}`);
    try {
      expect(isPatched()).toBe(false);
      const s1 = patchFs(clineRoutes({ home }));
      expect(isPatched()).toBe(true);
      const s2 = patchFs(clineRoutes({ home })); // 再次调用：幂等
      expect(getFsSink()).toBe(s1);
      expect(s2).toBe(s1);
      // 幂等：第二次 patch 不重复挂镜像
      expect(s1.hits).toBe(0);
    } finally {
      unpatchFs();
    }
  });

  it("unpatchFs 恢复原 fs 行为", () => {
    const home = path.join(os.tmpdir(), "fssink-patch", `restore-${Date.now()}`);
    patchFs(clineRoutes({ home }));
    try {
      expect(rf().writeFileSync).not.toBe(fs.writeFileSync);
      // ESM 命名绑定是加载时快照：静态 fs.writeFileSync 仍指向原函数，patch 只作用于 CJS 属性
      expect(rf().writeFileSync).not.toBe(fs.writeFileSync);
    } finally {
      unpatchFs();
    }
    expect(isPatched()).toBe(false);
    expect(rf().writeFileSync).toBe(fs.writeFileSync);
  });
});
