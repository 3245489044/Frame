/** ModuleRule 属性定义 */
export interface ModuleRule {
  path: string;
  title: string;
  layer: number;
  description: string;
  keywords: string;
  rules: string[];
  prompts: string[];
  hasModuleRule: boolean;
}

/** 层定义 */
export interface LayerInfo {
  id: number;
  name: string;
  description: string;
}

/** 依赖关系 */
export interface DependencyInfo {
  path: string;
  title: string;
  dependencies: string[];
  dependencyCount: number;
}

/** 工具处理函数签名 */
export type ToolHandler = (args: Record<string, unknown>) => Promise<string>;

/** 工具定义（来自 tools.config.json） */
export interface ToolDefinition {
  name: string;
  description: string;
  handler: string;
  inputSchema: {
    type: "object";
    properties: Record<string, unknown>;
    required?: string[];
  };
}

// SlnProject 定义已移至 sln-parser.ts，请从那里导入
