# ClineApi — Cline 桥接 API

在 **C# / PowerShell / 任意进程** 中通过 JSON CLI 调用 **Cline 编码代理**（基于 @cline/sdk 内嵌引擎）。

## 架构

```
┌─────────────┐   spawn node    ┌─────────────────────┐   内嵌运行    ┌──────────────┐
│  C# / 其他   │ ──────────────▶ │  ClineApi (build/   │ ────────────▶ │ ClineCore    │
│  调用方      │  单个 JSON 输出  │  cli.js) 桥接层      │  @cline/sdk  │ (agent 引擎) │
└─────────────┘                └─────────────────────┘  backendMode  └──────┬───────┘
                                       │                     :local        │
                                       ▼                                  ▼
                        ┌────────────────────────────┐            AI 提供商 API
                        │ credentials/cline.json      │   ← 集中管理 API Key（git 忽略）
                        │ （<WorkFlowPrj>/credentials/）│
                        └────────────────────────────┘
```

- **调用方（C#）**：`node <ClineApi>/build/cli.js ask "提示词"`，stdout 返回单个 JSON。
- **桥接层（本目录）**：基于 **`@cline/sdk`（ClineCore）** 内嵌 agent 引擎
  （`backendMode: "local"` 进程内自包含），prompt 以 **API 参数**直接传递，
  中文内容原生支持，无命令行编码问题。
- **API Key 注入**：默认所有 Provider 的 Key 集中在 `<WorkFlowPrj>/credentials/cline.json`
  （git 忽略），由桥接层构造 `CoreModelConfig.apiKey` 注入。调用方也可通过
  `--api-key` / `--base-url`（库调用方则通过 `ClineRunOptions.apiKey / baseUrl`）
  **直接传入 API 凭据**，传入值优先于集中凭据文件。

## 快速开始

### 1. 安装依赖（@cline/sdk 为本地依赖）

agent 引擎以 `@cline/sdk` 本地依赖方式嵌入（非 spawn CLI 进程）：

```bash
# 在仓库根 TypeScriptPrj 执行（pnpm workspace）
pnpm install
```

> 说明：`@cline/sdk@0.0.70` 由 `@cline/core` 提供完整 runtime（local 模式进程内自包含，
> 不依赖 VS Code / hub）。不再需要安装全局 `cline` CLI。

### 2. 配置集中凭据

在 `<WorkFlowPrj>/credentials/cline.json` 创建并填入真实 Key（该目录被 git 忽略，首次需手动创建）：

```bash
New-Item -Force -Path ..\..\..\credentials\cline.json
```

凭据文件结构（`<WorkFlowPrj>/credentials/cline.json`，仓库级集中管理）：

```json
{
  "version": 1,
  "defaultProvider": "deepseek",
  "providers": {
    "deepseek": {
      "apiKey": "sk-xxxx",
      "model": "deepseek-v4-flash"
    }
  }
}
```

> ⚠️ `credentials/` 已在根 `.gitignore` 中忽略，**切勿提交**。

### 3. 安装依赖并构建

```bash
cd TsWorkspace/ClineApi
npm install
npm run build
```

### 4. 验证

```bash
node build/cli.js version
node build/cli.js config          # 显示生效配置（不含 Key）
node build/cli.js history --limit 5
```

### 5. 执行任务

```bash
# act 模式
node build/cli.js ask "重构 src 下的工具函数并补充注释"

# plan 模式（只规划）
node build/cli.js plan "设计数据库迁移方案" --timeout 300

# 恢复会话
node build/cli.js resume --id <sessionId> "继续之前的任务"

# 覆盖 Provider / 模型 / 工作目录
node build/cli.js ask "..." --provider deepseek --model deepseek-v4-flash --cwd "D:/project"

# 由调用方直接传入 API Key / Base URL（优先于集中凭据文件）
node build/cli.js ask "..." --provider deepseek --api-key sk-xxx \
  --base-url https://api.example.com --model deepseek-v4-flash
```

## CLI 输出格式

`ask/plan/resume` 返回单个 JSON 对象：

```json
{
  "ok": true,
  "exitCode": 0,
  "messages": [{ "type": "say", "text": "...", "ts": 1760501486669 }],
  "resultText": "最终结果文本",
  "sessionId": null,
  "error": null
}
```

## 配置（cline.config.json）

| 字段 | 说明 |
|------|------|
| `provider` | 默认 Provider（须存在于集中凭据） |
| `model` | 默认模型 |
| `cwd` | 默认工作目录 |
| `timeoutSec` | 默认超时（秒） |
| `autoApprove` | 默认是否自动批准工具 |
| `thinking` | 推理努力级别 |
| `credentialsFile` | 集中凭据文件路径（相对本目录） |
| `commandPermissions` | `CLINE_COMMAND_PERMISSIONS` 命令白/黑名单 |

环境变量可覆盖：`CLINE_PROVIDER` / `CLINE_MODEL` / `CLINE_CWD` / `CLINE_TIMEOUT` / `CLINE_CREDENTIALS_PATH` / `CLINE_BIN`。

API 凭据解析优先级：**`--api-key` / `--base-url` 调用方传入 > 集中凭据文件**。

## C# 使用

C# 调用方复用 `Agent/src/Agent.Modules/`（`ClineClient` 客户端），
`FileDriven` 的 `ClineLibrary` 经 `ProjectReference` 引用它。

## 项目结构

```
ClineApi/
├── cline.config.json        # 运行配置
├── ClineApi.csproj          # MSBuild 桥接卡片（tsm 构建）
├── package.json
├── tsconfig.json
├── src/
│   ├── cli.ts               # CLI 入口（JSON 输出）
│   ├── client.ts            # ClineClient 类型化客户端
│   ├── cline-cli.ts         # 调用通道门面（SDK 封装）
│   ├── transport/
│   │   └── sdk-transport.ts # @cline/sdk (ClineCore) 核心通道
│   ├── config.ts            # 配置加载
│   ├── credentials.ts       # 集中凭据管理
│   ├── types.ts             # 类型定义
│   └── index.ts             # 库导出
```

## 已知问题与规避

### 1. ~~cline Windows 二进制对中文 prompt 的解析 bug~~（已解决）

早期版本通过 spawn `cline` CLI 进程传递 prompt，其 Windows 二进制对非 ASCII argv
解析异常（中文被当作命令）。**现改为 @cline/sdk 内嵌引擎，prompt 以 API 参数直接
传递，该问题彻底消除**（不再有临时文件 workaround）。

### 2. @ai-sdk/* 上游版本不同步

`@cline/llms` 的部分依赖可能引用尚未发布的 `@ai-sdk/*` 版本。已在
`TypeScriptPrj/package.json` 的 `pnpm.overrides` 中固定到已发布版本（保留以防升级后复现）。

### 3. 会话恢复（resume）

`ask/plan` 返回的 `sessionId` 可直接用于 `resume --id <sessionId>` 恢复同一会话继续执行
（SDK 原生支持，无需自维护会话状态）。

### 4. 认证失败

若返回 `Authentication Fails, Your api key: ... is invalid`，说明集中凭据中的
Key 无效或已过期。更新 `<WorkFlowPrj>/credentials/cline.json` 中的 `apiKey` 即可。
