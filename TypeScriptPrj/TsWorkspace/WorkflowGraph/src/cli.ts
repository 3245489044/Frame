#!/usr/bin/env node
/**
 * WorkflowGraph — CLI：读工作流 JSON 文件，输出 SVG 到 stdout 或文件。
 *
 * 用法：
 *   workflow-graph <input.json> [--out <out.svg>] [--html] [--title <标题>]
 */
import { readFile } from "node:fs/promises";
import { renderWorkflow } from "./index.js";

function usage(): never {
  console.error("用法: workflow-graph <input.json> [--out <out.svg>] [--html] [--title <标题>]");
  process.exit(2);
}

const args = process.argv.slice(2);
if (args.length === 0) usage();

const input = args[0];
let outPath: string | undefined;
let htmlMode = false;
let title: string | undefined;

for (let i = 1; i < args.length; i++) {
  const a = args[i];
  if (a === "--out") outPath = args[++i];
  else if (a === "--html") htmlMode = true;
  else if (a === "--title") title = args[++i];
  else usage();
}

const json = await readFile(input, "utf8");
const result = renderWorkflow(json, { title });
const output = htmlMode ? result.html : result.svg;

if (outPath) {
  const { writeFile } = await import("node:fs/promises");
  await writeFile(outPath, output, "utf8");
  console.error(`已写入 ${outPath}`);
} else {
  process.stdout.write(output);
}