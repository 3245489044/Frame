import { defineConfig } from "vitest/config";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { sdkEntry } from "./src/config.js";

const here = path.dirname(fileURLToPath(import.meta.url));

export default defineConfig({
  test: {
    globals: true,
    environment: "node",
    include: ["src/**/*.test.ts"],
    // 测试真实调用 Cline SDK，单用例放宽超时
    testTimeout: 60_000,
    hookTimeout: 30_000,
  },
  resolve: {
    alias: {
      // 指向 ClineApi 依赖树中已安装的 @cline/sdk（含 @cline/core 及传递依赖）
      "@cline/sdk": sdkEntry,
    },
  },
});
