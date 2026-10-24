@{
    # ===== 图库路径单源基线（纯数据单源；守卫 = Operation\DSL\Data\Check-GraphPathSingleSource.ps1）=====
    Schema = 'gate-baseline-v1'
    Gate   = 'GraphPathSingleSource'
    Source = 'Frame\PowerShellScript\Operation\DSL\Data\Check-GraphPathSingleSource.ps1'

    # GP1 图库路径解析函数定义点（唯一 = DatabaseBootstrapper.ResolveGraphLogPath）。
    GraphPathResolveDefinition = @{
        Value   = 1
        Origin  = 'Derived'
        DueWhen = '解析迁入 Frame 组合根后改判据指向（仍必须为 1）'
    }

    # GP2 图库文件名权威常量定义点（唯一 = GraphLogFileName）。
    GraphFileNameDefinition = @{
        Value   = 1
        Origin  = 'Derived'
        DueWhen = 'RepositoryPaths 单源化后改判据指向（仍必须为 1）'
    }

    # GP3 启动指纹发射点（唯一 = StorageBundleFingerprint）。
    BundleFingerprintEmission = @{
        Value   = 1
        Origin  = 'Derived'
        DueWhen = '保持 1；新增第二指纹即判红（禁双口径）'
    }

    # GP4 两库同目录结构断言（唯一 = StorageCoreCompositionRoot）。
    SameDirectoryAssertion = @{
        Value   = 1
        Origin  = 'Derived'
        DueWhen = '保持 ≥1（值 = 断言点计数）'
    }
}
