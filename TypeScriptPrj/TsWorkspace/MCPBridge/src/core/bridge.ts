/**
 * McpBridge — 桥接核心
 *
 * 整合工具聚合器、请求路由器、健康检查器、Docker 管理器，
 * 通过启动流水线管理初始化流程。
 *
 * 核心流程（流水线）：
 *   1. 加载配置
 *   2. 检测 Docker Desktop 安装
 *   3. 自动启动 Docker Desktop（如需）
 *   4. 等待 Docker 守护进程就绪
 *   5. 启动容器（docker compose up -d）
 *   6. 等待容器端口就绪
 *   7. 聚合所有 MCP 工具
 *   8. 启动健康检查器
 *
 * 本地工具：
 * - 工具定义从 tools-definitions/*.json 加载（配置驱动）
 * - 实现在 src/tools/ 目录下
 * - 不经过 Docker 容器，直接在宿主机执行
 */

import * as fs from "fs";
import * as path from "path";
import { ToolAggregator } from "./aggregator.js";
import { RequestRouter } from "./router.js";
import { HealthChecker } from "../monitor/health-check.js";
import { EnvironmentProvider } from "./interfaces/environment-provider.js";
import { PathRewriter } from "./interfaces/path-rewriter.js";
import { buildProviders } from "../provision/build-providers.js";
import { Pipeline, PipelineStage, PipelineProgress, registerStageLabel } from "mcp-framework/core/pipeline";
import { logger } from "../utils/logger.js";
import { BridgeError, ErrorCode, formatError } from "../utils/errors.js";
import {
  BridgeConfig,
  BridgeStats,
  BridgeToolDefinition,
  ServerStatus,
} from "./types.js";
import { McpToolDefinition, CallToolResult } from "mcp-framework/core/types";
import { DEFAULT_HEALTH_CHECK_INTERVAL } from "mcp-framework/utils/constants";
import {
  DOCKER_CONTAINER_START_TIMEOUT,
  DEFAULT_CONFIG_FILE,
} from "../utils/constants.js";
import { getConfigFilePath, getBridgeRoot, loadPathsConfig } from "../utils/paths.js";
import { localToolRegistry } from "../tools/registry.js";

// ─── 自定义流水线阶段 ───────────────────────────────────────

/** 聚合 MCP 工具 */
const STAGE_AGGREGATE_TOOLS = "aggregate-tools";
/** 启动健康检查 */
const STAGE_START_HEALTH_CHECK = "start-health-check";

// 注册自定义阶段标签
registerStageLabel(STAGE_AGGREGATE_TOOLS, "聚合 MCP 工具");
registerStageLabel(STAGE_START_HEALTH_CHECK, "启动健康检查");

// ─── 桥接器类 ───────────────────────────────────────────────

/**
 * McpBridge 桥接器
 *
 * 整合所有子模块，通过流水线管理启动流程。
 */
export class McpBridge {
  private config!: BridgeConfig;
  private aggregator!: ToolAggregator;
  private router!: RequestRouter;
  private healthChecker!: HealthChecker;
  private providers: EnvironmentProvider[] = [];
  private pathRewriter!: PathRewriter;
  private pipeline!: Pipeline;
  private startTime: Date = new Date();
  private initialized = false;

  /** 本地工具定义列表（从 tools-definitions/*.json 加载） */
  private localToolDefs: McpToolDefinition[] = [];

  /** 本地工具名称集合 */
  private localToolNames = new Set<string>();

  // ─── 初始化 ───────────────────────────────────────────────

  /**
   * 初始化桥接器
   *
   * @param configPath 配置文件路径（可选，默认查找 mcp-bridge.config.json）
   */
  async initialize(configPath?: string): Promise<void> {
    // 1. 加载配置
    this.config = this.loadConfig(configPath);
    logger.info(
      "McpBridge",
      `加载配置: ${this.config.bridge?.version || "unknown"}`
    );
    logger.info(
      "McpBridge",
      `配置了 ${(this.config.servers || []).length} 个远程 MCP 服务器`
    );

    // 2. 加载本地工具定义（从 tools-definitions/*.json）
    this.loadLocalToolDefinitions();

    // 3. 初始化子模块
    this.aggregator = new ToolAggregator((routeMap) => {
      logger.debug("McpBridge", `路由映射已更新: ${routeMap.size} 个工具`);
    });

    await this.aggregator.initialize(this.config.servers || []);

    // 装配环境供给 Provider（唯一 docker.enabled 引用点，编排零 Docker 感知）
    const { providers, pathRewriter } = buildProviders(this.config);
    this.providers = providers;
    this.pathRewriter = pathRewriter;

    // 挂接就绪回调：环境供给就绪（如容器启动）→ 触发重新聚合
    for (const provider of this.providers) {
      provider.onReady(() => {
        this.aggregator.aggregateAll().catch((err) => {
          logger.error("McpBridge", `环境供给就绪后重新聚合失败: ${formatError(err)}`);
        });
      });
    }

    this.router = new RequestRouter(this.aggregator, this.pathRewriter);

    this.healthChecker = new HealthChecker(
      this.aggregator,
      DEFAULT_HEALTH_CHECK_INTERVAL,
      (serverName, oldStatus, newStatus) => {
        logger.info("McpBridge", `状态变更: ${serverName} ${oldStatus} → ${newStatus}`);
      }
    );

    // 5. 初始化流水线
    this.setupPipeline();

    this.initialized = true;
    logger.info("McpBridge", `初始化完成（环境供给: ${this.providers.map((p) => p.kind).join(", ") || "无"}）`);
  }

  /**
   * 从 tools-definitions/*.json 加载本地工具定义
   *
   * 工具定义目录路径从 mcp-bridge.config.json 的 localTools.definitionsDir 读取，
   * 默认为 "tools-definitions"（相对于桥接器根目录）。
   *
   * 同时初始化本地工具注册表，将 handler 映射到实际实现。
   */
  private loadLocalToolDefinitions(): void {
    const cfg = loadPathsConfig();
    const bridgeRoot = path.resolve(cfg.projectRoot, cfg.bridgeRoot);

    // 从配置读取 definitionsDir，默认为 "tools-definitions"
    const localToolsConfig = (this.config as any).localTools;
    const defsDirName = localToolsConfig?.definitionsDir || "tools-definitions";
    const defsDir = path.resolve(bridgeRoot, defsDirName);

    if (!fs.existsSync(defsDir)) {
      logger.warn("McpBridge", `本地工具定义目录未找到: ${defsDir}`);
      return;
    }

    const files = fs.readdirSync(defsDir)
      .filter(f => f.endsWith(".json"))
      .sort();

    for (const file of files) {
      try {
        const filePath = path.join(defsDir, file);
        const def = JSON.parse(fs.readFileSync(filePath, "utf-8")) as McpToolDefinition;
        this.localToolDefs.push(def);
        this.localToolNames.add(def.name);
        logger.debug("McpBridge", `已加载本地工具定义: ${def.name} (${file})`);
      } catch (err) {
        logger.warn("McpBridge", `加载工具定义失败: ${file} - ${formatError(err)}`);
      }
    }

    // 初始化本地工具注册表（异步，不阻塞初始化流程）
    localToolRegistry.initialize().catch((err) => {
      logger.warn("McpBridge", `本地工具注册表初始化失败: ${formatError(err)}`);
    });

    logger.info(
      "McpBridge",
      `已加载 ${this.localToolDefs.length} 个本地工具定义`
    );
  }

  /**
   * 设置启动流水线
   *
   * 环境供给阶段由各 Provider 自报 stage() 动态插入，
   * 编排不预注册任何环境供给阶段名。
   */
  private setupPipeline(): void {
    this.pipeline = new Pipeline((progress: PipelineProgress) => {
      if (progress.currentStage) {
        logger.debug("McpBridge", `流水线进度: ${progress.currentLabel}`);
      }
    });

    // Phase 0: 加载配置（已在 initialize 中完成）
    this.pipeline.register(PipelineStage.LOAD_CONFIG, async () => {
      const servers = this.config.servers || [];
      if (!this.config || !servers.length) {
        throw new BridgeError(ErrorCode.BRIDGE_INIT_FAILED, "配置为空或无效");
      }
    });

    // Phase 1: 环境供给阶段（由各 Provider 自报 stage() 动态插入；无 Provider 时跳过）
    for (const provider of this.providers) {
      const stageKey = provider.stage();
      registerStageLabel(stageKey, `准备环境 (${provider.kind})`);
      this.pipeline.register(stageKey, async () => {
        await provider.ensureReady();
      }, DOCKER_CONTAINER_START_TIMEOUT);
    }

    // Phase 2: 聚合 MCP 工具
    this.pipeline.register(STAGE_AGGREGATE_TOOLS, async () => {
      logger.info("McpBridge", "正在聚合远程 MCP 工具...");
      const result = await this.aggregator.aggregateAll();
      logger.info(
        "McpBridge",
        `聚合完成: ${result.succeeded}/${result.total} 成功, ${result.toolsCount} 个工具`
      );
      if (result.failed > 0) {
        logger.warn("McpBridge", `${result.failed} 个服务器连接失败`);
        if (this.providers.length > 0) {
          logger.info("McpBridge", "部分服务器连接失败；环境供给就绪后工具将自动重新聚合");
        }
      }
    });

    // Phase 3: 启动健康检查
    this.pipeline.register(STAGE_START_HEALTH_CHECK, async () => {
      this.healthChecker.start();
      logger.info("McpBridge", "健康检查已启动");
    });
  }

  /**
   * 启动桥接器（执行流水线）
   */
  async start(): Promise<void> {
    if (!this.initialized) {
      throw new Error("桥接器未初始化，请先调用 initialize()");
    }

    this.startTime = new Date();
    await this.pipeline.run();
  }

  /**
   * 停止桥接器
   */
  stop(): void {
    this.healthChecker.stop();
    for (const provider of this.providers) {
      provider.stop();
    }
    logger.info("McpBridge", "已停止");
  }

  // ─── MCP 接口 ─────────────────────────────────────────────

  /**
   * 权威来源查询工具名（C2）：返回「本地 vs 聚合远程」origin 映射，供消费方（如 CallCenter）连接后先调用，
   * 避免靠工具名猜测本地 TS/PS 或聚合远程造成误判。工具不存在（旧版桥）时调用方回落名字分类。
   */
  static readonly ORIGINS_TOOL = "bridge_origins";

  /**
   * 获取所有工具定义（用于 ListTools 响应），标注 origin（C2）。
   *
   * 合并远程 MCP 工具（origin=remote + serverName）与本地工具（origin=local），
   * 使消费方不靠名字猜测本地 TS/PS vs 聚合远程。
   */
  getToolDefinitions(): BridgeToolDefinition[] {
    const remoteTools = this.aggregator.getAllToolDefinitions();
    const localTools: BridgeToolDefinition[] = this.localToolDefs.map((d) => ({
      ...d,
      origin: "local",
    }));
    return [...remoteTools, ...localTools];
  }

  /**
   * 执行工具调用（用于 CallTool 处理）
   *
   * 优先匹配本地工具，未匹配时路由到远程 MCP 服务器。
   * bridge_origins 为桥级元工具，直接在本桥应答（不经本地注册表，因需访问聚合器）。
   *
   * @param toolName 工具名称
   * @param args 工具参数
   */
  async callTool(
    toolName: string,
    args: Record<string, unknown>
  ): Promise<CallToolResult> {
    // 0. 桥级元工具：权威 origin 映射（C2）
    if (toolName === McpBridge.ORIGINS_TOOL) {
      return this.callOriginsTool();
    }

    // 1. 优先匹配本地工具
    if (this.localToolNames.has(toolName)) {
      return this.callLocalTool(toolName, args);
    }

    // 2. 未匹配时路由到远程 MCP 服务器
    return this.router.routeCall(toolName, args);
  }

  /**
   * 返回权威 origin 映射（文本 JSON）：{ local: string[], remote: [{ name, serverName }] }
   */
  private callOriginsTool(): CallToolResult {
    const local = Array.from(this.localToolNames).sort();
    const remote = this.aggregator.getRemoteToolOrigins();
    return {
      content: [
        {
          type: "text",
          text: JSON.stringify({ local, remote }),
        },
      ],
    };
  }

  /**
   * 执行本地工具
   *
   * 通过本地工具注册表查找并执行，无需硬编码 switch 语句。
   * 新增本地工具只需在 tools-definitions/ 下添加 JSON 定义，
   * 并在 src/tools/ 下添加对应的 handler 实现即可。
   *
   * @param toolName 工具名称
   * @param args 工具参数
   */
  private async callLocalTool(
    toolName: string,
    args: Record<string, unknown>
  ): Promise<CallToolResult> {
    return localToolRegistry.callTool(toolName, args);
  }

  // ─── 管理接口 ─────────────────────────────────────────────

  /**
   * 获取桥接统计信息
   */
  getStats(): BridgeStats {
    const routerStats = this.router.getStats();
    const servers = this.aggregator.getAllServers();

    return {
      startTime: this.startTime.getTime(),
      uptime: Date.now() - this.startTime.getTime(),
      totalServers: this.aggregator.getServerCount(),
      connectedServers: this.aggregator.getConnectedCount(),
      totalTools: this.aggregator.getToolCount() + this.localToolDefs.length,
      totalRequests: routerStats.totalRequests,
      successfulRequests: routerStats.totalRequests - routerStats.failedRequests,
      failedRequests: routerStats.failedRequests,
      serverDetails: servers.map((s) => ({
        name: s.name,
        url: s.url,
        connected: s.connected,
        status: s.status || "",
        toolCount: s.tools?.length || 0,
        failCount: s.failCount || 0,
        lastError: s.lastError,
        lastCheck: s.lastCheck,
      })),
      conflictCount: this.aggregator.getConflicts().size,
    };
  }

  /**
   * 获取健康状态摘要
   */
  getHealthSummary(): string {
    return this.healthChecker.getStatusSummary();
  }

  /**
   * 获取流水线进度
   */
  getPipelineProgress(): PipelineProgress {
    return this.pipeline.getProgress();
  }

  /**
   * 获取所有环境供给 Provider 的状态列表
   *
   * 取代原 getDockerState()，编排不解析状态形状。
   * 无 Provider 时返回空数组.
   */
  getProviderStates(): Record<string, unknown>[] {
    return this.providers.map((p) => p.state());
  }

  /**
   * 重新加载配置并重新聚合
   */
  async reload(configPath?: string): Promise<void> {
    this.healthChecker.stop();
    for (const provider of this.providers) {
      provider.stop();
    }
    await this.initialize(configPath);
    await this.start();
  }

  /**
   * 刷新单个服务器的工具列表
   *
   * @param serverName 服务器名称
   */
  async refreshServer(serverName: string): Promise<boolean> {
    try {
      await this.aggregator.aggregateServer(serverName);
      return true;
    } catch {
      return false;
    }
  }

  // ─── 配置加载 ─────────────────────────────────────────────

  /**
   * 加载桥接配置
   *
   * 优先使用 paths.ts 集中管理的路径配置查找配置文件。
   * 如果 paths.ts 找不到，回退到统一路径解析器（hybrid 策略）。
   *
   * @param configPath 配置文件路径（可选，指定时直接使用）
   */
  private loadConfig(configPath?: string): BridgeConfig {
    if (configPath) {
      if (!fs.existsSync(configPath)) {
        throw new BridgeError(
          ErrorCode.BRIDGE_INIT_FAILED,
          `配置文件未找到: ${configPath}`
        );
      }
      return JSON.parse(fs.readFileSync(configPath, "utf-8"));
    }

    // 1. 优先使用 paths.ts 集中管理的路径
    const configFromPaths = getConfigFilePath();
    if (fs.existsSync(configFromPaths)) {
      logger.info("McpBridge", `配置文件已通过 paths 配置找到: ${configFromPaths}`);
      return JSON.parse(fs.readFileSync(configFromPaths, "utf-8"));
    }

    // 2. 最后尝试从桥接器根目录查找
    const bridgeRoot = getBridgeRoot();
    const bridgeConfigPath = path.resolve(bridgeRoot, DEFAULT_CONFIG_FILE);
    if (fs.existsSync(bridgeConfigPath)) {
      logger.info("McpBridge", `配置文件已通过桥接器根目录找到: ${bridgeConfigPath}`);
      return JSON.parse(fs.readFileSync(bridgeConfigPath, "utf-8"));
    }

    throw new BridgeError(
      ErrorCode.BRIDGE_INIT_FAILED,
      `配置文件未找到。请创建 ${DEFAULT_CONFIG_FILE} 或指定配置文件路径。`
    );
  }
}
