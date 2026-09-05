/**
 * BasicMethod — 启动流水线框架
 *
 * 从 MCPFramework 的 Pipeline 中提取的通用启动流水线逻辑。
 * 将启动流程分解为明确的阶段式流水线，每阶段有名称、超时、重试策略。
 *
 * 设计原则：
 * - 零外部依赖（仅依赖 basic-method 自身的 error/constants/logger）
 * - 不绑定任何特定业务场景
 * - 与 MCPFramework 的 Pipeline 接口兼容
 * - 支持容错模式（阶段失败不影响后续阶段）
 *
 * 使用方式：
 * ```typescript
 * import { Pipeline } from "basic-method/network/pipeline";
 *
 * const pipeline = new Pipeline((progress) => {
 *   console.log(`进度: ${progress.currentLabel}`);
 * });
 *
 * pipeline.register("load-config", async () => {
 *   // 加载配置
 * });
 *
 * pipeline.register("init-modules", async () => {
 *   // 初始化模块
 * });
 *
 * await pipeline.run();
 * ```
 */

import { BasicError, ErrorCode, formatError } from "../error.js";
import { logger } from "../logger.js";
import { PIPELINE_DEFAULT_TIMEOUT } from "../constants.js";

// ═══════════════════════════════════════════════════════════════
// 类型定义
// ═══════════════════════════════════════════════════════════════

/** 阶段执行器类型 */
export type StageExecutor = () => Promise<void>;

/** 流水线进度信息 */
export interface PipelineProgress {
  /** 当前阶段标识 */
  currentStage: string | null;
  /** 当前阶段显示名称 */
  currentLabel: string;
  /** 已完成阶段列表 */
  completedStages: string[];
  /** 失败阶段列表 */
  failedStages: Array<{ stage: string; error: string }>;
  /** 是否已完成 */
  done: boolean;
  /** 是否已取消 */
  cancelled: boolean;
  /** 总阶段数 */
  totalStages: number;
}

// ═══════════════════════════════════════════════════════════════
// 阶段标签管理
// ═══════════════════════════════════════════════════════════════

/** 阶段标签映射：阶段标识 → 显示名称 */
const stageLabels = new Map<string, string>();

/**
 * 获取阶段的显示名称
 *
 * @param stage 阶段标识
 * @returns 显示名称（未注册时返回阶段标识本身）
 */
export function getStageLabel(stage: string): string {
  return stageLabels.get(stage) ?? stage;
}

/**
 * 注册阶段的显示名称
 *
 * @param stage 阶段标识
 * @param label 显示名称
 */
export function registerStageLabel(stage: string, label: string): void {
  stageLabels.set(stage, label);
}

// ═══════════════════════════════════════════════════════════════
// 流水线类
// ═══════════════════════════════════════════════════════════════

/**
 * 启动流水线
 *
 * 将启动流程分解为多个阶段，按注册顺序依次执行。
 * 支持容错模式（阶段失败继续执行后续阶段）、取消、进度通知。
 *
 * @example
 * ```typescript
 * // 基本使用
 * const pipeline = new Pipeline();
 * pipeline.register("phase-1", async () => { ... });
 * pipeline.register("phase-2", async () => { ... });
 * await pipeline.run();
 *
 * // 带进度回调
 * const pipeline = new Pipeline((progress) => {
 *   console.log(`阶段: ${progress.currentLabel}`);
 * });
 *
 * // 自定义超时
 * pipeline.register("long-task", async () => { ... }, 60000);
 * ```
 */
export class Pipeline {
  private executors = new Map<string, { executor: StageExecutor; timeout: number }>();
  private completed: string[] = [];
  private failed: Array<{ stage: string; error: string }> = [];
  private current: string | null = null;
  private cancelled = false;
  private onProgress?: (progress: PipelineProgress) => void;

  /**
   * @param onProgress 进度回调函数（可选）
   */
  constructor(onProgress?: (progress: PipelineProgress) => void) {
    this.onProgress = onProgress;
  }

  // ─── 注册阶段执行器 ───────────────────────────────────────

  /**
   * 注册一个阶段的执行器
   *
   * @param stage 阶段标识（可使用任意字符串）
   * @param executor 执行函数
   * @param timeout 超时时间（毫秒，默认 30 秒）
   *
   * @example
   * ```typescript
   * pipeline.register("load-config", async () => {
   *   const config = await loadConfig();
   *   if (!config) throw new Error("配置加载失败");
   * });
   * ```
   */
  register(
    stage: string,
    executor: StageExecutor,
    timeout: number = PIPELINE_DEFAULT_TIMEOUT
  ): void {
    this.executors.set(stage, { executor, timeout });
  }

  // ─── 执行流水线 ───────────────────────────────────────────

  /**
   * 按注册顺序执行所有阶段
   *
   * 如果某阶段失败，流水线继续执行后续阶段（容错模式）。
   * 如果某阶段抛出致命错误（INVALID_CONFIG），流水线停止。
   *
   * @example
   * ```typescript
   * await pipeline.run();
   * console.log(pipeline.getProgress());
   * ```
   */
  async run(): Promise<void> {
    const stages = Array.from(this.executors.keys());
    logger.info("Pipeline", `启动流水线，共 ${stages.length} 个阶段`);

    for (const stage of stages) {
      if (this.cancelled) {
        logger.warn("Pipeline", "流水线已取消");
        break;
      }

      this.current = stage;
      this.notifyProgress();

      const label = getStageLabel(stage);
      logger.info("Pipeline", `▶ 阶段 ${stages.indexOf(stage) + 1}/${stages.length}: ${label}`);

      try {
        const { executor, timeout } = this.executors.get(stage)!;
        await this.executeWithTimeout(executor, timeout);
        this.completed.push(stage);
        logger.info("Pipeline", `✅ 阶段完成: ${label}`);
      } catch (err) {
        const errMsg = formatError(err);
        this.failed.push({ stage, error: errMsg });
        logger.error("Pipeline", `❌ 阶段失败: ${label} - ${errMsg}`);

        // 如果是致命错误（配置加载失败等），停止流水线
        if (err instanceof BasicError && err.code === ErrorCode.INVALID_CONFIG) {
          logger.error("Pipeline", "致命错误，流水线终止");
          break;
        }
      }

      this.notifyProgress();
    }

    this.current = null;
    this.notifyProgress();

    const successCount = this.completed.length;
    const failCount = this.failed.length;
    logger.info(
      "Pipeline",
      `流水线完成: ${successCount}/${successCount + failCount} 阶段成功`
    );
  }

  /**
   * 带超时的执行包装
   *
   * @param executor 执行函数
   * @param timeout 超时时间（毫秒）
   */
  private async executeWithTimeout(
    executor: StageExecutor,
    timeout: number
  ): Promise<void> {
    return new Promise<void>((resolve, reject) => {
      // 如果已取消，直接拒绝
      if (this.cancelled) {
        reject(new Error("流水线已取消"));
        return;
      }

      const timer = setTimeout(() => {
        reject(new BasicError(ErrorCode.TIMEOUT, `阶段执行超时 (${timeout}ms)`));
      }, timeout);

      executor()
        .then((result) => {
          clearTimeout(timer);
          resolve(result);
        })
        .catch((err) => {
          clearTimeout(timer);
          reject(err);
        });
    });
  }

  // ─── 取消 ─────────────────────────────────────────────────

  /**
   * 取消流水线
   *
   * 取消后，当前阶段执行完毕后流水线停止。
   * 已取消的流水线不可恢复。
   */
  cancel(): void {
    this.cancelled = true;
    logger.info("Pipeline", "流水线已取消");
  }

  /**
   * 是否已取消
   */
  isCancelled(): boolean {
    return this.cancelled;
  }

  // ─── 状态查询 ─────────────────────────────────────────────

  /**
   * 获取当前进度
   *
   * @returns 流水线进度信息
   */
  getProgress(): PipelineProgress {
    return {
      currentStage: this.current,
      currentLabel: this.current ? getStageLabel(this.current) : "无",
      completedStages: [...this.completed],
      failedStages: [...this.failed],
      done: this.current === null && !this.cancelled,
      cancelled: this.cancelled,
      totalStages: this.executors.size,
    };
  }

  /**
   * 是否所有阶段都成功完成
   */
  isAllSuccessful(): boolean {
    return this.completed.length === this.executors.size && this.failed.length === 0;
  }

  // ─── 内部 ─────────────────────────────────────────────────

  private notifyProgress(): void {
    if (this.onProgress) {
      this.onProgress(this.getProgress());
    }
  }
}
