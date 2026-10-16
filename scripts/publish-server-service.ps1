param([string]$Dotnet='dotnet',[string]$SetupOutput='')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$output=Join-Path $root 'artifacts/win-x64/server-service'
foreach($component in @('server','relay','service')){
  $project=@{server='server/RemoteDesk.Server.csproj';relay='relay/RemoteDesk.Relay.csproj';service='service/RemoteDesk.Service.csproj'}[$component]
  & $Dotnet publish (Join-Path $root $project) -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o (Join-Path $output $component)
  if($LASTEXITCODE -ne 0){throw "Falha compilando $component"}
}
& $Dotnet publish (Join-Path $root 'uninstaller/RemoteDesk.Uninstall.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o (Join-Path $output 'uninstaller')
if($LASTEXITCODE -ne 0){throw 'Falha publicando desinstalador'}
$payload=Join-Path $root 'server-installer/payload.zip'
if(Test-Path -LiteralPath $payload){Remove-Item -LiteralPath $payload}
Compress-Archive -Path (Join-Path $output '*') -DestinationPath $payload -CompressionLevel Optimal
if([string]::IsNullOrWhiteSpace($SetupOutput)){$SetupOutput=Join-Path $root 'artifacts/win-x64/server-installer'}
& $Dotnet publish (Join-Path $root 'server-installer/RemoteDesk.ServerSetup.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $SetupOutput
if($LASTEXITCODE -ne 0){throw 'Falha compilando instalador servidor'}
& $Dotnet (Join-Path $root 'server-installer/bin/Release/net8.0-windows/win-x64/RemoteDesk-Server-Setup.dll') --verify-payload
if($LASTEXITCODE -ne 0){throw 'Payload servidor invalido'}
Get-FileHash -LiteralPath (Join-Path $SetupOutput 'RemoteDesk-Server-Setup.exe')
