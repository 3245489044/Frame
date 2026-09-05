/**
 * MCPFramework — 构建后脚本：编译 tools 目录到构建产物
 *
 * 由于 tsup 的 bundle 模式会将所有 import 打包到一个文件中，
 * 而 tools 目录下的 handler 是运行时通过 require 动态加载的，
 * 所以需要单独编译 tools 目录到构建产物中。
 *
 * 注意：
 * 1. tsc 会保留 src/tools 的目录结构，输出为 build/.tools-tmp/tools/xxx.js
 * 2. 我们需要将 .js 文件展平复制到 build/tools/ 目录下
 * 3. 需要修正 require 路径（../core/utils → ./utils）
 */

import * as fs from "fs";
import * as path from "path";
import { execSync } from "child_process";

const ROOT = path.resolve(__dirname, "..");
const SRC_TOOLS = path.join(ROOT, "src", "tools");
const DEST_TOOLS = path.join(ROOT, "build", "tools");
const SRC_DEFS = path.join(ROOT, "tools-definitions");
const DEST_DEFS = path.join(ROOT, "build", "tools-definitions");

function main() {
  console.log("📦 编译 tools 目录到构建产物...");

  // 1. 创建临时输出目录
  const TMP_DIR = path.join(ROOT, "build", ".tools-tmp");
  if (fs.existsSync(TMP_DIR)) {
    fs.rmSync(TMP_DIR, { recursive: true });
  }
  fs.mkdirSync(TMP_DIR, { recursive: true });

  // 2. 获取所有 tools 文件（排除 _template.ts）
  const files = fs.readdirSync(SRC_TOOLS).filter(
    (f: string) => f.endsWith(".ts") && f !== "_template.ts"
  );

  // 3. 使用 tsc 编译到临时目录
  for (const file of files) {
    const srcPath = path.join(SRC_TOOLS, file);

    try {
      execSync(
        `npx tsc "${srcPath}" --outDir "${TMP_DIR}" --module commonjs --target es2022 --esModuleInterop --skipLibCheck --resolveJsonModule --declaration`,
        { stdio: "pipe", cwd: ROOT }
      );
    } catch {
      // tsc 可能因为类型错误失败，但文件可能已经生成了
    }
  }

  // 4. 从临时目录展平复制 .js 和 .d.ts 文件到目标目录
  if (!fs.existsSync(DEST_TOOLS)) {
    fs.mkdirSync(DEST_TOOLS, { recursive: true });
  }

  function flattenCopy(src: string, dest: string) {
    const entries = fs.readdirSync(src, { withFileTypes: true });
    for (const entry of entries) {
      const srcPath = path.join(src, entry.name);
      if (entry.isDirectory()) {
        flattenCopy(srcPath, dest);
      } else if (entry.name.endsWith(".js") || entry.name.endsWith(".d.ts")) {
        const destPath = path.join(dest, entry.name);
        fs.copyFileSync(srcPath, destPath);
        console.log(`  ✅ ${entry.name}`);
      }
    }
  }

  flattenCopy(TMP_DIR, DEST_TOOLS);

  // 5. 修正 require 路径：../core/utils → ./utils
  //    因为 manage-context.ts 中 import 了 ../core/utils，
  //    但构建后 manage-context.js 和 utils.js 都在 build/tools/ 目录下
  const jsFiles = fs.readdirSync(DEST_TOOLS).filter((f: string) => f.endsWith(".js"));
  for (const file of jsFiles) {
    const filePath = path.join(DEST_TOOLS, file);
    let content = fs.readFileSync(filePath, "utf-8");
    const originalContent = content;

    // 修正 require('../core/utils') → require('./utils')
    content = content.replace(
      /require\("\.\.\/core\/utils"\)/g,
      'require("./utils")'
    );
    content = content.replace(
      /require\('\.\.\/core\/utils'\)/g,
      "require('./utils')"
    );

    if (content !== originalContent) {
      fs.writeFileSync(filePath, content, "utf-8");
      console.log(`  🔧 修正路径: ${file}`);
    }
  }

  // 6. 清理临时目录
  fs.rmSync(TMP_DIR, { recursive: true });

  // 7. 复制 tools-definitions 目录
  if (fs.existsSync(SRC_DEFS)) {
    if (!fs.existsSync(DEST_DEFS)) {
      fs.mkdirSync(DEST_DEFS, { recursive: true });
    }
    const defFiles = fs.readdirSync(SRC_DEFS).filter((f: string) => f.endsWith(".json"));
    for (const file of defFiles) {
      fs.copyFileSync(path.join(SRC_DEFS, file), path.join(DEST_DEFS, file));
    }
    console.log(`  ✅ tools-definitions 已复制 (${defFiles.length} 个文件)`);
  }

  console.log("✅ Tools 目录构建完成");
}

main();
