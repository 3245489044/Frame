import { defineConfig } from "vitest/config";
import path from "node:path";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));

export default defineConfig({
  test: {
    globals: true,
    environment: "node",
    include: ["test/**/*.test.ts"],
    testTimeout: 30_000,
    hookTimeout: 30_000,
  },
  resolve: {
    alias: {
      // 让测试直接引用 src 源码（TS 转译由 vitest 内置 esbuild 完成），避免依赖构建产物。
      "@fs-sink": path.resolve(here, "src/index.ts"),
    },
  },
});
