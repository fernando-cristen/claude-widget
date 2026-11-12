param([string]$RenderFolder)
# Compila o projeto e roda o auto-teste mais o teste de transições dentro do .exe (src\Testes\Transicoes.cs).
# Só dados sintéticos locais: nenhuma configuração, conta ou pedido ao Redmine é usado.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $root 'bin\Release\net8.0-windows\win-x64\MeuClaude.exe'
& dotnet build (Join-Path $root 'MeuClaude.csproj') -c Release -nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar o widget.' }
if ($RenderFolder) { [IO.Directory]::CreateDirectory($RenderFolder) | Out-Null; $arguments = @('--testar-transicoes', $RenderFolder) } else { $arguments = @('--testar-transicoes') }
& $exe @arguments | Write-Output
if ($LASTEXITCODE -ne 0) { throw 'Teste de transições falhou.' }
