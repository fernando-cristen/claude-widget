# MeuClaudeWidgetRadar v3. Runs as an async hook: no decisions and no stdout, Claude permissions stay unchanged.
$ErrorActionPreference = 'Stop'
$mutex = $null
$locked = $false
# Claude Code does not pass its PID; the ancestry finds it (for liveness) and the terminal window that hosts it.
function Find-Owner {
    $parents = @{}; $names = @{}
    foreach ($p in Get-CimInstance Win32_Process -Property ProcessId,ParentProcessId,Name) { $parents[[int]$p.ProcessId] = [int]$p.ParentProcessId; $names[[int]$p.ProcessId] = [string]$p.Name }
    $owner = @{ Claude = 0; Started = ''; Window = 0 }
    $current = $PID
    for ($i = 0; $i -lt 16 -and $parents.ContainsKey($current); $i++) {
        $current = $parents[$current]
        if (-not $names.ContainsKey($current) -or $names[$current] -eq 'explorer.exe') { break }
        if (-not $owner.Claude) {
            if ($names[$current] -match '^(claude|node)\.exe$') { $owner.Claude = $current; $owner.Started = (Get-Process -Id $current).StartTime.ToUniversalTime().ToString('o') }
        } else {
            try { if ((Get-Process -Id $current).MainWindowHandle -ne [IntPtr]::Zero) { $owner.Window = $current; break } } catch { }
        }
    }
    return $owner
}
try {
    $received = [DateTimeOffset]::UtcNow
    $payload = [Console]::In.ReadToEnd() | ConvertFrom-Json
    if (-not $payload.session_id) { exit 0 }
    $eventName = [string]$payload.hook_event_name
    $subagentEvent = $eventName -in @('SubagentStart','SubagentStop')
    # Tool events inside a subagent carry agent_id; subagents only add to the main session's counter.
    if ($payload.agent_id -and -not $subagentEvent) { exit 0 }
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
    if (-not $state -and -not $subagentEvent) { exit 0 }
    $hash = [Security.Cryptography.SHA256]::Create()
    try { $id = ([BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes([string]$payload.session_id)))).Replace('-','').ToLowerInvariant() } finally { $hash.Dispose() }
    $folder = Join-Path $env:LOCALAPPDATA 'MeuClaudeWidget\radar'
    [IO.Directory]::CreateDirectory($folder) | Out-Null
    $path = Join-Path $folder ($id + '.json')
    $owner = $null
    $known = $false
    try { $known = (Test-Path -LiteralPath $path) -and [IO.File]::ReadAllText($path) -match '"ClaudePid":[1-9]' } catch { }
    if ($eventName -eq 'SessionStart' -or -not $known) { try { $owner = Find-Owner } catch { } }
    $mutex = New-Object Threading.Mutex($false, ('Local\MeuClaudeRadar-' + $id))
    try { $locked = $mutex.WaitOne(2000) } catch [Threading.AbandonedMutexException] { $locked = $true }
    if (-not $locked) { exit 0 }
    $previous = $null
    if (Test-Path -LiteralPath $path) { try { $previous = [IO.File]::ReadAllText($path) | ConvertFrom-Json } catch { } }
    $record = [ordered]@{ Id = $id; Cwd = [string]$payload.cwd; State = 'ready'; Event = $eventName; Tool = ''; Updated = $received.ToString('o'); TurnStarted = ''; TurnEnded = ''; Subagents = 0; Agents = @(); ClaudePid = 0; ClaudeStarted = ''; WindowPid = 0 }
    if ($previous) { foreach ($name in @($record.Keys)) { if ($null -ne $previous.$name -and $name -ne 'Id') { $record[$name] = $previous.$name } } }
    if ($owner -and $owner.Claude) { $record.ClaudePid = $owner.Claude; $record.ClaudeStarted = $owner.Started; $record.WindowPid = $owner.Window }
    # Async hooks may finish out of order: an older state event never overwrites a newer one.
    $newer = -not $previous -or [DateTimeOffset]::Parse([string]$previous.Updated) -le $received
    # Subagents are tracked by id (start/stop) so a late or reordered event cannot leave a ghost counter.
    $agents = @{}
    foreach ($a in @($record.Agents)) { if ($a.Id -and $received - [DateTimeOffset]::Parse([string]$a.Time) -lt [TimeSpan]::FromHours(2)) { $agents[[string]$a.Id] = [ordered]@{ Id = [string]$a.Id; Stopped = [bool]$a.Stopped; Time = [string]$a.Time } } }
    if ($subagentEvent -and $payload.agent_id) {
        $key = [string]$payload.agent_id
        if ($eventName -eq 'SubagentStop') { $agents[$key] = [ordered]@{ Id = $key; Stopped = $true; Time = $received.ToString('o') } }
        elseif (-not $agents.ContainsKey($key)) { $agents[$key] = [ordered]@{ Id = $key; Stopped = $false; Time = $received.ToString('o') } }
    }
    if ($eventName -in @('SessionStart','SessionEnd')) { $agents = @{} }
    $record.Agents = @($agents.Values)
    $record.Subagents = @($agents.Values | Where-Object { -not $_.Stopped }).Count
    if ($state -and $newer) {
        $record.Cwd = [string]$payload.cwd
        $record.State = $state
        $record.Event = $eventName
        $record.Tool = if ($eventName -in @('PreToolUse','PermissionRequest','PostToolUse','PostToolUseFailure')) { [string]$payload.tool_name } else { '' }
        $record.Updated = $received.ToString('o')
        if ($eventName -eq 'UserPromptSubmit') { $record.TurnStarted = $received.ToString('o'); $record.TurnEnded = '' }
        if ($eventName -in @('Stop','StopFailure')) { $record.TurnEnded = $received.ToString('o') }
    } elseif (-not $subagentEvent -and -not $owner) { exit 0 }
    $temporary = $path + '.' + $PID + '.tmp'
    [IO.File]::WriteAllText($temporary, ($record | ConvertTo-Json -Compress -Depth 4), (New-Object Text.UTF8Encoding($false)))
    # NullString passes a true .NET null; PowerShell $null can become an invalid empty path.
    if (Test-Path -LiteralPath $path) { [IO.File]::Replace($temporary,$path,[System.Management.Automation.Language.NullString]::Value) } else { [IO.File]::Move($temporary,$path) }
} catch {
    # Observability must never stop or approve a Claude operation.
} finally {
    if ($locked -and $mutex) { $mutex.ReleaseMutex() }
    if ($mutex) { $mutex.Dispose() }
    if ($temporary -and (Test-Path -LiteralPath $temporary)) { Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue }
}
exit 0
