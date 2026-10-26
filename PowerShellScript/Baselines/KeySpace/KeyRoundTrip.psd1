@{
    # KB1 往返回归基线（单源；守卫 = Operation\DSL\KeySpace\Check-KeyRoundTrip.ps1）
    Schema = 'gate-baseline-v1'
    Gate   = 'KeyRoundTrip'

    MinimumEntries = @{ Value = 15; Origin = 'Derived'; DueWhen = '' }
    RequiredFamilies = @{ Value = @(
        'domain.message-stream'
        'domain.segment-stream'
        'domain.session-view'
        'domain.observability'
        'domain.archive'
        'domain.process'
        'domain.conversation'
    ); Origin = 'Derived'; DueWhen = '' }
}
