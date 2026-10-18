import { describe, it, expect, afterEach } from "vitest";
import * as fs from "node:fs";
import * as os from "node:os";
import * as path from "node:path";
import { createRequire } from "node:module";
import { MemoryVolume, type Route } from "fs-sink";
import {
  AgentStorageHost,
  ClaudeStorageProvider,
  FileAgentProvider,
  SESSION_BACKEND_METHODS,
  extractClaudeSessionId,
  parseStorageProfile,
  policyForProfile,
  type AgentStorageContext,
  type AgentStorageProvider,
} from "../src/index.js";

const require = createRequire(import.meta.url);
const temps: string[] = [];
const hosts: AgentStorageHost[] = [];

afterEach(() => {
  for (const h of hosts.splice(0)) h.dispose();
  for (const t of temps.splice(0)) {
    try {
      fs.rmSync(t, { recursive: true, force: true });
    } catch {
      /* 清理失败不阻断 */
    }
  }
});

function tempDir(tag: string): string {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), `agent-storage-${tag}-`));
  temps.push(dir);
  return dir;
}

function fakeProvider(dir: string): AgentStorageProvider {
  const sink = new MemoryVolume();
  const routes: Route[] = [
    { spec: { name: "fake-sessions", prefix: `${dir}/sessions/` }, sink, mirror: true },
  ];
  return {
    name: "fake",
    roots: [`${dir}/sessions`],
    dbRoots: [`${dir}/db`],
    extractSessionId: () => "s-1",
    extractors: [],
    buildRoutes: () => routes,
    context(env): AgentStorageContext {
      return {
        dataDir: dir,
        sessionsDir: `${dir}/sessions`,
        dbDir: `${dir}/db`,
        defaultDataDir: `${dir}/default`,
        defaultDbDir: `${dir}/default/db`,
        env,
      };
    },
    dbClaims(context, policy) {
      return [
        {
          name: "fake",
          roots: [`${context.dataDir}/db`],
          dbNames: ["sessions.db"],
          mode: policy.db,
        },
      ];
    },
    scratchRoots: (context) => [context.dbDir, context.sessionsDir],
  };
}

describe("storage policy", () => {
  it("默认 zeroDisk；显式 off 可回退；旧 CLINE_DB_SINK 映射", () => {
    expect(parseStorageProfile({})).toBe("zeroDisk");
    expect(parseStorageProfile({ AGENT_STORAGE_PROFILE: "off" })).toBe("off");
    expect(parseStorageProfile({ AGENT_STORAGE_PROFILE: "relocated" })).toBe("relocated");
    expect(parseStorageProfile({ CLINE_DB_SINK: "off" })).toBe("off");
    expect(parseStorageProfile({ CLINE_DB_SINK: "memory" })).toBe("zeroDisk");
    expect(policyForProfile("zeroDisk")).toMatchObject({ fs: "memory", db: "memory" });
    expect(policyForProfile("strictNoSqlite")).toMatchObject({ strictNoSqlite: true });
  });
});

describe("AgentStorageHost", () => {
  it("zeroDisk：会话路由走内存卷、DB 走 :memory:，磁盘零残留", async () => {
    const dir = tempDir("zero");
    const host = await AgentStorageHost.install({
      provider: fakeProvider(dir),
      env: {},
      verifyBindings: false,
    });
    hosts.push(host);

    expect(host.policy.profile).toBe("zeroDisk");
    const runtimeFs = require("node:fs") as typeof fs;
    runtimeFs.mkdirSync(`${dir}/sessions/s-1`, { recursive: true });
    runtimeFs.writeFileSync(`${dir}/sessions/s-1/a.messages.json`, "[]");
    expect(fs.existsSync(`${dir}/sessions/s-1/a.messages.json`)).toBe(false);

    const Shim = (require("node:sqlite") as Record<string, any>).DatabaseSync;
    const db: any = new Shim(`${dir}/db/sessions.db`);
    db.exec("CREATE TABLE t (id INTEGER)");
    expect(fs.existsSync(`${dir}/db/sessions.db`)).toBe(false);

    const obs = host.observe();
    expect(obs.db.mode).toBe("memory");
    expect(obs.db.driver).toBe("node:sqlite");
    expect(obs.db.memoryHits).toBe(1);
    expect(obs.fs.perSdk.fake.misses).toBe(0);
    expect(obs.fs.bindings).toEqual([]);
  });

  it("off：不 patch，真实落盘（一键回退）", async () => {
    const dir = tempDir("off");
    const host = await AgentStorageHost.install({
      provider: fakeProvider(dir),
      env: {},
      profile: "off",
      verifyBindings: false,
    });
    hosts.push(host);
    fs.mkdirSync(`${dir}/sessions/s-1`, { recursive: true });
    fs.writeFileSync(`${dir}/sessions/s-1/a.messages.json`, "[]");
    expect(fs.existsSync(`${dir}/sessions/s-1/a.messages.json`)).toBe(true);
    expect(host.observe().db.mode).toBe("off");
  });
});

describe("InMemorySessionService", () => {
  it("契约方法齐备（strictNoSqlite 灰度）", async () => {
    const mod = await import("../src/session-backend.js");
    const service = mod.createInMemorySessionService() as Record<string, any>;
    for (const m of SESSION_BACKEND_METHODS) expect(typeof service[m]).toBe("function");
    const created = await service.createSession({ sessionId: "s-1" });
    expect(created.id).toBe("s-1");
    await service.appendMessage("s-1", { role: "user" });
    expect((await service.getMessages("s-1")).length).toBe(1);
  });
});

describe("FileAgentProvider（Claude 等文件型 Agent 骨架）", () => {
  it("核心零修改：仅声明数据根/路由，DB 面为空", () => {
    const dir = tempDir("claude");
    const provider = new FileAgentProvider({ name: "claude", dataDir: dir });
    const ctx = provider.context({});
    expect(ctx.sessionsDir).toBe(`${dir}\\sessions`);
    expect(provider.dbClaims()).toEqual([]);
    const routes = provider.buildRoutes();
    expect(routes.length).toBeGreaterThan(0);
    expect(routes[0].spec.name).toBe("claude-sessions");
    expect(provider.extractSessionId(`${dir}\\sessions\\s-1\\x.messages.json`)).toBe("s-1");
  });
});

describe("ClaudeStorageProvider（真实文件型布局）", () => {
  it("声明 projects/*.jsonl 路由、DB 面为空、残留显式", () => {
    const dir = tempDir("claude-real");
    const provider = new ClaudeStorageProvider({ dataDir: dir });
    const ctx = provider.context({});
    expect(ctx.sessionsDir).toBe(`${dir}\\projects`);
    expect(ctx.dbDir).toBe(`${dir}\\db`);
    expect(provider.dbClaims()).toEqual([]);

    const routes = provider.buildRoutes();
    expect(routes.map((r) => r.spec.name)).toEqual([
      "claude-projects",
      "claude-transcripts",
      "claude-manifest",
    ]);
    expect(provider.residualPaths(ctx).length).toBe(2);
  });

  it("sessionId 从 projects/<cwd>/<id>.jsonl 解析", () => {
    expect(extractClaudeSessionId("C:\\Users\\u\\.claude\\projects\\D--Proj-Main\\abc-123.jsonl")).toBe("abc-123");
    expect(extractClaudeSessionId("/home/u/.claude/projects/-repo/def.jsonl")).toBe("def");
    expect(extractClaudeSessionId("/home/u/.claude/history.jsonl")).toBeUndefined();
  });
});
