/**
 * BasicMethod — 字符串工具函数
 *
 * 提供通用的字符串处理、格式化、Markdown 输出等工具函数。
 * 从 CsprojContext 和 MCPFramework 中提取的通用字符串逻辑。
 */

// ═══════════════════════════════════════════════════════════════
// Markdown 格式化
// ═══════════════════════════════════════════════════════════════

/**
 * 格式化 Markdown 表格
 *
 * @param headers 表头
 * @param rows 数据行
 * @returns Markdown 表格字符串
 *
 * @example
 * ```typescript
 * formatTable(["名称", "值"], [["a", "1"], ["b", "2"]])
 * // | 名称 | 值 |
 * // | --- | --- |
 * // | a | 1 |
 * // | b | 2 |
 * ```
 */
export function formatTable(headers: string[], rows: string[][]): string {
  const lines: string[] = [];
  lines.push(`| ${headers.join(" | ")} |`);
  lines.push(`| ${headers.map(() => "---").join(" | ")} |`);
  for (const row of rows) {
    lines.push(`| ${row.join(" | ")} |`);
  }
  return lines.join("\n");
}

/**
 * 格式化用户友好的错误消息（带 emoji 前缀）
 *
 * 与 error.ts 中的 formatError 不同，此函数面向终端用户输出，
 * 添加了 ❌ emoji 前缀，适合在 CLI 或日志中直接展示。
 *
 * @param err 任意错误值（Error、string、object 等）
 * @param fallback 兜底信息（可选，默认 "未知错误"）
 * @returns 格式化后的错误字符串
 *
 * @example
 * ```typescript
 * formatUserError(new Error("出错了"))  // → "❌ 错误: 出错了"
 * formatUserError("出错了")             // → "❌ 错误: 出错了"
 * formatUserError(null)                 // → "❌ 错误: 未知错误"
 * ```
 */
export function formatUserError(err: unknown, fallback = "未知错误"): string {
  if (err instanceof Error) {
    return `❌ 错误: ${err.message}`;
  }
  if (typeof err === "string") {
    return `❌ 错误: ${err}`;
  }
  try {
    return `❌ 错误: ${JSON.stringify(err)}`;
  } catch {
    return `❌ 错误: ${fallback}`;
  }
}

/**
 * 格式化成功消息
 *
 * @param message 成功描述
 * @returns 格式化后的成功字符串
 */
export function formatSuccess(message: string): string {
  return `✅ ${message}`;
}

/**
 * 格式化警告消息
 *
 * @param message 警告描述
 * @returns 格式化后的警告字符串
 */
export function formatWarning(message: string): string {
  return `⚠️ ${message}`;
}

/**
 * 格式化统计输出
 *
 * @param items 统计项列表
 * @returns 格式化后的 Markdown 统计字符串
 *
 * @example
 * ```typescript
 * formatStats([{ label: "总数", count: 10 }, { label: "有效", count: 8 }])
 * // ## 统计
 * // - **总数**: 10
 * // - **有效**: 8
 * ```
 */
export function formatStats(items: { label: string; count: number }[]): string {
  const lines: string[] = [];
  lines.push(`\n## 统计`);
  for (const item of items) {
    lines.push(`- **${item.label}**: ${item.count}`);
  }
  return lines.join("\n");
}

// ═══════════════════════════════════════════════════════════════
// 字符串分割与清理
// ═══════════════════════════════════════════════════════════════

/**
 * 按分隔符分割字符串，并清理每个片段（去空白、过滤空串）
 *
 * @param input 输入字符串
 * @param delimiter 分隔符（默认 ";"）
 * @returns 清理后的字符串数组
 *
 * @example
 * ```typescript
 * splitAndTrim("a; b; ; c")  // → ["a", "b", "c"]
 * splitAndTrim("x|y||z", "|") // → ["x", "y", "z"]
 * ```
 */
export function splitAndTrim(input: string, delimiter: string = ";"): string[] {
  return input
    .split(delimiter)
    .map((s) => s.trim())
    .filter(Boolean);
}

/**
 * 将字符串数组用分隔符连接
 *
 * @param items 字符串数组
 * @param delimiter 分隔符（默认 "; "）
 * @returns 连接后的字符串
 */
export function join(items: string[], delimiter: string = "; "): string {
  return items.join(delimiter);
}

// ═══════════════════════════════════════════════════════════════
// 路径处理
// ═══════════════════════════════════════════════════════════════

/**
 * 统一路径分隔符为正斜杠
 *
 * @param path 原始路径
 * @returns 统一后的路径
 *
 * @example
 * ```typescript
 * normalizePath("a\\b\\c")  // → "a/b/c"
 * normalizePath("a/b/c")    // → "a/b/c"
 * ```
 */
export function normalizePath(path: string): string {
  return path.replace(/\\/g, "/");
}

/**
 * 生成安全的标识符名称（仅保留字母、数字、下划线）
 *
 * @param name 原始名称
 * @returns 安全标识符
 *
 * @example
 * ```typescript
 * toSafeName("CsharpPrj/BasicMethod.csproj")  // → "CsharpPrj_BasicMethod_csproj"
 * toSafeName("my-module")                      // → "my_module"
 * ```
 */
export function toSafeName(name: string): string {
  return name.replace(/[^a-zA-Z0-9_]/g, "_");
}

/**
 * 检查路径是否应被排除（精确匹配目录名）
 *
 * @param dirName 目录名
 * @param excludePatterns 排除模式列表
 * @returns 是否应排除
 */
export function shouldExclude(dirName: string, excludePatterns: string[]): boolean {
  return excludePatterns.some((p) => dirName === p);
}

// ═══════════════════════════════════════════════════════════════
// XML/标签内容提取
// ═══════════════════════════════════════════════════════════════

/**
 * 从文本中提取单个 XML 标签的内容
 *
 * @param content 文本内容
 * @param tagName 标签名
 * @returns 标签内容，未找到时返回 null
 *
 * @example
 * ```typescript
 * extractTag("<Title>Hello</Title>", "Title")  // → "Hello"
 * extractTag("<a>1</a>", "b")                   // → null
 * ```
 */
export function extractTag(content: string, tagName: string): string | null {
  const regex = new RegExp(`<${tagName}>(.*?)<\/${tagName}>`);
  const match = content.match(regex);
  return match ? match[1] : null;
}

/**
 * 从文本中提取所有匹配的 XML 标签内容
 *
 * @param content 文本内容
 * @param tagName 标签名
 * @returns 标签内容数组
 *
 * @example
 * ```typescript
 * extractAllTags("<Item Include="a" /><Item Include="b" />", "Item")
 * // → ['a', 'b']  （假设使用 Include 属性模式）
 * ```
 */
export function extractAllTags(
  content: string,
  tagName: string,
  attribute?: string
): string[] {
  const results: string[] = [];
  if (attribute) {
    // 提取属性值：<TagName Attribute="value" />
    const regex = new RegExp(`<${tagName}\\s+${attribute}="(.*?)"`, "g");
    let match: RegExpExecArray | null;
    while ((match = regex.exec(content)) !== null) {
      results.push(match[1]);
    }
  } else {
    // 提取标签内容：<TagName>content</TagName>
    const regex = new RegExp(`<${tagName}>(.*?)<\/${tagName}>`, "g");
    let match: RegExpExecArray | null;
    while ((match = regex.exec(content)) !== null) {
      results.push(match[1]);
    }
  }
  return results;
}

/**
 * 从文本中删除匹配的 XML 块
 *
 * @param content 文本内容
 * @param tagName 顶层标签名
 * @param attribute 可选属性过滤
 * @returns 删除后的文本
 *
 * @example
 * ```typescript
 * removeTagBlock("<Old>data</Old>", "Old")  // → ""
 * ```
 */
export function removeTagBlock(
  content: string,
  tagName: string,
  attribute?: string
): string {
  if (attribute) {
    // 删除带属性的标签块：<TagName Attribute="...">...</TagName>
    const regex = new RegExp(
      `\\s*<${tagName}\\s+${attribute}="[^"]*">[\\s\\S]*?<\\/${tagName}>`,
      "g"
    );
    return content.replace(regex, "");
  }
  // 删除标签块：<TagName>...</TagName>
  const regex = new RegExp(`\\s*<${tagName}>[\\s\\S]*?<\\/${tagName}>`, "g");
  return content.replace(regex, "");
}

// ═══════════════════════════════════════════════════════════════
// 字符串匹配
// ═══════════════════════════════════════════════════════════════

/**
 * 忽略大小写的字符串包含检查
 *
 * @param text 被搜索的文本
 * @param keyword 关键词
 * @returns 是否包含
 *
 * @example
 * ```typescript
 * containsIgnoreCase("Hello World", "world")  // → true
 * containsIgnoreCase("Hello World", "xyz")    // → false
 * ```
 */
export function containsIgnoreCase(text: string, keyword: string): boolean {
  return text.toLowerCase().includes(keyword.toLowerCase());
}

/**
 * 检查字符串是否以指定前缀开头（忽略大小写）
 *
 * @param text 被检查的文本
 * @param prefix 前缀
 * @returns 是否匹配
 */
export function startsWithIgnoreCase(text: string, prefix: string): boolean {
  return text.toLowerCase().startsWith(prefix.toLowerCase());
}

/**
 * 检查字符串是否以指定后缀结尾（忽略大小写）
 *
 * @param text 被检查的文本
 * @param suffix 后缀
 * @returns 是否匹配
 */
export function endsWithIgnoreCase(text: string, suffix: string): boolean {
  return text.toLowerCase().endsWith(suffix.toLowerCase());
}

// ═══════════════════════════════════════════════════════════════
// 通用列表格式化
// ═══════════════════════════════════════════════════════════════

/**
 * 格式化 Markdown 列表
 *
 * @param items 列表项
 * @param prefix 前缀符号（默认 "-"）
 * @returns Markdown 列表字符串
 */
export function formatList(items: string[], prefix: string = "-"): string {
  return items.map((item) => `${prefix} ${item}`).join("\n");
}

/**
 * 格式化 Markdown 键值对
 *
 * @param key 键
 * @param value 值
 * @param code 是否将值格式化为行内代码
 * @returns Markdown 键值对字符串
 */
export function formatKeyValue(key: string, value: string, code: boolean = false): string {
  const formattedValue = code ? `\`${value}\`` : value;
  return `- **${key}**: ${formattedValue}`;
}

/**
 * 构建 Markdown 标题
 *
 * @param text 标题文本
 * @param level 标题级别（1-6，默认 1）
 * @returns Markdown 标题字符串
 */
export function formatHeading(text: string, level: number = 1): string {
  const prefix = "#".repeat(Math.max(1, Math.min(6, level)));
  return `${prefix} ${text}\n`;
}
