param([switch]$SelfTest)
$ErrorActionPreference = 'Stop'
try {
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing
    $source = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Widget.cs'), [System.Text.Encoding]::UTF8)
    $source += "`r`n" + [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Radar.cs'), [System.Text.Encoding]::UTF8)
    Add-Type -TypeDefinition $source -Language CSharp -ReferencedAssemblies @('System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll', 'System.Net.Http.dll', 'System.Web.Extensions.dll', 'System.Security.dll')
    if ($SelfTest) {
        $result = [MeuClaudeWidget.Program]::SelfTest()
        Write-Output $result
        [System.Windows.Forms.MessageBox]::Show($result, 'Teste do widget') | Out-Null
    } else {
        [MeuClaudeWidget.Program]::Run()
    }
} catch {
    $message = $_.Exception.Message
    if ($message.Length -gt 4500) { $message = $message.Substring(0, 4500) }
    [System.Windows.Forms.MessageBox]::Show("Nao foi possivel abrir o widget.`r`n`r`n$message", 'Meu Claude - erro') | Out-Null
    exit 1
}
