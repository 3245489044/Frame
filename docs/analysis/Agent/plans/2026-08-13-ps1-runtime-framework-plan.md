# Ps1 运行时框架实施计划（v2：通用化 + LLM 语序对齐）

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 构建一个**通用**的 Ps1 契约运行时（`PowerShellScript/PsRuntime/`），任何宿主可复用；以**动词优先的动作**（`read.file`）为契约单位，提供单一入口 `Invoke-PsAction -Action <verb.object> -Params @{}`，匹配 LLM「动词+对象+参数」的自然语序。Agent 侧仅留薄绑定（配置 + 入口适配）。

**Architecture:** 运行时复用 `PowerShellScript/PScriptManager`（注册表/扫描器）与 `PowerShellScript/BasicMethod`（能力库，仅追加 `@ps-action` 动词优先标签）。核心是「动作→契约→执行」引擎：`Invoke-PsAction` 解析动作→CanHandler 门控→命中则 `Invoke-PsContract` 结构化输出；未命中且逻辑闭合则走完整拓展协议（脚手架→注册→用量→升级）。能力匹配为确定性「object 过滤 + action 精确匹配」，零 LLM。

**LLM 语序对齐要点：**
- 动作命名动词优先：`read.file` / `write.file` / `replace.file.batch` / `resolve.path`（非 `file.ops.read`）
- 契约 `identity` 以 `action`/`verb`/`object` 打头，顺序同 LLM 发出顺序
- LLM 唯一需发出的调用：`Invoke-PsAction -Action "read.file" -Params @{ path = "X" }`（动作在前、参数在后）

**Tech Stack:** PowerShell 5.1+ / Pester 5 / JSON（契约 sidecar + schema）

---

## 文件结构

```
PowerShellScript/PScriptManager/
  └── contract-schema.json              (新建: 通用四维契约 schema，action 优先)

PowerShellScript/PsRuntime/             (新建: 通用 Ps1 契约运行时，复用 PScriptManager 注册表)
  ├── _init.ps1                         (模块加载器)
  ├── README.md
  ├── Contract/
  │   ├── ContractSchema.psm1           (加载/校验 schema)
  │   ├── ContractParser.psm1           (解析 @ps-action 标签 + <file>.contracts.json sidecar，支持多契约)
  │   └── ContractValidator.psm1        (参数/环境预校验)
  ├── Capability/
  │   └── CapabilityResolver.psm1       (两层: object/category 过滤 → action 精确匹配)
  ├── Gate/
  │   └── CanHandler.psm1               (Test-PsCanHandle 确定性门控)
  ├── Runtime/
  │   ├── OutputSchema.psm1             (New-PsResult: {status,message,data,errors})
  │   ├── Invoke-PsContract.ps1         (按契约执行: 校验→执行→结构化输出)
  │   └── Invoke-PsAction.ps1           (★ LLM 单一入口: 动作→解析→门控→执行/拓展)
  ├── Extension/
  │   ├── ContractScaffold.psm1         (New-PsContractScript: 按动作生成合规脚手架)
  │   ├── ContractRegistrar.psm1        (Register-PsContract: 注册入库，幂等)
  │   ├── UsageTracker.psm1             (用量累计 + 升级阈值 ≥3/30d)
  │   └── Invoke-PsExtension.ps1        (编排: 检测→决策→脚手架→注册→用量→升级)
  ├── Bootstrap/
  │   └── Register-PsContracts.ps1      (通用: 扫描配置目录，产出契约集)
  └── Tests/*.Tests.ps1

PowerShellScript/BasicMethod/           (复用: 仅追加 @ps-action 动词优先标签 + sidecar)
  ├── FileOps/FileAccess.psm1           (+ FileAccess.contracts.json)
  ├── FileOps/FileBatch.psm1            (+ FileBatch.contracts.json)
  └── Core/Path.psm1                    (+ Path.contracts.json)

Agent/Config/ps1-runtime.json           (新建: Agent 绑定配置)
Agent/PsRuntimeBinding/
  └── Invoke-AgentStep.ps1              (薄绑定: 加载 PsRuntime + Agent 配置，暴露 Agent 入口)
```

**职责边界：** PScriptManager = 通用注册/扫描；PsRuntime = 通用契约执行引擎（不绑定 Agent/Cline）；BasicMethod = 纯能力（仅加动词优先元数据）；`Agent/` = 薄绑定（配置 + 入口适配）。任何宿主（Cline/MCP/人）均可直接用 `Invoke-PsAction`。

---

## 全局约定

- **动作命名**：`<verb>.<object>[.<modifier>]`，动词优先，如 `read.file`、`replace.file.batch`
- **契约四维**：`identity`（action/verb/object/purpose/function/path）、`params`、`output_schema`（status/message/data/errors）、`env_tags`
- **结构化输出**：`{ status: "success"|"failure", message, data, errors }`
- **LLM 单一入口**：`Invoke-PsAction -Action "read.file" -Params @{ path = "X" }`
- **sidecar 命名**：`<ScriptName>.contracts.json`，内容 `{ "contracts": [ {...}, {...} ] }`（一模块多契约）
- **测试命令**：`Invoke-Pester -Path .\PowerShellScript\PsRuntime\Tests\<File>.Tests.ps1 -Output Detailed`
- **项目根**：`d:\Project\Program\WorkFlowPrj`

---

## Task 1: contract-schema.json + ContractSchema.psm1

**Files:**
- Create: `PowerShellScript/PScriptManager/contract-schema.json`
- Create: `PowerShellScript/PsRuntime/Contract/ContractSchema.psm1`
- Test: `PowerShellScript/PsRuntime/Tests/ContractSchema.Tests.ps1`

- [ ] **Step 1: 写失败测试**

```powershell
# PowerShellScript/PsRuntime/Tests/ContractSchema.Tests.ps1
BeforeAll { . $PSScriptRoot\..\Contract\ContractSchema.psm1 }

Describe "ContractSchema" {
    It "加载 schema 含四维必填键" {
        $schema = Get-PsContractSchema -ProjectRoot (Resolve-Path "$PSScriptRoot\..\..\..")
        $schema.required | Should -Contain "identity"
        $schema.required | Should -Contain "params"
        $schema.required | Should -Contain "output_schema"
        $schema.required | Should -Contain "env_tags"
    }
    It "Test-PsContract 合法契约通过（action 优先）" {
        $c = @{
            identity = @{ action = "read.file"; verb = "read"; object = "file"; purpose = "读"; function = "Read-File"; path = "a.psm1" }
            params = @(@{ name = "Path"; type = "string"; required = $true })
            output_schema = @{ status = "string"; message = "string"; data = "object"; errors = "array" }
            env_tags = @{ ps_version = "5.1+"; os = "windows"; modules = @() }
        }
        (Test-PsContract -Contract $c).IsValid | Should -Be $true
    }
    It "Test-PsContract 缺 identity 失败" {
        (Test-PsContract -Contract @{ params = @(); output_schema = @{}; env_tags = @{} }).IsValid | Should -Be $false
    }
}
```

- [ ] **Step 2: 运行确认失败**

Run: `Invoke-Pester -Path .\PowerShellScript\PsRuntime\Tests\ContractSchema.Tests.ps1 -Output Detailed`
Expected: FAIL

- [ ] **Step 3: 创建 schema**

```json
// PowerShellScript/PScriptManager/contract-schema.json
{
  "$schema": "http://json-schema.org/draft-07/schema#",
  "title": "PsContract",
  "type": "object",
  "required": ["identity", "params", "output_schema", "env_tags"],
  "properties": {
    "identity": {
      "type": "object",
      "required": ["action", "verb", "object", "purpose", "function", "path"],
      "properties": {
        "action": { "type": "string", "description": "动词优先动作 ID，如 read.file" },
        "verb": { "type": "string" },
        "object": { "type": "string" },
        "purpose": { "type": "string" },
        "function": { "type": "string" },
        "path": { "type": "string" },
        "version": { "type": "string" }
      }
    },
    "params": {
      "type": "array",
      "items": {
        "type": "object",
        "required": ["name", "type", "required"],
        "properties": {
          "name": { "type": "string" },
          "type": { "type": "string" },
          "required": { "type": "boolean" },
          "constraint": { "type": "string" },
          "default": {}
        }
      }
    },
    "output_schema": {
      "type": "object",
      "required": ["status", "message", "data", "errors"],
      "properties": {
        "status": { "type": "string", "enum": ["success", "failure"] },
        "message": { "type": "string" },
        "data": { "type": "object" },
        "errors": { "type": "array" }
      }
    },
    "env_tags": {
      "type": "object",
      "required": ["ps_version", "os"],
      "properties": {
        "ps_version": { "type": "string" },
        "admin": { "type": "boolean" },
        "os": { "type": "string" },
        "modules": { "type": "array", "items": { "type": "string" } }
      }
    }
  }
}
```

- [ ] **Step 4: 实现 ContractSchema.psm1**

```powershell
# PowerShellScript/PsRuntime/Contract/ContractSchema.psm1
$script:_SchemaCache = $null

function Get-PsContractSchema {
    param([string]$ProjectRoot)
    if ($script:_SchemaCache) { return $script:_SchemaCache }
    $path = Join-Path $ProjectRoot "PowerShellScript\PScriptManager\contract-schema.json"
    $script:_SchemaCache = Get-Content $path -Raw -Encoding UTF8 | ConvertFrom-Json
    return $script:_SchemaCache
}

function Test-PsContract {
    param([Parameter(Mandatory)][hashtable]$Contract)
    $errors = @()
    foreach ($k in @("identity", "params", "output_schema", "env_tags")) {
        if (-not $Contract.ContainsKey($k)) { $errors += $k }
    }
    if ($Contract.ContainsKey("identity")) {
        foreach ($k in @("action", "verb", "object", "function", "path")) {
            if (-not $Contract.identity.ContainsKey($k)) { $errors += "identity.$k" }
        }
    }
    return [PSCustomObject]@{ IsValid = ($errors.Count -eq 0); Errors = $errors }
}

Export-ModuleMember -Function Get-PsContractSchema, Test-PsContract
```

- [ ] **Step 5: 运行确认通过 → Step 6: 提交**

Run: `Invoke-Pester -Path .\PowerShellScript\PsRuntime\Tests\ContractSchema.Tests.ps1 -Output Detailed` → PASS

```bash
git add PowerShellScript/PScriptManager/contract-schema.json PowerShellScript/PsRuntime/Contract/ContractSchema.psm1 PowerShellScript/PsRuntime/Tests/ContractSchema.Tests.ps1
git commit -m "feat(psrt): add action-first contract schema and validator"
```

---

## Task 2: OutputSchema.psm1（结构化输出）

**Files:** Create `PowerShellScript/PsRuntime/Runtime/OutputSchema.psm1` + Test

- [ ] **Step 1: 写失败测试**

```powershell
# PowerShellScript/PsRuntime/Tests/OutputSchema.Tests.ps1
BeforeAll { . $PSScriptRoot\..\Runtime\OutputSchema.psm1 }

Describe "OutputSchema" {
    It "New-PsResult success 四字段" {
        $r = New-PsResult -Status success -Message "ok" -Data @{ a = 1 }
        $r.status | Should -Be "success"; $r.data.a | Should -Be 1; $r.errors | Should -BeNullOrEmpty
    }
    It "ConvertTo-PsResult 包装裸对象" {
        (ConvertTo-PsResult -InputObject @{ x = 2 }).data.x | Should -Be 2
    }
    It "ConvertTo-PsResult 已结构化原样返回" {
        $raw = New-PsResult -Status success -Message "ok"
        (ConvertTo-PsResult -InputObject $raw) | Should -Be $raw
    }
}
```

- [ ] **Step 2: 运行确认失败 → Step 3: 实现**

```powershell
# PowerShellScript/PsRuntime/Runtime/OutputSchema.psm1
function New-PsResult {
    param(
        [ValidateSet("success", "failure")][string]$Status,
        [string]$Message = "", $Data = $null, [array]$Errors = @()
    )
    [PSCustomObject]@{ status = $Status; message = $Message; data = $Data; errors = $Errors }
}
function ConvertTo-PsResult {
    param($InputObject)
    if ($null -ne $InputObject -and $InputObject -is [pscustomobject] -and $InputObject.PSObject.Properties.Name -contains "status") { return $InputObject }
    return New-PsResult -Status success -Data $InputObject
}
Export-ModuleMember -Function New-PsResult, ConvertTo-PsResult
```

- [ ] **Step 4: 运行确认通过 → Step 5: 提交**

```bash
git add PowerShellScript/PsRuntime/Runtime/OutputSchema.psm1 PowerShellScript/PsRuntime/Tests/OutputSchema.Tests.ps1
git commit -m "feat(psrt): add structured output helper"
```

---

## Task 3: ContractParser.psm1（动词优先标签 + 多契约 sidecar）

**Files:** Create `PowerShellScript/PsRuntime/Contract/ContractParser.psm1` + Test

- [ ] **Step 1: 写失败测试**

```powershell
# PowerShellScript/PsRuntime/Tests/ContractParser.Tests.ps1
BeforeAll { . $PSScriptRoot\..\Contract\ContractParser.psm1 }

Describe "ContractParser" {
    BeforeAll {
        $script:tmp = Join-Path $env:TEMP "psrt_$(Get-Random)"; New-Item -Path $script:tmp -ItemType Directory -Force | Out-Null
        $script:f = Join-Path $script:tmp "demo.psm1"
        @"
# @ps-action: read.file
# @verb: read
# @object: file
# @exports: Read-File
function Read-File { param([string]`$Path) `$Path }
"@ | Set-Content $script:f -Encoding UTF8
        $script:side = Join-Path $script:tmp "demo.contracts.json"
        @"
{ "contracts": [
  { "identity": { "action": "read.file", "verb": "read", "object": "file", "purpose": "读", "function": "Read-File", "path": "demo.psm1" },
    "params": [{ "name": "Path", "type": "string", "required": true }],
    "output_schema": { "status": "string", "message": "string", "data": "object", "errors": "array" },
    "env_tags": { "ps_version": "5.1+", "os": "windows", "modules": [] } },
  { "identity": { "action": "write.file", "verb": "write", "object": "file", "purpose": "写", "function": "Write-File", "path": "demo.psm1" },
    "params": [{ "name": "Path", "type": "string", "required": true }, { "name": "Content", "type": "string", "required": true }],
    "output_schema": { "status": "string", "message": "string", "data": "object", "errors": "array" },
    "env_tags": { "ps_version": "5.1+", "os": "windows", "modules": [] } } ] }
"@ | Set-Content $script:side -Encoding UTF8
    }
    AfterAll { Remove-Item $script:tmp -Recurse -Force -ErrorAction SilentlyContinue }

    It "Get-PsScriptTags 解析 @ps-action/@verb/@object" {
        $t = Get-PsScriptTags -Path $script:f
        $t.'ps-action' | Should -Be "read.file"
        $t.verb | Should -Be "read"; $t.object | Should -Be "file"
    }
    It "Get-PsContracts 读 sidecar 返回多契约" {
        $cs = Get-PsContracts -ScriptPath $script:f
        $cs.Count | Should -Be 2
        $cs[0].identity.action | Should -Be "read.file"
        $cs[1].identity.action | Should -Be "write.file"
    }
    It "无 sidecar 时从标签派生单契约" {
        $nf = Join-Path $env:TEMP "noside_$(Get-Random).psm1"
        "# @ps-action: resolve.path`n# @verb: resolve`n# @object: path`n# @exports: Resolve-Path`nfunction Resolve-Path {}" | Set-Content $nf -Encoding UTF8
        $cs = Get-PsContracts -ScriptPath $nf
        $cs.Count | Should -Be 1
        $cs[0].identity.action | Should -Be "resolve.path"
        Remove-Item $nf -Force
    }
}
```

- [ ] **Step 2: 运行确认失败 → Step 3: 实现**

```powershell
# PowerShellScript/PsRuntime/Contract/ContractParser.psm1
function Get-PsScriptTags {
    param([Parameter(Mandatory)][string]$Path)
    $tags = @{}
    foreach ($line in (Get-Content $Path -TotalCount 30 -Encoding UTF8 -ErrorAction SilentlyContinue)) {
        if ($line -match '^\s*#\s*@(\w+):\s*(.+?)\s*$') { $tags[$Matches[1]] = $Matches[2].Trim() }
    }
    return $tags
}

function Get-PsContracts {
    param([Parameter(Mandatory)][string]$ScriptPath)
    $fileName = Split-Path $ScriptPath -Leaf
    $sidecar = Join-Path (Split-Path $ScriptPath -Parent) ($fileName -replace '\.(ps1|psm1)$', '.contracts.json')
    if (Test-Path $sidecar) {
        $data = Get-Content $sidecar -Raw -Encoding UTF8 | ConvertFrom-Json -AsHashtable
        return $data.contracts
    }
    # 无 sidecar：从标签派生单契约
    $t = Get-PsScriptTags -Path $ScriptPath
    if (-not $t.ContainsKey('ps-action')) { return @() }
    $action = $t.'ps-action'
    $parts = $action -split '\.'
    $fn = if ($t.ContainsKey('exports')) { ($t.exports -split ',')[0].Trim() } else { "" }
    return @(@{
        identity = @{
            action = $action
            verb = if ($t.ContainsKey('verb')) { $t.verb } else { $parts[0] }
            object = if ($t.ContainsKey('object')) { $t.object } else { ($parts | Select-Object -Skip 1) -join '.' }
            purpose = if ($t.ContainsKey('description')) { $t.description } else { "" }
            function = $fn; path = $fileName
        }
        params = @()
        output_schema = @{ status = "success|failure"; message = "string"; data = "object"; errors = "array" }
        env_tags = @{ ps_version = "5.1+"; os = "windows"; modules = @() }
    })
}

Export-ModuleMember -Function Get-PsScriptTags, Get-PsContracts
```

- [ ] **Step 4: 运行确认通过 → Step 5: 提交**

```bash
git add PowerShellScript/PsRuntime/Contract/ContractParser.psm1 PowerShellScript/PsRuntime/Tests/ContractParser.Tests.ps1
git commit -m "feat(psrt): add verb-first contract parser with multi-contract sidecar"
```

---

## Task 4: ContractValidator.psm1（参数/环境预校验）

**Files:** Create `PowerShellScript/PsRuntime/Contract/ContractValidator.psm1` + Test

- [ ] **Step 1: 写失败测试**

```powershell
# PowerShellScript/PsRuntime/Tests/ContractValidator.Tests.ps1
BeforeAll { . $PSScriptRoot\..\Contract\ContractValidator.psm1 }

Describe "ContractValidator" {
    It "缺必填参数失败" {
        $c = @{ params = @(@{ name = "Path"; type = "string"; required = $true }) }
        $r = Test-PsContractParams -Contract $c -Arguments @{}
        $r.IsValid | Should -Be $false; $r.Errors | Should -Contain "Path"
    }
    It "参数满足通过" {
        $c = @{ params = @(@{ name = "Path"; type = "string"; required = $true }) }
        (Test-PsContractParams -Contract $c -Arguments @{ Path = "x" }).IsValid | Should -Be $true
    }
    It "缺模块失败" {
        $c = @{ env_tags = @{ ps_version = "5.1+"; os = "windows"; modules = @("NoSuchMod") } }
        (Test-PsContractEnv -Contract $c).IsValid | Should -Be $false
    }
    It "无模块要求通过" {
        $c = @{ env_tags = @{ ps_version = "5.1+"; os = "windows"; modules = @() } }
        (Test-PsContractEnv -Contract $c).IsValid | Should -Be $true
    }
}
```

- [ ] **Step 2: 运行确认失败 → Step 3: 实现**

```powershell
# PowerShellScript/PsRuntime/Contract/ContractValidator.psm1
function Test-PsContractParams {
    param([hashtable]$Contract, [hashtable]$Arguments)
    $errors = @()
    foreach ($p in $Contract.params) {
        if ($p.required -and -not $Arguments.ContainsKey($p.name)) { $errors += $p.name }
    }
    return [PSCustomObject]@{ IsValid = ($errors.Count -eq 0); Errors = $errors }
}
function Test-PsContractEnv {
    param([hashtable]$Contract)
    $errors = @(); $env = $Contract.env_tags
    if ($env.os -and $env.os -ne "windows" -and -not $IsWindows -and $PSVersionTable.Platform -ne "Win32NT") { $errors += "os" }
    foreach ($m in $env.modules) {
        if ($m -and -not (Get-Module -Name $m -ListAvailable -ErrorAction SilentlyContinue)) { $errors += "module:$m" }
    }
    return [PSCustomObject]@{ IsValid = ($errors.Count -eq 0); Errors = $errors }
}
Export-ModuleMember -Function Test-PsContractParams, Test-PsContractEnv
```

- [ ] **Step 4: 运行确认通过 → Step 5: 提交**

```bash
git add PowerShellScript/PsRuntime/Contract/ContractValidator.psm1 PowerShellScript/PsRuntime/Tests/ContractValidator.Tests.ps1
git commit -m "feat(psrt): add param/env pre-validation"
```

---

## Task 5: CapabilityResolver.psm1（归一化 + 两层检索，action 精确匹配）

**Files:** Create `PowerShellScript/PsRuntime/Capability/CapabilityResolver.psm1` + Test

> **复用 CommandFramework：** 本模块复用 C# [CommandParser.Normalize](file:///d:\Project\Program\WorkFlowPrj\CsharpPrj\Share\CommandFramework\Parsing\CommandParser.cs#L103) + `NormalizationRegistry` 的归一化思想。C# 把简写补全为完整命令（`read` → `do read`），Ps1 把简写 action 补全为 `verb.object`（`read` → `read.file`），并支持别名归一化，避免 LLM 简写直接落 `no_capability`。

- [ ] **Step 1: 写失败测试**

```powershell
# PowerShellScript/PsRuntime/Tests/CapabilityResolver.Tests.ps1
BeforeAll { . $PSScriptRoot\..\Capability\CapabilityResolver.psm1 }

Describe "CapabilityResolver" {
    BeforeAll {
        $script:cs = @(
            @{ identity = @{ action = "read.file"; verb = "read"; object = "file"; function = "Read-File"; path = "a.psm1" }; env_tags = @{ ps_version = "5.1+"; os = "windows"; modules = @() } }
            @{ identity = @{ action = "write.file"; verb = "write"; object = "file"; function = "Write-File"; path = "b.psm1" }; env_tags = @{ ps_version = "5.1+"; os = "windows"; modules = @() } }
            @{ identity = @{ action = "clone.git"; verb = "clone"; object = "git"; function = "Invoke-GitClone"; path = "c.psm1" }; env_tags = @{ ps_version = "5.1+"; os = "windows"; modules = @() } }
        )
    }
    It "按 object 过滤候选集" {
        (Resolve-PsCapability -Contracts $script:cs -Object "file").Count | Should -Be 2
    }
    It "按 action 精确命中" {
        (Resolve-PsCapability -Contracts $script:cs -Action "write.file").identity.action | Should -Be "write.file"
    }
    It "按 verb+object 命中" {
        (Resolve-PsCapability -Contracts $script:cs -Verb "clone" -Object "git").identity.action | Should -Be "clone.git"
    }
    It "未命中返回 null" {
        (Resolve-PsCapability -Contracts $script:cs -Action "nope.nope") | Should -BeNullOrEmpty
    }
}

Describe "Normalize-PsAction（归一化，复用 C# Normalize 思想）" {
    BeforeAll {
        # 注册别名：对应 C# NormalizationRegistry.RegisterNormalization(shorthand, fullCommand)
        Register-PsActionAlias -Alias "读取" -Action "read.file"
        Register-PsActionAlias -Alias "cat" -Action "read.file"
    }
    It "缺 object 时按 verb 补默认 object" {
        # 等价 C# Normalize: 无空格且非已知 verb → 补 "do "；此处补默认 object
        (Normalize-PsAction -Action "read" -DefaultObject "file") | Should -Be "read.file"
    }
    It "别名归一化为完整 action" {
        (Normalize-PsAction -Action "读取") | Should -Be "read.file"
        (Normalize-PsAction -Action "cat") | Should -Be "read.file"
    }
    It "已是 verb.object 原样返回" {
        (Normalize-PsAction -Action "write.file") | Should -Be "write.file"
    }
    It "无默认 object 时缺 object 原样返回（交由 resolve 判 no_capability）" {
        (Normalize-PsAction -Action "clone") | Should -Be "clone"
    }
}
```

- [ ] **Step 2: 运行确认失败 → Step 3: 实现**

```powershell
# PowerShellScript/PsRuntime/Capability/CapabilityResolver.psm1
# 归一化别名表（对应 C# NormalizationRegistry）：alias -> verb.object
$script:_ActionAliases = @{}

function Register-PsActionAlias {
    param([Parameter(Mandatory)][string]$Alias, [Parameter(Mandatory)][string]$Action)
    $script:_ActionAliases[$Alias] = $Action
}

function Normalize-PsAction {
    # 复用 C# CommandParser.Normalize 思想：
    #   C#: 无空格且非已知 verb → 补 "do "
    #   Ps1: 无点且非别名 → 按 verb 补默认 object（若有）；别名 → 归一化完整 action
    param(
        [Parameter(Mandatory)][string]$Action,
        [string]$DefaultObject   # 可选：verb 缺 object 时的默认 object
    )
    # 1. 别名优先归一化（等价 C# NormalizationMap.TryGet）
    if ($script:_ActionAliases.ContainsKey($Action)) { return $script:_ActionAliases[$Action] }
    # 2. 已是 verb.object 原样返回
    if ($Action -match '\.') { return $Action }
    # 3. 仅 verb：有默认 object 则补全，否则原样交由 resolve 判 no_capability
    if ($DefaultObject) { return "$Action.$DefaultObject" }
    return $Action
}

function Resolve-PsCapability {
    param(
        [array]$Contracts,
        [string]$Action,      # 动词优先动作 ID，如 read.file
        [string]$Verb,
        [string]$Object,
        [string]$Category     # 兼容：等同 object
    )
    # 第一层：object/category 过滤
    $obj = if ($Object) { $Object } elseif ($Category) { $Category }
    $cands = $Contracts
    if ($Action) {
        $parts = $Action -split '\.'
        if (-not $Verb) { $Verb = $parts[0] }
        if (-not $obj) { $obj = ($parts | Select-Object -Skip 1) -join '.' }
    }
    if ($obj) { $cands = $cands | Where-Object { $_.identity.object -eq $obj } }

    # 第二层：精确匹配
    if ($Action) {
        $hit = $cands | Where-Object { $_.identity.action -eq $Action }
        if ($hit) { return $hit }
    }
    if ($Verb) {
        $hit = $cands | Where-Object { $_.identity.verb -eq $Verb }
        if ($hit -and $cands.Count -eq 1) { return $hit }
        return $hit   # 返回该 verb 下所有候选（调用方再决断）
    }
    return $cands
}
Export-ModuleMember -Function Register-PsActionAlias, Normalize-PsAction, Resolve-PsCapability
```

- [ ] **Step 4: 运行确认通过 → Step 5: 提交**

```bash
git add PowerShellScript/PsRuntime/Capability/CapabilityResolver.psm1 PowerShellScript/PsRuntime/Tests/CapabilityResolver.Tests.ps1
git commit -m "feat(psrt): add action normalizer (alias/verb default) + two-layer capability resolver"
```

---

## Task 6: CanHandler.psm1（确定性边界判定）

**Files:** Create `PowerShellScript/PsRuntime/Gate/CanHandler.psm1` + Test

- [ ] **Step 1: 写失败测试**

```powershell
# PowerShellScript/PsRuntime/Tests/CanHandler.Tests.ps1
BeforeAll {
    . $PSScriptRoot\..\Contract\ContractValidator.psm1
    . $PSScriptRoot\..\Gate\CanHandler.psm1
}
Describe "CanHandler" {
    It "命中且参数满足返回 true" {
        $c = @{ params = @(@{ name = "Path"; type = "string"; required = $true }); env_tags = @{ ps_version = "5.1+"; os = "windows"; modules = @() } }
        (Test-PsCanHandle -Contract $c -Arguments @{ Path = "x" }).CanHandle | Should -Be $true
    }
    It "缺参数返回 false 标 param" {
        $c = @{ params = @(@{ name = "Path"; type = "string"; required = $true }); env_tags = @{ ps_version = "5.1+"; os = "windows"; modules = @() } }
        $r = Test-PsCanHandle -Contract $c -Arguments @{}
        $r.CanHandle | Should -Be $false; $r.Reason | Should -Match "param"
    }
    It "无契约返回 false 标 no_contract" {
        (Test-PsCanHandle -Contract $null -Arguments @{}).Reason | Should -Be "no_contract"
    }
}
```

- [ ] **Step 2: 运行确认失败 → Step 3: 实现**

```powershell
# PowerShellScript/PsRuntime/Gate/CanHandler.psm1
function Test-PsCanHandle {
    param([hashtable]$Contract, [hashtable]$Arguments)
    if (-not $Contract) { return [PSCustomObject]@{ CanHandle = $false; Reason = "no_contract" } }
    $p = Test-PsContractParams -Contract $Contract -Arguments $Arguments
    if (-not $p.IsValid) { return [PSCustomObject]@{ CanHandle = $false; Reason = "param_missing: $($p.Errors -join ',')" } }
    $e = Test-PsContractEnv -Contract $Contract
    if (-not $e.IsValid) { return [PSCustomObject]@{ CanHandle = $false; Reason = "env_unmet: $($e.Errors -join ',')" } }
    return [PSCustomObject]@{ CanHandle = $true; Reason = "ok" }
}
Export-ModuleMember -Function Test-PsCanHandle
```

- [ ] **Step 4: 运行确认通过 → Step 5: 提交**

```bash
git add PowerShellScript/PsRuntime/Gate/CanHandler.psm1 PowerShellScript/PsRuntime/Tests/CanHandler.Tests.ps1
git commit -m "feat(psrt): add deterministic CanHandler gate"
```

---

## Task 7: Invoke-PsContract.ps1（按契约执行，stage 化可插拔）

**Files:** Create `PowerShellScript/PsRuntime/Runtime/Invoke-PsContract.ps1` + Test

> **复用 CommandFramework：** 本模块复用 C# [IPipelineStage](file:///d:\Project\Program\WorkFlowPrj\CsharpPrj\Share\CommandFramework\Execution\Shared\Abstractions\ICommand.cs#L171) 的 stage 化思想。C# 把命令执行拆成 ParseCommand → Validation → ExecuteCommand → ResultCollect（带 [RetryStage](file:///d:\Project\Program\WorkFlowPrj\CsharpPrj\Share\CommandFramework\Pipeline\Stages\RetryStage.cs)/[LoggingStage](file:///d:\Project\Program\WorkFlowPrj\CsharpPrj\Share\CommandFramework\Pipeline\Stages\LoggingStage.cs)），Ps1 把契约执行拆成同样四段 stage，预留 retry/logging 挂载点，与项目记忆中「Polly retry/timeout」「新功能靠增强现有 loop 而非新模块」一致。

- [ ] **Step 1: 写失败测试**

```powershell
# PowerShellScript/PsRuntime/Tests/Invoke-PsContract.Tests.ps1
BeforeAll {
    . $PSScriptRoot\..\Runtime\OutputSchema.psm1
    . $PSScriptRoot\..\Contract\ContractValidator.psm1
    . $PSScriptRoot\..\Runtime\Invoke-PsContract.ps1
    $script:tmp = Join-Path $env:TEMP "psrt_exec_$(Get-Random)"; New-Item -Path $script:tmp -ItemType Directory -Force | Out-Null
    $script:mod = Join-Path $script:tmp "Echo.psm1"
    @"
# @ps-action: echo.test
function Invoke-Echo { param([string]`$Msg) @{ ok = `$Msg } }
"@ | Set-Content $script:mod -Encoding UTF8
}
AfterAll { Remove-Item $script:tmp -Recurse -Force -ErrorAction SilentlyContinue }

Describe "Invoke-PsContract" {
    It "命中执行返回 success" {
        $c = @{ identity = @{ action = "echo.test"; function = "Invoke-Echo"; path = $script:mod }; params = @(@{ name = "Msg"; type = "string"; required = $true }); env_tags = @{ ps_version = "5.1+"; os = "windows"; modules = @() } }
        $r = Invoke-PsContract -Contract $c -Arguments @{ Msg = "hi" }
        $r.status | Should -Be "success"; $r.data.ok | Should -Be "hi"
    }
    It "参数校验失败返回 failure" {
        $c = @{ identity = @{ action = "echo.test"; function = "Invoke-Echo"; path = $script:mod }; params = @(@{ name = "Msg"; type = "string"; required = $true }); env_tags = @{ ps_version = "5.1+"; os = "windows"; modules = @() } }
        (Invoke-PsContract -Contract $c -Arguments @{}).status | Should -Be "failure"
    }
    It "执行抛异常返回 failure 带错误" {
        $bad = Join-Path $script:tmp "Bad.psm1"; "function Invoke-Bad { throw 'boom' }" | Set-Content $bad -Encoding UTF8
        $c = @{ identity = @{ action = "x.bad"; function = "Invoke-Bad"; path = $bad }; params = @(); env_tags = @{ ps_version = "5.1+"; os = "windows"; modules = @() } }
        $r = Invoke-PsContract -Contract $c -Arguments @{}
        $r.status | Should -Be "failure"; $r.errors[0] | Should -Match "boom"
    }
}

Describe "Invoke-PsContract stage 化（复用 C# IPipelineStage）" {
    It "暴露 4 个 stage 名且顺序固定" {
        $c = @{ identity = @{ action = "echo.test"; function = "Invoke-Echo"; path = $script:mod }; params = @(@{ name = "Msg"; type = "string"; required = $true }); env_tags = @{ ps_version = "5.1+"; os = "windows"; modules = @() } }
        $r = Invoke-PsContract -Contract $c -Arguments @{ Msg = "hi" }
        # stage 链: ValidateParams -> ValidateEnv -> ExecuteFunction -> CollectResult
        $r.data._stages | Should -Not -BeNullOrEmpty
        $r.data._stages | Should -Contain "ValidateParams"
        $r.data._stages | Should -Contain "ExecuteFunction"
        $r.data._stages[-1] | Should -Be "CollectResult"
    }
    It "校验失败时短路，不执行后续 stage" {
        $c = @{ identity = @{ action = "echo.test"; function = "Invoke-Echo"; path = $script:mod }; params = @(@{ name = "Msg"; type = "string"; required = $true }); env_tags = @{ ps_version = "5.1+"; os = "windows"; modules = @() } }
        $r = Invoke-PsContract -Contract $c -Arguments @{}
        $r.status | Should -Be "failure"
        $r.data._stages | Should -Not -Contain "ExecuteFunction"
    }
    It "支持注入自定义 stage（挂载点：retry/logging）" {
        $c = @{ identity = @{ action = "echo.test"; function = "Invoke-Echo"; path = $script:mod }; params = @(@{ name = "Msg"; type = "string"; required = $true }); env_tags = @{ ps_version = "5.1+"; os = "windows"; modules = @() } }
        $log = @()
        $hook = { param($ctx) $log += $ctx.stage }
        $r = Invoke-PsContract -Contract $c -Arguments @{ Msg = "hi" } -AfterStageHook $hook
        $log.Count | Should -BeGreaterThan 0
    }
}
```

- [ ] **Step 2: 运行确认失败 → Step 3: 实现**

```powershell
# PowerShellScript/PsRuntime/Runtime/Invoke-PsContract.ps1
# stage 化执行：复用 C# IPipelineStage 思想，每个 stage 独立可测、可短路、可挂载。
# stage 链: ValidateParams -> ValidateEnv -> ExecuteFunction -> CollectResult
#   对应 C# Pipeline: ValidationStage -> ValidationStage(env) -> ExecuteCommandStage -> ResultCollectStage

function Invoke-PsContract {
    param(
        [Parameter(Mandatory)][hashtable]$Contract,
        [Parameter(Mandatory)][hashtable]$Arguments,
        [scriptblock]$AfterStageHook   # 可选：每个 stage 后回调（挂载 retry/logging，对应 C# RetryStage/LoggingStage）
    )
    # stage 上下文（对应 C# IPipelineContext）：携带 stage 链与短路状态
    $ctx = [PSCustomObject]@{
        stages = [System.Collections.Generic.List[string]]::new()
        aborted = $false
        stage = ""
        raw = $null
        result = $null
    }

    function _RunStage {
        param([string]$Name, [scriptblock]$Body)
        if ($ctx.aborted) { return }
        $ctx.stage = $Name
        & $Body
        $ctx.stages.Add($Name)
        if ($AfterStageHook) { & $AfterStageHook $ctx }
    }

    # Stage 1: ValidateParams（对应 C# ValidationStage）
    _RunStage "ValidateParams" {
        $p = Test-PsContractParams -Contract $Contract -Arguments $Arguments
        if (-not $p.IsValid) {
            $ctx.result = New-PsResult -Status failure -Message "param validation failed" -Errors $p.Errors
            $ctx.aborted = $true
        }
    }
    # Stage 2: ValidateEnv（对应 C# ValidationStage，环境维度）
    _RunStage "ValidateEnv" {
        $e = Test-PsContractEnv -Contract $Contract
        if (-not $e.IsValid) {
            $ctx.result = New-PsResult -Status failure -Message "env validation failed" -Errors $e.Errors
            $ctx.aborted = $true
        }
    }
    # Stage 3: ExecuteFunction（对应 C# ExecuteCommandStage）
    _RunStage "ExecuteFunction" {
        try {
            . $Contract.identity.path
            $ctx.raw = & $Contract.identity.function @Arguments
        } catch {
            $ctx.result = New-PsResult -Status failure -Message "execution error" -Errors @($_.Exception.Message)
            $ctx.aborted = $true
        }
    }
    # Stage 4: CollectResult（对应 C# ResultCollectStage）
    _RunStage "CollectResult" {
        $ctx.result = ConvertTo-PsResult -InputObject $ctx.raw
    }

    # 附带 stage 链到 data，便于诊断（对应 C# StageSuccess 字典）
    if ($null -eq $ctx.result) {
        $ctx.result = New-PsResult -Status failure -Message "no stage produced result"
    }
    # 给 data 注入 _stages（若 data 是 hashtable 则直接加，否则包一层）
    if ($ctx.result.data -is [hashtable]) {
        $ctx.result.data["_stages"] = $ctx.stages.ToArray()
    } else {
        $ctx.result = New-PsResult -Status $ctx.result.status -Message $ctx.result.message `
            -Data @{ payload = $ctx.result.data; _stages = $ctx.stages.ToArray() } -Errors $ctx.result.errors
    }
    return $ctx.result
}
```

- [ ] **Step 4: 运行确认通过 → Step 5: 提交**

```bash
git add PowerShellScript/PsRuntime/Runtime/Invoke-PsContract.ps1 PowerShellScript/PsRuntime/Tests/Invoke-PsContract.Tests.ps1
git commit -m "feat(psrt): add stage-based contract executor (validate->env->execute->collect) with hook mount"
```

---

## Task 8: 为 BasicMethod 打动词优先契约（Bootstrap 数据）

**Files:**
- Modify: `PowerShellScript/BasicMethod/FileOps/FileAccess.psm1`（头部加标签）
- Create: `PowerShellScript/BasicMethod/FileOps/FileAccess.contracts.json`
- Create: `PowerShellScript/BasicMethod/FileOps/FileBatch.contracts.json`
- Create: `PowerShellScript/BasicMethod/Core/Path.contracts.json`
- Test: `PowerShellScript/PsRuntime/Tests/Bootstrap.Tests.ps1`

- [ ] **Step 1: 写失败测试**

```powershell
# PowerShellScript/PsRuntime/Tests/Bootstrap.Tests.ps1
BeforeAll { . $PSScriptRoot\..\Contract\ContractParser.psm1; $script:root = Resolve-Path "$PSScriptRoot\..\..\.." }

Describe "BasicMethod 动词优先契约" {
    It "FileAccess sidecar 含 read.file 与 write.file" {
        $f = Join-Path $script:root "PowerShellScript\BasicMethod\FileOps\FileAccess.psm1"
        $cs = Get-PsContracts -ScriptPath $f
        ($cs | Where-Object { $_.identity.action -eq "read.file" }) | Should -Not -BeNullOrEmpty
        ($cs | Where-Object { $_.identity.action -eq "write.file" }) | Should -Not -BeNullOrEmpty
    }
    It "FileBatch sidecar 含 replace.file.batch" {
        $cs = Get-PsContracts -ScriptPath (Join-Path $script:root "PowerShellScript\BasicMethod\FileOps\FileBatch.psm1")
        ($cs | Where-Object { $_.identity.action -eq "replace.file.batch" }) | Should -Not -BeNullOrEmpty
    }
    It "Path sidecar 含 resolve.path" {
        $cs = Get-PsContracts -ScriptPath (Join-Path $script:root "PowerShellScript\BasicMethod\Core\Path.psm1")
        ($cs | Where-Object { $_.identity.action -eq "resolve.path" }) | Should -Not -BeNullOrEmpty
    }
}
```

- [ ] **Step 2: 运行确认失败 → Step 3: 加标签与 sidecar**

在 `FileAccess.psm1` 头部注释块后追加：
```powershell
# @ps-action: read.file
# @verb: read
# @object: file
# @exports: Read-File, Search-Files, Write-File, Replace-InFile, Analyze-File, List-Directory
```
创建 `FileAccess.contracts.json`（read.file + write.file + replace.file 三个契约，function 分别为 Read-File/Write-File/Replace-InFile，path 均为 `FileAccess.psm1`，params 对照各函数实际参数）。

创建 `FileBatch.contracts.json`（`replace.file.batch` → BatchReplace-File 或该模块主函数）。
创建 `Path.contracts.json`（`resolve.path` → Resolve-ProjectPath）。

- [ ] **Step 4: 运行确认通过 → Step 5: 提交**

```bash
git add PowerShellScript/BasicMethod/FileOps/FileAccess.psm1 PowerShellScript/BasicMethod/FileOps/FileAccess.contracts.json PowerShellScript/BasicMethod/FileOps/FileBatch.contracts.json PowerShellScript/BasicMethod/Core/Path.contracts.json PowerShellScript/PsRuntime/Tests/Bootstrap.Tests.ps1
git commit -m "feat(psrt): annotate BasicMethod with verb-first contracts"
```

---

## Task 9: ContractScaffold.psm1（合规脚手架生成）

**Files:** Create `PowerShellScript/PsRuntime/Extension/ContractScaffold.psm1` + Test

- [ ] **Step 1: 写失败测试**

```powershell
# PowerShellScript/PsRuntime/Tests/ContractScaffold.Tests.ps1
BeforeAll {
    . $PSScriptRoot\..\Runtime\OutputSchema.psm1
    . $PSScriptRoot\..\Extension\ContractScaffold.psm1
}
Describe "ContractScaffold" {
    It "生成含动词优先标签与 New-PsResult 的脚手架" {
        $spec = @{ action = "count.file.lines"; verb = "count"; object = "file"; purpose = "统计行数"; function = "Get-FileLineCount"; params = @(@{ name = "Path"; type = "string"; required = $true }) }
        $out = Join-Path $env:TEMP "sc_$(Get-Random).psm1"
        $r = New-PsContractScript -Spec $spec -OutputPath $out
        $r.status | Should -Be "success"
        $c = Get-Content $out -Raw -Encoding UTF8
        $c | Should -Match "@ps-action: count.file.lines"
        $c | Should -Match "function Get-FileLineCount"
        $c | Should -Match "New-PsResult"
        $side = $out -replace '\.psm1$', '.contracts.json'
        (Get-Content $side -Raw -Encoding UTF8 | ConvertFrom-Json).contracts[0].identity.action | Should -Be "count.file.lines"
        Remove-Item $out, $side -Force
    }
}
```

- [ ] **Step 2: 运行确认失败 → Step 3: 实现**

```powershell
# PowerShellScript/PsRuntime/Extension/ContractScaffold.psm1
function New-PsContractScript {
    param([hashtable]$Spec, [string]$OutputPath)
    try {
        $fn = $Spec.function
        $paramBlock = (($Spec.params | ForEach-Object { $p = $_; $l = "    [$($p.type)]`$$($p.name)"; if ($p.default) { $l += " = $($p.default)" }; $l }) -join ",`n")
        if (-not $paramBlock) { $paramBlock = "" }
        $body = @"
<#
.SYNOPSIS
    $($Spec.purpose)
#>
# @ps-action: $($Spec.action)
# @verb: $($Spec.verb)
# @object: $($Spec.object)
# @exports: $fn
# @requires: PsRuntime

function $fn {
    param(
$paramBlock
    )
    # TODO: 由 SK 规划器填入实现逻辑，成功后返回 New-PsResult
    return New-PsResult -Status failure -Message "not implemented" -Errors @("scaffold 未填实现")
}
"@
        Set-Content -Path $OutputPath -Value $body -Encoding UTF8
        $sidecar = $OutputPath -replace '\.(ps1|psm1)$', '.contracts.json'
        $contract = @{
            identity = @{ action = $Spec.action; verb = $Spec.verb; object = $Spec.object; purpose = $Spec.purpose; function = $fn; path = (Split-Path $OutputPath -Leaf) }
            params = $Spec.params
            output_schema = @{ status = "success|failure"; message = "string"; data = "object"; errors = "array" }
            env_tags = @{ ps_version = "5.1+"; os = "windows"; modules = @("PsRuntime") }
        }
        @{ contracts = @($contract) } | ConvertTo-Json -Depth 6 | Set-Content -Path $sidecar -Encoding UTF8
        return New-PsResult -Status success -Message "scaffold generated" -Data @{ path = $OutputPath; sidecar = $sidecar }
    } catch {
        return New-PsResult -Status failure -Message "scaffold error" -Errors @($_.Exception.Message)
    }
}
Export-ModuleMember -Function New-PsContractScript
```

- [ ] **Step 4: 运行确认通过 → Step 5: 提交**

```bash
git add PowerShellScript/PsRuntime/Extension/ContractScaffold.psm1 PowerShellScript/PsRuntime/Tests/ContractScaffold.Tests.ps1
git commit -m "feat(psrt): add verb-first contract scaffold generator"
```

---

## Task 10: ContractRegistrar.psm1（注册入库，幂等）

**Files:** Create `PowerShellScript/PsRuntime/Extension/ContractRegistrar.psm1` + Test

- [ ] **Step 1: 写失败测试**

```powershell
# PowerShellScript/PsRuntime/Tests/ContractRegistrar.Tests.ps1
BeforeAll { . $PSScriptRoot\..\Runtime\OutputSchema.psm1; . $PSScriptRoot\..\Extension\ContractRegistrar.psm1 }
Describe "ContractRegistrar" {
    It "写入函数到注册表" {
        $reg = Join-Path $env:TEMP "reg_$(Get-Random).json"; @{ Functions = @{} } | ConvertTo-Json -Depth 5 | Set-Content $reg -Encoding UTF8
        $c = @{ identity = @{ action = "x.y"; function = "Do-X"; path = "X.psm1"; object = "y"; purpose = "p" } }
        (Register-PsContract -Contract $c -RegistryPath $reg).status | Should -Be "success"
        (Get-Content $reg -Raw -Encoding UTF8 | ConvertFrom-Json).Functions.'Do-X'.Path | Should -Be "X.psm1"
        Remove-Item $reg -Force
    }
    It "重复注册幂等" {
        $reg = Join-Path $env:TEMP "reg2_$(Get-Random).json"; @{ Functions = @{} } | ConvertTo-Json -Depth 5 | Set-Content $reg -Encoding UTF8
        $c = @{ identity = @{ action = "x.y"; function = "Do-X"; path = "X.psm1"; object = "y"; purpose = "p" } }
        Register-PsContract -Contract $c -RegistryPath $reg | Out-Null
        Register-PsContract -Contract $c -RegistryPath $reg | Out-Null
        ((Get-Content $reg -Raw -Encoding UTF8 | ConvertFrom-Json).Functions.PSObject.Properties | Where-Object Name -eq 'Do-X').Count | Should -Be 1
        Remove-Item $reg -Force
    }
}
```

- [ ] **Step 2: 运行确认失败 → Step 3: 实现**

```powershell
# PowerShellScript/PsRuntime/Extension/ContractRegistrar.psm1
function Register-PsContract {
    param([hashtable]$Contract, [string]$RegistryPath)
    try {
        $reg = Get-Content $RegistryPath -Raw -Encoding UTF8 | ConvertFrom-Json -AsHashtable
        if (-not $reg.ContainsKey("Functions")) { $reg["Functions"] = @{} }
        $fn = $Contract.identity.function
        $reg.Functions[$fn] = @{
            Version = ""; Description = $Contract.identity.purpose
            Alias = $Contract.identity.action; Path = $Contract.identity.path
            Author = "PsRuntime"; Category = $Contract.identity.object
        }
        $reg | ConvertTo-Json -Depth 6 | Set-Content $RegistryPath -Encoding UTF8
        return New-PsResult -Status success -Message "registered" -Data @{ function = $fn }
    } catch {
        return New-PsResult -Status failure -Message "register error" -Errors @($_.Exception.Message)
    }
}
Export-ModuleMember -Function Register-PsContract
```

- [ ] **Step 4: 运行确认通过 → Step 5: 提交**

```bash
git add PowerShellScript/PsRuntime/Extension/ContractRegistrar.psm1 PowerShellScript/PsRuntime/Tests/ContractRegistrar.Tests.ps1
git commit -m "feat(psrt): add idempotent contract registrar"
```

---

## Task 11: UsageTracker.psm1（用量与升级阈值）

**Files:** Create `PowerShellScript/PsRuntime/Extension/UsageTracker.psm1` + Test

- [ ] **Step 1: 写失败测试**

```powershell
# PowerShellScript/PsRuntime/Tests/UsageTracker.Tests.ps1
BeforeAll { . $PSScriptRoot\..\Extension\UsageTracker.psm1 }
Describe "UsageTracker" {
    It "累计调用次数" {
        $s = Join-Path $env:TEMP "u_$(Get-Random).json"
        Update-PsContractUsage -Action "x.y" -StorePath $s | Out-Null
        (Get-PsContractUsage -Action "x.y" -StorePath $s).Count | Should -Be 1
        Remove-Item $s -Force
    }
    It "达阈值返回应升级" {
        $s = Join-Path $env:TEMP "u2_$(Get-Random).json"
        1..3 | ForEach-Object { Update-PsContractUsage -Action "p.q" -StorePath $s | Out-Null }
        (Get-PsContractPromotion -Action "p.q" -StorePath $s -Threshold 3 -WindowDays 30).ShouldPromote | Should -Be $true
        Remove-Item $s -Force
    }
    It "未达阈值不升级" {
        $s = Join-Path $env:TEMP "u3_$(Get-Random).json"
        Update-PsContractUsage -Action "p.q" -StorePath $s | Out-Null
        (Get-PsContractPromotion -Action "p.q" -StorePath $s -Threshold 3 -WindowDays 30).ShouldPromote | Should -Be $false
        Remove-Item $s -Force
    }
}
```

- [ ] **Step 2: 运行确认失败 → Step 3: 实现**

```powershell
# PowerShellScript/PsRuntime/Extension/UsageTracker.psm1
function _Load-Usage { param([string]$StorePath); if (Test-Path $StorePath) { return Get-Content $StorePath -Raw -Encoding UTF8 | ConvertFrom-Json -AsHashtable } ; return @{ records = @{} } }
function Update-PsContractUsage {
    param([string]$Action, [string]$StorePath)
    $u = _Load-Usage -StorePath $StorePath
    if (-not $u.records.ContainsKey($Action)) { $u.records[$Action] = @() }
    $u.records[$Action] += (Get-Date).ToString("o")
    $u | ConvertTo-Json -Depth 6 | Set-Content $StorePath -Encoding UTF8
    return $u.records[$Action].Count
}
function Get-PsContractUsage { param([string]$Action, [string]$StorePath); $u = _Load-Usage -StorePath $StorePath; if (-not $u.records.ContainsKey($Action)) { return @() }; return $u.records[$Action] }
function Get-PsContractPromotion {
    param([string]$Action, [string]$StorePath, [int]$Threshold = 3, [int]$WindowDays = 30)
    $recs = Get-PsContractUsage -Action $Action -StorePath $StorePath
    $cutoff = (Get-Date).AddDays(-$WindowDays)
    $recent = $recs | Where-Object { [datetime]$_ -ge $cutoff }
    return [PSCustomObject]@{ ShouldPromote = ($recent.Count -ge $Threshold); RecentCount = $recent.Count; Threshold = $Threshold }
}
Export-ModuleMember -Function Update-PsContractUsage, Get-PsContractUsage, Get-PsContractPromotion
```

- [ ] **Step 4: 运行确认通过 → Step 5: 提交**

```bash
git add PowerShellScript/PsRuntime/Extension/UsageTracker.psm1 PowerShellScript/PsRuntime/Tests/UsageTracker.Tests.ps1
git commit -m "feat(psrt): add usage tracker and promotion threshold"
```

---

## Task 12: Invoke-PsExtension.ps1（拓展协议编排）

**Files:** Create `PowerShellScript/PsRuntime/Extension/Invoke-PsExtension.ps1` + Test

- [ ] **Step 1: 写失败测试**

```powershell
# PowerShellScript/PsRuntime/Tests/Invoke-PsExtension.Tests.ps1
BeforeAll {
    . $PSScriptRoot\..\Runtime\OutputSchema.psm1
    . $PSScriptRoot\..\Contract\ContractValidator.psm1
    . $PSScriptRoot\..\Extension\ContractScaffold.psm1
    . $PSScriptRoot\..\Extension\ContractRegistrar.psm1
    . $PSScriptRoot\..\Extension\UsageTracker.psm1
    . $PSScriptRoot\..\Extension\Invoke-PsExtension.ps1
}
Describe "Invoke-PsExtension" {
    It "产出脚手架并注册，phase=scaffolded" {
        $tmp = Join-Path $env:TEMP "ext_$(Get-Random)"; New-Item -Path $tmp -ItemType Directory -Force | Out-Null
        $spec = @{ action = "demo.ops.run"; verb = "demo"; object = "ops"; purpose = "demo"; function = "Invoke-Demo"; params = @() }
        $reg = Join-Path $tmp "reg.json"; @{ Functions = @{} } | ConvertTo-Json -Depth 5 | Set-Content $reg -Encoding UTF8
        $r = Invoke-PsExtension -Spec $spec -OutDir $tmp -RegistryPath $reg -UsagePath (Join-Path $tmp "u.json") -Arguments @{}
        $r.status | Should -Be "success"; $r.data.phase | Should -Be "scaffolded"; $r.data.action | Should -Be "demo.ops.run"
        Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
    }
}
```

- [ ] **Step 2: 运行确认失败 → Step 3: 实现**

```powershell
# PowerShellScript/PsRuntime/Extension/Invoke-PsExtension.ps1
function Invoke-PsExtension {
    param(
        [hashtable]$Spec, [hashtable]$Arguments,
        [string]$OutDir, [string]$RegistryPath, [string]$UsagePath,
        [int]$PromotionThreshold = 3, [int]$PromotionWindowDays = 30
    )
    # 1.检测：调用方已判定 CanHandle=false（逻辑闭合）  2.决策：固定生成一次性脚本，升级由用量判定
    # 3.生成脚手架
    $scriptPath = Join-Path $OutDir "$($Spec.function).psm1"
    $gen = New-PsContractScript -Spec $Spec -OutputPath $scriptPath
    if ($gen.status -ne "success") { return $gen }
    # 4.注册
    $contract = @{
        identity = @{ action = $Spec.action; verb = $Spec.verb; object = $Spec.object; purpose = $Spec.purpose; function = $Spec.function; path = $scriptPath }
        params = $Spec.params; env_tags = @{ ps_version = "5.1+"; os = "windows"; modules = @() }
    }
    $reg = Register-PsContract -Contract $contract -RegistryPath $RegistryPath
    if ($reg.status -ne "success") { return $reg }
    # 5.执行：脚手架未填实现，返回 scaffolded 等待 SK 填充后重试（不在 PS1 内调 LLM）
    # 6.升级：记录用量
    $null = Update-PsContractUsage -Action $Spec.action -StorePath $UsagePath
    $promo = Get-PsContractPromotion -Action $Spec.action -StorePath $UsagePath -Threshold $PromotionThreshold -WindowDays $PromotionWindowDays
    return New-PsResult -Status success -Message "extension scaffolded, awaiting implementation" -Data @{
        phase = "scaffolded"; action = $Spec.action; function = $Spec.function; path = $scriptPath; promotion = $promo
    }
}
Export-ModuleMember -Function Invoke-PsExtension
```

- [ ] **Step 4: 运行确认通过 → Step 5: 提交**

```bash
git add PowerShellScript/PsRuntime/Extension/Invoke-PsExtension.ps1 PowerShellScript/PsRuntime/Tests/Invoke-PsExtension.Tests.ps1
git commit -m "feat(psrt): add extension protocol orchestrator"
```

---

## Task 13: Invoke-PsAction.ps1（★ LLM 单一入口，动词映射 + 归一化）

**Files:** Create `PowerShellScript/PsRuntime/Runtime/Invoke-PsAction.ps1` + Test

> **复用 CommandFramework：** 本入口复用 C# [CommandParser](file:///d:\Project\Program\WorkFlowPrj\CsharpPrj\Share\CommandFramework\Parsing\CommandParser.cs#L31) 的「动词+对象+参数」三元组模型与 [ExecuteCommandStage](file:///d:\Project\Program\WorkFlowPrj\CsharpPrj\Share\CommandFramework\Invoke\ExecuteCommandStage.cs#L66) 的三段式 dispatch（精确 handler → workflow fallback → non-event 兜底）。在 resolve 前调用 Task 5 的 `Normalize-PsAction`，对应 C# `Normalize()`，处理 LLM 简写。

**动词映射表（Ps1 ↔ C# ParsedCommand）：**

| Ps1 `verb.object` | C# `Verb` | C# `Target` | C# `Action`(do 前缀) | 说明 |
|---|---|---|---|---|
| `read.file` | `read` | `file` | `do read file` | 动词=verb，对象=object，dot 替代空格 |
| `write.file` | `write` | `file` | `do write file` | 同上 |
| `replace.file.batch` | `replace` | `file.batch` | `do replace file.batch` | modifier 进 object |
| `resolve.path` | `resolve` | `path` | `do resolve path` | 同上 |

> 映射规则：`verb.object` ↔ C# `Verb.Target`；Ps1 用 dot 分隔避免空格歧义，C# 用空格 + `do` 前缀。`Params` 双方一致（key=value 字典）。`Action`（C# do 后第一段）在 Ps1 中即完整 `verb.object`。

- [ ] **Step 1: 写失败测试**

```powershell
# PowerShellScript/PsRuntime/Tests/Invoke-PsAction.Tests.ps1
BeforeAll {
    . $PSScriptRoot\..\Runtime\OutputSchema.psm1
    . $PSScriptRoot\..\Contract\ContractValidator.psm1
    . $PSScriptRoot\..\Capability\CapabilityResolver.psm1
    . $PSScriptRoot\..\Gate\CanHandler.psm1
    . $PSScriptRoot\..\Runtime\Invoke-PsContract.ps1
    . $PSScriptRoot\..\Extension\Invoke-PsExtension.ps1
    . $PSScriptRoot\..\Runtime\Invoke-PsAction.ps1
    $script:tmp = Join-Path $env:TEMP "psa_$(Get-Random)"; New-Item -Path $script:tmp -ItemType Directory -Force | Out-Null
    $script:mod = Join-Path $script:tmp "Echo.psm1"
    @"
# @ps-action: echo.test
function Invoke-Echo { param([string]`$Msg) @{ ok = `$Msg } }
"@ | Set-Content $script:mod -Encoding UTF8
    $script:cs = @(@{ identity = @{ action = "echo.test"; verb = "echo"; object = "test"; function = "Invoke-Echo"; path = $script:mod }; params = @(@{ name = "Msg"; type = "string"; required = $true }); env_tags = @{ ps_version = "5.1+"; os = "windows"; modules = @() } } })
}
AfterAll { Remove-Item $script:tmp -Recurse -Force -ErrorAction SilentlyContinue }

Describe "Invoke-PsAction" {
    It "命中动作→执行→success（LLM 单一调用）" {
        $r = Invoke-PsAction -Action "echo.test" -Params @{ Msg = "hi" } -Contracts $script:cs
        $r.status | Should -Be "success"; $r.data.ok | Should -Be "hi"
    }
    It "未命中且无 Spec→返回 failure 标 no_capability" {
        $r = Invoke-PsAction -Action "nope.nope" -Params @{} -Contracts $script:cs
        $r.status | Should -Be "failure"; $r.errors[0] | Should -Match "no_capability"
    }
    It "未命中但有 Spec→走拓展协议 scaffolded" {
        $spec = @{ action = "demo.run"; verb = "demo"; object = "run"; purpose = "d"; function = "Invoke-Demo"; params = @() }
        $reg = Join-Path $script:tmp "reg.json"; @{ Functions = @{} } | ConvertTo-Json -Depth 5 | Set-Content $reg -Encoding UTF8
        $r = Invoke-PsAction -Action "demo.run" -Params @{} -Contracts $script:cs -ExtensionSpec $spec -OutDir $script:tmp -RegistryPath $reg -UsagePath (Join-Path $script:tmp "u.json")
        $r.data.phase | Should -Be "scaffolded"
    }
}

Describe "Invoke-PsAction 归一化（复用 C# Normalize，转交 Task 5 Normalize-PsAction）" {
    It "LLM 简写 verb + DefaultObject→归一化后命中" {
        # LLM 发 "echo"（缺 object），配 DefaultObject=test 后归一化为 echo.test
        $r = Invoke-PsAction -Action "echo" -Params @{ Msg = "hi" } -Contracts $script:cs -DefaultObject "test"
        $r.status | Should -Be "success"; $r.data.ok | Should -Be "hi"
    }
    It "别名经 Register-PsActionAlias→归一化后命中" {
        Register-PsActionAlias -Alias "回显" -Action "echo.test"
        $r = Invoke-PsAction -Action "回显" -Params @{ Msg = "hi" } -Contracts $script:cs
        $r.status | Should -Be "success"; $r.data.ok | Should -Be "hi"
    }
    It "未归一化且无 DefaultObject→原样落 no_capability" {
        $r = Invoke-PsAction -Action "echo" -Params @{} -Contracts $script:cs
        $r.status | Should -Be "failure"; $r.errors[0] | Should -Match "no_capability"
    }
}
```

- [ ] **Step 2: 运行确认失败 → Step 3: 实现**

```powershell
# PowerShellScript/PsRuntime/Runtime/Invoke-PsAction.ps1
# 动词映射表（Ps1 verb.object ↔ C# ParsedCommand.Verb/Target）：
#   read.file      ↔ Verb=read, Target=file,    C# do read file
#   write.file     ↔ Verb=write, Target=file,   C# do write file
#   resolve.path   ↔ Verb=resolve, Target=path, C# do resolve path
# dispatch 三段式（复用 C# ExecuteCommandStage 第 79-97 行）：
#   精确命中(handler) → gate 门控(workflow) → extension 兜底(non-event)
function Invoke-PsAction {
    param(
        [Parameter(Mandatory)][string]$Action,     # 动词优先，如 read.file
        [Parameter(Mandatory)][hashtable]$Params,
        [array]$Contracts,                          # 已注册契约集（由 Register-PsContracts 产出）
        [hashtable]$ExtensionSpec,                  # 可选：未命中时的拓展协议输入
        [string]$DefaultObject,                     # 可选：归一化时 verb 缺 object 的默认 object（对应 C# Normalize 默认补 do）
        [string]$OutDir, [string]$RegistryPath, [string]$UsagePath,
        [int]$PromotionThreshold = 3, [int]$PromotionWindowDays = 30
    )
    # 0.归一化（对应 C# CommandParser.Normalize）：处理 LLM 简写/别名
    $normalized = if ($DefaultObject) { Normalize-PsAction -Action $Action -DefaultObject $DefaultObject }
                  else { Normalize-PsAction -Action $Action }

    # 1.解析动作 → 命中契约（对应 C# ExecuteCommandStage 精确 handler 命中）
    $contract = Resolve-PsCapability -Contracts $Contracts -Action $normalized
    if ($contract) {
        # 2.CanHandler 门控 → 命中执行（对应 C# workflow fallback 之前的 handler 校验）
        $gate = Test-PsCanHandle -Contract $contract -Arguments $Params
        if ($gate.CanHandle) {
            return Invoke-PsContract -Contract $contract -Arguments $Params
        }
        return New-PsResult -Status failure -Message "cannot handle: $($gate.Reason)" -Errors @($gate.Reason)
    }
    # 3.未命中：有 Spec → 拓展协议（对应 C# non-event 兜底/拓展）；无 Spec → 报缺能力
    if ($ExtensionSpec) {
        return Invoke-PsExtension -Spec $ExtensionSpec -Arguments $Params -OutDir $OutDir -RegistryPath $RegistryPath -UsagePath $UsagePath -PromotionThreshold $PromotionThreshold -PromotionWindowDays $PromotionWindowDays
    }
    return New-PsResult -Status failure -Message "no capability for action: $normalized" -Errors @("no_capability")
}
Export-ModuleMember -Function Invoke-PsAction
```

- [ ] **Step 4: 运行确认通过 → Step 5: 提交**

```bash
git add PowerShellScript/PsRuntime/Runtime/Invoke-PsAction.ps1 PowerShellScript/PsRuntime/Tests/Invoke-PsAction.Tests.ps1
git commit -m "feat(psrt): add Invoke-PsAction LLM single entry (action-first) with normalization + verb mapping"
```

---

## Task 14: _init.ps1 + 配置 + Register-PsContracts + README

**Files:**
- Create: `PowerShellScript/PsRuntime/_init.ps1`
- Create: `PowerShellScript/PsRuntime/Bootstrap/Register-PsContracts.ps1`
- Create: `PowerShellScript/PsRuntime/README.md`
- Test: `PowerShellScript/PsRuntime/Tests/Init.Tests.ps1`

- [ ] **Step 1: 写失败测试**

```powershell
# PowerShellScript/PsRuntime/Tests/Init.Tests.ps1
Describe "PsRuntime init" {
    It "init 后核心函数可用" {
        . "$PSScriptRoot\..\_init.ps1"
        (Get-Command Invoke-PsAction -ErrorAction SilentlyContinue) | Should -Not -BeNullOrEmpty
        (Get-Command New-PsResult -ErrorAction SilentlyContinue) | Should -Not -BeNullOrEmpty
        (Get-Command Register-PsContracts -ErrorAction SilentlyContinue) | Should -Not -BeNullOrEmpty
    }
    It "Register-PsContracts 扫描 BasicMethod 返回契约数 >0" {
        . "$PSScriptRoot\..\_init.ps1"
        $root = Resolve-Path "$PSScriptRoot\..\..\.."
        $r = Register-PsContracts -ProjectRoot $root -ContractDirs @("PowerShellScript\BasicMethod")
        $r.status | Should -Be "success"; $r.data.count | Should -BeGreaterThan 0
    }
}
```

- [ ] **Step 2: 运行确认失败 → Step 3: 实现 _init.ps1**

```powershell
# PowerShellScript/PsRuntime/_init.ps1
$script:PsRuntimeRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$modules = @(
    "Runtime\OutputSchema.psm1", "Contract\ContractSchema.psm1", "Contract\ContractParser.psm1",
    "Contract\ContractValidator.psm1", "Capability\CapabilityResolver.psm1", "Gate\CanHandler.psm1",
    "Runtime\Invoke-PsContract.ps1", "Extension\ContractScaffold.psm1", "Extension\ContractRegistrar.psm1",
    "Extension\UsageTracker.psm1", "Extension\Invoke-PsExtension.ps1", "Runtime\Invoke-PsAction.ps1",
    "Bootstrap\Register-PsContracts.ps1"
)
foreach ($m in $modules) { $p = Join-Path $script:PsRuntimeRoot $m; if (Test-Path $p) { . $p } }
```

- [ ] **Step 4: 实现 Register-PsContracts.ps1**

```powershell
# PowerShellScript/PsRuntime/Bootstrap/Register-PsContracts.ps1
function Register-PsContracts {
    param([string]$ProjectRoot, [string[]]$ContractDirs)
    try {
        $contracts = @()
        foreach ($dir in $ContractDirs) {
            $abs = Join-Path $ProjectRoot $dir
            if (-not (Test-Path $abs)) { continue }
            foreach ($f in (Get-ChildItem -Path $abs -Recurse -Include *.psm1, *.ps1 -File -ErrorAction SilentlyContinue)) {
                $cs = Get-PsContracts -ScriptPath $f.FullName
                foreach ($c in $cs) { if ($c.identity.action) { $contracts += $c } }
            }
        }
        return New-PsResult -Status success -Message "scanned" -Data @{ count = $contracts.Count; contracts = $contracts }
    } catch {
        return New-PsResult -Status failure -Message "scan error" -Errors @($_.Exception.Message)
    }
}
Export-ModuleMember -Function Register-PsContracts
```

- [ ] **Step 5: 创建 README.md**

```markdown
# PsRuntime — 通用 Ps1 契约运行时

> 复用 PowerShellScript/PScriptManager（注册表）与 BasicMethod（能力库）。
> 通用：不绑定任何宿主（Cline/MCP/人 均可调用）。

## LLM 单一入口
. .\PowerShellScript\PsRuntime\_init.ps1
$cs = (Register-PsContracts -ProjectRoot $root -ContractDirs @("PowerShellScript\BasicMethod")).data.contracts
Invoke-PsAction -Action "read.file" -Params @{ path = "X" } -Contracts $cs

## 流程
Action → Resolve-PsCapability → Test-PsCanHandle → Invoke-PsContract → {status,message,data,errors}
未命中（有 Spec）→ Invoke-PsExtension → 脚手架→注册→用量→升级

## 设计依据
Agent/docs/Ps1执行框架设计.md（动词优先动作命名：read.file）
```

- [ ] **Step 6: 运行确认通过 → Step 7: 跑全量测试**

Run: `Invoke-Pester -Path .\PowerShellScript\PsRuntime\Tests\ -Output Detailed` → 全部 PASS

- [ ] **Step 8: 提交**

```bash
git add PowerShellScript/PsRuntime/_init.ps1 PowerShellScript/PsRuntime/Bootstrap/Register-PsContracts.ps1 PowerShellScript/PsRuntime/README.md PowerShellScript/PsRuntime/Tests/Init.Tests.ps1
git commit -m "feat(psrt): add init loader, generic bootstrap scanner, readme"
```

---

## Task 15: Agent 薄绑定（配置 + 入口适配）

**Files:**
- Create: `Agent/Config/ps1-runtime.json`
- Create: `Agent/PsRuntimeBinding/Invoke-AgentStep.ps1`
- Test: `Agent/PsRuntimeBinding/Invoke-AgentStep.Tests.ps1`

- [ ] **Step 1: 写失败测试**

```powershell
# Agent/PsRuntimeBinding/Invoke-AgentStep.Tests.ps1
Describe "AgentStep binding" {
    It "Invoke-AgentStep 加载 PsRuntime + Agent 配置并暴露入口" {
        . "$PSScriptRoot\Invoke-AgentStep.ps1"
        (Get-Command Invoke-AgentStep -ErrorAction SilentlyContinue) | Should -Not -BeNullOrEmpty
    }
    It "Invoke-AgentStep read.file 命中返回 success" {
        . "$PSScriptRoot\Invoke-AgentStep.ps1"
        $r = Invoke-AgentStep -Action "read.file" -Params @{ path = "$PSScriptRoot\Invoke-AgentStep.Tests.ps1" }
        $r.status | Should -Be "success"
    }
}
```

- [ ] **Step 2: 运行确认失败 → Step 3: 创建配置**

```json
// Agent/Config/ps1-runtime.json
{
  "projectRoot": "..\\..",
  "contractDirs": [ "PowerShellScript\\BasicMethod" ],
  "registry": { "psmRegistry": "PowerShellScript\\PScriptManager\\psm-registry.json" },
  "usage": { "store": "Agent\\Config\\ps1-usage.json", "promotionThreshold": 3, "promotionWindowDays": 30 }
}
```

- [ ] **Step 4: 实现 Invoke-AgentStep.ps1**

```powershell
# Agent/PsRuntimeBinding/Invoke-AgentStep.ps1
$agentRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$psrtRoot = Join-Path $agentRoot "..\PowerShellScript\PsRuntime"
. (Join-Path $psrtRoot "_init.ps1")

function Invoke-AgentStep {
    param(
        [Parameter(Mandatory)][string]$Action,
        [Parameter(Mandatory)][hashtable]$Params,
        [hashtable]$ExtensionSpec
    )
    $cfg = Get-Content (Join-Path $agentRoot "Config\ps1-runtime.json") -Raw -Encoding UTF8 | ConvertFrom-Json
    $root = Join-Path $agentRoot $cfg.projectRoot
    $cs = (Register-PsContracts -ProjectRoot $root -ContractDirs $cfg.contractDirs).data.contracts
    return Invoke-PsAction -Action $Action -Params $Params -Contracts $cs -ExtensionSpec $ExtensionSpec `
        -OutDir (Join-Path $agentRoot "Config\generated") `
        -RegistryPath (Join-Path $root $cfg.registry.psmRegistry) `
        -UsagePath (Join-Path $agentRoot $cfg.usage.store) `
        -PromotionThreshold $cfg.usage.promotionThreshold -PromotionWindowDays $cfg.usage.promotionWindowDays
}
```

- [ ] **Step 5: 运行确认通过 → Step 6: 提交**

```bash
git add Agent/Config/ps1-runtime.json Agent/PsRuntimeBinding/Invoke-AgentStep.ps1 Agent/PsRuntimeBinding/Invoke-AgentStep.Tests.ps1
git commit -m "feat(psrt): add thin Agent binding (config + Invoke-AgentStep entry)"
```

---

## 自检结果

**1. 设计文档覆盖（对照 Ps1执行框架设计.md）：**
- §三 契约元数据规范 → Task 1（schema）+ Task 3（解析，动词优先 + 多契约 sidecar）+ Task 8（落地）✓
- §四 能力解析器两层检索 → Task 5（object 过滤 + action 精确匹配）✓
- §五.1 CanHandler → Task 6 ✓
- §五.2 确定性执行包装 → Task 7 + Task 13（统一入口）✓
- §五.3 拓展协议六步 → Task 9/10/11/12 ✓
- §五.4 能力飞轮 → Task 11/12 用量与升级阈值 ✓
- §七 文件结构 → 通用运行时 + Agent 薄绑定 ✓
- §八 验收 4 条：Ps1 优先（Task 13/8）、边界拓展闭环（Task 12/13）、确定性输出（Task 2/7）、参数/环境预校验（Task 4/6）✓

**2. 通用化：** 运行时位于 `PowerShellScript/PsRuntime/`，不引用 Agent/Cline；任何宿主经 `Invoke-PsAction` 即可用；Agent 侧仅 Task 15 薄绑定。✓

**3. LLM 语序对齐：** 动作动词优先（`read.file`）；契约 `identity` 以 action/verb/object 打头；LLM 唯一调用 `Invoke-PsAction -Action -Params`（动作在前、参数在后）。✓

**4. 类型一致：** `New-PsResult`/`ConvertTo-PsResult`、`Get-PsContracts`/`Get-PsScriptTags`、`Test-PsContractParams`/`Test-PsContractEnv`、`Resolve-PsCapability`、`Test-PsCanHandle`、`Invoke-PsContract`、`New-PsContractScript`、`Register-PsContract`、`Update-PsContractUsage`/`Get-PsContractPromotion`、`Invoke-PsExtension`、`Invoke-PsAction`、`Register-PsContracts`、`Invoke-AgentStep` 跨任务命名一致。✓

**已知边界：**
- 拓展协议「执行」步骤：脚手架返回 `scaffolded`，逻辑填空由 Agent C# 侧 SK 规划器完成（PS1 框架不调 LLM），填充后再次 `Invoke-PsAction`。
- 能力匹配为确定性精确匹配（无语义/向量），与既定决策一致。
- `ConvertFrom-Json -AsHashtable` 需 PS 6+；PS 5.1 下在 `_init.ps1` 增加哈希降级（实现时按实际版本适配）。

**5. 复用 CommandFramework 动词逻辑（新增）：**
- **归一化层**（Task 5）：新增 `Normalize-PsAction` + `Register-PsActionAlias`，对应 C# [CommandParser.Normalize](file:///d:\Project\Program\WorkFlowPrj\CsharpPrj\Share\CommandFramework\Parsing\CommandParser.cs#L103) + `NormalizationRegistry`。LLM 简写（`read` → `read.file`）与别名（`读取` → `read.file`）在 resolve 前归一化，避免直接落 `no_capability`。
- **stage 化执行**（Task 7）：`Invoke-PsContract` 由内联改为 4 段 stage（ValidateParams → ValidateEnv → ExecuteFunction → CollectResult），对应 C# [IPipelineStage](file:///d:\Project\Program\WorkFlowPrj\CsharpPrj\Share\CommandFramework\Execution\Shared\Abstractions\ICommand.cs#L171) 链；`-AfterStageHook` 挂载点预留 retry/logging（对应 C# RetryStage/LoggingStage），与项目记忆「Polly retry/timeout」「增强现有 loop 而非新模块」一致。
- **动词映射表 + 归一化入口**（Task 13）：`Invoke-PsAction` 在 resolve 前调用 `Normalize-PsAction`，并显式声明 `verb.object` ↔ C# `Verb/Target` 映射（dot 替代空格，`do` 前缀省略），dispatch 三段式对应 C# [ExecuteCommandStage](file:///d:\Project\Program\WorkFlowPrj\CsharpPrj\Share\CommandFramework\Invoke\ExecuteCommandStage.cs#L66)（精确 handler → gate 门控 → extension 兜底）。
- **不复用项**：`ICommand.UndoAsync`、`IEnvironmentAdapter`（Do/Get/Look/Wait/Assert GUI 五动词）、`_eventHandlers`（set/click）、`ExecutionCell`/`IRuntimeContext`/`JsonVerb` 枚举——均为 GUI/容器语义，与 Ps1 通用 LLM 入口无关。

---

## 执行方式选择

计划已保存至 `Agent/docs/plans/2026-08-13-ps1-runtime-framework-plan.md`。两种执行方式：

**1. Subagent-Driven（推荐）** — 每个 Task 派发独立子代理，任务间审查，快速迭代。

**2. Inline Execution** — 在当前会话内按任务批量执行，带检查点审查。

请选择执行方式。
