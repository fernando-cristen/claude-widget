# MeuClaudeWidgetStatus v2. Shares model, context and cost with the widget and prints a short status line.
# With the widget's Redmine clock, it also shows the hours of the issue this session works on.
$ErrorActionPreference = 'Stop'
$temporary = $null
# Subscription limits arrive here too; the widget falls back to them when the usage API refuses.
function Get-Percent($limit) { if ($null -ne $limit -and $null -ne $limit.used_percentage) { [double]$limit.used_percentage } else { -1 } }
function Get-Reset($limit) {
    if ($null -eq $limit -or $null -eq $limit.resets_at) { return '' }
    $value = $limit.resets_at
    if ($value -is [string]) { $parsed = [DateTimeOffset]::MinValue; if ([DateTimeOffset]::TryParse($value, [ref]$parsed)) { return $parsed.ToString('o') } else { return '' } }
    return [DateTimeOffset]::FromUnixTimeSeconds([long]$value).ToString('o')
}
# Written by the widget once a minute: Redmine host and the hours of my issues in "Em andamento".
function Read-Clock {
    $file = Join-Path $env:LOCALAPPDATA 'MeuClaudeWidget\redmine-clock.json'
    if (-not (Test-Path -LiteralPath $file)) { return $null }
    $clock = [IO.File]::ReadAllText($file) | ConvertFrom-Json
    if (-not $clock.Host) { return $null }
    return $clock
}
# The session's issue is the last Redmine link typed by the user. Only new transcript bytes are read;
# tool results and subagent prompts are skipped because they may mention other issues.
function Find-Issue([string]$transcript, [string]$hostName, [long]$offset, [ref]$next) {
    $next.Value = $offset
    if (-not $transcript -or -not (Test-Path -LiteralPath $transcript)) { return 0 }
    $stream = [IO.File]::Open($transcript, 'Open', 'Read', 'ReadWrite')
    try {
        if ($stream.Length -lt $offset) { $offset = 0 }
        $start = [Math]::Max($offset, $stream.Length - 33554432)
        $count = [int]($stream.Length - $start)
        if ($count -le 0) { return 0 }
        $bytes = New-Object byte[] $count
        $stream.Position = $start
        $read = 0; while ($read -lt $count) { $n = $stream.Read($bytes, $read, $count - $read); if ($n -le 0) { break }; $read += $n }
        $end = [Array]::LastIndexOf($bytes, [byte]10, $read - 1)
        if ($end -lt 0) { return 0 }
        $next.Value = $start + $end + 1
        $text = [Text.Encoding]::UTF8.GetString($bytes, 0, $end)
    } finally { $stream.Dispose() }
    $pattern = 'https?://' + [regex]::Escape($hostName) + '/issues/(\d+)'
    $found = 0
    foreach ($line in $text.Split([char]10)) {
        if ($line.IndexOf('/issues/') -lt 0 -or $line.IndexOf('"type":"user"') -lt 0) { continue }
        try { $entry = $line | ConvertFrom-Json } catch { continue }
        if ($entry.type -ne 'user' -or $entry.isSidechain -or $entry.isMeta) { continue }
        $content = $entry.message.content
        $typed = if ($content -is [string]) { $content } else { (@($content) | Where-Object { $_.type -eq 'text' } | ForEach-Object { [string]$_.text }) -join "`n" }
        $match = [regex]::Match($typed, $pattern)
        if ($match.Success) { $found = [int]$match.Groups[1].Value }
    }
    return $found
}
function Get-IssueLabel($clock, [int]$issue) {
    $updated = [DateTimeOffset]::MinValue
    $fresh = [DateTimeOffset]::TryParse([string]$clock.Updated, [ref]$updated) -and ([DateTimeOffset]::UtcNow - $updated).TotalMinutes -lt 10
    if (-not $fresh) { return '#' + $issue }
    $task = @($clock.Tasks) | Where-Object { [int]$_.Id -eq $issue } | Select-Object -First 1
    if ($task) { return '#' + $issue + ' ' + $task.Total + ' em andamento' }
    return '#' + $issue + ' fora de andamento'
}
# The widget's creature, in a few characters: its face follows this session's state, then its name and level.
function Get-Creature([string]$id) {
    try {
        $file = Join-Path $env:LOCALAPPDATA 'MeuClaudeWidget\bicho.json'
        if (-not (Test-Path -LiteralPath $file)) { return '' }
        $pet = [IO.File]::ReadAllText($file) | ConvertFrom-Json
        $state = ''
        $radar = Join-Path $env:LOCALAPPDATA ('MeuClaudeWidget\radar\' + $id + '.json')
        if (Test-Path -LiteralPath $radar) { $state = [string](([IO.File]::ReadAllText($radar) | ConvertFrom-Json).State) }
        $face = switch ($state) {
            'working' { '(•_•)⌨' } 'background' { '(•_•)⌨' } 'waiting' { '(°o°)!' } 'done' { '\(^o^)/' } 'error' { '(×_×)' }
            default { if ($pet.Night) { '(-.-)zZ' } else { '(•‿•)' } }
        }
        $label = $face
        if ($pet.Name) { $label += ' ' + $pet.Name }
        return $label
    } catch { return '' }
}
try {
    [Console]::OutputEncoding = New-Object Text.UTF8Encoding($false)
    $data = [Console]::In.ReadToEnd() | ConvertFrom-Json
    $model = [string]$data.model.display_name
    $context = $data.context_window.used_percentage
    $cost = $data.cost.total_cost_usd
    $parts = @()
    if ($model) { $parts += $model }
    if ($null -ne $context) { $parts += ('contexto ' + [Math]::Round([double]$context) + '%') }
    if ($null -ne $cost) { $parts += ('US$ ' + ([double]$cost).ToString('0.00', [Globalization.CultureInfo]::GetCultureInfo('pt-BR'))) }
    if (-not $data.session_id) { [Console]::Out.Write(($parts -join ' | ')); exit 0 }
    $hash = [Security.Cryptography.SHA256]::Create()
    try { $id = ([BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes([string]$data.session_id)))).Replace('-','').ToLowerInvariant() } finally { $hash.Dispose() }
    $creature = Get-Creature $id
    if ($creature) { $parts = @($creature) + $parts }
    $folder = Join-Path $env:LOCALAPPDATA 'MeuClaudeWidget\radar\status'
    [IO.Directory]::CreateDirectory($folder) | Out-Null
    $path = Join-Path $folder ($id + '.json')
    $previous = $null
    if (Test-Path -LiteralPath $path) { try { $previous = [IO.File]::ReadAllText($path) | ConvertFrom-Json } catch { } }
    $issue = 0; $scanned = [long]0
    if ($previous) { if ($previous.Issue) { $issue = [int]$previous.Issue }; if ($previous.Scanned) { $scanned = [long]$previous.Scanned } }
    try {
        $clock = Read-Clock
        if ($clock) {
            $next = $scanned
            $found = Find-Issue ([string]$data.transcript_path) ([string]$clock.Host) $scanned ([ref]$next)
            $scanned = $next
            if ($found -gt 0) { $issue = $found }
            if ($issue -gt 0) { $parts += (Get-IssueLabel $clock $issue) }
        }
    } catch { }
    [Console]::Out.Write(($parts -join ' | '))
    $record = [ordered]@{
        Model = $model
        Context = if ($null -ne $context) { [double]$context } else { -1 }
        Cost = if ($null -ne $cost) { [double]$cost } else { -1 }
        Updated = [DateTimeOffset]::UtcNow.ToString('o')
        Five = Get-Percent $data.rate_limits.five_hour
        FiveReset = Get-Reset $data.rate_limits.five_hour
        Week = Get-Percent $data.rate_limits.seven_day
        WeekReset = Get-Reset $data.rate_limits.seven_day
        Issue = $issue
        Scanned = $scanned
    }
    $temporary = $path + '.' + $PID + '.tmp'
    [IO.File]::WriteAllText($temporary, ($record | ConvertTo-Json -Compress), (New-Object Text.UTF8Encoding($false)))
    if (Test-Path -LiteralPath $path) { [IO.File]::Replace($temporary,$path,[System.Management.Automation.Language.NullString]::Value) } else { [IO.File]::Move($temporary,$path) }
} catch {
    # A status line failure must never disturb Claude Code.
} finally {
    if ($temporary -and (Test-Path -LiteralPath $temporary)) { Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue }
}
exit 0
