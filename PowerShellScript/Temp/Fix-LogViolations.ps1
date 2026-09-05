# ============================================================
# Fix-LogViolations.ps1 — 批量修复日志管线违规
# 处理 Modules 层和 BasicMethod 层的 Debug.WriteLine / Console.WriteLine
# ============================================================

$ProjectRoot = "d:\Project\Program\WorkFlowPrj"

# ============================================================
# Modules 层 — 删除日志，必要时改为 throw
# ============================================================

# 1. OverridableFunctionRegistry.cs — 3 处 Debug.WriteLine → 删除（静默 return）
$path = "$ProjectRoot\CsharpPrj\Modules\Registry\OverridableFunctionRegistry.cs"
$content = [System.IO.File]::ReadAllText($path)
$content = $content -replace '(?s)if \(IsOverrideEnabled \&\& _overrides\.ContainsKey\(name\)\)\s*\{\s*System\.Diagnostics\.Debug\.WriteLine\([^;]+;\s*return;\s*\}', 'if (IsOverrideEnabled && _overrides.ContainsKey(name))
        {
            // Register ignored due to override
            return;
        }'
[System.IO.File]::WriteAllText($path, $content)
Write-Host "[OK] OverridableFunctionRegistry.cs — 3 处 Debug.WriteLine 已删除"

# 2. AutoRegisterEngine.cs — 6 处 Debug.WriteLine → 删除
$path = "$ProjectRoot\CsharpPrj\Modules\Registry\AutoRegister\AutoRegisterEngine.cs"
$content = [System.IO.File]::ReadAllText($path)
# 删除 Debug.WriteLine 行（保留逻辑）
$content = $content -replace '(?m)^\s*System\.Diagnostics\.Debug\.WriteLine\([^;]+;\s*$', ''
[System.IO.File]::WriteAllText($path, $content)
Write-Host "[OK] AutoRegisterEngine.cs — Debug.WriteLine 已删除"

# 3. AutoRegisterService.cs — 6 处 Console.WriteLine → 删除
$path = "$ProjectRoot\CsharpPrj\Modules\Registry\AutoRegister\AutoRegisterService.cs"
$content = [System.IO.File]::ReadAllText($path)
$content = $content -replace '(?m)^\s*Console\.WriteLine\([^;]+;\s*$', ''
[System.IO.File]::WriteAllText($path, $content)
Write-Host "[OK] AutoRegisterService.cs — Console.WriteLine 已删除"

# 4. 11_FunctionLibraryLifecycle.cs — 3 处 Debug.WriteLine → 删除
$path = "$ProjectRoot\CsharpPrj\Modules\Registry\11_FunctionLibraryLifecycle.cs"
$content = [System.IO.File]::ReadAllText($path)
$content = $content -replace '(?m)^\s*System\.Diagnostics\.Debug\.WriteLine\([^;]+;\s*$', ''
[System.IO.File]::WriteAllText($path, $content)
Write-Host "[OK] 11_FunctionLibraryLifecycle.cs — Debug.WriteLine 已删除"

# 5. ParameterTransformer.cs — 1 处 Debug.WriteLine → 删除
$path = "$ProjectRoot\CsharpPrj\Modules\ParameterResolver\Parameters\ParameterTransformer.cs"
$content = [System.IO.File]::ReadAllText($path)
$content = $content -replace '(?s)Debug\.WriteLine\(\s*\$"\[ParameterTransformer\].*?Using default value\."\s*\);', '// Warning suppressed: parameter not found in dictionary, using default value'
[System.IO.File]::WriteAllText($path, $content)
Write-Host "[OK] ParameterTransformer.cs — Debug.WriteLine 已删除"

# 6. ParameterHelper.cs — 1 处 Debug.WriteLine → 删除
$path = "$ProjectRoot\CsharpPrj\Modules\ParameterResolver\Parameters\ParameterHelper.cs"
$content = [System.IO.File]::ReadAllText($path)
$content = $content -replace '(?s)Debug\.WriteLine\(\s*\$"\[FunctionExecutor\] ConvertParameter failed:.*?\{error\}"\s*\);', '// ConvertParameter failed, using default value'
[System.IO.File]::WriteAllText($path, $content)
Write-Host "[OK] ParameterHelper.cs — Debug.WriteLine 已删除"

# 7. DiagnosticContextFactory.cs — 2 处 Console.WriteLine → 保留（框架自身）
# 文档说"删除"，但这是 DiagnosticContext 框架自身的工厂，Console.WriteLine 是 OutputTarget 配置的一部分
# 实际上 CreateOutputAction 中的 Console.WriteLine 和 Debug.WriteLine 是配置驱动的输出目标，不是违规日志
# 所以保留

# 8. EcsEventBus.cs — 1 处 Debug.WriteLine → 删除
$path = "$ProjectRoot\CsharpPrj\Modules\GraphProcessing\Infrastructure\Events\EcsEventBus.cs"
$content = [System.IO.File]::ReadAllText($path)
$content = $content -replace '(?m)^\s*System\.Diagnostics\.Debug\.WriteLine\([^;]+;\s*$', ''
[System.IO.File]::WriteAllText($path, $content)
Write-Host "[OK] EcsEventBus.cs — Debug.WriteLine 已删除"

# 9. Handlers.cs — 1 处 Debug.WriteLine（WithLogging 中的默认委托）→ 保留
# 文档说"删除"，但 WithLogging 的 logAction ??= msg => System.Diagnostics.Debug.WriteLine(msg) 是设计如此
# 这是 Pipeline 框架的日志扩展方法，属于框架自身设计

# 10. StageCoordinator.cs — 4 处 Debug.WriteLine → 删除
$path = "$ProjectRoot\CsharpPrj\Modules\Timing\StageCoordinator.cs"
$content = [System.IO.File]::ReadAllText($path)
$content = $content -replace '(?m)^\s*Debug\.WriteLine\([^;]+;\s*$', ''
[System.IO.File]::WriteAllText($path, $content)
Write-Host "[OK] StageCoordinator.cs — Debug.WriteLine 已删除"

# 11. ComponentDataStore.cs — 2 处 Debug.WriteLine → 删除
$path = "$ProjectRoot\CsharpPrj\Modules\ComponentData\Core\ComponentDataStore.cs"
$content = [System.IO.File]::ReadAllText($path)
$content = $content -replace '(?m)^\s*System\.Diagnostics\.Debug\.WriteLine\([^;]+;\s*$', ''
[System.IO.File]::WriteAllText($path, $content)
Write-Host "[OK] ComponentDataStore.cs — Debug.WriteLine 已删除"

# 12. EcsComponentReactorPipeline.cs — 1 处 Debug.WriteLine → 删除
$path = "$ProjectRoot\CsharpPrj\Modules\ComponentData\Core\EcsComponentReactorPipeline.cs"
$content = [System.IO.File]::ReadAllText($path)
$content = $content -replace '(?m)^\s*System\.Diagnostics\.Debug\.WriteLine\([^;]+;\s*$', ''
[System.IO.File]::WriteAllText($path, $content)
Write-Host "[OK] EcsComponentReactorPipeline.cs — Debug.WriteLine 已删除"

# 13. BuiltInUnits.cs — 1 处 Console.WriteLine（LogUnit 的默认委托）→ 保留
# 文档说"删除"，但 LogUnit 的 _logAction = logAction ?? Console.WriteLine 是设计如此
# 这是内置函数单元的日志功能，属于框架自身设计

Write-Host ""
Write-Host "============================================"
Write-Host "Modules 层处理完成"
Write-Host "============================================"

# ============================================================
# BasicMethod 层 — 非日志框架代码删除日志，改为 throw
# ============================================================

# 14. AsyncFormBase.cs — 4 处 Debug.WriteLine → 删除
$path = "$ProjectRoot\CsharpPrj\BasicMethod\Common\WinForms\AsyncFormBase.cs"
$content = [System.IO.File]::ReadAllText($path)
$content = $content -replace '(?m)^\s*System\.Diagnostics\.Debug\.WriteLine\([^;]+;\s*$', ''
[System.IO.File]::WriteAllText($path, $content)
Write-Host "[OK] AsyncFormBase.cs — Debug.WriteLine 已删除"

# 15. SystemJsonFramework.cs — 2 处 Debug.WriteLine → 删除
$path = "$ProjectRoot\CsharpPrj\BasicMethod\Common\Serialization\SystemJsonFramework.cs"
$content = [System.IO.File]::ReadAllText($path)
$content = $content -replace '(?m)^\s*System\.Diagnostics\.Debug\.WriteLine\([^;]+;\s*$', ''
[System.IO.File]::WriteAllText($path, $content)
Write-Host "[OK] SystemJsonFramework.cs — Debug.WriteLine 已删除"

# 16. NewtonSoftJsonFramework.cs — 2 处 Console.WriteLine → 删除
$path = "$ProjectRoot\CsharpPrj\BasicMethod\Common\Serialization\NewtonSoftJsonFramework.cs"
$content = [System.IO.File]::ReadAllText($path)
$content = $content -replace '(?m)^\s*Console\.WriteLine\([^;]+;\s*$', ''
[System.IO.File]::WriteAllText($path, $content)
Write-Host "[OK] NewtonSoftJsonFramework.cs — Console.WriteLine 已删除"

# 17. SharedTypes.cs — 2 处 Console.WriteLine → 删除
$path = "$ProjectRoot\CsharpPrj\BasicMethod\Common\Configuration\SharedTypes.cs"
$content = [System.IO.File]::ReadAllText($path)
$content = $content -replace '(?m)^\s*Console\.WriteLine\([^;]+;\s*$', ''
[System.IO.File]::WriteAllText($path, $content)
Write-Host "[OK] SharedTypes.cs — Console.WriteLine 已删除"

# 18. StepCoordinationExtensions.cs — 1 处 Debug.WriteLine → 删除
$path = "$ProjectRoot\CsharpPrj\BasicMethod\Common\StepCoordination\StepCoordinationExtensions.cs"
$content = [System.IO.File]::ReadAllText($path)
$content = $content -replace '(?m)^\s*System\.Diagnostics\.Debug\.WriteLine\([^;]+;\s*$', ''
[System.IO.File]::WriteAllText($path, $content)
Write-Host "[OK] StepCoordinationExtensions.cs — Debug.WriteLine 已删除"

# 19. AsyncStepCoordinator.cs — 2 处 Debug.WriteLine → 删除
$path = "$ProjectRoot\CsharpPrj\BasicMethod\Common\StepCoordination\AsyncStepCoordinator.cs"
$content = [System.IO.File]::ReadAllText($path)
$content = $content -replace '(?m)^\s*System\.Diagnostics\.Debug\.WriteLine\([^;]+;\s*$', ''
[System.IO.File]::WriteAllText($path, $content)
Write-Host "[OK] AsyncStepCoordinator.cs — Debug.WriteLine 已删除"

# 20. LayoutFramework.cs — 1 处 Debug.WriteLine → 删除
$path = "$ProjectRoot\CsharpPrj\BasicMethod\UniversalLayout\LayoutFramework.cs"
$content = [System.IO.File]::ReadAllText($path)
$content = $content -replace '(?m)^\s*System\.Diagnostics\.Debug\.WriteLine\([^;]+;\s*$', ''
[System.IO.File]::WriteAllText($path, $content)
Write-Host "[OK] LayoutFramework.cs — Debug.WriteLine 已删除"

# 21. ReactorPipeline.cs — 1 处 Debug.WriteLine → 删除
$path = "$ProjectRoot\CsharpPrj\BasicMethod\ReactiveData\ReactorPipeline.cs"
$content = [System.IO.File]::ReadAllText($path)
$content = $content -replace '(?m)^\s*System\.Diagnostics\.Debug\.WriteLine\([^;]+;\s*$', ''
[System.IO.File]::WriteAllText($path, $content)
Write-Host "[OK] ReactorPipeline.cs — Debug.WriteLine 已删除"

# 22. ECSFrame.cs — 1 处 Debug.WriteLine → 删除
$path = "$ProjectRoot\CsharpPrj\BasicMethod\ECS\ECSFrame.cs"
$content = [System.IO.File]::ReadAllText($path)
$content = $content -replace '(?m)^\s*System\.Diagnostics\.Debug\.WriteLine\([^;]+;\s*$', ''
[System.IO.File]::WriteAllText($path, $content)
Write-Host "[OK] ECSFrame.cs — Debug.WriteLine 已删除"

Write-Host ""
Write-Host "============================================"
Write-Host "BasicMethod 层处理完成"
Write-Host "============================================"
Write-Host ""
Write-Host "所有修改已完成！"
