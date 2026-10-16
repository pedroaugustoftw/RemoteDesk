param(
    [string]$Dotnet = 'E:\AnyCraft\.tools\dotnet\dotnet.exe',
    [ValidateSet('win-x64','win-arm64')][string]$Runtime = 'win-x64'
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
if (-not (Test-Path -LiteralPath $Dotnet)) { throw "SDK não encontrado: $Dotnet" }
$outputRoot = Join-Path $projectRoot "artifacts\$Runtime"
$clientOutput = Join-Path $outputRoot 'client'
$serverOutput = Join-Path $outputRoot 'server'
$relayOutput = Join-Path $outputRoot 'relay'
$lanToolsOutput = Join-Path $outputRoot 'lan-tools'
$setupOutput = Join-Path $outputRoot 'installer'
New-Item -ItemType Directory -Force $outputRoot | Out-Null
& $Dotnet publish (Join-Path $projectRoot 'client\RemoteDesk.csproj') -c Release -r $Runtime --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $clientOutput
if ($LASTEXITCODE -ne 0) { throw 'Falha na publicação do cliente.' }
& $Dotnet publish (Join-Path $projectRoot 'server\RemoteDesk.Server.csproj') -c Release -o $serverOutput
if ($LASTEXITCODE -ne 0) { throw 'Falha na publicação do servidor.' }
& $Dotnet publish (Join-Path $projectRoot 'relay\RemoteDesk.Relay.csproj') -c Release -o $relayOutput
if ($LASTEXITCODE -ne 0) { throw 'Falha na publicação do relay.' }
& $Dotnet publish (Join-Path $projectRoot 'scripts\LanSetup\RemoteDesk.LanSetup.csproj') -c Release -o $lanToolsOutput
if ($LASTEXITCODE -ne 0) { throw 'Falha na publicação da ferramenta LAN.' }
$payload = Join-Path $projectRoot 'installer\payload.zip'
if (Test-Path -LiteralPath $payload) { Remove-Item -LiteralPath $payload -Force }
Compress-Archive -Path (Join-Path $clientOutput '*') -DestinationPath $payload -CompressionLevel Optimal
& $Dotnet publish (Join-Path $projectRoot 'installer\RemoteDesk.Setup.csproj') -c Release -r $Runtime --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $setupOutput
if ($LASTEXITCODE -ne 0) { throw 'Falha na publicação do instalador.' }
$setupExe = Join-Path $setupOutput 'RemoteDesk-Setup.exe'
$clientHash = (Get-FileHash -LiteralPath (Join-Path $clientOutput 'RemoteDesk.exe') -Algorithm SHA256).Hash
$verification = Start-Process -FilePath $setupExe -ArgumentList @('--verify-payload', '--expected-client-sha256', $clientHash) -WindowStyle Hidden -Wait -PassThru
if ($verification.ExitCode -ne 0) { throw 'Falha na verificação somente-leitura do payload do instalador.' }
$deliverables = @((Join-Path $clientOutput 'RemoteDesk.exe'), (Join-Path $setupOutput 'RemoteDesk-Setup.exe'))
foreach ($file in $deliverables) {
    if (-not (Test-Path -LiteralPath $file)) { throw "Arquivo esperado ausente: $file" }
    Get-FileHash -LiteralPath $file -Algorithm SHA256
}
