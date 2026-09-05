# ClineSdk.Tests — Cline SDK 独立调用测试框架

> 目录：`CentralTest/TypeScriptTest/ClineSdk.Tests`
> 目的：把「**Cline 调用本身**」从项目框架（daemon 池 / 引擎 / 看门狗）中**剥离**，
> 独立复现、验证与定位 Cline 的 `@cline/core` 初始化挂起，与框架复杂度无关。

---

## 为什么需要这个框架

框架只负责「调用 Cline」，它的复杂度与「Cline 调用是否卡住」无因果关系。要回答
**「挂起是不是 Cline 调用本身的问题」**，就必须绕过框架、单独直接调 `@cline/core`。

**当前实测结论**（本框架验证）：

- 独立 `ClineCore.create({ backendMode: "local" })` **约 2s 成功，不挂**；
- 说明挂起并非 Cline 调用本身必现，而是**框架运行时特定条件**（并发 worker、C# 继承的环境变量、传输时序等）触发的；
- 本框架据此把"调用本身"做成可独立、可重复、有界验证的回归测试。

---

## 架构：为什么用「子进程 + 进程级硬超时」

`@cline/core` 的模块加载 / create 可能**同步阻塞事件循环**。同步阻塞时：

- JS 侧 `setTimeout`（引擎心跳、超时、看门狗）**都不会触发**；
- 因此任何"JS 层超时"都救不回一个同步阻塞的挂起。

所以本框架把每次 Cline 调用放进**独立子进程**（`runner.mjs`），由父进程
（`probe.ts` / `run-all.mjs`）在超时后 `kill("SIGKILL")` —— 把一个
「同步阻塞的无限挂起」变成**有界的、可观测的** `timeout` 结果。

```
父进程（vitest 用例 或 run-all.mjs）
  └─ spawn node runner.mjs <sdkEntry> <mode> [--start]
       └─ 绝对路径 import @cline/sdk → ClineCore.create → (可选) core.start
         阶段日志 → stderr（同步 flush）；终态 → stdout `RESULT:<ok|error>`
  超时 → kill("SIGKILL") → status=timeout
```

- SDK 经**绝对路径**加载（见 `src/config.ts` 的向上查找），指向 `ClineApi` 依赖树中
  已安装的 `@cline/sdk`（pnpm，含 `@cline/core` 及全部传递依赖），
  因此**不依赖本工程自身安装 vitest / @cline** 即可运行。

---

## 目录

| 文件 | 职责 |
|------|------|
| `package.json` | 工程声明；`test`(vitest) / `test:cli`(纯 node) |
| `vitest.config.ts` | vitest 配置 + `@cline/sdk` alias |
| `src/config.ts` | 向上查找 `@cline/sdk` 绝对路径入口 |
| `src/runner.mjs` | 子进程执行器：import → create → (start)，阶段日志 + `RESULT` 终态 |
| `src/probe.ts` | 子进程封装 + 进程级硬超时（vitest 用） |
| `src/run-all.mjs` | 纯 node 独立运行器（无需 vitest） |
| `src/cline-sdk.test.ts` | vitest 用例 |

---

## 运行方式

```bash
# 方式一：纯 node，无需安装任何依赖
cd /d d:\Project\Program\WorkFlowPrj\CentralTest\TypeScriptTest\ClineSdk.Tests
node src/run-all.mjs

# 方式二：vitest（需先安装 vitest）
npm install          # 仅安装 vitest（devDep）
npm test
```

---

## 用例覆盖

| 用例 | 验证点 |
|------|--------|
| `create(backendMode=local)` | 独立调用成功且不挂（约 2s） |
| 并发 3×create(local) | 复现框架 3-worker 场景，全部有界返回 |
| `create(backendMode=auto)` | 诊断 hub/网络副作用，有界返回（结果仅记录） |
| （可选 `--start`）| 验证 create 后能否真正起会话 |

---

## 与框架的对接（诊断挂起归属）

当某次框架运行再次挂起时，用本框架对照验证：

1. `node src/run-all.mjs` 独立跑 → 若 **ok**，说明 Cline 调用本身没问题，挂起源于框架运行条件；
2. 若独立跑复现 **timeout**，说明 Cline 调用本身在特定环境/参数下同步阻塞 → 用 `runner.mjs` 的
   stderr 阶段（`import` / `create` / `start`）定位具体卡点，再配合 `node --cpu-prof` 剖面定到具体模块。
