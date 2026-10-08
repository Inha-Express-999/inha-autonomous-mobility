[CmdletBinding()]
param([ValidateRange(1024, 65535)] [int]$Port = 8765)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$venvPython = Join-Path $repoRoot 'tmp/mvp-venv/Scripts/python.exe'
$python = if (Test-Path -LiteralPath $venvPython -PathType Leaf) { $venvPython } else { 'python' }
& $python (Join-Path $PSScriptRoot 'RunMinimalDemo.py') --port $Port
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
