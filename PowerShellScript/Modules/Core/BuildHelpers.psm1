<#
.SYNOPSIS
    构建辅助函数模块 - 统一管理构建脚本中的通用逻辑

.DESCRIPTION
    提供构建脚本中重复出现的通用函数，消除 Invoke-FullRebuild.ps1、
    Invoke-TypeScriptBuild.ps1、Invoke-CleanAll.ps1 之间的代码重复。

    包含功能:
        - Invoke-Step: 步骤执行器（带计时、错误处理、关键/非关键步骤区分）
        - Test-CommandExists: 命令可用性检查
        - Confirm-Action: 统一确认提示
        - Invoke-VerificationCheck: 输出完整性验证

    用法:
        Import-Module BuildHelpers  # 或通过 Import-Modules.ps1 统一加载
        Invoke-Step -Name "编译" -Critical $true -ScriptBlock { ... }
        Test-CommandExists "dotnet"
        Confirm-Action -ActionDescription @("步骤1", "步骤2")
        Invoke-VerificationCheck -Files $verificationFiles -ProjectRoot $projectRoot
#>

# ============================================
#  步骤执行器
# ============================================

function Invoke-Step {
    <#
    .SYNOPSIS
        执行一个步骤，带错误处理和计时

    .PARAMETER Name
        步骤名称

    .PARAMETER ScriptBlock
        要执行的脚本块

    .PARAMETER Critical
        是否关键步骤（失败时中止整个流程），默认 $true
    #>
    param(
        [Parameter(Mandatory=$true)]
        [string]$Name,

        [Parameter(Mandatory=$true)]
        [scriptblock]$ScriptBlock,

        [bool]$Critical = $true
    )

    Write-Host ""
    Write-SubHeader "▶ $Name"
    $startTime = Get-Date

    try {
        & $ScriptBlock
        if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) {
            throw "步骤失败，退出代码: $LASTEXITCODE"
        }
        $elapsed = (Get-Date) - $startTime
        Write-Success "✓ $Name 完成（$($elapsed.TotalSeconds.ToString('0.0')) 秒）"
        return $true
    }
    catch {
        $elapsed = (Get-Date) - $startTime
        Write-Error "✗ $Name 失败（$($elapsed.TotalSeconds.ToString('0.0')) 秒）"
        Write-Host "  错误: $($_.Exception.Message)" -ForegroundColor Red

        if ($Critical) {
            Write-Host ""
            Write-Host "════════════════════════════════════════════════" -ForegroundColor Red
            Write-Host "  关键步骤失败，流程中止" -ForegroundColor Red
            Write-Host "════════════════════════════════════════════════" -ForegroundColor Red
            exit 1
        }
        return $false
    }
}

# ============================================
#  命令可用性检查
# ============================================

function Test-CommandExists {
    <#
    .SYNOPSIS
        检查命令是否可用

    .PARAMETER Command
        要检查的命令名称

    .OUTPUTS
        [bool] 命令可用返回 $true，否则 $false
    #>
    param(
        [Parameter(Mandatory=$true)]
        [string]$Command
    )

    $exists = $null -ne (Get-Command $Command -ErrorAction SilentlyContinue)
    if (-not $exists) {
        Write-Warning "命令 '$Command' 不可用，请确保已安装"
    }
    return $exists
}

# ============================================
#  统一确认提示
# ============================================

function Confirm-Action {
    <#
    .SYNOPSIS
        显示操作确认提示，支持自定义操作列表

    .PARAMETER ActionDescription
        操作描述字符串数组

    .PARAMETER Title
        标题文本（可选，默认 "即将执行以下操作:"）

    .PARAMETER DefaultYes
        默认是否选择 Y，默认 $true

    .OUTPUTS
        [bool] 确认返回 $true，取消返回 $false
    #>
    param(
        [Parameter(Mandatory=$true)]
        [string[]]$ActionDescription,

        [string]$Title = "即将执行以下操作:",

        [bool]$DefaultYes = $true
    )

    Write-Host $Title -ForegroundColor Yellow
    for ($i = 0; $i -lt $ActionDescription.Count; $i++) {
        Write-Host "  $($i+1). $($ActionDescription[$i])" -ForegroundColor Gray
    }
    Write-Host ""

    $defaultText = if ($DefaultYes) { "Y" } else { "N" }
    $choice = Read-Host "是否继续？(Y/N，默认 $defaultText)"
    if ($choice -ne "" -and $choice -ne "Y" -and $choice -ne "y") {
        Write-Host ""
        Write-Host "已取消操作。"
        return $false
    }
    Write-Host ""
    return $true
}

# ============================================
#  输出完整性验证
# ============================================

function Invoke-VerificationCheck {
    <#
    .SYNOPSIS
        验证输出文件完整性

    .PARAMETER Files
        要验证的文件列表，格式: @{ Path = "相对路径"; Label = "显示名称" }

    .PARAMETER ProjectRoot
        项目根目录（用于拼接绝对路径）

    .OUTPUTS
        [hashtable] @{ AllFound = [bool]; FoundCount = [int]; TotalCount = [int] }
    #>
    param(
        [Parameter(Mandatory=$true)]
        [hashtable[]]$Files,

        [Parameter(Mandatory=$true)]
        [string]$ProjectRoot
    )

    $allFound = $true
    $foundCount = 0

    Write-Host "  检查 $($Files.Count) 个关键输出文件..." -ForegroundColor Yellow
    Write-Host ""

    foreach ($item in $Files) {
        $fullPath = Join-Path $ProjectRoot $item.Path
        if (Test-Path $fullPath) {
            $fileInfo = Get-Item $fullPath
            $sizeKB = [math]::Round($fileInfo.Length / 1KB, 1)
            Write-Host "  ✓ $($item.Label)" -ForegroundColor Green
            Write-Host "    $($item.Path) ($sizeKB KB)" -ForegroundColor DarkGray
            $foundCount++
        } else {
            Write-Host "  ✗ $($item.Label)" -ForegroundColor Red
            Write-Host "    $($item.Path) — 文件不存在" -ForegroundColor DarkRed
            $allFound = $false
        }
    }

    Write-Host ""
    if ($allFound) {
        Write-Success "所有 $foundCount 个关键输出文件均已就绪"
    } else {
        Write-Warning "已找到 $foundCount/$($Files.Count) 个关键输出文件"
        Write-Host "  缺失的文件可能不影响运行，请手动确认" -ForegroundColor Gray
    }

    return @{
        AllFound = $allFound
        FoundCount = $foundCount
        TotalCount = $Files.Count
    }
}

# ============================================
#  构建输出解析
# ============================================

function Format-BuildOutput {
    <#
    .SYNOPSIS
        格式化显示构建输出中的错误和警告

    .PARAMETER BuildOutput
        构建命令的输出行数组

    .PARAMETER ErrorPattern
        错误行匹配模式（正则），默认 'error\s*:'

    .PARAMETER WarningPattern
        警告行匹配模式（正则），默认 'warning\s*:'

    .PARAMETER MaxErrorLines
        最多显示的错误行数，默认 10

    .PARAMETER MaxOutputLines
        构建失败时显示的最大输出行数（前后各半），默认 30
    #>
    param(
        [Parameter(Mandatory=$true)]
        [string[]]$BuildOutput,

        [string]$ErrorPattern = 'error\s*:',

        [string]$WarningPattern = 'warning\s*:',

        [int]$MaxErrorLines = 10,

        [int]$MaxOutputLines = 30
    )

    $errorLines = $BuildOutput | Where-Object { $_ -match $ErrorPattern }
    $warningLines = $BuildOutput | Where-Object { $_ -match $WarningPattern }
    $buildSucceeded = $BuildOutput -match 'Build succeeded' -or $LASTEXITCODE -eq 0

    if ($errorLines.Count -gt 0) {
        Write-Host "  错误 ($($errorLines.Count) 个):" -ForegroundColor Red
        foreach ($err in ($errorLines | Select-Object -First $MaxErrorLines)) {
            Write-Host "    $err" -ForegroundColor Red
        }
        if ($errorLines.Count -gt $MaxErrorLines) {
            Write-Host "    ... 还有 $($errorLines.Count - $MaxErrorLines) 个错误未显示" -ForegroundColor DarkRed
        }
    }

    if ($warningLines.Count -gt 0) {
        Write-Host "  警告 ($($warningLines.Count) 个)" -ForegroundColor Yellow
    }

    if (-not $buildSucceeded) {
        $halfLines = [math]::Floor($MaxOutputLines / 2)
        Write-Host "  完整输出（前 $halfLines 行）:" -ForegroundColor DarkGray
        $BuildOutput | Select-Object -First $halfLines | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }
        Write-Host "  ..." -ForegroundColor DarkGray
        $BuildOutput | Select-Object -Last $halfLines | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }
    }

    return $buildSucceeded
}

# ============================================
#  模块导出
# ============================================

Export-ModuleMember -Function @(
    'Invoke-Step',
    'Test-CommandExists',
    'Confirm-Action',
    'Invoke-VerificationCheck',
    'Format-BuildOutput'
)
