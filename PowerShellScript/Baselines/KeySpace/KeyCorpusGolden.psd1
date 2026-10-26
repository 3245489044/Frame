@{
    # KB1 语料门禁基线（单源；守卫 = Operation\DSL\KeySpace\Check-KeyCorpusGolden.ps1）
    Schema = 'gate-baseline-v1'
    Gate   = 'KeyCorpusGolden'

    MinimumEntries = @{ Value = 20; Origin = 'Derived'; DueWhen = '' }
    RequiredFamilies = @{ Value = @(
        'domain.message-stream'
        'domain.segment-stream'
        'domain.session-view'
        'domain.observability'
        'domain.archive'
        'domain.process'
        'domain.conversation'
    ); Origin = 'Derived'; DueWhen = '' }
    RequiredDisplayFamilies = @{ Value = @(
        'domain.message-stream'
        'domain.segment-stream'
        'domain.session-view'
        'domain.observability'
        'domain.process'
        'domain.conversation'
    ); Origin = 'Derived'; DueWhen = '' }
    BoundarySamples = @{ Value = @(
        'smsg.task.a'
        'obs.task.a.b'
        'cline.session.c1.round.2.message.7'
        'cline.session.c1.round.2'
        'proc.c1'
        'proc.c1.r2.step9'
        'smsg.c1.'
        'seg.c1.'
        'sv.c1.'
        'session.c1.r2.'
    ); Origin = 'Derived'; DueWhen = '' }
}
