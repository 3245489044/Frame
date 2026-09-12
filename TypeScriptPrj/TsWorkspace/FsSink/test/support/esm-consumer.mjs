// ESM 消费者 fixture —— **必须**在 patch 之后被动态 import 才会建立 node:fs 的 ESM 命名绑定；
// 这正是真实 SDK（`import { writeFileSync } from "node:fs"`）的取函数方式（与 createRequire 探针不同）。
import { writeFileSync } from "node:fs";

export function write(p) {
  writeFileSync(p, "esm-consumer");
}
