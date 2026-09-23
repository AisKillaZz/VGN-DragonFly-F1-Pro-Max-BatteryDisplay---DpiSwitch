[CmdletBinding()]
param(
    [switch]$Publish
)

$ErrorActionPreference = 'Stop'

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    Write-Error '找不到 dotnet。请先安装 .NET 8 SDK：https://dotnet.microsoft.com/download/dotnet/8.0'
}

$sdkList = & $dotnet.Source --list-sdks 2>$null
if (-not $sdkList) {
    Write-Error '当前只有 .NET Runtime，没有 .NET SDK。请安装 .NET 8 SDK 或 Visual Studio 2022 的“.NET 桌面开发”工作负载。'
}

if ($Publish) {
    & $dotnet.Source publish .\VgnTrayBattery.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
} else {
    & $dotnet.Source run --project .\VgnTrayBattery.csproj
}
