param(
    [string]$ProjectRoot = '',
    [string]$AgentRoot = '',
    [string]$ConfigRoot = ''
)

# PsRuntime 长驻宿主（JSON Lines stdio）：一次启动、多次调用；启动先自检合同数 > 0。
# 请求行：{"id":"1","action":"read.file","params":{...}}
# 响应行：{"id":"1","status":"success|failure","message":"","data":...,"errors":[...]}

$ErrorActionPreference = 'Stop'
try {
    [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
    [Console]::InputEncoding = [System.Text.UTF8Encoding]::new($false)
} catch { }

. (Join-Path $PSScriptRoot 'Invoke-AgentStep.ps1')

function Write-Line([object]$Payload) {
    $json = $Payload | ConvertTo-Json -Depth 32 -Compress
    [Console]::Out.WriteLine($json)
    [Console]::Out.Flush()
}

# 启动自检：契约数 > 0，否则显式失败（不静默“无能力”）。
$contractCount = 0
try {
    $scanRoot = if ($ProjectRoot) { $ProjectRoot } else { (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path }
    $cfgPath = if ($ConfigRoot) { Join-Path $ConfigRoot 'ps1-runtime.json' } else { Join-Path $AgentRoot 'Config\ps1-runtime.json' }
    if (Test-Path $cfgPath) {
        $cfg = Get-Content $cfgPath -Raw -Encoding UTF8 | ConvertFrom-Json
        $psRoot = if ([System.IO.Path]::IsPathRooted([string]$cfg.projectRoot)) { [string]$cfg.projectRoot }
                  else { Join-Path $scanRoot ([string]$cfg.projectRoot) }
        $dirs = @($cfg.contractDirs)
        foreach ($d in $dirs) {
            $abs = if ([System.IO.Path]::IsPathRooted($d)) { $d } else { Join-Path $psRoot $d }
            if (Test-Path $abs) {
                $scan = Register-PsContracts -ProjectRoot $psRoot -ContractDirs @($d)
                if ($scan.status -eq 'success') { $contractCount += [int]$scan.data.count }
            }
        }
    }
} catch { }

Write-Line ([ordered]@{ id = '0'; status = if ($contractCount -gt 0) { 'ready' } else { 'failure' }; contractCount = $contractCount })

if ($contractCount -le 0) { exit 2 }

while ($true) {
    $line = [Console]::In.ReadLine()
    if ($null -eq $line) { break }
    $line = $line.TrimStart([char]0xFEFF)
    if ([string]::IsNullOrWhiteSpace($line)) { continue }

    # 每次调用前重置 CWD（E30）：长驻进程共享，避免上一任务的 Set-Location 污染下一任务。
    if ($ProjectRoot) { try { Set-Location -LiteralPath $ProjectRoot } catch { } }

    $request = $null
    try { $request = $line | ConvertFrom-Json } catch {
        Write-Line ([ordered]@{ id = ''; status = 'failure'; message = 'request parse error'; errors = @($_.Exception.Message) })
        continue
    }

    $id = [string]$request.id
    try {
        # 写幂等（内容哈希）：非追加写且目标内容逐字相同 ⇒ no-op（不落盘、不改 mtime）。
        if ([string]$request.action -eq 'write.file' -and $null -ne $request.params) {
            $wp = $request.params
            $isAppend = [bool]$wp.Append
            # Backup/Verify 显式请求时不得走 no-op（否则备份/校验约定失效）。
            $allowNoop = -not ([bool]$wp.Backup) -and -not ([bool]$wp.Verify)
            if ($allowNoop -and -not $isAppend -and $null -ne $wp.Content -and -not [string]::IsNullOrWhiteSpace([string]$wp.Path)) {
                try {
                    $targetPath = [string]$wp.Path
                    if (Test-Path -LiteralPath $targetPath) {
                        $encodingName = if ($wp.PSObject.Properties.Name -contains 'Encoding') { [string]$wp.Encoding } else { '' }
                        $encoding = switch ($encodingName.ToUpperInvariant()) {
                            'UTF8-BOM' { New-Object System.Text.UTF8Encoding($true) }
                            'ASCII' { [System.Text.Encoding]::ASCII }
                            'UNICODE' { [System.Text.Encoding]::Unicode }
                            'UTF16' { [System.Text.Encoding]::Unicode }
                            default { New-Object System.Text.UTF8Encoding($false) }
                        }
                        # 按请求编码比较字节（含 BOM），不同编码的"同文"不得判为 no-op。
                        $body = $encoding.GetBytes([string]$wp.Content)
                        $preamble = $encoding.GetPreamble()
                        $newBytes = [byte[]]($preamble + $body)
                        $newHash = [System.Convert]::ToHexString(
                            [System.Security.Cryptography.SHA256]::HashData($newBytes))
                        $existingHash = (Get-FileHash -LiteralPath $targetPath -Algorithm SHA256).Hash
                        if ([string]::Equals($existingHash, $newHash, [System.StringComparison]::OrdinalIgnoreCase)) {
                            Write-Line ([ordered]@{
                                id = $id
                                status = 'success'
                                message = 'idempotent-noop'
                                data = [ordered]@{ Path = $targetPath; Changed = $false; Idempotent = $true; Verified = $true }
                                errors = @()
                            })
                            continue
                        }
                    }
                } catch { }
            }
        }

        $params = @{}
        if ($null -ne $request.params) {
            foreach ($p in $request.params.PSObject.Properties) { $params[$p.Name] = $p.Value }
        }
        $result = Invoke-AgentStep -Action ([string]$request.action) -Params $params `
            -ProjectRoot $ProjectRoot -AgentRoot $AgentRoot -ConfigRoot $ConfigRoot
        $payload = [ordered]@{
            id = $id
            status = [string]$result.status
            message = [string]$result.message
            data = $result.data
            errors = @($result.errors)
        }
        Write-Line $payload
    } catch {
        Write-Line ([ordered]@{ id = $id; status = 'failure'; message = 'agent step error'; errors = @($_.Exception.Message) })
    }
}
