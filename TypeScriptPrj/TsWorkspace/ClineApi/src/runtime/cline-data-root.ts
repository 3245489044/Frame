/**
 * cline-data-root — Cline 数据根解析权威（TS 侧唯一）。
 *
 * 算法与 SDK `@cline/shared/dist/storage` 的 `resolveClineDir/resolveClineDataDir/
 * resolveSessionDataDir/resolveDbDataDir` **逐字对齐**（含 HOME 回退链），使「路由根」与「SDK 实际写盘根」
 * 天然一致，不再靠「猜两边」（历史上误用 SDK 不识别的私有变量名，导致隔离根未生效、写盘漏到默认根）。
 *
 * 跨语言单点语义：C# 侧同算法实现 = `Frame/CallCenter/CallCenter.Core/Cline/ClineDataRoot.cs`。
 * 消费方：入口 preload（→ ClineRouteProvider({ dataDir })）、SdkTransport.resolveMessagesPath。
 * 静态依赖洁净（I1）：本模块及其静态闭包禁止 `import ... from "node:fs*"`，只允许 node:path / node:os。
 */

import * as os from "node:os";
import * as path from "node:path";

/** Cline 数据根四路径（与 SDK 四个 resolve* 一一对应）。 */
export interface ClineDataRootPaths {
  /** `resolveClineDir()`：`CLINE_DIR ?? <home>/.cline`。 */
  clineDir: string;
  /** `resolveClineDataDir()`：`CLINE_DATA_DIR ?? <clineDir>/data`。 */
  dataDir: string;
  /** `resolveSessionDataDir()`：`CLINE_SESSION_DATA_DIR ?? <dataDir>/sessions`（会话 json/messages 落点）。 */
  sessionsDir: string;
  /** `resolveDbDataDir()`：`CLINE_DB_DATA_DIR ?? <dataDir>/db`（sessions.db 索引库落点，原生 SQLite 不可 patch）。 */
  dbDir: string;
}

/** 读环境变量并 trim（空串按「未设置」处理，与 SDK 的 `?.trim()` 真值判断一致）。 */
function envValue(env: NodeJS.ProcessEnv, name: string): string | undefined {
  const v = env[name]?.trim();
  return v ? v : undefined;
}

/** HOME 回退链（对齐 SDK `resolveHomeDir`）：HOME → USERPROFILE → HOMEDRIVE+HOMEPATH → os.homedir()。 */
function resolveHome(env: NodeJS.ProcessEnv): string {
  const home = envValue(env, "HOME");
  if (home && home !== "~") return home;
  const userProfile = envValue(env, "USERPROFILE");
  if (userProfile) return userProfile;
  const homeDrive = envValue(env, "HOMEDRIVE");
  const homePath = envValue(env, "HOMEPATH");
  if (homeDrive && homePath) return `${homeDrive}${homePath}`;
  const fallback = os.homedir().trim();
  return fallback && fallback !== "~" ? fallback : "~";
}

/** 由 clineDir 派生四路径（纯函数；供 C#/调用方显式指定根时复用同一布局规则）。 */
export function fromClineDir(clineDir: string): ClineDataRootPaths {
  const dataDir = path.join(clineDir, "data");
  return {
    clineDir,
    dataDir,
    sessionsDir: path.join(dataDir, "sessions"),
    dbDir: path.join(dataDir, "db"),
  };
}

/**
 * 默认根解析：忽略所有 `CLINE_*` 覆盖（= SDK 在无注入时的落点），供「SDK 又走了默认盘」泄漏观测。
 * 算法与 <see cref="resolveClineDataRoot"/> 完全一致，仅剥掉覆盖项——单一权威仍在本模块。
 */
export function resolveDefaultClineDataRoot(env: NodeJS.ProcessEnv = process.env): ClineDataRootPaths {
  const {
    CLINE_DIR: _clineDir,
    CLINE_DATA_DIR: _clineDataDir,
    CLINE_SESSION_DATA_DIR: _clineSessionDataDir,
    CLINE_DB_DATA_DIR: _clineDbDataDir,
    ...rest
  } = env;
  void _clineDir;
  void _clineDataDir;
  void _clineSessionDataDir;
  void _clineDbDataDir;
  return resolveClineDataRoot(rest);
}

/**
 * 解析当前进程的 Cline 数据根（env 可注入，便于测试）。
 * 进程环境与 SDK 同源：engine 子进程由 C# 注入 `CLINE_DIR`（Isolated）或全部缺省（Shared → `~/.cline`）。
 */
export function resolveClineDataRoot(env: NodeJS.ProcessEnv = process.env): ClineDataRootPaths {
  const home = resolveHome(env);
  const clineDir = envValue(env, "CLINE_DIR") ?? path.join(home, ".cline");
  // 派生链必须基于「生效」的 dataDir（SDK 同序：sessions/db 由 resolveClineDataDir() 派生，非默认值）。
  const dataDir = envValue(env, "CLINE_DATA_DIR") ?? path.join(clineDir, "data");
  return {
    clineDir,
    dataDir,
    sessionsDir: envValue(env, "CLINE_SESSION_DATA_DIR") ?? path.join(dataDir, "sessions"),
    dbDir: envValue(env, "CLINE_DB_DATA_DIR") ?? path.join(dataDir, "db"),
  };
}
