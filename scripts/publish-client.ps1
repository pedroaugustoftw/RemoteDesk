param([string]$Dotnet='dotnet')
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
$clientOutput=Join-Path $projectRoot 'artifacts\win-x64\client'
$setupOutput=Join-Path $projectRoot 'artifacts\win-x64\installer'
& $Dotnet publish (Join-Path $projectRoot 'client\RemoteDesk.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $clientOutput
if($LASTEXITCODE -ne 0){throw 'Falha publicando cliente'}
& $Dotnet publish (Join-Path $projectRoot 'uninstaller/RemoteDesk.Uninstall.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o (Join-Path $projectRoot 'artifacts/win-x64/uninstaller')
if($LASTEXITCODE -ne 0){throw 'Falha publicando desinstalador'}
Copy-Item -LiteralPath (Join-Path $projectRoot 'artifacts/win-x64/uninstaller/RemoteDesk.Uninstall.exe') -Destination (Join-Path $clientOutput 'RemoteDesk.Uninstall.exe') -Force
$payload=Join-Path $projectRoot 'installer\payload.zip'
if(Test-Path -LiteralPath $payload){Remove-Item -LiteralPath $payload}
Compress-Archive -Path (Join-Path $clientOutput '*') -DestinationPath $payload -CompressionLevel Optimal
& $Dotnet publish (Join-Path $projectRoot 'installer\RemoteDesk.Setup.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $setupOutput
if($LASTEXITCODE -ne 0){throw 'Falha publicando instalador'}
$clientHash=(Get-FileHash -LiteralPath (Join-Path $clientOutput 'RemoteDesk.exe')).Hash
$setupExe=Join-Path $setupOutput 'RemoteDesk-Setup.exe'
$verification=Start-Process -FilePath $setupExe -ArgumentList @('--verify-payload','--expected-client-sha256',$clientHash) -WindowStyle Hidden -Wait -PassThru
if($verification.ExitCode -ne 0){throw 'Payload do cliente inválido'}
Get-FileHash -LiteralPath (Join-Path $clientOutput 'RemoteDesk.exe'),$setupExe
