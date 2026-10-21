/**
 * cline 工具面注入（P1）：把 Agent 侧声明的委派工具暴露为 SDK AgentTool。
 *
 * 执行不在这里发生：`execute` 只发 `tool-call` 帧给 daemon，由 C# 侧
 * `ToolFaceHost.InvokeAsync` 执行（工具面单源），结果经 `tool-result` 帧回填。
 * 因此本模块零 SDK 类型依赖、零业务语义。
 */

/** 宿主回桥（SdkTransport 实现）。 */
export interface AgentToolBridge {
  /** 发一次工具调用并等待 `tool-result`。 */
  requestAgentTool(name: string, args: unknown, timeoutMs?: number): Promise<AgentToolCallResult>;
}

/** 工具调用回执（与 C# 侧 tool-result 帧同形）。 */
export interface AgentToolCallResult {
  ok: boolean;
  output: string;
  error?: string;
}

/** SDK AgentTool 的最小结构面（避免直接依赖 @cline/sdk 类型导出）。 */
export interface AgentToolLike {
  name: string;
  description: string;
  inputSchema: Record<string, unknown>;
  execute: (input: unknown) => Promise<unknown>;
}

/** 子智能体标签描述（来自 declarations）。 */
export interface SubAgentTagInfo {
  tag: string;
  description?: string;
}

/**
 * 按声明清单构造 cline 可注入工具集（当前仅 delegate_subagent）。
 *
 * @param bridge 回桥实现（由 transport 提供）
 * @param toolNames 声明面允许注入的工具名（tool-face.ushar 的 cline 面）
 * @param tags 可委派标签（subagents.ushar）
 */
export function createAgentTools(
  bridge: AgentToolBridge,
  toolNames: readonly string[],
  tags: readonly SubAgentTagInfo[]
): AgentToolLike[] {
  const wanted = new Set(toolNames.map((n) => n.trim().toLowerCase()).filter((n) => n.length > 0));
  if (!wanted.has("delegate_subagent")) {
    return [];
  }

  const tagHint = tags.length
    ? tags.map((t) => `${t.tag}（${t.description ?? t.tag}）`).join("；")
    : "（声明面未给出标签清单，按 Config/subagents.ushar 已登记标签）";

  return [
    {
      name: "delegate_subagent",
      description:
        "把需求交给已登记的子智能体执行并返回结构化回执。只描述目标/输入/期望产出，禁止脚本、命令、工具名。",
      inputSchema: {
        type: "object",
        properties: {
          agentTag: {
            type: "string",
            description: `子智能体标签：${tagHint}`,
          },
          goal: {
            type: "string",
            description: "需求级描述（目标/输入/期望产出）；禁脚本/命令/代码围栏",
          },
          constraints: {
            type: "object",
            description: "约束（workingRoot / mode=run|plan-only / maxOutputBytes）",
          },
          idempotencyKey: {
            type: "string",
            description: "幂等键（同 tag + 同 key 不重复执行）",
          },
          timeoutSeconds: {
            type: "integer",
            description: "整次委派等待上限（1..300 秒）",
          },
          context: {
            type: "object",
            description: "{historyId, workflowId, relatedNodes[]}",
          },
        },
        required: ["agentTag", "goal"],
      },
      async execute(input: unknown): Promise<unknown> {
        const result = await bridge.requestAgentTool("delegate_subagent", input ?? {}, 300_000);
        if (result.ok) {
          return result.output;
        }
        return JSON.stringify({ ok: false, error: result.error ?? "委派失败" });
      },
    },
  ];
}
