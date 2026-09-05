/**
 * MemoryVolume — 基于 memfs Volume 的内存卷封装，实现 Sink 接口。
 *
 * 承担「SDK 以为它在写真实文件」的承载体：
 *  - 写/读/stat/readdir 全走内存（memfs Volume），物理不落盘；
 *  - 维护 `Map<path, Buffer>` 快照，供 DbMirror 的 flush(path) 读取最新内容 / snapshotAll 批处理；
 *  - 写后经 onWrite 回调通知 DbMirror（含 createWriteStream close 事件）。
 */

import { Volume } from "memfs";
import type { Sink, SinkData, SnapshotEntry, WriteListener } from "./sink.js";

function toBuffer(data: SinkData): Buffer {
  if (typeof data === "string") return Buffer.from(data, "utf8");
  // ArrayBufferView（Uint8Array / DataView 等）：仅拷贝本视图字节，规避 buffer 偏移。
  return Buffer.from(data.buffer, data.byteOffset, data.byteLength);
}

function dirnameOf(p: string): string {
  const i = p.lastIndexOf("/");
  return i <= 0 ? "/" : p.slice(0, i);
}

export class MemoryVolume implements Sink {
  private readonly vol = new Volume();
  private readonly snapshots = new Map<string, Buffer>();
  private writeListener?: WriteListener;

  onWrite(cb: WriteListener): void {
    this.writeListener = cb;
  }

  private notify(p: string): void {
    this.writeListener?.(p);
  }

  /** 写前确保父目录存在（三处写类方法复用）。 */
  private ensureParentDir(p: string): void {
    this.vol.mkdirSync(dirnameOf(p), { recursive: true });
  }

  // ---- 写类 ----
  writeFileSync(p: string, data: SinkData, _opts?: unknown): void {
    this.ensureParentDir(p);
    this.vol.writeFileSync(p, toBuffer(data));
    this.snapshots.set(p, toBuffer(data));
    this.notify(p);
  }

  async writeFile(p: string, data: SinkData, _opts?: unknown): Promise<void> {
    this.writeFileSync(p, data);
  }

  appendFileSync(p: string, data: SinkData, _opts?: unknown): void {
    this.ensureParentDir(p);
    this.vol.appendFileSync(p, toBuffer(data));
    this.snapshots.set(p, this.vol.readFileSync(p) as Buffer);
    this.notify(p);
  }

  async appendFile(p: string, data: SinkData, _opts?: unknown): Promise<void> {
    this.appendFileSync(p, data);
  }

  createWriteStream(p: string, _opts?: unknown): NodeJS.WritableStream {
    this.ensureParentDir(p);
    const stream = this.vol.createWriteStream(p);
    // 流关闭（flush + end）后回读最新内容入快照并通知镜像。
    const onClose = () => {
      stream.removeListener("close", onClose);
      try {
        this.snapshots.set(p, this.vol.readFileSync(p) as Buffer);
      } catch {
        /* 流未产生内容则忽略 */
      }
      this.notify(p);
    };
    stream.on("close", onClose);
    return stream;
  }

  mkdir(p: string, opts?: unknown): void {
    const recursive = Boolean(
      opts && typeof opts === "object" && (opts as { recursive?: boolean }).recursive
    );
    this.vol.mkdirSync(p, recursive ? { recursive: true } : undefined);
  }

  mkdirp(p: string): string {
    this.mkdir(p, { recursive: true });
    return p;
  }

  rm(p: string, opts?: unknown): void {
    const o = (opts ?? {}) as { recursive?: boolean; force?: boolean };
    if (o.recursive) {
      this.vol.rmSync(p, { recursive: true, force: o.force ?? false });
      this.snapshots.delete(p);
    } else {
      // 非递归删除即 unlink 语义（含快照清理），复用避免重复。
      this.unlink(p);
    }
  }

  unlink(p: string): void {
    this.vol.unlinkSync(p);
    this.snapshots.delete(p);
  }

  rename(oldP: string, newP: string): void {
    this.vol.renameSync(oldP, newP);
    const data = this.snapshots.get(oldP);
    if (data) {
      this.snapshots.delete(oldP);
      this.snapshots.set(newP, data);
    }
    this.notify(newP);
  }

  // ---- 读类 ----
  readFileSync(p: string, opts?: unknown): Buffer | string {
    const buf = this.vol.readFileSync(p) as Buffer;
    // opts 可为 encoding 简写字符串（"utf8"）或 { encoding } 对象
    let enc: string | undefined;
    if (typeof opts === "string") enc = opts;
    else if (opts && typeof opts === "object") enc = (opts as { encoding?: string }).encoding;
    return enc ? buf.toString(enc as BufferEncoding) : buf;
  }

  async readFile(p: string, opts?: unknown): Promise<Buffer | string> {
    return this.readFileSync(p, opts);
  }

  existsSync(p: string): boolean {
    return this.vol.existsSync(p);
  }

  stat(p: string): unknown {
    return this.vol.statSync(p);
  }

  readdir(p: string): string[] {
    return this.vol.readdirSync(p) as string[];
  }

  createReadStream(p: string, _opts?: unknown): NodeJS.ReadableStream {
    return this.vol.createReadStream(p) as unknown as NodeJS.ReadableStream;
  }

  // ---- 镜像支持 ----
  snapshotAll(): SnapshotEntry[] {
    return Array.from(this.snapshots.entries()).map(([path, data]) => ({ path, data }));
  }

  flushAll(): void {
    for (const { path } of this.snapshotAll()) {
      this.notify(path);
    }
  }
}
