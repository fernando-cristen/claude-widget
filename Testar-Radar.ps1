# Integration tests run ONLY inside a disposable directory; never use your Claude config.
$ErrorActionPreference='Stop'
$root=Join-Path ([IO.Path]::GetTempPath()) ('Meu Claude Radar Test-'+[Guid]::NewGuid().ToString('N'))
$oldConfig=$env:CLAUDE_CONFIG_DIR
$oldLocal=$env:LOCALAPPDATA
try {
    [IO.Directory]::CreateDirectory($root) | Out-Null
    $env:CLAUDE_CONFIG_DIR=Join-Path $root 'claude'
    $env:LOCALAPPDATA=Join-Path $root 'local'
    [IO.Directory]::CreateDirectory($env:CLAUDE_CONFIG_DIR) | Out-Null
    $settings=Join-Path $env:CLAUDE_CONFIG_DIR 'settings.json'
    '{"model":"keep-model","permissions":{"allow":["Read"]},"hooks":{"Stop":[{"hooks":[{"type":"command","command":"echo keep-existing"}]}]}}' | Set-Content -LiteralPath $settings -Encoding UTF8
    $shell=Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $installer=Join-Path $PSScriptRoot 'Integracao-Radar.ps1'
    1..2 | ForEach-Object {
        & $shell -NoProfile -ExecutionPolicy Bypass -File $installer
        if ($LASTEXITCODE -ne 0) { throw 'Falha ao ativar no ambiente isolado.' }
    }
    $backups=@(Get-ChildItem -LiteralPath $env:CLAUDE_CONFIG_DIR -Filter 'settings.json.meu-widget-*.bak')
    if ($backups.Count -lt 2) { throw 'Backups atomicos nao foram criados.' }
    foreach ($copy in $backups) {
        $saved=Get-Content -LiteralPath $copy.FullName -Raw | ConvertFrom-Json
        if ($saved.model -ne 'keep-model') { throw 'Backup nao preservou a configuracao original.' }
    }
    $c=Get-Content -LiteralPath $settings -Raw | ConvertFrom-Json
    if ($c.model -ne 'keep-model' -or $c.permissions.allow[0] -ne 'Read') { throw 'Configuracao existente nao preservada.' }
    if (@($c.hooks.Stop).Count -ne 2 -or @($c.hooks.PreToolUse).Count -ne 1) { throw 'Duplicacao de hooks.' }
    $hook=Join-Path $PSScriptRoot 'RadarHook.ps1'
    $events=@(
        @('SessionStart','ready'), @('UserPromptSubmit','working'),
        @('PermissionRequest','waiting'), @('PostToolUse','working'),
        @('Stop','done'), @('StopFailure','error'), @('SessionEnd','ended')
    )
    foreach($pair in $events) {
        @{session_id='test-session';cwd=$root;hook_event_name=$pair[0]} | ConvertTo-Json -Compress | & $shell -NoProfile -ExecutionPolicy Bypass -File $hook
        if ($LASTEXITCODE -ne 0) { throw 'Hook retornou erro.' }
        $files=@(Get-ChildItem -LiteralPath (Join-Path $env:LOCALAPPDATA 'MeuClaudeWidget\radar') -Filter '*.json')
        if ($files.Count -ne 1) { throw 'Arquivo de sessao ausente ou duplicado.' }
        $record=Get-Content -LiteralPath $files[0].FullName -Raw | ConvertFrom-Json
        if ($record.State -ne $pair[1]) { throw ('Estado incorreto para '+$pair[0]) }
    }
    @{session_id='test-session';agent_id='subagent';cwd=$root;hook_event_name='UserPromptSubmit'} | ConvertTo-Json -Compress | & $shell -NoProfile -ExecutionPolicy Bypass -File $hook
    $record=Get-Content -LiteralPath $files[0].FullName -Raw | ConvertFrom-Json
    if ($record.State -ne 'ended') { throw 'Subagente alterou o estado da sessao principal.' }
    & $shell -NoProfile -ExecutionPolicy Bypass -File $installer -Remover
    if ($LASTEXITCODE -ne 0) { throw 'Falha na remocao.' }
    $c=Get-Content -LiteralPath $settings -Raw | ConvertFrom-Json
    if (@($c.hooks.Stop).Count -ne 1 -or $c.hooks.Stop[0].hooks[0].command -ne 'echo keep-existing') { throw 'Remocao afetou outro hook.' }
    Write-Host 'OK: instalacao idempotente, configuracao preservada, estados, isolamento de subagentes e remocao.' -ForegroundColor Green
} catch {
    Write-Host ('FALHOU: '+$_.Exception.Message) -ForegroundColor Red
    exit 1
} finally {
    $env:CLAUDE_CONFIG_DIR=$oldConfig
    $env:LOCALAPPDATA=$oldLocal
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
