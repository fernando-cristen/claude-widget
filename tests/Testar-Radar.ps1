# Integration tests run ONLY inside a disposable directory; never use your Claude config.
$ErrorActionPreference='Stop'
# O instalador do radar agora vive dentro do .exe: compila antes de isolar o ambiente.
$exe=Join-Path $PSScriptRoot '..\bin\Release\net8.0-windows\win-x64\MeuClaude.exe'
& dotnet build (Join-Path $PSScriptRoot '..\MeuClaude.csproj') -c Release -nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar o widget.' }
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
    1..2 | ForEach-Object {
        & $exe --ativar-radar | Write-Output
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
    if ($c.hooks.PreToolUse[0].hooks[0].async -ne $true -or @($c.hooks.SubagentStart).Count -ne 1) { throw 'Hooks assincronos ou de subagentes ausentes.' }
    if (-not $c.statusLine.command) { throw 'Barra de status nao instalada.' }
    $hook=Join-Path $env:LOCALAPPDATA 'MeuClaudeWidget\RadarHook.ps1' # gravado pelo .exe ao ativar o radar
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
    if (-not $record.TurnStarted -or -not $record.TurnEnded) { throw 'Tempo do turno nao registrado.' }
    @{session_id='test-session';agent_id='subagent';cwd=$root;hook_event_name='UserPromptSubmit'} | ConvertTo-Json -Compress | & $shell -NoProfile -ExecutionPolicy Bypass -File $hook
    $record=Get-Content -LiteralPath $files[0].FullName -Raw | ConvertFrom-Json
    if ($record.State -ne 'ended') { throw 'Subagente alterou o estado da sessao principal.' }
    # Two subagents start, one stops, and a reordered stop/start pair must not leave a ghost.
    foreach ($step in @(@('SubagentStart','a1'), @('SubagentStart','a2'), @('SubagentStop','a1'), @('SubagentStop','a3'), @('SubagentStart','a3'))) {
        @{session_id='test-session';agent_id=$step[1];cwd=$root;hook_event_name=$step[0]} | ConvertTo-Json -Compress | & $shell -NoProfile -ExecutionPolicy Bypass -File $hook
    }
    $record=Get-Content -LiteralPath $files[0].FullName -Raw | ConvertFrom-Json
    if ($record.Subagents -ne 1 -or $record.State -ne 'ended') { throw ('Contagem de subagentes incorreta: ' + $record.Subagents) }
    $status=Join-Path $env:LOCALAPPDATA 'MeuClaudeWidget\StatusLine.ps1'
    $limits=@{five_hour=@{used_percentage=19;resets_at=1792704000};seven_day=@{used_percentage=77;resets_at='2026-10-29T13:00:00Z'}}
    $line=@{session_id='test-session';model=@{display_name='Opus'};context_window=@{used_percentage=41.6};cost=@{total_cost_usd=1.5};rate_limits=$limits} | ConvertTo-Json -Compress -Depth 4 | & $shell -NoProfile -ExecutionPolicy Bypass -File $status
    if ($line -ne 'Opus | contexto 42% | US$ 1,50') { throw ('Barra de status inesperada: ' + $line) }
    $shared=Get-Content -LiteralPath (Join-Path $env:LOCALAPPDATA ('MeuClaudeWidget\radar\status\' + $files[0].Name)) -Raw | ConvertFrom-Json
    if ($shared.Model -ne 'Opus' -or $shared.Context -ne 41.6 -or $shared.Cost -ne 1.5) { throw 'Dados da barra de status nao compartilhados.' }
    if ($shared.Five -ne 19 -or [DateTimeOffset]::Parse($shared.FiveReset).ToUnixTimeSeconds() -ne 1792704000 -or $shared.Week -ne 77 -or [DateTimeOffset]::Parse($shared.WeekReset).Hour -ne 13) { throw 'Limites de uso da barra de status incorretos.' }
    & $exe --remover-radar | Write-Output
    if ($LASTEXITCODE -ne 0) { throw 'Falha na remocao.' }
    $c=Get-Content -LiteralPath $settings -Raw | ConvertFrom-Json
    if (@($c.hooks.Stop).Count -ne 1 -or $c.hooks.Stop[0].hooks[0].command -ne 'echo keep-existing') { throw 'Remocao afetou outro hook.' }
    if ($c.PSObject.Properties['statusLine']) { throw 'Barra de status do widget nao foi removida.' }
    if (Test-Path -LiteralPath (Join-Path $env:LOCALAPPDATA 'MeuClaudeWidget\RadarHook.ps1')) { throw 'Script do hook ficou para tras.' }
    # A status line that belongs to the user is never replaced or removed.
    '{"statusLine":{"type":"command","command":"echo minha"}}' | Set-Content -LiteralPath $settings -Encoding UTF8
    & $exe --ativar-radar | Write-Output
    $c=Get-Content -LiteralPath $settings -Raw | ConvertFrom-Json
    if ($c.statusLine.command -ne 'echo minha') { throw 'Barra de status do usuario foi substituida.' }
    & $exe --remover-radar | Write-Output
    $c=Get-Content -LiteralPath $settings -Raw | ConvertFrom-Json
    if ($c.statusLine.command -ne 'echo minha') { throw 'Barra de status do usuario foi removida.' }
    Write-Host 'OK: instalacao idempotente, hooks assincronos, configuracao preservada, estados, tempo do turno, subagentes, barra de status e remocao.' -ForegroundColor Green
} catch {
    Write-Host ('FALHOU: '+$_.Exception.Message) -ForegroundColor Red
    exit 1
} finally {
    $env:CLAUDE_CONFIG_DIR=$oldConfig
    $env:LOCALAPPDATA=$oldLocal
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
