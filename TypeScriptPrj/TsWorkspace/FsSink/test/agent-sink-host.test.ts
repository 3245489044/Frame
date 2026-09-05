/**
 * AgentSinkHost 集成测试 —— 覆盖通用验收用例（文档 §5.2）：
 *   命中写→磁盘零残留 / 读回闭环 / 放行未命中 / 流式写 / 目录递归 mkdir /
 *   提取+落库 / miss 观测 / 默认根回退拦截 / 多 provider 并行 / sdk 隔离。
 *
 * 顶层静态 `import * as fs` 在 patch 前已对 node:fs 命名空间快照 → 其方法始终是「原函数」，
 * 用其断言真实磁盘状态；用 createRequire 动态读取当前属性 → 调用到 patch 后的方法（内存卷拦截）。
 */

import { describe, it, expect, beforeEach, afterEach } from "vitest";
import * as fs from "node:fs";
import { createRequire } from "node:module";
import * as path from "node:path";
import * as os from "node:os";
import {
  AgentSinkHost,
  ClineRouteProvider,
  MemoryVolume,
  normalizeFsPath,
  InMemoryDbBackend,
  BridgeToCSharpDbBackend,
  DaemonIpcPersistenceTransport,
  type AgentRouteProvider,
  type ArtifactExtractor,
  type Route,
} from "../src/index.js";
import { ClineMessagesExtractor } from "../src/cline/cline-extractor.js";

const require = createRequire(import.meta.url);
const rf = (): Record<string, any> => require("node:fs") as Record<string, any>;

let tmpRoot: string;

/** 最小 Claude 占位 provider：独立根 + 独立 sdk，用于多 SDK / sdk 隔离验证。 */
class FakeClaudeProvider implements AgentRouteProvider {
  readonly name = "claude";
  readonly roots: string[];
  readonly extractSessionId = (p: string): string | undefined =>
    /\/sessions\/([^/]+)\/.*\.messages\.json$/i.exec(p)?.[1];
  readonly extractors: ArtifactExtractor[] = [new ClineMessagesExtractor()];

  constructor(root: string) {
    this.roots = [normalizeFsPath(root)];
  }

  buildRoutes(): Route[] {
    const sink = new MemoryVolume();
    return [
      {
        spec: { name: "claude-messages", prefix: `${this.roots[0]}/data/sessions/` },
        sink,
        mirror: true,
      },
    ];
  }
}

function newTempDir(tag: string): string {
  return path.join(os.tmpdir(), "fssink-host", `${tag}-${Date.now()}-${Math.random()}`);
}

describe("AgentSinkHost — 通用验收（ClineRouteProvider 参数化）", () => {
  let host: AgentSinkHost;
  let home: string;
  let msgsPath: string;
  let otherPath: string;

  beforeEach(() => {
    tmpRoot = newTempDir("case");
    home = path.join(tmpRoot, "home");
    msgsPath = path.join(home, "data", "sessions", "sid1", "sid1.messages.json");
    otherPath = path.join(tmpRoot, "src", "a.cs");
    host = new AgentSinkHost([new ClineRouteProvider({ home })]);
    host.patchAll();
  });

  afterEach(() => {
    host.unpatchAll();
    try {
      fs.rmSync(tmpRoot, { recursive: true, force: true });
    } catch {
      /* 忽略清理失败 */
    }
  });

  it("1. 命中写 → 磁盘零残留 + 内存卷存在", () => {
    rf().writeFileSync(msgsPath, JSON.stringify({ a: 1 }));
    expect(fs.existsSync(msgsPath)).toBe(false);
    expect(rf().existsSync(msgsPath)).toBe(true);
  });

  it("2. 读回闭环：内容一致", () => {
    const content = JSON.stringify({ text: "hello" });
    rf().writeFileSync(msgsPath, content);
    expect(rf().readFileSync(msgsPath, "utf8")).toBe(content);
  });

  it("3. 放行未命中 → 真实落盘", () => {
    fs.mkdirSync(path.dirname(otherPath), { recursive: true });
    rf().writeFileSync(otherPath, "class A {}");
    expect(fs.existsSync(otherPath)).toBe(true);
  });

  it("4. 流式写 close → 内存卷可见、磁盘不可见", async () => {
    await new Promise<void>((resolve, reject) => {
      const ws = rf().createWriteStream(msgsPath);
      ws.on("close", () => resolve());
      ws.on("error", reject);
      ws.end(JSON.stringify({ stream: true }));
    });
    expect(fs.existsSync(msgsPath)).toBe(false);
    expect(rf().existsSync(msgsPath)).toBe(true);
  });

  it("5. 目录递归 mkdir → 内存成立、磁盘不落", () => {
    const dir = path.join(home, "data", "sessions", "sid1");
    rf().mkdirSync(dir, { recursive: true });
    expect(fs.existsSync(dir)).toBe(false);
    expect(rf().existsSync(dir)).toBe(true);
  });

  it("6. 提取 + 落库：flushAll → getMessages(cline, sid) 返回消息", async () => {
    const msgs = [{ role: "user", content: "hi" }];
    rf().writeFileSync(msgsPath, JSON.stringify({ messages: msgs }));
    await host.flushAll();
    expect(await host.getMessages("cline", "sid1")).toEqual(msgs);
    expect(host.getMessagesSync("cline", "sid1")).toEqual(msgs);
  });

  it("7. miss 观测：命中根候选但未拦截的写计为该 sdk miss，命中路由计 hit", () => {
    // 真实父目录先建（miss/透传写走 realFs，writeFileSync 不自动建目录）
    fs.mkdirSync(home, { recursive: true });
    fs.mkdirSync(path.dirname(otherPath), { recursive: true });
    // 根下但未命中路由（无 data/sessions 前缀）→ 落根内却未拦截 → miss
    const leaked = path.join(home, "other.json");
    rf().writeFileSync(leaked, "x");
    // 工具源码路径不在任何根下 → 不计 miss
    rf().writeFileSync(otherPath, "y");
    // 命中路由的写 → 计 hit
    rf().writeFileSync(msgsPath, JSON.stringify({ messages: [] }));
    const obs = host.observe();
    expect(obs.perSdk["cline"].misses).toBeGreaterThanOrEqual(1);
    expect(obs.perSdk["cline"].hits).toBeGreaterThanOrEqual(1);
  });

  it("8. 默认根回退也被拦截（双候选根）", () => {
    const defaultHome = path.join(os.homedir(), ".cline");
    const fallbackPath = path.join(defaultHome, "data", "sessions", "sidX", "sidX.messages.json");
    rf().writeFileSync(fallbackPath, JSON.stringify({ messages: [] }));
    expect(fs.existsSync(fallbackPath)).toBe(false);
    expect(rf().existsSync(fallbackPath)).toBe(true);
  });
});

describe("AgentSinkHost — 多 SDK 并行 + sdk 隔离 + observe 帧", () => {
  it("同进程并行 patch Cline + Claude，同名 sessionId 不串库", async () => {
    tmpRoot = newTempDir("multi");
    const clineHome = path.join(tmpRoot, "cline");
    const claudeHome = path.join(tmpRoot, "claude");
    const host = new AgentSinkHost([
      new ClineRouteProvider({ home: clineHome }),
      new FakeClaudeProvider(claudeHome),
    ]);
    host.patchAll();
    try {
      const clineMsg = path.join(clineHome, "data", "sessions", "abc", "abc.messages.json");
      const claudeMsg = path.join(claudeHome, "data", "sessions", "abc", "abc.messages.json");
      rf().writeFileSync(clineMsg, JSON.stringify({ messages: [{ role: "user", text: "cline" }] }));
      rf().writeFileSync(claudeMsg, JSON.stringify({ messages: [{ role: "user", text: "claude" }] }));
      await host.flushAll();
      // 同名 sessionId "abc"，两 sdk 各自独立
      const clineMsgs = await host.getMessages("cline", "abc");
      const claudeMsgs = await host.getMessages("claude", "abc");
      expect(clineMsgs).toEqual([{ role: "user", text: "cline" }]);
      expect(claudeMsgs).toEqual([{ role: "user", text: "claude" }]);
      // observe 帧含两 sdk 独立命中
      const obs = host.observe();
      expect(obs.perSdk["cline"]).toBeDefined();
      expect(obs.perSdk["claude"]).toBeDefined();
    } finally {
      host.unpatchAll();
      try {
        fs.rmSync(tmpRoot, { recursive: true, force: true });
      } catch {
        /* 忽略 */
      }
    }
  });

  it("observe 返回 perRoute / perSdk / archives 结构", async () => {
    tmpRoot = newTempDir("obs");
    const host = new AgentSinkHost([new ClineRouteProvider({ home: path.join(tmpRoot, "h") })]);
    host.patchAll();
    try {
      const msg = path.join(tmpRoot, "h", "data", "sessions", "s", "s.messages.json");
      rf().writeFileSync(msg, JSON.stringify({ messages: [{ role: "assistant", text: "ok" }] }));
      await host.flushAll();
      const obs = host.observe();
      expect(obs.archives.attempted).toBeGreaterThanOrEqual(1);
      expect(obs.archives.failed).toBe(0);
      expect(obs.perRoute["cline-sessions"]).toBeGreaterThanOrEqual(1);
    } finally {
      host.unpatchAll();
      try {
        fs.rmSync(tmpRoot, { recursive: true, force: true });
      } catch {
        /* 忽略 */
      }
    }
  });
});

describe("BridgeToCSharpDbBackend — 写侧推送到传输 + 本地读缓存", () => {
  it("writeMessages 落到本地缓存并推送到传输；getMessages 从本地读", async () => {
    const frames: Array<Record<string, unknown>> = [];
    const transport = new DaemonIpcPersistenceTransport((f) => frames.push(f));
    const backend = new BridgeToCSharpDbBackend(transport);
    const msgs = [{ role: "user", content: "hi" }];
    await backend.writeMessages("cline", "s1", msgs);
    // 本地缓存读回
    expect(backend.getMessagesSync("cline", "s1")).toEqual(msgs);
    expect(await backend.getMessages("cline", "s1")).toEqual(msgs);
    // 传输收到 persist 帧（messages）
    expect(frames).toHaveLength(1);
    const frame = frames[0] as { type: string; detail: { kind: string; sdk: string; sessionId: string } };
    expect(frame.type).toBe("persist");
    expect(frame.detail.kind).toBe("messages");
    expect(frame.detail.sdk).toBe("cline");
    expect(frame.detail.sessionId).toBe("s1");
  });

  it("sdk 维度命名空间隔离：同名 sessionId 不同 sdk 不串库", async () => {
    const backend = new BridgeToCSharpDbBackend(new DaemonIpcPersistenceTransport(() => {}));
    await backend.writeMessages("cline", "abc", [{ role: "user", text: "cline" }]);
    await backend.writeMessages("claude", "abc", [{ role: "user", text: "claude" }]);
    expect(backend.getMessagesSync("cline", "abc")).toEqual([{ role: "user", text: "cline" }]);
    expect(backend.getMessagesSync("claude", "abc")).toEqual([{ role: "user", text: "claude" }]);
  });

  it("InMemoryDbBackend keys/sdks 测试辅助", async () => {
    const db = new InMemoryDbBackend();
    await db.writeMessages("cline", "s1", []);
    await db.upsertSession("claude", "s2", { title: "x" });
    expect(db.sdks()).toContain("cline");
    expect(db.sdks()).toContain("claude");
    expect(db.keys("cline")).toEqual(["s1"]);
    expect(db.keys("claude")).toEqual(["s2"]);
  });
});

