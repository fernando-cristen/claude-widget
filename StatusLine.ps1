# MeuClaudeWidgetStatus v1. Shares model, context and cost with the widget and prints a short status line.
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
    [Console]::Out.Write(($parts -join ' | '))
    if (-not $data.session_id) { exit 0 }
    $hash = [Security.Cryptography.SHA256]::Create()
    try { $id = ([BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes([string]$data.session_id)))).Replace('-','').ToLowerInvariant() } finally { $hash.Dispose() }
    $folder = Join-Path $env:LOCALAPPDATA 'MeuClaudeWidget\radar\status'
    [IO.Directory]::CreateDirectory($folder) | Out-Null
    $path = Join-Path $folder ($id + '.json')
    $record = [ordered]@{
        Model = $model
        Context = if ($null -ne $context) { [double]$context } else { -1 }
        Cost = if ($null -ne $cost) { [double]$cost } else { -1 }
        Updated = [DateTimeOffset]::UtcNow.ToString('o')
        Five = Get-Percent $data.rate_limits.five_hour
        FiveReset = Get-Reset $data.rate_limits.five_hour
        Week = Get-Percent $data.rate_limits.seven_day
        WeekReset = Get-Reset $data.rate_limits.seven_day
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
