# ContractSchema.psm1 — 通用四维契约 schema 加载与校验（边界安全：null/缺失字段容错）
$script:_SchemaCache = $null
$script:_SchemaPath = Join-Path "PowerShellScript" (Join-Path "PScriptManager" "contract-schema.json")

function Get-PsContractSchema {
    param([string]$ProjectRoot)
    if ($script:_SchemaCache) { return $script:_SchemaCache }
    $path = if ($ProjectRoot) { Join-Path $ProjectRoot $script:_SchemaPath } else { $script:_SchemaPath }
    # 文件不存在直接抛（边界：让调用方感知 schema 缺失，而非静默返回空）
    $script:_SchemaCache = Get-Content $path -Raw -Encoding UTF8 -ErrorAction Stop | ConvertFrom-Json
    return $script:_SchemaCache
}

function Test-PsContract {
    # 边界安全：允许 null/空，不抛异常，只产出 errors
    param([hashtable]$Contract)
    $errors = @()

    # null 契约：四个顶层维度全标记
    if (-not $Contract) {
        foreach ($k in @("identity", "params", "output_schema", "env_tags")) { $errors += $k }
        return [PSCustomObject]@{ IsValid = $false; Errors = $errors }
    }

    # 顶层维度缺失检测
    foreach ($k in @("identity", "params", "output_schema", "env_tags")) {
        if (-not $Contract.ContainsKey($k)) { $errors += $k }
    }

    # identity 子字段缺失检测（identity 存在但为 null 时所有子字段标记）
    if ($Contract.ContainsKey("identity")) {
        $id = $Contract.identity
        if ($id) {
            foreach ($k in @("action", "verb", "object", "function", "path")) {
                if (-not $id.ContainsKey($k)) { $errors += "identity.$k" }
            }
        } else {
            # identity 为 null：所有子字段标记
            foreach ($k in @("action", "verb", "object", "function", "path")) { $errors += "identity.$k" }
        }
    }

    return [PSCustomObject]@{ IsValid = ($errors.Count -eq 0); Errors = $errors }
}

Export-ModuleMember -Function Get-PsContractSchema, Test-PsContract
