@{
    # ===== BuildFrameworkModules 门禁基线（单源；守卫 = Projects\BuildFramework\Gates\Check-BuildFrameworkModules.ps1）=====
    Schema = 'gate-baseline-v1'
    Gate   = 'BuildFrameworkModules'

    # 模块 → 归属 glob（仓库相对；'**' 跨段）。每个文件恰属一个模块。
    Modules = @{
        'M0 Foundation'   = @('Foundation/**')
        'M1 Entry'        = @('Entry/**')
        'M2 Graph'        = @('Graph/**')
        'M3 Safety'       = @('Safety/**')
        'M4 Mapping'      = @('Mapping/**')
        'M5 Distribution' = @('Distribution/**')
        'M6 Bake'         = @('Bake/**')
        'M7 DataRoot'     = @('DataRoot/**')
        'X1 Gates'        = @('Gates/**')
        'X3 Docs'         = @('README.md')
    }

    # 体量阈值（行）。
    EntryMaxLines    = 400
    ModuleMaxLines   = 1200

    # 例外（空 = 无）。
    Exceptions = @()
}
