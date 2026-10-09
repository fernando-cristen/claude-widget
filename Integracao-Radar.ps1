param([switch]$Remover)
$ErrorActionPreference = 'Stop'
function Set-Property($object, $name, $value) {
    if ($object.PSObject.Properties[$name]) { $object.$name = $value }
    else { $object | Add-Member -NotePropertyName $name -NotePropertyValue $value }
}
try {
    $configDir = $env:CLAUDE_CONFIG_DIR
    if ([string]::IsNullOrWhiteSpace($configDir)) { $configDir = Join-Path $env:USERPROFILE '.claude' }
    $configDir = [IO.Path]::GetFullPath($configDir)
    $path = Join-Path $configDir 'settings.json'
    $widgetDir = Join-Path $env:LOCALAPPDATA 'MeuClaudeWidget'
    $hookPath = Join-Path $widgetDir 'RadarHook.ps1'
    # EncodedCommand removes shell interpretation of spaces and special characters in paths.
    $path64 = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($hookPath))
    $wrapper = "# MeuClaudeWidgetRadar`n& ([Text.Encoding]::Unicode.GetString([Convert]::FromBase64String('$path64')))"
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($wrapper))
    $command = 'powershell.exe -NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -EncodedCommand ' + $encoded
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
    if (-not $Remover) {
        if ($config.disableAllHooks -eq $true) { throw 'Os hooks estao desativados na sua configuracao. Nenhuma configuracao foi alterada.' }
        foreach ($eventName in @('SessionStart','UserPromptSubmit','PreToolUse','PermissionRequest','PostToolUse','PostToolUseFailure','Notification','Stop','StopFailure','SessionEnd')) {
            $handler = [ordered]@{ type='command'; command=$command; timeout=3 }
            $group = [ordered]@{ hooks=@($handler) }
            if ($eventName -eq 'Notification') { $group['matcher'] = 'permission_prompt|idle_prompt|elicitation_dialog|elicitation_url_dialog|agent_needs_input' }
            $existing = @()
            if ($config.hooks.PSObject.Properties[$eventName]) { $existing = @($config.hooks.$eventName) }
            Set-Property $config.hooks $eventName @($existing + [pscustomobject]$group)
        }
    }
    $json = $config | ConvertTo-Json -Depth 100 -WarningAction Stop
    $null = $json | ConvertFrom-Json
    [IO.Directory]::CreateDirectory($configDir) | Out-Null
    [IO.Directory]::CreateDirectory($widgetDir) | Out-Null
    if ((Test-Path -LiteralPath $path) -ne $existed) { throw 'A configuracao mudou durante a operacao. Tente novamente.' }
    if ($existed -and [IO.File]::ReadAllText($path) -cne $original) { throw 'A configuracao mudou durante a operacao. Tente novamente.' }
    # Replace creates the backup atomically. A real path avoids PowerShell null-to-string conversion.
    $backup = $path + '.meu-widget-' + [DateTime]::Now.ToString('yyyyMMdd-HHmmss-fff') + '.bak'
    if (-not $Remover) { [IO.File]::Copy((Join-Path $PSScriptRoot 'RadarHook.ps1'),$hookPath,$true) }
    $temp = $path + '.meu-widget-' + $PID + '.tmp'
    [IO.File]::WriteAllText($temp,$json,(New-Object Text.UTF8Encoding($false)))
    if ($existed) { [IO.File]::Replace($temp,$path,$backup) } else { [IO.File]::Move($temp,$path) }
    if ($Remover) { Write-Host 'Integracao removida. Os demais hooks foram preservados.' }
    else { Write-Host 'Radar ativado! Reabra as sessoes do Claude Code e envie uma mensagem.' }
    if ($existed) { Write-Host ('Backup: ' + $backup) }
    Write-Host ('Configuracao: ' + $path)
    Write-Host 'Nenhuma permissao de ferramenta foi aprovada ou alterada.'
} catch {
    Write-Host ('Falha: ' + $_.Exception.Message) -ForegroundColor Red
    exit 1
}
