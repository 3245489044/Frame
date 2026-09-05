/**
 * MCPFramework — 启动流水线
 *
 * 基于 BasicMethod 的通用 Pipeline，添加 MCP 特定的阶段枚举和标签。
 *
 * 通用流水线阶段（子项目可扩展）：
 *   Phase 0: 加载配置
 *   Phase 1: 初始化模块
 *   Phase 2: 注册工具
 *   Phase 3: 启动服务器
 */

import {
  Pipeline as BasePipeline,
  registerStageLabel,
  getStageLabel,
} from "basic-method/network/pipeline";
export type { StageExecutor, PipelineProgress } from "basic-method/network/pipeline";

// ─── 阶段定义 ───────────────────────────────────────────────

/**
 * 通用流水线阶段枚举
 *
 * 子项目可通过继承 Pipeline 类并添加自定义阶段来扩展。
 */
export enum PipelineStage {
  LOAD_CONFIG = "load-config",
  INIT_MODULES = "init-modules",
  REGISTER_TOOLS = "register-tools",
  START_SERVER = "start-server",
}

// 注册 MCP 特定阶段标签
registerStageLabel(PipelineStage.LOAD_CONFIG, "加载配置");
registerStageLabel(PipelineStage.INIT_MODULES, "初始化模块");
registerStageLabel(PipelineStage.REGISTER_TOOLS, "注册工具");
registerStageLabel(PipelineStage.START_SERVER, "启动服务器");

// 重新导出 getStageLabel 和 registerStageLabel 保持接口兼容
export { getStageLabel, registerStageLabel };

// ─── 流水线类 ───────────────────────────────────────────────

/**
 * MCPFramework 流水线
 *
 * 继承 BasicMethod 的通用 Pipeline，添加 MCP 特定的阶段枚举。
 * 所有核心逻辑由基类提供，此类仅负责注册 MCP 阶段标签。
 */
export class Pipeline extends BasePipeline {
  constructor(onProgress?: (progress: import("basic-method/network/pipeline").PipelineProgress) => void) {
    super(onProgress);
  }
}
