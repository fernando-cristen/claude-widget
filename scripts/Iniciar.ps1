param([switch]$SelfTest)
$ErrorActionPreference = 'Stop'
try {
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing
    # All the C# under src\ is compiled together: Widget.cs first (it holds the file-level usings), then the rest.
    $src = Join-Path (Split-Path $PSScriptRoot -Parent) 'src'
    $files = @(Get-ChildItem -LiteralPath $src -Recurse -Filter '*.cs' | Sort-Object { if ($_.Name -eq 'Widget.cs') { '' } else { $_.FullName } })
    $source = ($files | ForEach-Object { [System.IO.File]::ReadAllText($_.FullName, [System.Text.Encoding]::UTF8) }) -join "`r`n"
    $references = @('System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll', 'System.Net.Http.dll', 'System.Web.Extensions.dll', 'System.Security.dll')
    # UI Automation (finds the weather button and the taskbar icons) lives in the WPF folder of .NET Framework.
    foreach ($name in @('UIAutomationClient', 'UIAutomationTypes', 'WindowsBase')) { $references += [System.Reflection.Assembly]::Load($name + ', Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35').Location }
    # Compiling takes seconds; the DLL is cached by source hash and rebuilt only when a .cs file changes.
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { $hash = ([BitConverter]::ToString($sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($source)))).Replace('-', '').Substring(0, 16) } finally { $sha.Dispose() }
    $cache = Join-Path $env:LOCALAPPDATA 'MeuClaudeWidget\bin'
    $dll = Join-Path $cache ('MeuClaudeWidget-' + $hash + '.dll')
    if (-not (Test-Path -LiteralPath $dll)) {
        [System.IO.Directory]::CreateDirectory($cache) | Out-Null
        foreach ($old in @(Get-ChildItem -LiteralPath $cache -Filter '*.dll')) { try { Remove-Item -LiteralPath $old.FullName -Force -ErrorAction Stop } catch { } }
        $build = Join-Path $cache ('build-' + $PID + '.dll')
        Add-Type -TypeDefinition $source -Language CSharp -ReferencedAssemblies $references -OutputAssembly $build -OutputType Library
        if (Test-Path -LiteralPath $dll) { Remove-Item -LiteralPath $build -Force } else { [System.IO.File]::Move($build, $dll) }
    }
    Add-Type -Path $dll
    if ($SelfTest) {
        $result = [MeuClaudeWidget.Program]::SelfTest()
        Write-Output $result
        [System.Windows.Forms.MessageBox]::Show($result, 'Teste do widget') | Out-Null
    } else {
        [MeuClaudeWidget.Program]::Run($PSScriptRoot)
    }
} catch {
    $message = $_.Exception.Message
    if ($message.Length -gt 4500) { $message = $message.Substring(0, 4500) }
    [System.Windows.Forms.MessageBox]::Show("Nao foi possivel abrir o widget.`r`n`r`n$message", 'Meu Claude - erro') | Out-Null
    exit 1
}
