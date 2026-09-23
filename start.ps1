[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$exe = Join-Path $PSScriptRoot 'bin\Debug\net8.0-windows\VgnTrayBattery.exe'
if (-not (Test-Path -LiteralPath $exe)) {
    throw '还没有构建程序。请先运行 .\build.ps1'
}
Start-Process -FilePath $exe -WorkingDirectory (Split-Path -Parent $exe)
