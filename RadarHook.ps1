# MeuClaudeWidgetRadar v2. No decisions and no stdout: Claude permissions stay unchanged.
$ErrorActionPreference = 'Stop'
$mutex = $null
$locked = $false
try {
    $received = [DateTimeOffset]::UtcNow
    $payload = [Console]::In.ReadToEnd() | ConvertFrom-Json
    if (-not $payload.session_id -or $payload.agent_id) { exit 0 }
    $eventName = [string]$payload.hook_event_name
    $state = switch ($eventName) {
        'SessionStart' { 'ready' }
        'UserPromptSubmit' { 'working' }
        'PreToolUse' { if ($payload.tool_name -eq 'AskUserQuestion') { 'waiting' } else { 'working' } }
        'PermissionRequest' { 'waiting' }
        'PostToolUse' { 'working' }
        'PostToolUseFailure' { 'working' }
        'Stop' { if (@($payload.background_tasks).Count -gt 0 -and $null -ne $payload.background_tasks) { 'background' } else { 'done' } }
        'StopFailure' { 'error' }
        'SessionEnd' { 'ended' }
        'Notification' {
            if ($payload.notification_type -in @('permission_prompt','elicitation_dialog','elicitation_url_dialog','agent_needs_input')) { 'waiting' }
            elseif ($payload.notification_type -eq 'idle_prompt') { 'idle' }
        }
    }
    if (-not $state) { exit 0 }
    $hash = [Security.Cryptography.SHA256]::Create()
    try { $id = ([BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes([string]$payload.session_id)))).Replace('-','').ToLowerInvariant() } finally { $hash.Dispose() }
    $folder = Join-Path $env:LOCALAPPDATA 'MeuClaudeWidget\radar'
    [IO.Directory]::CreateDirectory($folder) | Out-Null
    $path = Join-Path $folder ($id + '.json')
    $mutex = New-Object Threading.Mutex($false, ('Local\MeuClaudeRadar-' + $id))
    try { $locked = $mutex.WaitOne(800) } catch [Threading.AbandonedMutexException] { $locked = $true }
    if (-not $locked) { exit 0 }
    if (Test-Path -LiteralPath $path) {
        try {
            $previous = [IO.File]::ReadAllText($path) | ConvertFrom-Json
            if ([DateTimeOffset]::Parse($previous.Updated) -gt $received) { exit 0 }
        } catch { }
    }
    $record = [ordered]@{
        Id = $id
        Cwd = [string]$payload.cwd
        State = $state
        Event = $eventName
        Tool = if ($eventName -in @('PreToolUse','PermissionRequest','PostToolUse','PostToolUseFailure')) { [string]$payload.tool_name } else { '' }
        Updated = $received.ToString('o')
    }
    $temporary = $path + '.' + $PID + '.tmp'
    [IO.File]::WriteAllText($temporary, ($record | ConvertTo-Json -Compress), (New-Object Text.UTF8Encoding($false)))
    # NullString passes a true .NET null; PowerShell $null can become an invalid empty path.
    if (Test-Path -LiteralPath $path) { [IO.File]::Replace($temporary,$path,[System.Management.Automation.Language.NullString]::Value) } else { [IO.File]::Move($temporary,$path) }
} catch {
    # Observability must never stop or approve a Claude operation.
} finally {
    if ($locked -and $mutex) { $mutex.ReleaseMutex() }
    if ($mutex) { $mutex.Dispose() }
}
exit 0
