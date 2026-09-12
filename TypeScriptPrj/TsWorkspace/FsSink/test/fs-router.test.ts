/**
 * FsRouter / FsSink 集成测试 —— 覆盖实施文档 §12 核心用例。
 *
 * 关键机制（Spike 已证）：patch 替换的是 CJS `require('node:fs')` 的 module.exports 属性；
 * 本文件顶层静态 `import * as fs` 在 patch 前已对 node:fs 命名空间快照 → 其方法始终是「原函数」，
 * 因此用 `fs.existsSync/writeFileSync` 断言真实磁盘状态（磁盘零残留 / 放行落盘）；
 * 用 createRequire 动态读取当前属性 → 调用到 patch 后的方法（内存卷拦截 / 读回闭环）。
 */

import { describe, it, expect, beforeEach, afterEach } from "vitest";
import * as fs from "node:fs";
import { createRequire } from "node:module";
import * as path from "node:path";
import * as os from "node:os";
import { FsSink, clineRoutes, globToRegExp } from "@fs-sink";

const require = createRequire(import.meta.url);
// 当前（可能已 patch）的 node:fs 模块属性 —— 动态读取，能看到 patch。
const rf = (): Record<string, any> => require("node:fs") as Record<string, any>;

let tmpRoot: string;

describe("FsSink — 拦截/放行/读回闭环", () => {
  let sink: FsSink;
  let dataDir: string;
  let msgsPath: string;
  let otherPath: string;

  beforeEach(() => {
    tmpRoot = path.join(os.tmpdir(), "fssink-test", `case-${Date.now()}-${Math.random()}`);
    dataDir = path.join(tmpRoot, "home", "data");
    msgsPath = path.join(dataDir, "sessions", "sid1", "sid1.messages.json");
    otherPath = path.join(tmpRoot, "src", "a.cs");
    sink = new FsSink();
    sink.patchFs(clineRoutes({ dataDir }));
  });

  afterEach(() => {
    sink.unpatchFs();
    try {
      fs.rmSync(tmpRoot, { recursive: true, force: true });
    } catch {
      /* 忽略清理失败 */
    }
  });

  it("写命中规则：磁盘零残留 + 内存卷存在", () => {
    rf().writeFileSync(msgsPath, JSON.stringify({ a: 1 }));
    // 真实磁盘（静态快照=原函数）不存在
    expect(fs.existsSync(msgsPath)).toBe(false);
    // 内存卷（patch 后）存在
    expect(rf().existsSync(msgsPath)).toBe(true);
  });

  it("读回闭环：内容一致", () => {
    const content = JSON.stringify({ text: "hello" });
    rf().writeFileSync(msgsPath, content);
    expect(rf().readFileSync(msgsPath, "utf8")).toBe(content);
  });

  it("放行未命中：真实磁盘落盘，内存卷不含", () => {
    // 真实磁盘先建父目录（原函数 writeFileSync 不会自动建目录）
    fs.mkdirSync(path.dirname(otherPath), { recursive: true });
    rf().writeFileSync(otherPath, "class A {}");
    expect(fs.existsSync(otherPath)).toBe(true);
    expect(rf().existsSync(msgsPath)).toBe(false);
  });

  it("流式写：close 后内存卷可见、磁盘不可见", async () => {
    await new Promise<void>((resolve, reject) => {
      const ws = rf().createWriteStream(msgsPath);
      ws.on("close", () => resolve());
      ws.on("error", reject);
      ws.end(JSON.stringify({ stream: true }));
    });
    expect(fs.existsSync(msgsPath)).toBe(false);
    expect(rf().existsSync(msgsPath)).toBe(true);
  });

  it("目录递归：mkdir recursive 命中前缀，内存卷成立、磁盘不落", () => {
    const dir = path.join(dataDir, "sessions", "sid1");
    rf().mkdirSync(dir, { recursive: true });
    expect(fs.existsSync(dir)).toBe(false);
    expect(rf().existsSync(dir)).toBe(true);
  });

  it("镜像落库：messages.json({messages}) → flushAll → getMessages 返回消息数组", async () => {
    const msgs = [
      { role: "user", content: "hi" },
      { role: "assistant", content: "ok" },
    ];
    // 真实 Cline 形状：{ messages: [...] }
    rf().writeFileSync(msgsPath, JSON.stringify({ messages: msgs }));
    await sink.flushAll();
    expect(await sink.getMessages("sid1")).toEqual(msgs);
    expect(sink.getMessagesSync("sid1")).toEqual(msgs);
  });

  it("防递归：一次写命中计数恰 +1（未递归重复路由）", () => {
    const before = sink.hits;
    rf().writeFileSync(msgsPath, "x");
    expect(sink.hits - before).toBe(1);
  });

  it("URL 路径命中路由：内存卷拦截、磁盘零残留", () => {
    // node:fs 允许 URL 作路径，此前会绕过拦截写真实磁盘（已修复）。
    const url = new URL(`file:///${msgsPath.replace(/\\/g, "/")}`);
    rf().writeFileSync(url, JSON.stringify({ u: 1 }));
    expect(fs.existsSync(msgsPath)).toBe(false);
    expect(rf().existsSync(msgsPath)).toBe(true);
  });

  it("回调式 mkdir 错误经回调交付而非同步抛出", async () => {
    // 先用文件占位，再用同路径 mkdir（非递归）→ 应报 EEXIST，且经回调返回而不是同步抛出。
    rf().writeFileSync(msgsPath, "x");
    const err = await new Promise<Error | null>((resolve) => {
      rf().mkdir(msgsPath, (e?: Error | null) => resolve(e ?? null));
    });
    expect(err).toBeInstanceOf(Error);
  });
});

describe("globToRegExp", () => {
  it("双星号加斜杠可匹配根级文件（零级目录）", () => {
    expect(globToRegExp("**/*.messages.json").test("root.messages.json")).toBe(true);
    expect(globToRegExp("**/*.messages.json").test("a/b/x.messages.json")).toBe(true);
    expect(globToRegExp("**/*.messages.json").test("a/x.messages.json")).toBe(true);
  });

  it("大小写不敏感（glob 与路径统一小写）", () => {
    expect(globToRegExp("**/*.MESSAGES.json").test("a/x.messages.json")).toBe(true);
    expect(globToRegExp("**/X.Messages.json").test("a/x.messages.json")).toBe(true);
  });

  it("单星号不跨目录", () => {
    expect(globToRegExp("a/*/c.json").test("a/b/c.json")).toBe(true);
    expect(globToRegExp("a/*/c.json").test("a/b/d/c.json")).toBe(false);
  });
});
