/**
 * Sink — 拦截目标：内存卷（+ 可选 DB 镜像）。
 *
 * 一个 Sink 是「SDK 以为它在写真实文件」的承载体。SDK 对命中路径的全部写/读/元数据操作
 * 都路由到 Sink，使 mkdir→write→read/exists→stat 的闭环在内存卷成立，物理不落盘。
 */

/** 内存卷快照条目：已写路径的字节内容（供 DbMirror 批处理 / flushAll）。 */
export interface SnapshotEntry {
  path: string;
  data: Buffer;
}

/** 写数据载荷：字符串或二进制视图（对齐 node:fs 的 data 形参）。 */
export type SinkData = NodeJS.ArrayBufferView | string;

/** 文件写入事件回调（path 为已归一化路径；DbMirror 订阅）。 */
export type WriteListener = (path: string) => void;

/**
 * 拦截目标接口。实现方需同时承担：
 *  - 写类：writeFileSync/writeFile/appendFile/createWriteStream/mkdir/rm/unlink/rename
 *  - 读类：readFileSync/readFile/existsSync/stat/readdir/createReadStream（让 SDK 读回闭环）
 *  - 镜像支持：onWrite 订阅 + snapshotAll + flushAll（供 DbMirror 批处理）
 */
export interface Sink {
  // ---- 写类 ----
  writeFileSync(p: string, data: SinkData, opts?: unknown): void;
  writeFile(p: string, data: SinkData, opts?: unknown): Promise<void>;
  appendFileSync(p: string, data: SinkData, opts?: unknown): void;
  appendFile(p: string, data: SinkData, opts?: unknown): Promise<void>;
  createWriteStream(p: string, opts?: unknown): NodeJS.WritableStream;
  mkdir(p: string, opts?: unknown): void;
  mkdirp(p: string): string;
  rm(p: string, opts?: unknown): void;
  unlink(p: string): void;
  rename(oldP: string, newP: string): void;
  // ---- 读类 ----
  readFileSync(p: string, opts?: unknown): Buffer | string;
  readFile(p: string, opts?: unknown): Promise<Buffer | string>;
  existsSync(p: string): boolean;
  stat(p: string): unknown;
  readdir(p: string): string[];
  createReadStream(p: string, opts?: unknown): NodeJS.ReadableStream;
  // ---- 镜像支持 ----
  onWrite(cb: WriteListener): void;
  snapshotAll(): SnapshotEntry[];
  flushAll(): void;
}
