# ComfyUI 漫画套件 — 分批下载/安装文档

> 目标：无 UI 纯代码运行，复用 `PowerShellScript\Modules\Install` 的「声明式 Manifest + Provider + 分批执行」模式，从零搭建 AI 漫画生产所需的 ComfyUI 环境。

***

## 1. 背景与目标

基于 Agent 框架搭建 AI 漫画生成流水线，需要本地运行 ComfyUI（节点式工作流引擎）及一批开源节点与模型。本方案聚焦**安装环节的自动化**：

- 只保留代码运行部分，剥离 Install Framework 的 UI。

- 复用其核心设计：`Manifest`（声明安装项）+ `Provider`（不同安装类型）+ 按 `phase` 分批。

- 一个主脚本 + 一个清单 JSON，即可完成环境、框架、节点、模型的全部安装。

最终成果仅两个文件（均在 `Modules\ComfyUI\` 目录）：

- `comfyui-manifest.json` — 安装清单（12 项，分 3 批）

- `comfyui-download.ps1` — 批量下载/安装主脚本

***

## 2. 设计原理：复用 Install 的下载模式

Install Framework 的抽象为：**每个安装项 = { type, source, installDir }**，由对应 Provider 处理，并带「已安装探测」。本脚本沿袭该模式，去 UI 后核心机制：

| 概念          | 实现                                                                   |
| ----------- | -------------------------------------------------------------------- |
| Manifest 声明 | JSON 数组，每项 `name / type / source / installDir / phase / description` |
| Provider 分派 | `switch ($Item.type)` 映射到安装实现                                        |
| 已安装探测       | `Test-Installed`：按 type 检查产物是否存在（venv/python.exe、git 文件、模型文件）        |
| 幂等 / skip   | 已装则跳过，`-Force` 强制重装                                                  |
| 分批控制        | `-Phase N` 只跑某一批；`-Name X` 只装某一项                                     |
| 无交互         | 纯控制台输出，`-WhatIf` 仅预览                                                 |

支持的 Provider 类型：

- `venv` — `python -m venv`

- `pip` — 指定包 + 可选 `pipExtra`（如 PyTorch 的 `--index-url`）

- `pip-req` — 安装 `requirements.txt`

- `git-clone` — 浅克隆 GitHub 仓库（可指定 branch）

- `http-file` — 下载模型，支持断点续传与断流重试

***

## 3. 分三批安装（phase）

### Phase 1 — 环境与框架

| name         | type      | 说明                                                                 |
| ------------ | --------- | ------------------------------------------------------------------ |
| venv         | venv      | 创建 `D:\DevEnv\ComfyUI\.venv`                                       |
| torch-cu124  | pip       | torch / torchvision / torchaudio（CUDA12.4，`--index-url .../cu124`） |
| comfyui-repo | git-clone | 克隆 ComfyUI 主仓库（branch master）                                      |
| comfyui-deps | pip-req   | 安装 ComfyUI `requirements.txt` 依赖                                   |

### Phase 2 — 自定义节点（漫画生产核心）

| name              | 仓库                               | 作用      |
| ----------------- | -------------------------------- | ------- |
| comfyui-manager   | ltdrdata/ComfyUI-Manager         | 节点管理器   |
| comfyui-ipadapter | cubiq/ComfyUI\_IPAdapter\_plus   | 角色/画风一致 |
| comicverse        | GeeeXYZ/ComfyUI-Comicverse       | 漫画量产节点  |
| kjnodes           | kijai/ComfyUI-KJNodes            | 多宫格/排版  |
| storydiffusion    | smthemex/ComfyUI\_StoryDiffusion | 多格连贯    |

### Phase 3 — 模型与依赖（可选）

| name                 | type      | 说明                                   |
| -------------------- | --------- | ------------------------------------ |
| ipadapter-sdxl-model | http-file | IPAdapter SDXL 模型（666MB，角色一致核心）      |
| clip-vision-vit-h    | http-file | CLIP-ViT-H 图像编码器（2.4GB，IPAdapter 前置） |
| insightface          | pip       | 人脸检测（FaceID / StoryDiffusion 需要）     |

***

## 4. 文件清单结构示例

```json
{
  "$schema": "../Install/manifest-schema.json",
  "name": "ComfyUI 漫画套件",
  "target": "local",
  "comfyRoot": "D:\\DevEnv\\ComfyUI",
  "torchIndex": "https://download.pytorch.org/whl/cu124",
  "items": [
    { "phase": 1, "name": "venv", "type": "venv", "installDir": "D:\\DevEnv\\ComfyUI\\.venv" },
    { "phase": 1, "name": "torch-cu124", "type": "pip",
      "source": "torch torchvision torchaudio",
      "pipExtra": "--index-url https://download.pytorch.org/whl/cu124" },
    { "phase": 2, "name": "comfyui-manager", "type": "git-clone",
      "source": "https://github.com/ltdrdata/ComfyUI-Manager.git",
      "requiredFiles": ["__init__.py"] },
    { "phase": 3, "name": "ipadapter-sdxl-model", "type": "http-file",
      "source": "https://hf-mirror.com/h94/IP-Adapter/resolve/main/sdxl_models/ip-adapter_sdxl_vit-h.safetensors",
      "fileName": "ip-adapter_sdxl_vit-h.safetensors" }
  ]
}
```

> **关于** **`requiredFiles`**：`git-clone` 型用它做已装探测，必须与实际仓库根目录存在的关键文件一致，否则每次都会判定「未安装」而重复克隆。

***

## 5. 主脚本用法

```powershell
# 全部安装
powershell -ExecutionPolicy Bypass -File .\comfyui-download.ps1

# 只跑某一批
powershell -ExecutionPolicy Bypass -File .\comfyui-download.ps1 -Phase 1   # 环境/框架
powershell -ExecutionPolicy Bypass -File .\comfyui-download.ps1 -Phase 2   # 节点
powershell -ExecutionPolicy Bypass -File .\comfyui-download.ps1 -Phase 3   # 模型/依赖

# 单项 / 强制 / 预览
powershell -ExecutionPolicy Bypass -File .\comfyui-download.ps1 -Name ipadapter-sdxl-model -Force
powershell -ExecutionPolicy Bypass -File .\comfyui-download.ps1 -Phase 2 -WhatIf
```

参数：`-Manifest`（清单路径，默认同目录）、`-Phase`（1/2/3）、`-Name`（单项）、`-Force`（强制重装）、`-WhatIf`（仅预览）。

***

## 6. 本次实际安装结果

| 批次     | 内容                                                 | 结果    |
| ------ | -------------------------------------------------- | ----- |
| Phase1 | venv + torch 2.6.0+cu124 + ComfyUI 主仓库 + 依赖        | ✅ 完成  |
| Phase2 | 5 个节点仓库                                            | ✅ 5/5 |
| Phase3 | IPAdapter(666MB) + CLIP-ViT-H(2.4GB) + insightface | ✅ 完成  |

最终目录：`D:\DevEnv\ComfyUI`

- `.venv`（Python 虚拟环境，含 torch、torchvision、torchaudio、insightface 等）

- 主仓库（main.py 等）

- `custom_nodes\`：ComfyUI-Manager、ComfyUI\_IPAdapter\_plus、ComfyUI-Comicverse、ComfyUI-KJNodes、ComfyUI\_StoryDiffusion

- `models\ipadapter\ip-adapter_sdxl_vit-h.safetensors`

- `models\clip_vision\CLIP-ViT-H-14-laion2B-s32B-b79K.safetensors`

***

## 7. 过程中解决的问题（经验）

1. **PowerShell 变量名冲突**：解析后的 JSON 对象不可复用 `$Manifest`（与路径参数同名），改为用 `$cfg` 承接对象。
2. **PS5.1 的** **`-Raw`** **兼容**：用 `[System.IO.File]::ReadAllText` 读取 JSON，避免 `Get-Content -Raw` 报错。
3. **嵌套** **`powershell -Command`** **拼接陷阱**（重点）：原实现把 `& 'path' -m pip ... 2>$null` 拼成字符串再交给子进程，`$null`/转义引号/重定向在两层引用间极易被错误展开与解析，导致大量误报与解析错误。

   - **解决**：改为 `Invoke-Exe` 直接调用可执行文件 + `@ArgList` 参数数组（无字符串拼接）。

   - 并通过 `| Out-Host` 防止原生命令 stdout 混入返回值、污染退出码 `$LASTEXITCODE`（否则 pip 输出会把 `$ec` 变成数组、`-ne 0` 误判安装失败）。
4. **网络不可达处理**：

   - `github.com` 直连不稳（被墙/波动）→ 重试即可；必要时配置 git 代理或镜像。

   - `huggingface.co` 被墙 → 下载源换国内镜像 `hf-mirror.com`。
5. **断流续传（重点）**：HF 大模型下载会在中途断流卡死。

   - `curl.exe -C -` 从 `.part` 断点续传；

   - 配合 `--connect-timeout 20`、`--speed-limit 1024 --speed-time 60`：速度低于阈值 60 秒即自动中断；

   - 外层 `while` 循环最多重试 8 次续传，成功从 911MB 处恢复完成 2.4GB 模型。

***

## 8. 启动验证

```powershell
D:\DevEnv\ComfyUI\.venv\Scripts\python.exe D:\DevEnv\ComfyUI\main.py --listen
```

启动后观察自定义节点是否全部加载成功（无 ImportError），说明环境与节点装配正常。

***

## 9. 后续可扩展方向

- 把 ComfyUI 的 HTTP API（`/prompt` `/view` `/history`）通过 MCP Bridge 暴露给 Agent 工作流框架，形成「工作流 → 出图 → 回读」闭环。

- 补充更多模型（如 SDXL / SD1.5 底模、LoRA、ControlNet）与实际漫画工作流模板，直接复用本脚本的 `http-file` / `git-clone` Provider 追加到 manifest 即可。

