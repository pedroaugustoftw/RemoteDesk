param([string]$Dotnet)
$ErrorActionPreference = 'Stop'
$script:ProjectRoot = Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrWhiteSpace($Dotnet)) {
    $workspaceDotnet = Join-Path (Split-Path $script:ProjectRoot -Parent) '.tools\dotnet\dotnet.exe'
    if (Test-Path -LiteralPath $workspaceDotnet) { $Dotnet = $workspaceDotnet }
    else { $Dotnet = (Get-Command dotnet -ErrorAction Stop).Source }
}
$script:DotnetPath = (Resolve-Path -LiteralPath $Dotnet).Path
$script:UtilityDll = Join-Path $PSScriptRoot 'LanSetup\bin\Release\net8.0-windows\RemoteDesk.LanSetup.dll'
if (-not (Test-Path -LiteralPath $script:UtilityDll)) {
    & $script:DotnetPath build (Join-Path $PSScriptRoot 'LanSetup\RemoteDesk.LanSetup.csproj') -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar ferramenta LAN.' }
}
