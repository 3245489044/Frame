# PScriptManager — PowerShell 脚本管理框架

> **统一管理 WorkFlowPrj 项目中所有 PowerShell 脚本的核心框架**
> 版本：1.0.0 | 依赖：PowerShell 5.1+

---

## 概述

PScriptManager 是 WorkFlowPrj 项目的 PowerShell 脚本管理中枢，提供**自动扫描、元数据解析、配置注册、函数注册表、依赖解析**等核心能力。所有 PowerShell 脚本通过 PScriptManager 统一管理，实现**函数名解耦物理路径**的设计目标。

```mermaid
graph TB
    subgraph "PScriptManager 框架"
        Init["_init.ps1<br/>一键初始化"]
        PSM["PScriptManager.psm1<br/>模块入口"]
        Manifest["manifest.json<br/>框架配置"]
    end

    subgraph "核心模块 Core"
        Scanner["ScriptScanner<br/>脚本扫描器"]
        ConfigGen["ConfigGenerator<br/>配置生成器"]
        ManifestMgr["ManifestManager<br/>清单管理器"]
        Registry["ScriptRegistry<br/>函数注册中心"]
    end

    subgraph "命令入口 Commands"
        Scan["Scan-Scripts.ps1<br/>扫描脚本"]
        Register["Register-Script.ps1<br/>注册配置"]
        BuildReg["Build-Registry.ps1<br/>构建注册表"]
        Menu["Show-LauncherMenu.ps1<br/>启动器菜单"]
    end

    subgraph "外部依赖"
        BM["BasicMethod<br/>基础方法库"]
        LC["launcher.config.json<br/>启动器配置"]
        REG["psm-registry.json<br/>函数注册表文件"]
    end

    Init --> Scanner
    Init --> ConfigGen
    Init --> ManifestMgr
    Init --> Registry
    Init --> BM

    Scan --> Scanner
    Register --> Scanner
    Register --> ConfigGen
    BuildReg --> Scanner
    BuildReg --> Registry

    Scanner --> Manifest
    ConfigGen --> LC
    Registry --> REG

    Menu --> LC
    Menu --> BM
```

---

## 架构分层

```mermaid
graph LR
    subgraph "应用层"
        CMD["Commands/<br/>用户命令入口"]
    end

    subgraph "核心层"
        SC["ScriptScanner<br/>扫描解析"]
        CG["ConfigGenerator<br/>配置注册"]
        MM["ManifestManager<br/>清单生成"]
        SR["ScriptRegistry<br/>函数注册表"]
    end

    subgraph "基础层"
        BM["BasicMethod<br/>文件/输出/路径工具"]
    end

    subgraph "数据层"
        MJ["manifest.json<br/>框架配置"]
        LC2["launcher.config.json<br/>工具配置"]
        REG2["psm-registry.json<br/>函数映射"]
    end

    CMD --> SC
    CMD --> CG
    CMD --> MM
    CMD --> SR

    SC --> MJ
    CG --> LC2
    SR --> REG2

    SC --> BM
    CG --> BM
    SR --> BM
```

---

## 核心模块

### 1. ScriptScanner — 脚本扫描器

递归扫描 `.ps1` / `.psm1` 文件，解析文件头注释中的元数据标签。

```mermaid
graph TD
    A["扫描目录"] --> B["递归查找 .ps1/.psm1"]
    B --> C["应用排除模式"]
    C --> D["读取文件前 50 行"]
    D --> E{"解析注释元数据"}
    E --> F["@requires 依赖"]
    E --> G["@version 版本"]
    E --> H["@author 作者"]
    E --> I["@description 描述"]
    E --> J["@category 分类"]
    E --> K["@alias 别名"]
    E --> L["@deprecated 弃用标记"]
    E --> M["<# .SYNOPSIS #> 块注释"]
    F --> N["返回元数据列表"]
    G --> N
    H --> N
    I --> N
    J --> N
    K --> N
    L --> N
    M --> N
```

**关键函数：**

| 函数 | 说明 |
|------|------|
| `Invoke-ScriptScan` | 扫描目录，返回所有脚本的元数据 |
| `Get-ScriptMetadata` | 解析单个脚本文件的注释元数据 |
| `Get-ScriptSummary` | 生成扫描结果的统计摘要 |
| `Show-ScriptScanResult` | 格式化显示扫描结果 |
| `Parse-BlockComment` | 解析 `<# ... #>` 块注释 |

---

### 2. ConfigGenerator — 配置生成器

将扫描结果自动注册到 `launcher.config.json`，支持新增、更新、弃用移除。

```mermaid
graph LR
    A["脚本元数据"] --> B["ConvertTo-ToolEntry<br/>转换为工具条目"]
    B --> C{"检查是否已注册"}
    C -->|"已存在"| D["Update-ToolEntry<br/>更新字段"]
    C -->|"不存在"| E["Add-Member<br/>新增条目"]
    D --> F["Save-LauncherConfig<br/>保存配置"]
    E --> F
    F --> G["launcher.config.json"]
```

**关键函数：**

| 函数 | 说明 |
|------|------|
| `Register-ScriptsToConfig` | 核心注册函数，将脚本注册到配置 |
| `ConvertTo-ToolEntry` | 脚本元数据 → 工具条目格式 |
| `Update-ToolEntry` | 更新已存在的工具条目 |
| `Add-CategoryIfNotExists` | 自动创建不存在的分类 |
| `Load-LauncherConfig` | 加载配置文件 |
| `Save-LauncherConfig` | 保存配置文件 |

---

### 3. ManifestManager — 清单管理器

根据扫描结果自动生成 `.psd1` 模块清单文件。

```mermaid
graph TD
    A["脚本元数据列表"] --> B["检测 RootModule"]
    A --> C["收集 FunctionsToExport"]
    B --> D["生成 .psd1 内容"]
    C --> D
    D --> E["写入文件"]
    E --> F["ModuleName.psd1"]
```

**关键函数：**

| 函数 | 说明 |
|------|------|
| `New-ModuleManifest` | 为指定目录生成 .psd1 模块清单 |

---

### 4. ScriptRegistry — 函数注册中心

核心设计：**函数名 ↔ 脚本路径** 的查找表，实现完全解耦。

```mermaid
graph TB
    subgraph "构建注册表"
        A["扫描脚本"] --> B["提取 @exports"]
        A --> C["解析 function 定义"]
        B --> D["建立映射表"]
        C --> D
        D --> E["保存到 psm-registry.json"]
    end

    subgraph "调用流程"
        F["Invoke-Function<br/>-Name 'Build-Project'"] --> G["Find-Function<br/>查找注册表"]
        G --> H{"脚本已加载？"}
        H -->|"否"| I["加载脚本"]
        H -->|"是"| J["解析 @requires 依赖"]
        I --> J
        J --> K["调用函数"]
    end

    E -.->|"持久化"| REG["psm-registry.json"]
    REG -.->|"启动加载"| G
```

**关键函数：**

| 函数 | 说明 |
|------|------|
| `Build-ScriptRegistry` | 扫描脚本并构建函数注册表 |
| `Invoke-Function` | 通过函数名调用脚本（核心调用代理） |
| `Find-Function` | 查找函数名对应的脚本信息 |
| `Show-Registry` | 显示注册表内容 |
| `Save-Registry` / `Load-Registry` | 注册表持久化 |

---

## 命令入口

### 一键初始化

```powershell
# 加载 PScriptManager 框架（自动加载所有核心模块 + BasicMethod）
. .\PScriptManager\_init.ps1
```

### 扫描脚本

```powershell
# 扫描 PowerShellScript 目录
.\PScriptManager\Commands\Scan-Scripts.ps1

# 扫描指定目录并显示详细信息
.\PScriptManager\Commands\Scan-Scripts.ps1 -Path "..\CentralTest" -ShowAll

# 输出 JSON 到文件
.\PScriptManager\Commands\Scan-Scripts.ps1 -OutputJson -OutputFile "scan-result.json"
```

### 注册到启动器配置

```powershell
# 预览模式（不实际写入）
.\PScriptManager\Commands\Register-Script.ps1 -DryRun

# 实际注册
.\PScriptManager\Commands\Register-Script.ps1

# 移除已弃用的脚本
.\PScriptManager\Commands\Register-Script.ps1 -RemoveDeprecated
```

### 构建函数注册表

```powershell
# 构建注册表
.\PScriptManager\Commands\Build-Registry.ps1

# 强制重新解析所有函数定义
.\PScriptManager\Commands\Build-Registry.ps1 -Force
```

### 启动器菜单

```powershell
# 显示交互菜单
.\PScriptManager\Commands\Show-LauncherMenu.ps1

# 直接运行指定工具
.\PScriptManager\Commands\Show-LauncherMenu.ps1 -Tool clean

# 列出所有工具
.\PScriptManager\Commands\Show-LauncherMenu.ps1 -List
```

---

## 数据流

```mermaid
sequenceDiagram
    participant User as 用户
    participant CMD as Commands
    participant SC as ScriptScanner
    participant CG as ConfigGenerator
    participant SR as ScriptRegistry
    participant BM as BasicMethod
    participant FS as 文件系统

    User->>CMD: 运行命令
    CMD->>SC: Invoke-ScriptScan
    SC->>FS: 递归扫描 .ps1/.psm1
    FS-->>SC: 文件列表
    SC->>SC: 解析注释元数据
    SC-->>CMD: 元数据列表

    CMD->>CG: Register-ScriptsToConfig
    CG->>FS: 读取 launcher.config.json
    CG->>CG: 转换/更新工具条目
    CG->>FS: 保存配置

    CMD->>SR: Build-ScriptRegistry
    SR->>SC: 获取元数据
    SR->>SR: 建立函数名→路径映射
    SR->>FS: 保存 psm-registry.json

    User->>SR: Invoke-Function 'Build-Project'
    SR->>SR: 查找注册表
    SR->>BM: 加载依赖模块
    SR->>FS: 加载脚本
    SR-->>User: 返回函数结果
```

---

## 配置说明

### manifest.json — 框架配置

```json
{
  "name": "PScriptManager",
  "version": "1.0.0",
  "managedPaths": ["PowerShellScript", "CentralTest"],
  "scanExcludePatterns": ["**/backups/**", "**/Temp/**", "**/node_modules/**"],
  "metadataTags": {
    "requires": "@requires",
    "version": "@version",
    "category": "@category"
  },
  "registry": {
    "enabled": true,
    "autoLoad": true,
    "file": "psm-registry.json"
  }
}
```

### 脚本元数据标签规范

在脚本文件头部注释中添加以下标签：

```powershell
# @requires: ModuleA, ModuleB
# @version: 1.0.0
# @author: YourName
# @description: 脚本功能描述
# @category: 构建
# @alias: build
# @deprecated
# @exports: FunctionName1, FunctionName2
```

---

## 目录结构

```
PScriptManager/
├── _init.ps1                  # 一键初始化入口
├── PScriptManager.psm1        # 模块入口
├── manifest.json              # 框架配置
├── psm-registry.json          # 函数注册表（自动生成）
│
├── Core/                      # 核心模块
│   ├── ScriptScanner.ps1      # 脚本扫描器（dot-source 版）
│   ├── ScriptScanner.psm1     # 脚本扫描器（模块版）
│   ├── ConfigGenerator.ps1    # 配置生成器（dot-source 版）
│   ├── ConfigGenerator.psm1   # 配置生成器（模块版）
│   ├── ManifestManager.ps1    # 清单管理器（dot-source 版）
│   ├── ManifestManager.psm1   # 清单管理器（模块版）
│   └── ScriptRegistry.ps1     # 函数注册中心
│
├── Commands/                  # 用户命令入口
│   ├── Scan-Scripts.ps1       # 扫描脚本
│   ├── Register-Script.ps1    # 注册到配置
│   ├── Build-Registry.ps1     # 构建注册表
│   └── Show-LauncherMenu.ps1  # 启动器菜单
│
└── Templates/                 # 模板
    └── script/                # 脚本模板
```

---

## 设计哲学

```mermaid
mindmap
  root((PScriptManager))
    解耦
      函数名调用
      无需知道路径
      脚本移动不影响
    自动化
      自动扫描
      自动注册
      自动生成清单
    标准化
      统一元数据格式
      统一配置格式
      统一调用方式
    可扩展
      插件式模块
      依赖自动解析
      模板脚手架
```

---

## 与其他模块的关系

```mermaid
graph TB
    subgraph "PScriptManager"
        Init["_init.ps1"]
    end

    subgraph "BasicMethod"
        BM_Core["Core/"]
        BM_FileOps["FileOps/"]
        BM_DevOps["DevOps/"]
        BM_Tooling["Tooling/"]
    end

    subgraph "外部工具"
        Tools["PowerShellScript/Tools/"]
        Modules["PowerShellScript/Modules/"]
        Build["PowerShellScript/Build/"]
        Solution["PowerShellScript/Solution/"]
    end

    subgraph "配置文件"
        LC["launcher.config.json"]
        REG["psm-registry.json"]
    end

    Init -->|"自动加载"| BM_Core
    Init -->|"自动加载"| BM_FileOps
    Init -->|"自动加载"| BM_DevOps
    Init -->|"自动加载"| BM_Tooling

    Init -->|"注册到"| LC
    Init -->|"构建"| REG

    Tools -->|"通过注册表调用"| REG
    Modules -->|"通过注册表调用"| REG
    Build -->|"通过注册表调用"| REG
    Solution -->|"通过注册表调用"| REG
```

---

## 快速开始

```powershell
# 1. 加载框架
. .\PScriptManager\_init.ps1

# 2. 扫描所有脚本
.\PScriptManager\Commands\Scan-Scripts.ps1

# 3. 注册到启动器配置
.\PScriptManager\Commands\Register-Script.ps1

# 4. 构建函数注册表
.\PScriptManager\Commands\Build-Registry.ps1

# 5. 通过函数名调用（无需知道路径）
Invoke-Function -Name 'Build-Project' -Arguments @{Configuration='Release'}
```

代码仓库地址：`https://gitee.com/Ljy_111/ZhiLianStorage.git`
