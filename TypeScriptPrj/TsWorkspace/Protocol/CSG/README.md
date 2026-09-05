# CSG 协议（TypeScript 实现）— `csg-protocol`

> CSG（Configuration-StateMachine-Graph）统一中间表示协议的 TypeScript 实现。
> **镜像** `CsharpPrj/Protocol/CSG/CSG.Unified` 的核心数据模型，提供跨语言 CanonicalForm 数据契约。

## 定位

- **数据模型标准**，不定义传输协议、不绑定运行时、不依赖特定语言（与 C# CSG 一致）。
- 本包为 **TS 侧实现**，与 C# `CSG.Unified` 通过同一 `CanonicalForm` JSON 契约**直接对接**。

## 与 C# `CSG.Unified` 的对齐映射

| TS（本包） | C# (`CSG.Unified`) | 说明 |
|---|---|---|
| `CanonicalNodeType` / `CanonicalEdgeType` | `CanonicalNodeType` / `CanonicalEdgeType` | 字符串枚举（schema 契约） |
| `FormState` | `FormState` | Empty/Populated/Validated |
| `CanonicalNode` / `CanonicalEdge` / `CanonicalForm` | `CanonicalNode` / `CanonicalEdge` / `CanonicalForm` | camelCase 线格式 |
| `CanonicalFormBuilder` | `CanonicalFormBuilder` | 流式构建 |
| `CsgProtocol` | `CsgProtocol` | 版本 + 兼容校验（v1.0.0） |
| `IStateMachine` / `IConfiguration` / `IGraph` | `IStateMachine` / `IConfiguration` / `IGraph` | 三投影契约 |
| `LifecycleStateMachineAdapter` | `LifecycleStateMachineAdapter` | 状态机 → CanonicalForm |
| `ValidationResult` / `CsgErrorCode` | `ValidationResult` / `CsgErrorCode` | 错误码数值完全一致（1001–4004） |
| `resolveUri` / `listUriTemplates` | `CanonicalForm.ResolveUri` / `ListUriTemplates` | `csg://` 寻址 |

## 线格式约定（跨语言契约）

- 序列化：**camelCase** + `type` 为**字符串**，顶层 `nodes/edges/metadata`。
- 容错解析：`parseCanonicalNodeType/EdgeType` 同时接受**字符串**或**数字**（兼容 C# `System.Text.Json` 默认把枚举序列化为数字），归一化后统一为字符串。

## schema 单一来源

schema 文件**不复制到本包**，保持单一来源（避免漂移）：

- `CsharpPrj/Protocol/CSG/schemas/csg-canonical-form.schema.json`
- `CsharpPrj/Protocol/CSG/schemas/csg-type-registry.json`
- `CsharpPrj/Protocol/CSG/schemas/csg-operations.json`

## 构建

```bash
# 注册 workspace 后
npm run build -w csg-protocol     # 或 npx tsc -p CSG
```

## 对接示例（C# → TS）

```ts
import { canonicalFormFromJson, canonicalFormToJson, LifecycleStateMachineAdapter } from "csg-protocol";

// 消费 C# 侧经 LifecycleStateMachineAdapter 下发的 CanonicalForm JSON
const form = canonicalFormFromJson(csharpJson);   // 容错：type 为数字/字符串均可

// 重新序列化回传给 C#（字符串枚举，schema 契约）
const out = canonicalFormToJson(form);
```

## 说明

- 本包为纯协议数据模型，不含传输层；跨语言帧传输仍由调用方（如 Cline daemon JSON-Line）承载。
- 权威状态仍由 C# 侧持有；Node 侧通过本包**消费/生成** CanonicalForm 事实，不持有权威状态。
