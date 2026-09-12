/**
 * esm-binding-happy.test.ts —— 真实 ESM 消费者的拦截闭环（I2/I3 的正向判据）。
 *
 * 本文件**不得**在补丁前静态导入 node:fs（顶层只 import vitest/node 工具），
 * 真正的消费者 fixture 走子进程：先 patch（installSink 等价路径）→ 再动态 import fixture
 * （fixture 内 `import { writeFileSync } from "node:fs"`）→ 写命中路径。
 * 期望：磁盘无文件；内存卷有内容；bindings == []；hits > 0。
 */

import { describe, it, expect } from "vitest";
import { execFileSync } from "node:child_process";
import { existsSync, mkdtempSync, rmSync } from "node:fs";
import * as os from "node:os";
import * as path from "node:path";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));
const runner = path.join(here, "support", "esm-binding-run.mjs");
// 子进程需 import 构建产物（TS 源码无法被 node 直接加载）；缺失即跳过（Phase 4 先 build 再 test）。
const buildIndex = path.resolve(here, "..", "build", "index.js");

interface RunResult {
  bindings: string[];
  hits: number;
  diskExists: boolean;
  memExists: boolean;
}

describe("ESM 绑定闭环（子进程真实时序）", () => {
  it.skipIf(!existsSync(buildIndex))(
    "patch 先于 ESM 导入 → 消费者被拦截、绑定自检通过、磁盘零残留",
    () => {
      const tmpRoot = mkdtempSync(path.join(os.tmpdir(), "fssink-esm-"));
      const dataDir = path.join(tmpRoot, "data");
      try {
        const out = execFileSync(process.execPath, [runner, dataDir, buildIndex], {
          encoding: "utf8",
        });
        const r = JSON.parse(out.trim().split(/\r?\n/).pop() as string) as RunResult;
        expect(r.bindings).toEqual([]); // 绑定自检通过
        expect(r.hits).toBeGreaterThan(0); // 拦截真的发生
        expect(r.memExists).toBe(true); // 内存卷有内容
        expect(r.diskExists).toBe(false); // 磁盘零残留
      } finally {
        rmSync(tmpRoot, { recursive: true, force: true });
      }
    }
  );
});
