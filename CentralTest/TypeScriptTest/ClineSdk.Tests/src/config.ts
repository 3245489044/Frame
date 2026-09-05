import path from "node:path";
import { existsSync } from "node:fs";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));

/**
 * 从本文件所在目录向上查找已安装的 @cline/sdk 入口。
 *
 * 设计：ClineApi 依赖树（pnpm）已包含 @cline/sdk 与 @cline/core 及全部传递依赖。
 * 用「绝对路径 + 子进程」加载 SDK，完全绕开本测试工程自身的 node_modules 解析，
 * 使框架在未安装 vitest 依赖时也能稳定运行（见 src/run-all.mjs）。
 */
export function resolveSdkEntry(): string {
  let dir = here;
  for (let i = 0; i < 8; i++) {
    const candidate = path.join(
      dir,
      "TypeScriptPrj",
      "TsWorkspace",
      "ClineApi",
      "node_modules",
      "@cline",
      "sdk",
      "dist",
      "index.js",
    );
    if (existsSync(candidate)) return candidate;
    const parent = path.dirname(dir);
    if (parent === dir) break;
    dir = parent;
  }
  throw new Error(
    "未找到 @cline/sdk 入口。请确认 TypeScriptPrj/TsWorkspace/ClineApi 已安装依赖（node_modules/@cline/sdk）。",
  );
}

/** 绝对路径形式的 @cline/sdk 入口（file: URL 字符串，供 ESM import）。 */
export const sdkEntry = resolveSdkEntry();

/** 便于日志打印的可读路径。 */
export const sdkEntryDisplay = sdkEntry.replaceAll("\\", "/");
