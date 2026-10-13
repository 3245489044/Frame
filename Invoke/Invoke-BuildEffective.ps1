<#
.SYNOPSIS
    【入口】让改动生效（构建 → 生效面上效 → 生效面断言）
.DESCRIPTION
    Frame\Invoke 统一入口 —— 转发到 Projects\BuildFramework\Build-Effective.ps1。
    B′ + AI 行为护栏的**单一生效入口**（`BUILD_EFFECTIVE_SINGLE_ENTRY`，`.clinerules\project.md` §1.10）：
      内部 Frame→Projects 拓扑构建 → dev（就地即载荷）/ locked（产代 gen-<n> + 切 current）
      → 调 Check-EffectiveFace.ps1 断言「生效面指纹 == 构建产物指纹」。
    完成判据 = **生效面已对齐**（结构事实），非「我编译了」。
    「编译成功」≠「生效」；拆分式 build + 手工产代 ⇒ 违反单入口纪律（判 `ANTI-COMPILE-NOT-EFFECTIVE`）。
.PARAMETER Configuration
    构建配置（默认 Debug）。
.PARAMETER Mode
    装载面模式：dev（就地，默认）/ locked（代目录）。
.PARAMETER Assert
    仅复算断言（不构建）；未对齐 → 退出码 1。
.EXAMPLE
    .\Invoke-BuildEffective.ps1
.EXAMPLE
    .\Invoke-BuildEffective.ps1 -Assert
#>
param(
    [string]$Configuration = 'Debug',

    [ValidateSet('dev', 'locked')]
    [string]$Mode = 'dev',

    [switch]$Assert
)

$target = Join-Path $PSScriptRoot '..\..\Projects\BuildFramework\Build-Effective.ps1'

$params = @{}
if ($PSBoundParameters.ContainsKey('Configuration')) { $params['Configuration'] = $Configuration }
if ($PSBoundParameters.ContainsKey('Mode'))          { $params['Mode'] = $Mode }
if ($PSBoundParameters.ContainsKey('Assert'))        { $params['Assert'] = $Assert }

$rc = 0
try {
    & $target @params
    $rc = if ($null -eq $LASTEXITCODE) { 2 } else { $LASTEXITCODE }
} catch {
    $rc = 2
    Write-Error $_.Exception.Message
}
exit $rc
