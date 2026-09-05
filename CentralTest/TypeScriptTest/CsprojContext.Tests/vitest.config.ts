import { defineConfig } from "vitest/config";
import path from "path";

export default defineConfig({
  test: {
    globals: true,
    environment: "node",
    include: ["**/*.test.ts"],
  },
  resolve: {
    alias: {
      "@csprojcontext": path.resolve(__dirname, "../../CsprojContext/src"),
    },
  },
});
