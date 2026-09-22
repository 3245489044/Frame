/**
 * db-sink 单测 —— 模块层 sqlite shim 的契约（对齐 DbSink 文档 §7.1 与《零落盘方案对比》§3.5 的 D1–D5）。
 *
 * D1 白名单命中改写（memory：磁盘无库 + `db=:memory:`）
 * D2 根外/非白名单放行（真实落盘且 memoryHits 不增）
 * D3 安装失败 fail-open（不抛、形态退 off、真实库仍可打开）
 * D4 relocate 中间档（落受控根 + 保留 journal_mode=wal）
 * D5 TS 侧通透（prepare/exec/close 透传 + 未列名成员经 Proxy 兜底）+ unpatch 还原
 *
 * 测试文件可静态 import node:fs（洁净面 I1 只约束 src 侧 preload 闭包）。
 */

import { describe, it, expect, afterEach } from "vitest";
import * as fs from "node:fs";
import * as os from "node:os";
import * as path from "node:path";
import { createRequire } from "node:module";
import { DbSink } from "../src/db/db-sink.js";
import { ClineDbProvider } from "../src/cline/cline-db-provider.js";
import {
  parseDbSinkMode,
  routeDbPath,
  type DbRouteProvider,
  type DbSinkMode,
} from "../src/db/db-route-provider.js";

const require = createRequire(import.meta.url);
/** 每次 require 都为当前（可能已被 patch 的）导出视图；Real 在模块加载期捕获，作为对照真值。 */
const sqlite = (): Record<string, any> => require("node:sqlite") as Record<string, any>;
const RealDatabaseSync = sqlite().DatabaseSync;

const created: DbSink[] = [];
const temps: string[] = [];

afterEach(() => {
  for (const s of created.splice(0)) s.unpatchAll();
  for (const t of temps.splice(0)) {
    try {
      fs.rmSync(t, { recursive: true, force: true });
    } catch {
      /* 临时目录清理失败不阻断 */
    }
  }
});

function tempDir(tag: string): string {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), `fssink-dbsink-${tag}-`));
  temps.push(dir);
  return dir;
}

function provider(o: {
  roots: string[];
  mode: DbSinkMode;
  name?: string;
  claimRoots?: string[];
  dbNames?: string[];
  relocateRoot?: string;
}): DbRouteProvider {
  return {
    name: o.name ?? "cline",
    roots: o.roots,
    claimRoots: o.claimRoots,
    dbNames: o.dbNames,
    relocateRoot: o.relocateRoot,
    mode: () => o.mode,
  };
}

function patch(sink: DbSink): Record<string, any> {
  created.push(sink);
  expect(sink.patchAll()).toBe(true);
  return sqlite().DatabaseSync as Record<string, any>;
}

describe("db-route-provider — 形态解析与纯判定", () => {
  it("parseDbSinkMode：合法值透传，未设置/非法 → off", () => {
    expect(parseDbSinkMode("memory")).toBe("memory");
    expect(parseDbSinkMode(" RELOCATE ")).toBe("relocate");
    expect(parseDbSinkMode("observe")).toBe("observe");
    expect(parseDbSinkMode(undefined)).toBe("off");
    expect(parseDbSinkMode("bogus")).toBe("off");
  });

  it("ClineDbProvider：mode 由 env 注入决定（缺省 off = 零行为变更）", () => {
    const dataDir = path.join("C:\\", "iso", "worker-1", "data");
    const off = new ClineDbProvider({ dataDir, env: {} });
    expect(off.mode()).toBe("off");
    expect(off.roots).toEqual([path.join(dataDir, "db")]);
    expect(off.dbNames).toEqual(["sessions.db"]);
    const on = new ClineDbProvider({ dataDir, env: { CLINE_DB_SINK: "relocate" } });
    expect(on.mode()).toBe("relocate");
  });

  it("routeDbPath：未命中受控根 → passthrough/not-claimed", () => {
    const d = routeDbPath(path.join("C:\\", "outside", "sessions.db"), [
      provider({ roots: [path.join("C:\\", "db")], mode: "memory" }),
    ]);
    expect(d.kind).toBe("passthrough");
    expect(d.matched).toBe(false);
  });
});


describe("DbSink — 模块层拦截契约", () => {
  it("D1 白名单命中改写：磁盘无该库，落点为 :memory:", () => {
    const root = tempDir("d1");
    const dbPath = path.join(root, "sessions.db");
    const sink = new DbSink([provider({ roots: [root], dbNames: ["sessions.db"], mode: "memory" })]);
    const Shim = patch(sink);

    const db: any = new Shim(dbPath);
    db.exec("CREATE TABLE sessions (session_id TEXT PRIMARY KEY, status TEXT)");
    db.prepare("INSERT INTO sessions VALUES (?, ?)").run("s-1", "running");
    expect(db.prepare("SELECT count(*) AS n FROM sessions").get().n).toBe(1);

    expect(fs.existsSync(dbPath)).toBe(false); // 真值判据：磁盘无文件
    const obs = sink.observe();
    expect(obs.memoryHits).toBe(1);
    expect(obs.opened[0].db).toBe(":memory:");
    expect(obs.opened[0].path).toBe(dbPath);
    db.close();
  });

  it("D2 根外/非白名单放行：真实落盘且 memoryHits 不增", () => {
    const root = tempDir("d2");
    const outside = tempDir("d2-out");
    const cronDb = path.join(root, "cron.db"); // 受控根内非白名单
    const foreignDb = path.join(outside, "sessions.db"); // 根外
    const sink = new DbSink([provider({ roots: [root], dbNames: ["sessions.db"], mode: "memory" })]);
    const Shim = patch(sink);

    const a: any = new Shim(cronDb);
    a.exec("CREATE TABLE t (x INTEGER)");
    a.close();
    const b: any = new Shim(foreignDb);
    b.exec("CREATE TABLE t (x INTEGER)");
    b.close();

    expect(fs.existsSync(cronDb)).toBe(true);
    expect(fs.existsSync(foreignDb)).toBe(true);
    const obs = sink.observe();
    expect(obs.memoryHits).toBe(0);
    expect(obs.denied).toBe(2);
  });

  it("D3 安装失败 fail-open：不抛、形态退 off、真实库仍可打开", () => {
    const root = tempDir("d3");
    const dbPath = path.join(root, "sessions.db");

    const throwing = new DbSink([provider({ roots: [root], mode: "memory" })], {
      loadSqlite: () => {
        throw new Error("node:sqlite 不可用");
      },
    });
    created.push(throwing);
    expect(throwing.patchAll()).toBe(false);
    expect(throwing.isPatched).toBe(false);
    expect(throwing.observe().installError).toContain("node:sqlite 不可用");

    const empty = new DbSink([provider({ roots: [root], mode: "memory" })], {
      loadSqlite: () => ({}),
    });
    created.push(empty);
    expect(empty.patchAll()).toBe(false);
    expect(empty.observe().installError).toContain("DatabaseSync");

    // 真实库不受影响
    expect(sqlite().DatabaseSync).toBe(RealDatabaseSync);
    const real: any = new RealDatabaseSync(dbPath);
    real.exec("CREATE TABLE t (x INTEGER)");
    real.close();
    expect(fs.existsSync(dbPath)).toBe(true);
  });

  it("D4 relocate 中间档：落受控根（非无关根）且保留 journal_mode=wal", () => {
    const control = path.join(tempDir("d4-control"), "db");
    const foreign = path.join(tempDir("d4-foreign"), "db");
    const sink = new DbSink([
      provider({
        roots: [control],
        claimRoots: [foreign],
        relocateRoot: control,
        dbNames: ["sessions.db"],
        mode: "relocate",
      }),
    ]);
    const Shim = patch(sink);

    const requested = path.join(foreign, "sessions.db");
    const db: any = new Shim(requested);
    db.exec("PRAGMA journal_mode = WAL;");
    db.exec("CREATE TABLE sessions (session_id TEXT PRIMARY KEY)");
    expect(db.prepare("PRAGMA journal_mode").get().journal_mode).toBe("wal");

    expect(fs.existsSync(path.join(control, "sessions.db"))).toBe(true);
    expect(fs.existsSync(requested)).toBe(false);
    expect(sink.observe().relocated).toBe(1);
    db.close();
  });

  it("D5 TS 侧通透：prepare/exec/close 透传，未列名成员经 Proxy 兜底；unpatch 还原", () => {
    const root = tempDir("d5");
    const sink = new DbSink([provider({ roots: [root], mode: "memory" })]);
    const Shim = patch(sink);
    expect(Shim).not.toBe(RealDatabaseSync);

    const db: any = new Shim(path.join(root, "sessions.db"));
    expect(typeof db.close).toBe("function");
    expect("prepare" in db).toBe(true);
    db.exec("CREATE TABLE sessions (session_id TEXT PRIMARY KEY)");
    expect(db.prepare("SELECT 1 AS n").get().n).toBe(1);
    expect(db.someUnknownMember).toBeUndefined(); // 兜底：未列名成员不抛错
    db.close();

    sink.unpatchAll();
    expect(sink.isPatched).toBe(false);
    expect(sqlite().DatabaseSync).toBe(RealDatabaseSync);
  });

  it("observe-only：命中仍放行真实落盘（零行为变更的取证档）", () => {
    const root = tempDir("obs");
    const dbPath = path.join(root, "sessions.db");
    const sink = new DbSink([provider({ roots: [root], dbNames: ["sessions.db"], mode: "observe" })]);
    const Shim = patch(sink);

    const db: any = new Shim(dbPath);
    db.exec("CREATE TABLE sessions (session_id TEXT PRIMARY KEY)");
    db.close();
    expect(fs.existsSync(dbPath)).toBe(true);
    const obs = sink.observe();
    expect(obs.installed).toBe(true);
    expect(obs.mode).toBe("observe");
    expect(obs.opened[0].reason).toBe("observe-only");
    expect(obs.memoryHits).toBe(0);
    expect(obs.relocated).toBe(0);
  });
});
