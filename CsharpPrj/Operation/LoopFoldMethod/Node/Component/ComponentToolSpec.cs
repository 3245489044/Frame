namespace Operation.LoopFoldMethod.Node.Component;

/// <summary>
/// 声明式工具规格（零 LLM 载体）—— 仅描述工具名 / 说明 / 入参 Schema 的元数据，不持执行闭包。
/// 消费域（如 Agent 侧 binder）把本规格具象化为具体 LLM 工具；框架不解释 Schema 语义
/// （<see cref="InputSchema"/> 为 object，可承载 JSON 骨架 / 字典 / DTO 等任意序列化描述）。
/// 与确定性执行端键（<see cref="NodeComponent.ExecutorKey"/>）配套，由 <see cref="NodeComponent"/> 一次声明给出。
/// </summary>
/// <param name="Name">工具名（LLM Function-Calling 名称）。</param>
/// <param name="Description">工具说明（喂 LLM 的描述文案）。</param>
/// <param name="InputSchema">入参 Schema（可选，object 载体，消费域 binder 解释为具体参数契约）。</param>
public sealed record ComponentToolSpec(
    string Name,
    string Description,
    object? InputSchema = null);
