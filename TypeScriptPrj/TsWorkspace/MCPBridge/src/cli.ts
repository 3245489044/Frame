#!/usr/bin/env node

/**
 * McpBridge — CLI 入口
 *
 * 提供命令行管理功能：
 * - status: 查看桥接状态
 * - reload: 重新加载配置
 * - refresh: 刷新指定服务器的工具列表
 * - list: 列出所有已注册的工具
 */

import { Command } from "commander";
import { McpBridge } from "./core/bridge.js";
import { formatError } from "./utils/errors.js";

// ─── CLI 程序 ───────────────────────────────────────────────

const program = new Command();

program
  .name("mcp-bridge-cli")
  .description("McpBridge — Docker MCP 桥接管理 CLI")
  .version("2.0.0");

// ─── status 命令 ───────────────────────────────────────────

program
  .command("status")
  .description("查看桥接状态")
  .option("-c, --config <path>", "配置文件路径")
  .action(async (options) => {
    try {
      const bridge = new McpBridge();
      await bridge.initialize(options.config);
      await bridge.start();

      console.log(bridge.getHealthSummary());
      console.log("\n---\n");

      const stats = bridge.getStats();
      console.log(`运行时间: ${Math.floor(stats.uptime / 1000)} 秒`);
      console.log(`总请求数: ${stats.totalRequests}`);
      console.log(`失败请求: ${stats.failedRequests}`);

      // 环境供给状态（provider-agnostic，不再特化 Docker 字段）
      const providerStates = bridge.getProviderStates();
      for (const st of providerStates) {
        const kind = typeof st.kind === "string" ? st.kind : "unknown";
        console.log(`\n环境供给 (${kind}):`);
        for (const [key, value] of Object.entries(st)) {
          if (key === "kind" || value === undefined) continue;
          const display = typeof value === "boolean"
            ? (value ? "✅" : "❌")
            : String(value);
          console.log(`  ${key}: ${display}`);
        }
      }

      bridge.stop();
    } catch (err) {
      console.error(`❌ 获取状态失败: ${formatError(err)}`);
      process.exit(1);
    }
  });

// ─── reload 命令 ───────────────────────────────────────────

program
  .command("reload")
  .description("重新加载配置并重新聚合")
  .option("-c, --config <path>", "配置文件路径")
  .action(async (options) => {
    try {
      const bridge = new McpBridge();
      await bridge.reload(options.config);
      const stats = bridge.getStats();
      console.log(`✅ 重新加载完成`);
      console.log(`   服务器: ${stats.totalServers} 个`);
      console.log(`   已连接: ${stats.connectedServers} 个`);
      console.log(`   工具数: ${stats.totalTools} 个`);
      bridge.stop();
    } catch (err) {
      console.error(`❌ 重新加载失败: ${formatError(err)}`);
      process.exit(1);
    }
  });

// ─── refresh 命令 ──────────────────────────────────────────

program
  .command("refresh")
  .description("刷新指定服务器的工具列表")
  .argument("<server-name>", "服务器名称")
  .option("-c, --config <path>", "配置文件路径")
  .action(async (serverName, options) => {
    try {
      const bridge = new McpBridge();
      await bridge.initialize(options.config);
      await bridge.start();

      const success = await bridge.refreshServer(serverName);
      if (success) {
        console.log(`✅ 刷新成功: ${serverName}`);
      } else {
        console.log(`❌ 刷新失败: ${serverName}`);
      }

      bridge.stop();
    } catch (err) {
      console.error(`❌ 刷新失败: ${formatError(err)}`);
      process.exit(1);
    }
  });

// ─── list 命令 ─────────────────────────────────────────────

program
  .command("list")
  .description("列出所有已注册的工具")
  .option("-c, --config <path>", "配置文件路径")
  .option("-s, --server <name>", "按服务器筛选")
  .action(async (options) => {
    try {
      const bridge = new McpBridge();
      await bridge.initialize(options.config);
      await bridge.start();

      const stats = bridge.getStats();
      const serverDetails = stats.serverDetails || [];

      for (const server of serverDetails) {
        if (options.server && server.name !== options.server) continue;

        const statusEmoji =
          server.status === "connected"
            ? "✅"
            : server.status === "connecting"
            ? "🔄"
            : "❌";

        console.log(`\n${statusEmoji} ${server.name}`);
        console.log(`   状态: ${server.status}`);
        console.log(`   来源: ${server.origin ?? "ts"}`);
        console.log(`   工具数: ${server.toolCount}`);
        if (server.lastError) {
          console.log(`   错误: ${server.lastError}`);
        }
      }

      console.log(`\n总计: ${stats.totalTools} 个工具, ${stats.totalServers} 个服务器`);

      bridge.stop();
    } catch (err) {
      console.error(`❌ 列出工具失败: ${formatError(err)}`);
      process.exit(1);
    }
  });

// ─── 解析命令行参数 ─────────────────────────────────────────

program.parse(process.argv);

// 如果没有参数，显示帮助
if (!process.argv.slice(2).length) {
  program.outputHelp();
}
