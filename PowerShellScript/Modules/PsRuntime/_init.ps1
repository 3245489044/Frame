# _init.ps1 — PsRuntime 模块加载器
# .psm1 用 Import-Module（避免 dot-sourcing 在 PS 5.1+Pester 5 的 scoping 问题）
# .ps1 用 dot-source（函数进入调用方作用域）
$script:PsRuntimeRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

$modules = @(
    "Runtime\OutputSchema.psm1",
    "Contract\ContractSchema.psm1",
    "Contract\ContractParser.psm1",
    "Contract\ContractValidator.psm1",
    "Capability\CapabilityResolver.psm1",
    "Gate\CanHandler.psm1",
    "Extension\ContractScaffold.psm1",
    "Extension\ContractRegistrar.psm1",
    "Extension\UsageTracker.psm1"
)
$scripts = @(
    "Runtime\Invoke-PsContract.ps1",
    "Extension\Invoke-PsExtension.ps1",
    "Runtime\Invoke-PsAction.ps1",
    "Bootstrap\Register-PsContracts.ps1"
)

foreach ($m in $modules) {
    $p = Join-Path $script:PsRuntimeRoot $m
    if (Test-Path $p) { Import-Module $p -Force -ErrorAction Stop }
}
foreach ($s in $scripts) {
    $p = Join-Path $script:PsRuntimeRoot $s
    if (Test-Path $p) { . $p }
}
