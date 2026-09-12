/**
 * cline-data-root 单测 —— 断言 TS 解析算法与 SDK `@cline/shared/storage` 逐字一致。
 *
 * env 全部注入（不读 process.env），保证与机器/CI 环境无关。
 */

import { describe, it, expect } from "vitest";
import * as path from "node:path";
import { resolveClineDataRoot, resolveDefaultClineDataRoot, fromClineDir } from "../src/runtime/cline-data-root.js";

const HOME = "C:\\Users\\tester";

describe("resolveClineDataRoot — 四路径派生（env 全缺省）", () => {
  it("无 CLINE_* → <home>/.cline + data/{sessions,db}", () => {
    const r = resolveClineDataRoot({ USERPROFILE: HOME });
    expect(r.clineDir).toBe(path.join(HOME, ".cline"));
    expect(r.dataDir).toBe(path.join(HOME, ".cline", "data"));
    expect(r.sessionsDir).toBe(path.join(HOME, ".cline", "data", "sessions"));
    expect(r.dbDir).toBe(path.join(HOME, ".cline", "data", "db"));
  });

  it("空白值按未设置处理（trim 后空即回退）", () => {
    const r = resolveClineDataRoot({
      USERPROFILE: HOME,
      CLINE_DIR: "   ",
      CLINE_DATA_DIR: "",
      CLINE_SESSION_DATA_DIR: "",
      CLINE_DB_DATA_DIR: "",
    });
    expect(r.clineDir).toBe(path.join(HOME, ".cline"));
    expect(r.sessionsDir).toBe(path.join(HOME, ".cline", "data", "sessions"));
  });
});

describe("resolveClineDataRoot — 单变量覆盖（SDK 优先级）", () => {
  it("CLINE_DIR → dataDir 由其派生", () => {
    const r = resolveClineDataRoot({ USERPROFILE: HOME, CLINE_DIR: "D:\\isolated\\worker-1" });
    expect(r.clineDir).toBe("D:\\isolated\\worker-1");
    expect(r.dataDir).toBe(path.join("D:\\isolated\\worker-1", "data"));
    expect(r.sessionsDir).toBe(path.join("D:\\isolated\\worker-1", "data", "sessions"));
    expect(r.dbDir).toBe(path.join("D:\\isolated\\worker-1", "data", "db"));
  });

  it("CLINE_DATA_DIR 覆盖数据目录，会话/索引库随其派生", () => {
    const r = resolveClineDataRoot({
      USERPROFILE: HOME,
      CLINE_DIR: "D:\\isolated\\worker-1",
      CLINE_DATA_DIR: "E:\\custom-data",
    });
    expect(r.clineDir).toBe("D:\\isolated\\worker-1");
    expect(r.dataDir).toBe("E:\\custom-data");
    expect(r.sessionsDir).toBe(path.join("E:\\custom-data", "sessions"));
    expect(r.dbDir).toBe(path.join("E:\\custom-data", "db"));
  });

  it("CLINE_SESSION_DATA_DIR / CLINE_DB_DATA_DIR 各自独立覆盖终态目录", () => {
    const r = resolveClineDataRoot({
      USERPROFILE: HOME,
      CLINE_DIR: "D:\\isolated\\worker-1",
      CLINE_SESSION_DATA_DIR: "E:\\sess",
      CLINE_DB_DATA_DIR: "E:\\db",
    });
    expect(r.dataDir).toBe(path.join("D:\\isolated\\worker-1", "data"));
    expect(r.sessionsDir).toBe("E:\\sess");
    expect(r.dbDir).toBe("E:\\db");
  });
});

describe("resolveClineDataRoot — HOME 回退链（对齐 SDK resolveHomeDir）", () => {
  it("HOME 优先于 USERPROFILE", () => {
    const r = resolveClineDataRoot({ HOME: "D:\\home-a", USERPROFILE: HOME });
    expect(r.clineDir).toBe(path.join("D:\\home-a", ".cline"));
  });

  it("HOME 为 '~' 时回退 USERPROFILE", () => {
    const r = resolveClineDataRoot({ HOME: "~", USERPROFILE: HOME });
    expect(r.clineDir).toBe(path.join(HOME, ".cline"));
  });

  it("无 HOME/USERPROFILE 时用 HOMEDRIVE+HOMEPATH", () => {
    const r = resolveClineDataRoot({ HOMEDRIVE: "C:", HOMEPATH: "\\Users\\tester" });
    expect(r.clineDir).toBe(path.join("C:\\Users\\tester", ".cline"));
  });
});

describe("resolveDefaultClineDataRoot — 忽略 CLINE_* 覆盖（泄漏观测面）", () => {
  it("剥离 CLINE_DIR/DATA/SESSION/DB 后回到 ~/.cline/data/sessions", () => {
    const env = {
      USERPROFILE: HOME,
      CLINE_DIR: "D:\\isolated\\worker-1",
      CLINE_DATA_DIR: "E:\\custom-data",
      CLINE_SESSION_DATA_DIR: "E:\\sess",
      CLINE_DB_DATA_DIR: "E:\\db",
    };
    const actual = resolveClineDataRoot(env);
    const fallback = resolveDefaultClineDataRoot(env);
    expect(actual.sessionsDir).toBe("E:\\sess");
    expect(fallback.clineDir).toBe(path.join(HOME, ".cline"));
    expect(fallback.sessionsDir).toBe(path.join(HOME, ".cline", "data", "sessions"));
  });
});

describe("fromClineDir — 由显式根派生同一布局", () => {
  it("派生与 resolve 缺省分支同规则", () => {
    const r = fromClineDir("D:\\isolated\\worker-2");
    expect(r.dataDir).toBe(path.join("D:\\isolated\\worker-2", "data"));
    expect(r.sessionsDir).toBe(path.join("D:\\isolated\\worker-2", "data", "sessions"));
    expect(r.dbDir).toBe(path.join("D:\\isolated\\worker-2", "data", "db"));
  });
});
