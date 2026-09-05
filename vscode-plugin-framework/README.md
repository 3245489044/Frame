# VSCode 插件统一配置与执行框架

## 概述

一个模块化的 VS Code 插件配置与执行框架，解决插件兼容性问题和路径配置混乱问题。

## 架构

```
vscode-plugin-framework/
│
├── core/                          # 核心层：框架基础设施
│   ├── config-loader.bat          #   配置加载器
│   ├── path-resolver.bat          #   路径解析器
│   ├── menu.bat                   #   菜单系统
│   └── executor.bat               #   执行调度器
│
├── plugins/                       # 插件层：每个插件一个独立模块
│   ├── code-runner/               #   Code Runner 插件模块
│   │   ├── plugin.config.json     #     插件配置
│   │   ├── adapter.bat            #     标准适配器
│   │   └── fallback.bat           #     备用执行脚本
│   │
│   ├── dotnet-test/               #   .NET Test 插件模块
│   │   ├── plugin.config.json
│   │   ├── adapter.bat
│   │   └── fallback.bat
│   │
│   └── custom/                    #   自定义命令模块
│       ├── plugin.config.json
│       ├── adapter.bat
│       └── fallback.bat
│
├── config/                        # 配置层：全局配置
│   ├── global.config.json         #   全局配置
│   └── entries.config.json        #   启动项注册表
│
├── runner.bat                     # 入口：一键启动
├── install.bat                    # 安装：自动集成到 VS Code
└── README.md                      # 本文件
```

## 使用方式

### 1. 安装

```bash
install.bat [项目目录]
```

### 2. 编辑启动项

编辑 `config/entries.config.json`，添加或修改启动项：

```json
{
    "entries": [
        {
            "name": "启动 UI",
            "description": "启动图形界面",
            "type": "bat",
            "target": "UI启动.bat",
            "workDir": "%WORKSPACE_ROOT%",
            "plugin": "code-runner",
            "newWindow": true
        }
    ]
}
```

### 3. 运行

- **在 VS Code 中**: 按 `Ctrl+Shift+B`
- **直接运行**: 双击 `runner.bat`

## 添加新插件模块

1. 在 `plugins/` 下新建目录，如 `plugins/my-plugin/`
2. 创建 `plugin.config.json`（插件元数据）
3. 创建 `adapter.bat`（实现标准接口）
4. 可选：创建 `fallback.bat`（备用方案）
5. 在 `config/entries.config.json` 中注册启动项

### 适配器标准接口

每个适配器必须支持以下命令行接口：

```bash
adapter.bat --type <文件类型> --file <文件路径> --workdir <工作目录> [--newwindow <true/false>]
```

返回值：
- `0` = 成功
- `1` = 失败（将自动调用 fallback.bat）
- `2` = 插件不可用（将自动调用 fallback.bat）

## 解决的问题

| 问题 | 解决方案 |
|------|---------|
| 插件不兼容 | 适配器 + fallback 双重保障 |
| 路径混乱 | 自动检测工作区根目录，统一路径解析 |
| 配置分散 | 所有配置集中在 config/ 目录 |
| 难以扩展 | 插件模块化，即插即用 |
| 环境干扰 | 支持新窗口执行，完全隔离 |
