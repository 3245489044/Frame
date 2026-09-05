/**
 * node-select — 预算内注入（selection 层，框架侧强制预算）
 *
 * 给定 select 节点键集，加载载荷、逐节点压缩、按 token_budget 限额注入上下文文本。
 */

import { invokeBridge, readNodeDiscovery } from "./storage-bridge.js";

export interface NodeSelectArgs {
  taskKey?: string;
  workflowId?: string;
  select: string[];
  budgetTokens?: number;
}

export async function nodeSelect(args: NodeSelectArgs): Promise<string> {
  const nd = readNodeDiscovery();
  if (!nd.enabled) {
    return JSON.stringify({ status: "disabled", message: "nodeDiscovery.enabled=false" }, null, 2);
  }
  const budget = args.budgetTokens ?? nd.defaultBudgetTokens;
  const envelope = invokeBridge("node-select", {
    taskKey: args.taskKey ?? "",
    workflowId: args.workflowId ?? "",
    select: JSON.stringify(args.select ?? []),
    budgetTokens: String(budget),
  });
  return JSON.stringify(envelope.data, null, 2);
}

export default nodeSelect;
