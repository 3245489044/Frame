/**
 * node-project — 候选投影（projection 层，框架确定性）
 *
 * 给定 taskKey，沿 LiteDB 引用边投影本 task 候选节点索引（不携带载荷）：
 * task 作用域寻址（cross_db_link）+ NodeToNode 拓扑序 + 一句话摘要 + token 估算。
 */

import { invokeBridge, readNodeDiscovery } from "./storage-bridge.js";

export interface NodeProjectArgs {
  taskKey: string;
  workflowId?: string;
  limit?: number;
}

export async function nodeProject(args: NodeProjectArgs): Promise<string> {
  const nd = readNodeDiscovery();
  if (!nd.enabled) {
    return JSON.stringify({ status: "disabled", message: "nodeDiscovery.enabled=false" }, null, 2);
  }
  const envelope = invokeBridge("node-project", {
    taskKey: args.taskKey,
    workflowId: args.workflowId ?? "",
    limit: args.limit ? String(args.limit) : "",
  });
  return JSON.stringify(envelope.data, null, 2);
}

export default nodeProject;
