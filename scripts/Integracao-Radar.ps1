param([switch]$Remover)
$ErrorActionPreference = 'Stop'
$temp = $null
function Set-Property($object, $name, $value) {
    if ($object.PSObject.Properties[$name]) { $object.$name = $value }
    else { $object | Add-Member -NotePropertyName $name -NotePropertyValue $value }
}
# EncodedCommand removes shell interpretation of spaces and special characters in paths.
function New-Command($marker, $scriptPath) {
    $path64 = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($scriptPath))
    $wrapper = "# $marker`n& ([Text.Encoding]::Unicode.GetString([Convert]::FromBase64String('$path64')))"
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($wrapper))
    return 'powershell.exe -NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -EncodedCommand ' + $encoded
}
try {
    $configDir = $env:CLAUDE_CONFIG_DIR
    if ([string]::IsNullOrWhiteSpace($configDir)) { $configDir = Join-Path $env:USERPROFILE '.claude' }
    $configDir = [IO.Path]::GetFullPath($configDir)
    $path = Join-Path $configDir 'settings.json'
    $widgetDir = Join-Path $env:LOCALAPPDATA 'MeuClaudeWidget'
    $hookPath = Join-Path $widgetDir 'RadarHook.ps1'
    $statusPath = Join-Path $widgetDir 'StatusLine.ps1'
    $command = New-Command 'MeuClaudeWidgetRadar' $hookPath
    $statusCommand = New-Command 'MeuClaudeWidgetStatus' $statusPath
    $existed = Test-Path -LiteralPath $path
    $original = if ($existed) { [IO.File]::ReadAllText($path) } else { $null }
    $config = if ($existed) { $original | ConvertFrom-Json } else { [pscustomobject]@{} }
    if ($null -eq $config -or $config -isnot [pscustomobject]) { throw 'settings.json precisa ser um objeto JSON valido. Nenhum arquivo foi alterado.' }
    if (-not $config.PSObject.Properties['hooks']) { Set-Property $config 'hooks' ([pscustomobject]@{}) }
    if ($null -eq $config.hooks -or $config.hooks -isnot [pscustomobject]) { throw 'Campo hooks inesperado. Nenhum arquivo foi alterado.' }
    # Remove only this integration's exact command, leaving all other hook entries intact.
    foreach ($property in @($config.hooks.PSObject.Properties)) {
        $groups = @()
        foreach ($group in @($property.Value)) {
            if (-not $group.PSObject.Properties['hooks']) { $groups += $group; continue }
            $kept = @($group.hooks | Where-Object { $_.command -cne $command })
            if ($kept.Count -eq @($group.hooks).Count) { $groups += $group }
            elseif ($kept.Count -gt 0) { Set-Property $group 'hooks' $kept; $groups += $group }
        }
        if ($groups.Count -gt 0) { Set-Property $config.hooks $property.Name $groups }
        else { $config.hooks.PSObject.Properties.Remove($property.Name) }
    }
    # The status line is a single setting: it is only taken when empty or already ours.
    $statusOurs = $config.PSObject.Properties['statusLine'] -and $config.statusLine.command -ceq $statusCommand
    $statusFree = -not $config.PSObject.Properties['statusLine'] -or $null -eq $config.statusLine
    if ($statusOurs) { $config.PSObject.Properties.Remove('statusLine') }
    $statusMessage = ''
    if (-not $Remover) {
        if ($config.disableAllHooks -eq $true) { throw 'Os hooks estao desativados na sua configuracao. Nenhuma configuracao foi alterada.' }
        foreach ($eventName in @('SessionStart','UserPromptSubmit','PreToolUse','PermissionRequest','PostToolUse','PostToolUseFailure','Notification','SubagentStart','SubagentStop','Stop','StopFailure','SessionEnd')) {
            # async: Claude never waits for the radar; each event costs nothing on the turn.
            $handler = [ordered]@{ type='command'; command=$command; async=$true; timeout=10 }
            $group = [ordered]@{ hooks=@($handler) }
            if ($eventName -eq 'Notification') { $group['matcher'] = 'permission_prompt|idle_prompt|elicitation_dialog|elicitation_url_dialog|agent_needs_input' }
            $existing = @()
            if ($config.hooks.PSObject.Properties[$eventName]) { $existing = @($config.hooks.$eventName) }
            Set-Property $config.hooks $eventName @($existing + [pscustomobject]$group)
        }
        if ($statusOurs -or $statusFree) {
            Set-Property $config 'statusLine' ([pscustomobject][ordered]@{ type='command'; command=$statusCommand })
            $statusMessage = 'Barra de status ativada: modelo, contexto e custo aparecem no terminal e no widget.'
        } else { $statusMessage = 'Voce ja tem uma barra de status propria; ela foi mantida. Contexto e custo nao aparecerao no widget.' }
    }
    $json = $config | ConvertTo-Json -Depth 100 -WarningAction Stop
    $null = $json | ConvertFrom-Json
    [IO.Directory]::CreateDirectory($configDir) | Out-Null
    [IO.Directory]::CreateDirectory($widgetDir) | Out-Null
    if ((Test-Path -LiteralPath $path) -ne $existed) { throw 'A configuracao mudou durante a operacao. Tente novamente.' }
    if ($existed -and [IO.File]::ReadAllText($path) -cne $original) { throw 'A configuracao mudou durante a operacao. Tente novamente.' }
    # Replace creates the backup atomically. A real path avoids PowerShell null-to-string conversion.
    $backup = $path + '.meu-widget-' + [DateTime]::Now.ToString('yyyyMMdd-HHmmss-fff') + '.bak'
    if (-not $Remover) {
        [IO.File]::Copy((Join-Path $PSScriptRoot 'RadarHook.ps1'),$hookPath,$true)
        [IO.File]::Copy((Join-Path $PSScriptRoot 'StatusLine.ps1'),$statusPath,$true)
    }
    $temp = $path + '.meu-widget-' + $PID + '.tmp'
    [IO.File]::WriteAllText($temp,$json,(New-Object Text.UTF8Encoding($false)))
    if ($existed) { [IO.File]::Replace($temp,$path,$backup) } else { [IO.File]::Move($temp,$path) }
    if ($Remover) {
        foreach ($script in @($hookPath, $statusPath)) { if (Test-Path -LiteralPath $script) { Remove-Item -LiteralPath $script -Force } }
        Write-Host 'Integracao removida. Os demais hooks foram preservados.'
        if ($statusOurs) { Write-Host 'Barra de status do widget removida.' }
    } else {
        Write-Host 'Radar ativado! Reabra as sessoes do Claude Code e envie uma mensagem.'
        Write-Host $statusMessage
    }
    if ($existed) { Write-Host ('Backup: ' + $backup) }
    Write-Host ('Configuracao: ' + $path)
    Write-Host 'Nenhuma permissao de ferramenta foi aprovada ou alterada.'
} catch {
    if ($temp -and (Test-Path -LiteralPath $temp)) { Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue }
    Write-Host ('Falha: ' + $_.Exception.Message) -ForegroundColor Red
    exit 1
}
